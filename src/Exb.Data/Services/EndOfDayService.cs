using System.Text.Json;
using Exb.Core.Configuration;
using Exb.Core.Delivery;
using Exb.Core.Interest;
using Exb.Core.Packaging;
using Exb.Core.Reports;
using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Exb.Data.Services;

public sealed record EndOfDayResult(
    DateOnly Day,
    int VisitorsConsidered,
    int PacksBuilt,
    int ReportsBuilt,
    int EmailsQueued,
    int Skipped,
    IReadOnlyList<string> Problems)
{
    public bool Succeeded => Problems.Count == 0;
}

/// <summary>
/// The evening run: close the day, build each visitor's e-catalogue pack, upload
/// it, write their interest report, and queue the email that carries both.
///
/// It is written to be safely repeatable. An organiser will run it, spot that
/// three exhibitors had the wrong category, fix them and run it again, so
/// everything here upserts by (visitor, day) and rebuilds from source rather
/// than appending. The only thing that is not repeated is sending: an email
/// already handed to the transport is never re-queued, because the one outcome
/// that cannot be undone is a visitor receiving the same report twice.
/// </summary>
public sealed class EndOfDayService(
    IDbContextFactory<ExhibitionDbContext> factory,
    SettingsStore settings,
    InterestQueryService interest,
    VisitRepository visits,
    CatalogueStorage storage,
    ITransferProviderSelector transfers,
    ILogger<EndOfDayService> logger)
{
    public async Task<EndOfDayResult> RunAsync(
        DateOnly day,
        bool resend = false,
        int? onlyVisitorId = null,
        CancellationToken ct = default)
    {
        var app = settings.Current;
        var problems = new List<string>();
        int packs = 0, reports = 0, emails = 0, skipped = 0;

        // A visit still open at close of play has no end time, and so no dwell.
        await visits.CloseAllOpenAsync(app.Dwell, ct);

        var visitorIds = onlyVisitorId is not null
            ? [onlyVisitorId.Value]
            : await interest.ActiveVisitorIdsAsync(day, ct);

        var kiosks = await interest.KioskFactsAsync(ct);
        var categories = await interest.CategoryNamesAsync(ct);
        var allVisits = await interest.VisitFactsAsync(day, null, ct);
        var peers = await interest.PeerInterestAsync(day, ct);
        var analyser = new InterestAnalyser();
        var reportBuilder = new DailyReportBuilder();
        var packBuilder = new CataloguePackBuilder();

        foreach (int visitorId in visitorIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                var visitor = await db.Visitors.FirstOrDefaultAsync(v => v.Id == visitorId, ct);
                if (visitor is null) { skipped++; continue; }

                var requested = await interest.CatalogueRequestedKiosksAsync(visitorId, day, ct);
                var profile = analyser.Build(visitorId, day, allVisits, kiosks, categories, requested, peers);

                // --- the e-catalogue pack ------------------------------------
                PackLink? packLink = null;
                if (requested.Count > 0)
                {
                    var (job, link) = await BuildAndUploadPackAsync(db, visitor, day, kiosks, app, packBuilder, ct);
                    packLink = link;
                    if (job.Status == JobStatus.Succeeded) packs++;
                    else problems.Add($"{visitor.FullName}: pack failed - {job.Error}");
                }

                // --- the interest report --------------------------------------
                var built = reportBuilder.Build(
                    new ReportRecipient(visitor.Id, visitor.FullName, visitor.Email, visitor.Company),
                    profile, app.Exhibition, app.Dwell, packLink);

                var report = await db.DailyReports
                    .FirstOrDefaultAsync(r => r.VisitorId == visitorId && r.EventDate == day, ct);

                if (report is null)
                {
                    report = new DailyReport { VisitorId = visitorId, EventDate = day };
                    db.DailyReports.Add(report);
                }

                report.Html = built.Html;
                report.InterestJson = JsonSerializer.Serialize(profile.Categories);
                report.MissedJson = JsonSerializer.Serialize(profile.Missed.Select(m => new
                {
                    m.Kiosk.StandNumber,
                    m.Kiosk.ExhibitorName,
                    m.Kiosk.HallName,
                    m.Kiosk.Zone,
                    m.Kiosk.CategoryName,
                    m.Kiosk.SubCategoryName,
                    m.Kiosk.Website,
                    m.Reason,
                    m.Score,
                }));
                report.StandsVisited = profile.Visited.Count;
                report.StandsMissed = profile.Missed.Count;
                report.TotalDwellSeconds = profile.TotalDwellSeconds;
                report.GeneratedUtc = DateTime.UtcNow;
                reports++;

                // --- the covering email ---------------------------------------
                bool alreadySent = report.OutboxEmailId is not null;
                if (!visitor.ConsentEmail || string.IsNullOrWhiteSpace(visitor.Email))
                {
                    report.Status = JobStatus.Skipped;
                    skipped++;
                }
                else if (alreadySent && !resend)
                {
                    report.Status = JobStatus.Succeeded;
                }
                else
                {
                    var mail = new OutboxEmail
                    {
                        ToAddress = visitor.Email,
                        ToName = visitor.FullName,
                        Subject = built.Subject,
                        HtmlBody = built.Html,
                        TextBody = built.TextBody,
                        Kind = "daily-report",
                    };
                    db.OutboxEmails.Add(mail);
                    await db.SaveChangesAsync(ct);

                    report.OutboxEmailId = mail.Id;
                    report.Status = JobStatus.Pending;
                    emails++;
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "End-of-day processing failed for visitor {VisitorId}", visitorId);
                problems.Add($"visitor {visitorId}: {ex.Message}");
            }
        }

        await MarkDayClosedAsync(day, ct);

        logger.LogInformation(
            "End of day {Day}: {Visitors} visitor(s), {Packs} pack(s), {Reports} report(s), {Emails} email(s) queued, {Skipped} skipped.",
            day, visitorIds.Count, packs, reports, emails, skipped);

        return new EndOfDayResult(day, visitorIds.Count, packs, reports, emails, skipped, problems);
    }

    private async Task<(DeliveryJob Job, PackLink? Link)> BuildAndUploadPackAsync(
        ExhibitionDbContext db,
        Visitor visitor,
        DateOnly day,
        IReadOnlyDictionary<int, KioskFact> kiosks,
        AppSettings app,
        CataloguePackBuilder packBuilder,
        CancellationToken ct)
    {
        var job = await db.DeliveryJobs.FirstOrDefaultAsync(j => j.VisitorId == visitor.Id && j.EventDate == day, ct);
        if (job is null)
        {
            job = new DeliveryJob { VisitorId = visitor.Id, EventDate = day, DownloadToken = Tokens.New(24) };
            db.DeliveryJobs.Add(job);
        }
        if (string.IsNullOrEmpty(job.DownloadToken)) job.DownloadToken = Tokens.New(24);

        job.Status = JobStatus.Running;
        job.Attempts++;
        job.Error = null;
        await db.SaveChangesAsync(ct);

        try
        {
            var requests = await db.CatalogueRequests
                .AsNoTracking()
                .Where(r => r.VisitorId == visitor.Id && r.EventDate == day && r.Included)
                .Select(r => new { r.KioskId, r.ExhibitorId, r.RequestedUtc })
                .ToListAsync(ct);

            var assets = await db.CatalogueAssets
                .AsNoTracking()
                .Where(a => a.IsActive)
                .ToListAsync(ct);

            var exhibitorContacts = await db.Exhibitors
                .AsNoTracking()
                .Select(e => new { e.Id, e.Email })
                .ToDictionaryAsync(e => e.Id, e => e.Email, ct);

            var items = new List<PackItem>();
            foreach (var request in requests)
            {
                if (!kiosks.TryGetValue(request.KioskId, out var kiosk)) continue;

                var files = assets
                    .Where(a => a.ExhibitorId == request.ExhibitorId)
                    .Select(a => new { a.FileName, a.ContentType, Path = storage.ResolveStored(a.StoragePath) })
                    .Where(a => a.Path is not null)
                    .Select(a => new PackFile(a.FileName, a.ContentType, a.Path!))
                    .ToList();

                items.Add(new PackItem(
                    kiosk.ExhibitorId, kiosk.ExhibitorName, kiosk.StandNumber, kiosk.HallName,
                    kiosk.CategoryName, kiosk.SubCategoryName, kiosk.Website,
                    exhibitorContacts.GetValueOrDefault(kiosk.ExhibitorId),
                    kiosk.Summary, request.RequestedUtc, files));
            }

            string zipPath = storage.PackPathFor(day, visitor.Id, job.DownloadToken);
            var result = packBuilder.Build(zipPath, visitor.FullName, app.Exhibition.Name, day, items);

            long limit = (long)app.Delivery.MaxPackMegabytes * 1024 * 1024;
            if (result.SizeBytes > limit)
                throw new InvalidOperationException(
                    $"pack is {result.SizeBytes / 1024 / 1024} MB, over the {app.Delivery.MaxPackMegabytes} MB limit");

            var provider = transfers.Resolve(app);
            var transfer = await provider.UploadAsync(new TransferRequest(
                FilePath: zipPath,
                DisplayName: $"{app.Exhibition.Name} e-catalogues - {day:yyyy-MM-dd}",
                DownloadToken: job.DownloadToken,
                RecipientName: visitor.FullName,
                Message: app.Delivery.WeTransfer.Message), ct);

            job.ZipPath = storage.ToRelative(zipPath);
            job.ZipSizeBytes = result.SizeBytes;
            job.ItemCount = result.ItemCount;
            job.TransferProvider = transfer.Provider;
            job.TransferUrl = transfer.Url;
            job.TransferExpiresUtc = transfer.ExpiresUtc;
            job.Status = JobStatus.Succeeded;
            job.CompletedUtc = DateTime.UtcNow;

            foreach (string warning in result.Warnings)
                logger.LogInformation("Pack for {Visitor}: {Warning}", visitor.FullName, warning);

            await db.SaveChangesAsync(ct);
            return (job, new PackLink(transfer.Url, transfer.ExpiresUtc, result.ItemCount, result.SizeBytes));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
            job.CompletedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "Pack build failed for visitor {VisitorId}", visitor.Id);
            return (job, null);
        }
    }

    private async Task MarkDayClosedAsync(DateOnly day, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.EventDays.FirstOrDefaultAsync(d => d.Date == day, ct);

        if (row is null)
        {
            row = new EventDay { Date = day };
            db.EventDays.Add(row);
        }

        row.Closed = true;
        row.ClosedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Chooses which transfer provider to use for the current settings.</summary>
public interface ITransferProviderSelector
{
    ITransferProvider Resolve(AppSettings settings);
}
