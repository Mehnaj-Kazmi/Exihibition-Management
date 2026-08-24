using Exb.Data;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages;

/// <summary>
/// Where a stand's QR code lands.
///
/// The code resolves here rather than to the exhibitor's own website, because
/// the request has to be recorded against a visitor before the catalogue is
/// handed over — that is what makes one evening pack possible instead of thirty
/// separate downloads during the day.
///
/// A visitor we cannot identify still gets the stand's details and a way to
/// identify themselves. Sending them to a dead end would help nobody.
/// </summary>
public class ScanModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    CatalogueRequestService catalogues,
    SettingsStore settings) : PageModel
{
    public const string VisitorCookie = "exb_visitor";

    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    [BindProperty] public string? Identifier { get; set; }

    public ScanTarget? Target { get; private set; }
    public ScanOutcome Outcome { get; private set; } = ScanOutcome.NotIdentified;
    public string? VisitorName { get; private set; }
    public string? VisitorAccessToken { get; private set; }
    public int TodayCount { get; private set; }
    public string? Problem { get; private set; }
    public string ExhibitionName => settings.Current.Exhibition.Name;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await ProcessAsync(Request.Cookies[VisitorCookie], ct);
        return Page();
    }

    /// <summary>Identify by the code printed on the badge, then record the scan.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        string entered = (Identifier ?? "").Trim();

        if (entered.Length == 0)
        {
            Problem = "Enter the code printed on your badge.";
            await ProcessAsync(null, ct);
            return Page();
        }

        await using var db = await factory.CreateDbContextAsync(ct);

        string normalised = entered.ToUpperInvariant();
        var visitor = await db.Visitors
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.IsActive &&
                (v.RegistrationCode == normalised || v.AccessToken == normalised || v.Email == entered.ToLowerInvariant()), ct);

        if (visitor is null)
        {
            Problem = "We could not find that badge. Check the code, or ask at the registration desk.";
            await ProcessAsync(null, ct);
            return Page();
        }

        SetCookie(visitor.AccessToken);
        await ProcessAsync(visitor.AccessToken, ct);
        return Page();
    }

    private async Task ProcessAsync(string? accessToken, CancellationToken ct)
    {
        Target = await catalogues.ResolveAsync(Token, ct);
        if (Target is null)
        {
            Outcome = ScanOutcome.UnknownStand;
            return;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            Outcome = ScanOutcome.NotIdentified;
            return;
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var visitor = await db.Visitors.AsNoTracking()
            .FirstOrDefaultAsync(v => v.AccessToken == accessToken && v.IsActive, ct);

        if (visitor is null)
        {
            Response.Cookies.Delete(VisitorCookie);
            Outcome = ScanOutcome.NotIdentified;
            return;
        }

        VisitorName = visitor.FullName;
        VisitorAccessToken = visitor.AccessToken;

        var day = TrackingRuntime.LocalDate(settings.Current.Exhibition);
        var result = await catalogues.RecordAsync(Token, visitor.Id, day, "qr", ct);

        Outcome = result.Outcome;
        TodayCount = result.TodayCount;
    }

    private void SetCookie(string accessToken)
        => Response.Cookies.Append(VisitorCookie, accessToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            // Long enough to cover a multi-day show, so a visitor identifies
            // themselves once rather than at every stand.
            Expires = DateTimeOffset.UtcNow.AddDays(14),
        });
}
