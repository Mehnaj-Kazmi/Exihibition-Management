using Exb.Core.Interest;
using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Visitors;

/// <summary>
/// One visitor's day, exactly as the system understands it: every stop in order,
/// what it was classified as, and how confident the attribution was.
///
/// The quality columns are shown rather than hidden because this is the screen a
/// steward will open when a visitor asks "how do you know that?", and the honest
/// answer includes how strong the evidence was.
/// </summary>
public class DetailsModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    InterestQueryService interest,
    SettingsStore settings) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Day { get; set; }

    public Visitor? Visitor { get; private set; }
    public DateOnly SelectedDay { get; private set; }
    public IReadOnlyList<DateOnly> AvailableDays { get; private set; } = [];
    public VisitorDayProfile? Profile { get; private set; }
    public IReadOnlyList<Stop> Timeline { get; private set; } = [];
    public IReadOnlyList<ScanRow> Scans { get; private set; } = [];
    public DeliveryJob? Delivery { get; private set; }
    public DailyReport? Report { get; private set; }

    public record Stop(
        DateTime StartedUtc, DateTime EndedUtc, int DwellSeconds, string StandNumber, string Exhibitor,
        string Hall, string? Category, InterestLevel Level, double MeanConfidence, double MeanMarginM, int Samples);

    public record ScanRow(DateTime RequestedUtc, string StandNumber, string Exhibitor, bool Included, int Files);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        Visitor = await db.Visitors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == Id, ct);
        if (Visitor is null) return RedirectToPage("/Visitors/Index");

        AvailableDays = await db.Visits.Where(v => v.VisitorId == Id)
            .Select(v => v.EventDate).Distinct().OrderByDescending(d => d).ToListAsync(ct);

        SelectedDay = Day
            ?? (AvailableDays.Count > 0 ? AvailableDays[0] : TrackingRuntime.LocalDate(settings.Current.Exhibition));

        Timeline = await db.Visits.AsNoTracking()
            .Where(v => v.VisitorId == Id && v.EventDate == SelectedDay)
            .OrderBy(v => v.StartedUtc)
            .Select(v => new Stop(
                v.StartedUtc, v.EndedUtc, v.DwellSeconds,
                v.Kiosk.StandNumber, v.Kiosk.Exhibitor.CompanyName, v.Kiosk.Hall.Name,
                v.Kiosk.Exhibitor.Category != null ? v.Kiosk.Exhibitor.Category.Name : null,
                v.Level, v.MeanConfidence, v.MeanMarginM, v.SampleCount))
            .ToListAsync(ct);

        Scans = await db.CatalogueRequests.AsNoTracking()
            .Where(r => r.VisitorId == Id && r.EventDate == SelectedDay)
            .OrderBy(r => r.RequestedUtc)
            .Select(r => new ScanRow(
                r.RequestedUtc, r.Kiosk.StandNumber, r.Kiosk.Exhibitor.CompanyName, r.Included,
                r.Kiosk.Exhibitor.Catalogues.Count(c => c.IsActive)))
            .ToListAsync(ct);

        Delivery = await db.DeliveryJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.VisitorId == Id && j.EventDate == SelectedDay, ct);
        Report = await db.DailyReports.AsNoTracking()
            .FirstOrDefaultAsync(r => r.VisitorId == Id && r.EventDate == SelectedDay, ct);

        Profile = await interest.ProfileAsync(Id, SelectedDay, ct);
        return Page();
    }
}
