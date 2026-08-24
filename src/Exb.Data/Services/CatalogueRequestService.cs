using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Exb.Data.Services;

public sealed record ScanTarget(
    int KioskId,
    string StandNumber,
    int ExhibitorId,
    string ExhibitorName,
    string HallName,
    string? CategoryName,
    string? SubCategoryName,
    string? Summary,
    string? Website,
    int CatalogueFileCount);

public enum ScanOutcome
{
    /// <summary>Recorded; it will be in tonight's pack.</summary>
    Added,

    /// <summary>Already requested earlier today. Nothing changed.</summary>
    AlreadyRequested,

    /// <summary>The QR token does not match any active stand.</summary>
    UnknownStand,

    /// <summary>No visitor identified, so the scan cannot be attributed.</summary>
    NotIdentified,
}

public sealed record ScanResult(ScanOutcome Outcome, ScanTarget? Target, int TodayCount);

/// <summary>
/// Handles a visitor scanning a stand's QR code.
///
/// The code resolves to a page on this system rather than to the exhibitor's own
/// site, which is what makes the evening pack possible: the request has to be
/// recorded against a visitor before the catalogue is handed over. A scan by
/// someone we cannot identify still returns the stand's details, because sending
/// a visitor to a dead end helps nobody — it just cannot be added to a pack.
/// </summary>
public sealed class CatalogueRequestService(IDbContextFactory<ExhibitionDbContext> factory)
{
    /// <summary>
    /// Pull the stand token out of whatever a scanner read.
    ///
    /// Stand QR codes encode a full URL rather than a bare token, because they
    /// have to work when scanned by the phone's own camera app as well as by
    /// ours — a bare token would leave a visitor holding a meaningless string.
    /// The mobile app therefore sends back exactly what the camera saw, and this
    /// is the one place that knows how to unpack it, so the app does not have to
    /// be redeployed if the URL shape ever changes.
    ///
    /// A bare token passes through untouched, which is what the web scan page
    /// hands over.
    /// </summary>
    public static string NormaliseScannedValue(string? scanned)
    {
        string value = (scanned ?? "").Trim();
        if (value.Length == 0) return "";

        // Query and fragment first: a code carrying ?utm=... must not have that
        // become part of the token.
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            value = uri.AbsolutePath;

        int hash = value.IndexOf('#');
        if (hash >= 0) value = value[..hash];

        int question = value.IndexOf('?');
        if (question >= 0) value = value[..question];

        value = value.TrimEnd('/');

        int slash = value.LastIndexOf('/');
        if (slash >= 0) value = value[(slash + 1)..];

        return value.Trim();
    }

    public async Task<ScanTarget?> ResolveAsync(string qrToken, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Kiosks
            .AsNoTracking()
            .Where(k => k.QrToken == qrToken && k.IsActive && k.Exhibitor.IsActive)
            .Select(k => new ScanTarget(
                k.Id,
                k.StandNumber,
                k.ExhibitorId,
                k.Exhibitor.CompanyName,
                k.Hall.Name,
                k.Exhibitor.Category != null ? k.Exhibitor.Category.Name : null,
                k.Exhibitor.SubCategory != null ? k.Exhibitor.SubCategory.Name : null,
                k.Exhibitor.Summary,
                k.Exhibitor.Website,
                k.Exhibitor.Catalogues.Count(c => c.IsActive)))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ScanResult> RecordAsync(
        string qrToken, int? visitorId, DateOnly eventDate, string source = "qr", CancellationToken ct = default)
    {
        var target = await ResolveAsync(qrToken, ct);
        if (target is null) return new ScanResult(ScanOutcome.UnknownStand, null, 0);
        if (visitorId is null) return new ScanResult(ScanOutcome.NotIdentified, target, 0);

        await using var db = await factory.CreateDbContextAsync(ct);

        var existing = await db.CatalogueRequests.FirstOrDefaultAsync(
            r => r.VisitorId == visitorId && r.KioskId == target.KioskId && r.EventDate == eventDate, ct);

        var outcome = ScanOutcome.Added;

        if (existing is null)
        {
            db.CatalogueRequests.Add(new CatalogueRequest
            {
                VisitorId = visitorId.Value,
                KioskId = target.KioskId,
                ExhibitorId = target.ExhibitorId,
                EventDate = eventDate,
                Source = source,
                Included = true,
            });
        }
        else if (existing.Included)
        {
            outcome = ScanOutcome.AlreadyRequested;
        }
        else
        {
            // Scanning again after removing it from the pack means they want it back.
            existing.Included = true;
        }

        await db.SaveChangesAsync(ct);

        int count = await db.CatalogueRequests
            .CountAsync(r => r.VisitorId == visitorId && r.EventDate == eventDate && r.Included, ct);

        return new ScanResult(outcome, target, count);
    }

    /// <summary>Everything a visitor has asked for today, for their own page.</summary>
    public async Task<IReadOnlyList<ScanTarget>> TodayForVisitorAsync(
        int visitorId, DateOnly eventDate, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.CatalogueRequests
            .AsNoTracking()
            .Where(r => r.VisitorId == visitorId && r.EventDate == eventDate && r.Included)
            .OrderByDescending(r => r.RequestedUtc)
            .Select(r => new ScanTarget(
                r.KioskId,
                r.Kiosk.StandNumber,
                r.ExhibitorId,
                r.Kiosk.Exhibitor.CompanyName,
                r.Kiosk.Hall.Name,
                r.Kiosk.Exhibitor.Category != null ? r.Kiosk.Exhibitor.Category.Name : null,
                r.Kiosk.Exhibitor.SubCategory != null ? r.Kiosk.Exhibitor.SubCategory.Name : null,
                r.Kiosk.Exhibitor.Summary,
                r.Kiosk.Exhibitor.Website,
                r.Kiosk.Exhibitor.Catalogues.Count(c => c.IsActive)))
            .ToListAsync(ct);
    }

    public async Task<bool> SetIncludedAsync(
        int visitorId, int kioskId, DateOnly eventDate, bool included, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.CatalogueRequests.FirstOrDefaultAsync(
            r => r.VisitorId == visitorId && r.KioskId == kioskId && r.EventDate == eventDate, ct);
        if (row is null) return false;

        row.Included = included;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
