using Exb.Core.Dwell;
using Exb.Core.Interest;
using Xunit;

namespace Exb.Tests;

/// <summary>
/// The missed-stand engine is the part visitors and exhibitors actually pay for,
/// so its ranking is pinned down rather than left to look plausible.
/// </summary>
public class InterestAnalyserTests
{
    private static readonly DateOnly Day = new(2026, 8, 17);

    private const int Textiles = 1, Packaging = 2, Robotics = 3;
    private const int Weaving = 11, Dyeing = 12, Filling = 21;

    private static readonly Dictionary<int, string> CategoryNames = new()
    {
        [Textiles] = "Textile Machinery",
        [Packaging] = "Packaging",
        [Robotics] = "Robotics",
        [Weaving] = "Weaving",
        [Dyeing] = "Dyeing & Finishing",
        [Filling] = "Filling machines",
    };

    private static KioskFact Stand(int id, int? category, int? sub, string name = "") => new(
        KioskId: id,
        StandNumber: $"H1-{id:D3}",
        HallId: 1,
        HallCode: "H1",
        HallName: "Hall 1",
        Zone: "C4",
        ExhibitorId: id,
        ExhibitorName: string.IsNullOrEmpty(name) ? $"Exhibitor {id}" : name,
        CategoryId: category,
        CategoryName: category is null ? null : CategoryNames[category.Value],
        SubCategoryId: sub,
        SubCategoryName: sub is null ? null : CategoryNames[sub.Value],
        Website: "example.com",
        Summary: "Something useful",
        Country: "Pakistan",
        QrToken: $"TOKEN{id}");

