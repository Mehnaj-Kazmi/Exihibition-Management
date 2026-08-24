using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Visitors;

public class IndexModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    SettingsStore settings) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Filter { get; set; }

    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public int Total { get; private set; }
    public int Badged { get; private set; }
    public int NoTrackingConsent { get; private set; }
    public int SeenToday { get; private set; }
    public DateOnly Today { get; private set; }

    public record Row(
        int Id, string Name, string Email, string? Company, string RegistrationCode,
        string BadgeEpc, bool ConsentTracking, bool ConsentEmail,
        int StandsToday, int InterestedToday, int ScansToday, int DwellSecondsToday);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Today = TrackingRuntime.LocalDate(settings.Current.Exhibition);
        await using var db = await factory.CreateDbContextAsync(ct);

        Total = await db.Visitors.CountAsync(v => v.IsActive, ct);
        Badged = await db.Visitors.CountAsync(v => v.IsActive && v.BadgeEpc != "", ct);
        NoTrackingConsent = await db.Visitors.CountAsync(v => v.IsActive && !v.ConsentTracking, ct);
        SeenToday = await db.Visits.Where(v => v.EventDate == Today).Select(v => v.VisitorId).Distinct().CountAsync(ct);

        var query = db.Visitors.AsNoTracking().Where(v => v.IsActive);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            string term = Q.Trim();
            query = query.Where(v =>
                EF.Functions.Like(v.FullName, $"%{term}%") ||
                EF.Functions.Like(v.Email, $"%{term}%") ||
                EF.Functions.Like(v.Company!, $"%{term}%") ||
                EF.Functions.Like(v.RegistrationCode, $"%{term}%") ||
                EF.Functions.Like(v.BadgeEpc, $"%{term}%"));
        }

        query = Filter switch
        {
            "nobadge" => query.Where(v => v.BadgeEpc == ""),
            "notracking" => query.Where(v => !v.ConsentTracking),
            "noemail" => query.Where(v => !v.ConsentEmail),
            "active" => query.Where(v => v.Visits.Any(x => x.EventDate == Today)),
            _ => query,
        };

        var visitors = await query.OrderBy(v => v.FullName).Take(500).ToListAsync(ct);
        var ids = visitors.Select(v => v.Id).ToList();

        var visitStats = await db.Visits.AsNoTracking()
            .Where(v => v.EventDate == Today && ids.Contains(v.VisitorId) && v.Level >= InterestLevel.Browsed)
            .GroupBy(v => v.VisitorId)
            .Select(g => new
            {
                g.Key,
                Stands = g.Select(v => v.KioskId).Distinct().Count(),
                Interested = g.Count(v => v.Level >= InterestLevel.Interested),
                Seconds = g.Sum(v => v.DwellSeconds),
            })
            .ToDictionaryAsync(x => x.Key, x => x, ct);

        var scans = await db.CatalogueRequests.AsNoTracking()
            .Where(r => r.EventDate == Today && ids.Contains(r.VisitorId) && r.Included)
            .GroupBy(r => r.VisitorId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        Rows = visitors.Select(v =>
        {
            var stats = visitStats.GetValueOrDefault(v.Id);
            return new Row(
                v.Id, v.FullName, v.Email, v.Company, v.RegistrationCode, v.BadgeEpc,
                v.ConsentTracking, v.ConsentEmail,
                stats?.Stands ?? 0, stats?.Interested ?? 0,
                scans.GetValueOrDefault(v.Id), stats?.Seconds ?? 0);
        }).ToList();
    }
}
