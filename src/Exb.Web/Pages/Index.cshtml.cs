using Exb.Core.Facility;
using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages;

public class IndexModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    FacilityProvider facility,
    SettingsStore settings,
    TrackingRuntime runtime) : PageModel
{
    public DateOnly Today { get; private set; }
    public CoverageReport Coverage => facility.Current.Coverage;
    public string DriverName => runtime.DriverName;
    public bool TrackingRunning => runtime.IsRunning;

    public int Halls { get; private set; }
    public int Exhibitors { get; private set; }
    public int Stands { get; private set; }
    public int RegisteredVisitors { get; private set; }
    public int BadgedVisitors { get; private set; }

    public int VisitsToday { get; private set; }
    public int InterestedVisitsToday { get; private set; }
    public int VisitorsSeenToday { get; private set; }
    public int ScansToday { get; private set; }
    public int PacksSent { get; private set; }
    public int ReportsGenerated { get; private set; }
    public int OutboxPending { get; private set; }
    public int OutboxFailed { get; private set; }

    public IReadOnlyList<TopStand> BusiestStands { get; private set; } = [];
    public IReadOnlyList<TopCategory> TopCategories { get; private set; } = [];
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public record TopStand(string StandNumber, string Exhibitor, string Hall, int Visitors, int TotalSeconds);
    public record TopCategory(string Name, int Visitors, int TotalSeconds);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Today = TrackingRuntime.LocalDate(settings.Current.Exhibition);
        await using var db = await factory.CreateDbContextAsync(ct);

        Halls = await db.Halls.CountAsync(h => h.IsActive, ct);
        Exhibitors = await db.Exhibitors.CountAsync(e => e.IsActive, ct);
        Stands = await db.Kiosks.CountAsync(k => k.IsActive, ct);
        RegisteredVisitors = await db.Visitors.CountAsync(v => v.IsActive, ct);
        BadgedVisitors = await db.Visitors.CountAsync(v => v.IsActive && v.BadgeEpc != "", ct);

        var today = db.Visits.AsNoTracking().Where(v => v.EventDate == Today);
        VisitsToday = await today.CountAsync(ct);
        InterestedVisitsToday = await today.CountAsync(v => v.Level >= InterestLevel.Interested, ct);
        VisitorsSeenToday = await today.Select(v => v.VisitorId).Distinct().CountAsync(ct);

        ScansToday = await db.CatalogueRequests.CountAsync(r => r.EventDate == Today && r.Included, ct);
        PacksSent = await db.DeliveryJobs.CountAsync(j => j.EventDate == Today && j.Status == JobStatus.Succeeded, ct);
        ReportsGenerated = await db.DailyReports.CountAsync(r => r.EventDate == Today, ct);
        OutboxPending = await db.OutboxEmails.CountAsync(m => m.Status == JobStatus.Pending, ct);
        OutboxFailed = await db.OutboxEmails.CountAsync(m => m.Status == JobStatus.Failed, ct);

        BusiestStands = await db.Visits
            .AsNoTracking()
            .Where(v => v.EventDate == Today && v.Level >= InterestLevel.Browsed)
            .GroupBy(v => v.KioskId)
            .Select(g => new
            {
                g.Key,
                Visitors = g.Select(v => v.VisitorId).Distinct().Count(),
                Seconds = g.Sum(v => v.DwellSeconds),
            })
            .OrderByDescending(x => x.Visitors)
            .Take(10)
            .Join(db.Kiosks.AsNoTracking(), x => x.Key, k => k.Id,
                (x, k) => new TopStand(k.StandNumber, k.Exhibitor.CompanyName, k.Hall.Name, x.Visitors, x.Seconds))
            .ToListAsync(ct);

        TopCategories = await db.Visits
            .AsNoTracking()
            .Where(v => v.EventDate == Today && v.Level >= InterestLevel.Browsed && v.CategoryId != null)
            .GroupBy(v => v.CategoryId)
            .Select(g => new
            {
                g.Key,
                Visitors = g.Select(v => v.VisitorId).Distinct().Count(),
                Seconds = g.Sum(v => v.DwellSeconds),
            })
            .OrderByDescending(x => x.Seconds)
            .Take(8)
            .Join(db.Categories.AsNoTracking(), x => x.Key, c => c.Id,
                (x, c) => new TopCategory(c.Name, x.Visitors, x.Seconds))
            .ToListAsync(ct);

        Warnings = BuildWarnings();
    }

    /// <summary>
    /// The things that quietly ruin an exhibition if nobody notices them on the
    /// first morning. Each one is phrased as what will go wrong, not as what is
    /// misconfigured.
    /// </summary>
    private List<string> BuildWarnings()
    {
        var warnings = new List<string>();
        var app = settings.Current;

        if (Halls == 0)
            warnings.Add("No halls are configured, so nothing can be tracked. Add one in Settings > Halls.");

        if (Stands == 0 && Halls > 0)
            warnings.Add("No stands are placed on the floor, so no interest can be recorded. Add exhibitors and place their stands.");

        if (Coverage.Warning is not null)
            warnings.Add($"{Coverage.Warning} {Coverage.Remedy}");

        if (BadgedVisitors == 0 && RegisteredVisitors > 0)
            warnings.Add("No registered visitor has a badge EPC assigned, so no visit can be attributed to a person.");

        if (!TrackingRunning)
            warnings.Add("Tracking is not running. Check Settings > Readers, or enable the simulator.");

        if (app.Mail.Provider.Equals("outbox", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Email is set to hold in the Outbox, so nothing is being delivered to visitors. That is the safe default — switch it in Settings > Delivery when you are ready to send.");

        if (OutboxFailed > 0)
            warnings.Add($"{OutboxFailed} email(s) have failed to send. See Settings > Outbox.");

        if (app.Exhibition.PublicBaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            warnings.Add("The public base URL is still localhost, so QR codes printed now will not open on a visitor's phone. Set it in Settings > Delivery.");

        return warnings;
    }
}