    private static VisitFact Visit(int kioskId, int? category, int? sub, int seconds, DwellLevel level) =>
        new(VisitorId: 1, KioskId: kioskId, ExhibitorId: kioskId, HallId: 1,
            CategoryId: category, SubCategoryId: sub, DwellSeconds: seconds, Level: level,
            StartedUtc: new DateTime(2026, 8, 17, 11, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void RollsDwellTimeUpIntoCategoriesWithShares()
    {
        var kiosks = new Dictionary<int, KioskFact>
        {
            [1] = Stand(1, Textiles, Weaving),
            [2] = Stand(2, Textiles, Dyeing),
            [3] = Stand(3, Packaging, Filling),
        };

        var visits = new List<VisitFact>
        {
            Visit(1, Textiles, Weaving, 300, DwellLevel.Strong),
            Visit(2, Textiles, Dyeing, 100, DwellLevel.Interested),
            Visit(3, Packaging, Filling, 100, DwellLevel.Browsed),
        };

        var profile = new InterestAnalyser().Build(1, Day, visits, kiosks, CategoryNames, new HashSet<int>());

        Assert.Equal(500, profile.TotalDwellSeconds);
        Assert.Equal(3, profile.Visited.Count);
        Assert.Equal(2, profile.StandsWithInterest);   // Browsed does not count as interest

        var top = profile.Categories[0];
        Assert.Equal("Textile Machinery", top.CategoryName);
        Assert.Equal(400, top.TotalDwellSeconds);
        Assert.Equal(80.0, top.SharePct);
        Assert.Equal(2, top.SubCategories.Count);
    }

    [Fact]
    public void PassingByAStandIsNotTreatedAsHavingVisitedIt()
    {
        var kiosks = new Dictionary<int, KioskFact> { [1] = Stand(1, Textiles, Weaving) };
        var visits = new List<VisitFact> { Visit(1, Textiles, Weaving, 8, DwellLevel.PassBy) };

        var profile = new InterestAnalyser().Build(1, Day, visits, kiosks, CategoryNames, new HashSet<int>());

        Assert.Empty(profile.Visited);
        Assert.Empty(profile.Categories);
        Assert.Equal(1, profile.PassedBy);
        Assert.False(profile.HasInterest);
    }

    [Fact]
    public void SuggestsOnlyUnvisitedStandsInCategoriesTheVisitorActuallySpentTimeOn()
    {
        var kiosks = new Dictionary<int, KioskFact>
        {
            [1] = Stand(1, Textiles, Weaving),      // visited
            [2] = Stand(2, Textiles, Weaving),      // missed, exact sub-category match
            [3] = Stand(3, Textiles, Dyeing),       // missed, category match only
            [4] = Stand(4, Robotics, null),         // missed, but an unrelated category
            [5] = Stand(5, null, null),             // missed, unclassified
        };

        var visits = new List<VisitFact> { Visit(1, Textiles, Weaving, 600, DwellLevel.Strong) };

        var profile = new InterestAnalyser().Build(1, Day, visits, kiosks, CategoryNames, new HashSet<int>());
        var missedIds = profile.Missed.Select(m => m.Kiosk.KioskId).ToList();

        Assert.DoesNotContain(1, missedIds);   // they were there
        Assert.DoesNotContain(4, missedIds);   // not a category they cared about
        Assert.DoesNotContain(5, missedIds);   // unclassified
        Assert.Contains(2, missedIds);
        Assert.Contains(3, missedIds);

        // The exact sub-category match must rank above the category-only match.
        Assert.Equal(2, missedIds[0]);
        Assert.True(profile.Missed[0].SubCategoryMatch);
        Assert.False(profile.Missed[1].SubCategoryMatch);
    }

    [Fact]
    public void TheCategorySomeoneSpentMostTimeOnDominatesTheSuggestions()
    {
        var kiosks = new Dictionary<int, KioskFact>
        {
            [1] = Stand(1, Textiles, Weaving),
            [2] = Stand(2, Packaging, Filling),
            [10] = Stand(10, Textiles, Weaving, "Missed textile stand"),
            [20] = Stand(20, Packaging, Filling, "Missed packaging stand"),
        };

        // Nine tenths of the day went to textiles.
        var visits = new List<VisitFact>
        {
            Visit(1, Textiles, Weaving, 900, DwellLevel.Strong),
            Visit(2, Packaging, Filling, 100, DwellLevel.Browsed),
        };

        var profile = new InterestAnalyser().Build(1, Day, visits, kiosks, CategoryNames, new HashSet<int>());

        Assert.Equal(10, profile.Missed[0].Kiosk.KioskId);
        Assert.True(profile.Missed[0].Score > profile.Missed[1].Score);
        Assert.Contains("Textile Machinery", profile.Missed[0].Reason);
    }

    [Fact]
    public void PopularityOnlyBreaksTiesAndDoesNotOverrideRelevance()
    {
        var kiosks = new Dictionary<int, KioskFact>
        {
            [1] = Stand(1, Textiles, Weaving),
            [10] = Stand(10, Textiles, Weaving, "Quiet but exactly right"),
            [11] = Stand(11, Textiles, Dyeing, "Very popular, less exact"),
        };

        var visits = new List<VisitFact> { Visit(1, Textiles, Weaving, 600, DwellLevel.Strong) };

        // Stand 11 is wildly more popular, but matches less precisely.
        var peers = new Dictionary<int, int> { [10] = 1, [11] = 500 };

        var profile = new InterestAnalyser().Build(1, Day, visits, kiosks, CategoryNames, new HashSet<int>(), peers);

        Assert.Equal(10, profile.Missed[0].Kiosk.KioskId);
    }

    [Fact]
    public void MarksTheStandsWhoseCatalogueWasAlreadyRequested()
    {
        var kiosks = new Dictionary<int, KioskFact>
        {
            [1] = Stand(1, Textiles, Weaving),
            [2] = Stand(2, Textiles, Dyeing),
        };

        var visits = new List<VisitFact>
        {
            Visit(1, Textiles, Weaving, 300, DwellLevel.Strong),
            Visit(2, Textiles, Dyeing, 300, DwellLevel.Strong),
        };

        var profile = new InterestAnalyser().Build(1, Day, visits, kiosks, CategoryNames, new HashSet<int> { 1 });

        Assert.True(profile.Visited.Single(v => v.Kiosk.KioskId == 1).CatalogueRequested);
        Assert.False(profile.Visited.Single(v => v.Kiosk.KioskId == 2).CatalogueRequested);
    }

    [Fact]
    public void SeveralStopsAtOneStandAreAddedTogether()
    {
        var kiosks = new Dictionary<int, KioskFact> { [1] = Stand(1, Textiles, Weaving) };
        var visits = new List<VisitFact>
        {
            Visit(1, Textiles, Weaving, 100, DwellLevel.Interested),
            Visit(1, Textiles, Weaving, 250, DwellLevel.Strong),
        };

        var profile = new InterestAnalyser().Build(1, Day, visits, kiosks, CategoryNames, new HashSet<int>());

        var visited = Assert.Single(profile.Visited);
        Assert.Equal(350, visited.TotalDwellSeconds);
        Assert.Equal(2, visited.VisitCount);
        Assert.Equal(DwellLevel.Strong, visited.Level);
    }

    [Fact]
    public void OtherVisitorsDaysAreNeverMixedIn()
    {
        var kiosks = new Dictionary<int, KioskFact> { [1] = Stand(1, Textiles, Weaving) };
        var visits = new List<VisitFact>
        {
            Visit(1, Textiles, Weaving, 300, DwellLevel.Strong),
            new(VisitorId: 2, KioskId: 1, ExhibitorId: 1, HallId: 1, CategoryId: Textiles,
                SubCategoryId: Weaving, DwellSeconds: 9999, Level: DwellLevel.Strong, StartedUtc: DateTime.UtcNow),
        };

        var profile = new InterestAnalyser().Build(1, Day, visits, kiosks, CategoryNames, new HashSet<int>());
        Assert.Equal(300, profile.TotalDwellSeconds);
    }

    [Fact]
    public void AVisitorWithNoInterestGetsNoSuggestions()
    {
        var kiosks = new Dictionary<int, KioskFact> { [1] = Stand(1, Textiles, Weaving) };

        var profile = new InterestAnalyser().Build(1, Day, [], kiosks, CategoryNames, new HashSet<int>());

        Assert.Empty(profile.Missed);
        Assert.False(profile.HasInterest);
    }

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(60, "1 min")]
    [InlineData(260, "4 min 20 s")]
    [InlineData(3600, "1 h")]
    [InlineData(5400, "1 h 30 min")]
    public void DurationsReadTheWayAPersonWouldSayThem(int seconds, string expected)
        => Assert.Equal(expected, InterestFormatting.Duration(seconds));
}
