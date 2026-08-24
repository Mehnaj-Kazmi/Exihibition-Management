using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Reports;

/// <summary>
/// The evening run and its results.
///
/// The run button is deliberately explicit about what it will do before it does
/// it, because the same action both builds files and queues mail to real people.
/// </summary>
public class IndexModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    EndOfDayService endOfDay,
    SettingsStore settings) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? Day { get; set; }

    public DateOnly SelectedDay { get; private set; }
    public IReadOnlyList<DateOnly> Days { get; private set; } = [];
    public IReadOnlyList<Row> Rows { get; private set; } = [];

    public int ActiveVisitors { get; private set; }
    public int ReportsBuilt { get; private set; }
    public int PacksBuilt { get; private set; }
    public int EmailsQueued { get; private set; }
    public int EmailsSent { get; private set; }
    public bool DayClosed { get; private set; }
    public string MailProvider { get; private set; } = "";
    public string DeliveryProvider { get; private set; } = "";
    public bool MailWillSend { get; private set; }

    public string? Message { get; private set; }
    public string? Problem { get; private set; }

    public record Row(
        int VisitorId, int? ReportId, string Name, string Email, bool ConsentEmail,
        int StandsVisited, int StandsMissed, int DwellSeconds,
        JobStatus? ReportStatus, JobStatus? PackStatus, int PackItems, string? PackUrl,
        JobStatus? EmailStatus, string? EmailError);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostRunAsync(bool resend, CancellationToken ct)
    {
        var day = Day ?? TrackingRuntime.LocalDate(settings.Current.Exhibition);

        try
        {
            var result = await endOfDay.RunAsync(day, resend, ct: ct);
            TempData["message"] =
                $"{day:d MMMM}: {result.ReportsBuilt} report(s) built, {result.PacksBuilt} pack(s) assembled, " +
                $"{result.EmailsQueued} email(s) queued, {result.Skipped} skipped.";

            if (result.Problems.Count > 0)
                TempData["problem"] = string.Join(" · ", result.Problems.Take(5));
        }
        catch (Exception ex)
        {
            TempData["problem"] = $"The run failed: {ex.Message}";
        }

        return RedirectToPage(new { day = day.ToString("yyyy-MM-dd") });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Message = TempData["message"] as string;
        Problem = TempData["problem"] as string;

        var app = settings.Current;
        MailProvider = app.Mail.Provider;
        DeliveryProvider = app.Delivery.Provider;
        MailWillSend = app.Mail.Provider.Equals("smtp", StringComparison.OrdinalIgnoreCase)
                       && !string.IsNullOrWhiteSpace(app.Mail.Host);

        await using var db = await factory.CreateDbContextAsync(ct);

        var visitDays = await db.Visits.Select(v => v.EventDate).Distinct().ToListAsync(ct);
        var eventDays = await db.EventDays.Select(d => d.Date).ToListAsync(ct);
        Days = visitDays.Union(eventDays).OrderByDescending(d => d).Take(30).ToList();

        // Default to the venue's current day when it is one of the listed days.
        // Days is sorted newest-first, and a multi-day show has its later days
        // in the list before they have happened — defaulting to Days[0] would
        // land the operator on the final day mid-show, with the Run button
        // armed for the wrong date.
        var today = TrackingRuntime.LocalDate(app.Exhibition);
        SelectedDay = Day ?? (Days.Contains(today) ? today : Days.Count > 0 ? Days[0] : today);
        DayClosed = await db.EventDays.AnyAsync(d => d.Date == SelectedDay && d.Closed, ct);

        var visitorIds = await db.Visits.Where(v => v.EventDate == SelectedDay).Select(v => v.VisitorId).Distinct().ToListAsync(ct);
        var scanIds = await db.CatalogueRequests.Where(r => r.EventDate == SelectedDay).Select(r => r.VisitorId).Distinct().ToListAsync(ct);
        var ids = visitorIds.Union(scanIds).ToList();
        ActiveVisitors = ids.Count;

        var reports = await db.DailyReports.AsNoTracking()
            .Where(r => r.EventDate == SelectedDay).ToDictionaryAsync(r => r.VisitorId, ct);
        var jobs = await db.DeliveryJobs.AsNoTracking()
            .Where(j => j.EventDate == SelectedDay).ToDictionaryAsync(j => j.VisitorId, ct);

        ReportsBuilt = reports.Count;
        PacksBuilt = jobs.Count(j => j.Value.Status == JobStatus.Succeeded);

        var emailIds = reports.Values.Where(r => r.OutboxEmailId is not null).Select(r => r.OutboxEmailId!.Value).ToList();
        var emails = await db.OutboxEmails.AsNoTracking()
            .Where(m => emailIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        EmailsQueued = emails.Count(e => e.Value.Status == JobStatus.Pending);
        EmailsSent = emails.Count(e => e.Value.Status == JobStatus.Succeeded);

        var visitors = await db.Visitors.AsNoTracking().Where(v => ids.Contains(v.Id)).ToListAsync(ct);

        Rows = visitors
            .Select(v =>
            {
                reports.TryGetValue(v.Id, out var report);
                jobs.TryGetValue(v.Id, out var job);
                OutboxEmail? email = null;
                if (report?.OutboxEmailId is { } emailId) emails.TryGetValue(emailId, out email);

                return new Row(
                    v.Id, report?.Id, v.FullName, v.Email, v.ConsentEmail,
                    report?.StandsVisited ?? 0, report?.StandsMissed ?? 0, report?.TotalDwellSeconds ?? 0,
                    report?.Status, job?.Status, job?.ItemCount ?? 0, job?.TransferUrl,
                    email?.Status, email?.Error);
            })
            .OrderByDescending(r => r.DwellSeconds)
            .ToList();
    }
}
