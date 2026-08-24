using Exb.Core.Dwell;
using Exb.Core.Interest;
using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Exb.Data.Services;

/// <summary>
/// Reads the day's tracking data back out in the shape the interest analyser
/// wants.
///
/// The analyser itself is deliberately free of any database dependency so it can
/// be tested against fabricated days; this class is the only place that knows
/// both worlds.
/// </summary>
public sealed class InterestQueryService(
    IDbContextFactory<ExhibitionDbContext> factory,
    FacilityProvider facility)
{
    public async Task<IReadOnlyDictionary<int, KioskFact>> KioskFactsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.Kiosks
            .AsNoTracking()
            .Where(k => k.IsActive && k.Exhibitor.IsActive)
            .Select(k => new
            {
                k.Id,
                k.StandNumber,
                k.HallId,
                HallCode = k.Hall.Code,
                HallName = k.Hall.Name,
                k.X,
                k.Y,
                k.WidthM,
                k.DepthM,
                k.QrToken,
                k.ExhibitorId,
                k.Exhibitor.CompanyName,
                k.Exhibitor.CategoryId,
                CategoryName = k.Exhibitor.Category != null ? k.Exhibitor.Category.Name : null,
                k.Exhibitor.SubCategoryId,
                SubCategoryName = k.Exhibitor.SubCategory != null ? k.Exhibitor.SubCategory.Name : null,
                k.Exhibitor.Website,
                k.Exhibitor.Summary,
                k.Exhibitor.Country,
            })
            .ToListAsync(ct);

        var model = facility.Current;
        var facts = new Dictionary<int, KioskFact>(rows.Count);

        foreach (var r in rows)
        {
            var hall = model.HallById.GetValueOrDefault(r.HallId);
            string zone = hall is not null
                ? hall.ZoneLabel(r.X + r.WidthM / 2, r.Y + r.DepthM / 2)
                : "-";

            facts[r.Id] = new KioskFact(
                r.Id, r.StandNumber, r.HallId, r.HallCode, r.HallName, zone,
                r.ExhibitorId, r.CompanyName,
                r.CategoryId, r.CategoryName, r.SubCategoryId, r.SubCategoryName,
                r.Website, r.Summary, r.Country, r.QrToken);
        }

        return facts;
    }

    public async Task<IReadOnlyDictionary<int, string>> CategoryNamesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
    }

    public async Task<IReadOnlyList<VisitFact>> VisitFactsAsync(
        DateOnly day, int? visitorId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var query = db.Visits.AsNoTracking().Where(v => v.EventDate == day && !v.IsOpen);
        if (visitorId is not null) query = query.Where(v => v.VisitorId == visitorId);

        var rows = await query
            .Select(v => new
            {
                v.VisitorId, v.KioskId, v.ExhibitorId, v.HallId,
                v.CategoryId, v.SubCategoryId, v.DwellSeconds, v.Level, v.StartedUtc,
            })
            .ToListAsync(ct);

        return rows
            .Select(v => new VisitFact(
                v.VisitorId, v.KioskId, v.ExhibitorId, v.HallId,
                v.CategoryId, v.SubCategoryId, v.DwellSeconds, (DwellLevel)v.Level, v.StartedUtc))
            .ToList();
    }

    public async Task<ISet<int>> CatalogueRequestedKiosksAsync(
        int visitorId, DateOnly day, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ids = await db.CatalogueRequests
            .AsNoTracking()
            .Where(r => r.VisitorId == visitorId && r.EventDate == day && r.Included)
            .Select(r => r.KioskId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    /// <summary>
    /// How many other visitors found each stand interesting today. Used only as
    /// a tie-breaker when ranking missed stands, so that between two equally
    /// relevant stands the busier one is suggested first.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, int>> PeerInterestAsync(DateOnly day, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Visits
            .AsNoTracking()
            .Where(v => v.EventDate == day && v.Level >= InterestLevel.Interested)
            .GroupBy(v => v.KioskId)
            .Select(g => new { KioskId = g.Key, Visitors = g.Select(v => v.VisitorId).Distinct().Count() })
            .ToDictionaryAsync(x => x.KioskId, x => x.Visitors, ct);
    }

    /// <summary>Everything one visitor did on one day, ready to render.</summary>
    public async Task<VisitorDayProfile> ProfileAsync(int visitorId, DateOnly day, CancellationToken ct = default)
    {
        var kiosks = await KioskFactsAsync(ct);
        var categories = await CategoryNamesAsync(ct);
        var visits = await VisitFactsAsync(day, visitorId, ct);
        var requested = await CatalogueRequestedKiosksAsync(visitorId, day, ct);
        var peers = await PeerInterestAsync(day, ct);

        return new InterestAnalyser().Build(visitorId, day, visits, kiosks, categories, requested, peers);
    }

    /// <summary>Visitors with anything to report for a day: a stand visit or a scan.</summary>
    public async Task<IReadOnlyList<int>> ActiveVisitorIdsAsync(DateOnly day, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var fromVisits = await db.Visits
            .Where(v => v.EventDate == day)
            .Select(v => v.VisitorId)
            .Distinct()
            .ToListAsync(ct);

        var fromScans = await db.CatalogueRequests
            .Where(r => r.EventDate == day)
            .Select(r => r.VisitorId)
            .Distinct()
            .ToListAsync(ct);

        return fromVisits.Union(fromScans).ToList();
    }
}
