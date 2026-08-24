using Exb.Core.Interest;
using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages;

/// <summary>
/// The visitor's own page, opened from the QR code on their badge.
///
/// It shows only their own day, and it lets them take a stand back out of the
/// evening pack. That control matters: a visitor who scanned a code by accident
/// should not have to receive that catalogue, and having the option makes the
/// whole arrangement feel like something done with them rather than to them.
/// </summary>
public class MeModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    CatalogueRequestService catalogues,
    InterestQueryService interest,
    SettingsStore settings) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";

    public Visitor? Visitor { get; private set; }
    public DateOnly Today { get; private set; }
    public IReadOnlyList<ScanTarget> Scans { get; private set; } = [];
    public VisitorDayProfile? Profile { get; private set; }
    public DeliveryJob? Delivery { get; private set; }
    public string ExhibitionName => settings.Current.Exhibition.Name;
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return Page();

        Response.Cookies.Append(ScanModel.VisitorCookie, Visitor!.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(14),
        });

        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int kioskId, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return Page();

        await catalogues.SetIncludedAsync(Visitor!.Id, kioskId, Today, false, ct);
        TempData["message"] = "Removed from tonight's pack.";
        return RedirectToPage(new { token = Token });
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Message = TempData["message"] as string;
        Today = TrackingRuntime.LocalDate(settings.Current.Exhibition);

        await using var db = await factory.CreateDbContextAsync(ct);
        Visitor = await db.Visitors.AsNoTracking()
            .FirstOrDefaultAsync(v => v.AccessToken == Token && v.IsActive, ct);

        if (Visitor is null) return false;

        Scans = await catalogues.TodayForVisitorAsync(Visitor.Id, Today, ct);
        Delivery = await db.DeliveryJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.VisitorId == Visitor.Id && j.EventDate == Today, ct);

        if (Visitor.ConsentTracking)
            Profile = await interest.ProfileAsync(Visitor.Id, Today, ct);

        return true;
    }
}
