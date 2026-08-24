using Exb.Core.Dwell;

namespace Exb.Core.Interest;

/// <summary>
/// Works out what a visitor was interested in, and what they walked past
/// without ever reaching.
///
/// The second half is the part exhibitors and visitors both actually want. A
/// visitor who spent twenty minutes across three packaging-machinery stands
/// almost certainly wanted the other nine in that category, and on a floor of
/// several hundred stands they had no way to know those existed. That list,
/// with stand numbers, is the whole value of the evening email.
/// </summary>
public sealed class InterestAnalyser
{
    /// <summary>How many missed stands to put in the report before it stops being useful.</summary>
    public int MaxMissedRows { get; init; } = 25;

    /// <summary>A sub-category match is worth more than a bare category match.</summary>
    private const double SubCategoryWeight = 1.0;
    private const double CategoryOnlyWeight = 0.55;

    public VisitorDayProfile Build(
        int visitorId,
        DateOnly eventDate,
        IReadOnlyList<VisitFact> visits,
        IReadOnlyDictionary<int, KioskFact> kiosksById,
        IReadOnlyDictionary<int, string> categoryNames,
        ISet<int> catalogueRequestedKioskIds,
        IReadOnlyDictionary<int, int>? peerInterestByKiosk = null)
    {
        var mine = visits.Where(v => v.VisitorId == visitorId).ToList();

        // --- what they saw ---------------------------------------------------
        var visited = mine
            .Where(v => v.Level >= DwellLevel.Browsed)
            .GroupBy(v => v.KioskId)
            .Select(g =>
            {
                var kiosk = kiosksById.GetValueOrDefault(g.Key);
                if (kiosk is null) return null;
                return new VisitedStand(
                    Kiosk: kiosk,
                    TotalDwellSeconds: g.Sum(v => v.DwellSeconds),
                    VisitCount: g.Count(),
                    Level: g.Max(v => v.Level),
                    FirstSeenUtc: g.Min(v => v.StartedUtc),
                    CatalogueRequested: catalogueRequestedKioskIds.Contains(g.Key));
            })
            .Where(v => v is not null)
            .Select(v => v!)
            .OrderByDescending(v => v.TotalDwellSeconds)
            .ToList();

        int totalDwell = visited.Sum(v => v.TotalDwellSeconds);

        // --- which categories that adds up to --------------------------------
        var categories = BuildCategoryProfile(visited, categoryNames, totalDwell);

        // --- what they missed in those same categories -----------------------
        var visitedKioskIds = mine.Select(v => v.KioskId).ToHashSet();
        var missed = FindMissed(categories, visitedKioskIds, kiosksById, peerInterestByKiosk);

        return new VisitorDayProfile(
            VisitorId: visitorId,
            EventDate: eventDate,
            Visited: visited,
            Categories: categories,
            Missed: missed,
            TotalDwellSeconds: totalDwell,
            StandsWithInterest: visited.Count(v => v.Level >= DwellLevel.Interested),
            PassedBy: mine.Count(v => v.Level == DwellLevel.PassBy));
    }

    private static List<CategoryInterest> BuildCategoryProfile(
        List<VisitedStand> visited,
        IReadOnlyDictionary<int, string> categoryNames,
        int totalDwell)
    {
        return visited
            .Where(v => v.Kiosk.CategoryId is not null)
            .GroupBy(v => v.Kiosk.CategoryId!.Value)
            .Select(g =>
            {
                int dwell = g.Sum(v => v.TotalDwellSeconds);
                var subs = g.Where(v => v.Kiosk.SubCategoryId is not null)
                    .GroupBy(v => v.Kiosk.SubCategoryId!.Value)
                    .Select(sg => new SubCategoryInterest(
                        sg.Key,
                        categoryNames.GetValueOrDefault(sg.Key, sg.First().Kiosk.SubCategoryName ?? "Other"),
                        sg.Sum(v => v.TotalDwellSeconds),
                        sg.Count()))
                    .OrderByDescending(s => s.TotalDwellSeconds)
                    .ToList();

                return new CategoryInterest(
                    CategoryId: g.Key,
                    CategoryName: categoryNames.GetValueOrDefault(g.Key, g.First().Kiosk.CategoryName ?? "Other"),
                    TotalDwellSeconds: dwell,
                    StandCount: g.Count(),
                    BestLevel: g.Max(v => v.Level),
                    SharePct: totalDwell == 0 ? 0 : Math.Round(100.0 * dwell / totalDwell, 1),
                    SubCategories: subs);
            })
            .OrderByDescending(c => c.TotalDwellSeconds)
            .ToList();
    }

    /// <summary>
    /// Rank the stands the visitor never reached, within the categories they
    /// actually spent time in.
    ///
    /// Score is the visitor's own dwell share in that category, so the ranking
    /// is dominated by what they cared most about, multiplied by how exactly the
    /// stand matches (same sub-category beats same category), with a light nudge
    /// from how many other visitors found that stand interesting. The peer term
    /// is deliberately weak: it is a tie-breaker between equally relevant
    /// stands, not a popularity chart, or every report would recommend the same
    /// ten big stands to everybody.
    /// </summary>
    private List<MissedStand> FindMissed(
        List<CategoryInterest> categories,
        HashSet<int> visitedKioskIds,
        IReadOnlyDictionary<int, KioskFact> kiosksById,
        IReadOnlyDictionary<int, int>? peerInterestByKiosk)
    {
        if (categories.Count == 0) return [];

        var interestByCategory = categories.ToDictionary(c => c.CategoryId, c => c);
        var subInterest = categories
            .SelectMany(c => c.SubCategories.Select(s => (c.CategoryId, Sub: s)))
            .ToDictionary(x => x.Sub.SubCategoryId, x => x.Sub);

        int maxPeer = peerInterestByKiosk is { Count: > 0 } ? Math.Max(1, peerInterestByKiosk.Values.Max()) : 1;

        var rows = new List<MissedStand>();

        foreach (var kiosk in kiosksById.Values)
        {
            if (visitedKioskIds.Contains(kiosk.KioskId)) continue;
            if (kiosk.CategoryId is null || !interestByCategory.TryGetValue(kiosk.CategoryId.Value, out var interest))
                continue;

            bool subMatch = kiosk.SubCategoryId is not null && subInterest.ContainsKey(kiosk.SubCategoryId.Value);
            double match = subMatch ? SubCategoryWeight : CategoryOnlyWeight;

            int peers = peerInterestByKiosk?.GetValueOrDefault(kiosk.KioskId) ?? 0;
            double peerBoost = 1.0 + 0.15 * (peers / (double)maxPeer);

            double score = interest.SharePct / 100.0 * match * peerBoost;

            string reason = subMatch && kiosk.SubCategoryName is not null
                ? $"You spent {InterestFormatting.Duration(interest.TotalDwellSeconds)} on {interest.CategoryName}, including {kiosk.SubCategoryName}"
                : $"You spent {InterestFormatting.Duration(interest.TotalDwellSeconds)} on {interest.CategoryName}";

            rows.Add(new MissedStand(kiosk, Math.Round(score, 4), reason, subMatch, peers));
        }

        return rows
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Kiosk.HallCode)
            .ThenBy(r => r.Kiosk.StandNumber, StringComparer.OrdinalIgnoreCase)
            .Take(MaxMissedRows)
            .ToList();
    }
}
