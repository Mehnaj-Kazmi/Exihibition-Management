using Exb.Core.Dwell;

namespace Exb.Core.Interest;

/// <summary>A closed stand visit, flattened for analysis.</summary>
public sealed record VisitFact(
    int VisitorId,
    int KioskId,
    int ExhibitorId,
    int HallId,
    int? CategoryId,
    int? SubCategoryId,
    int DwellSeconds,
    DwellLevel Level,
    DateTime StartedUtc);

/// <summary>Everything the report needs to describe a stand, in one row.</summary>
public sealed record KioskFact(
    int KioskId,
    string StandNumber,
    int HallId,
    string HallCode,
    string HallName,
    string Zone,
    int ExhibitorId,
    string ExhibitorName,
    int? CategoryId,
    string? CategoryName,
    int? SubCategoryId,
    string? SubCategoryName,
    string? Website,
    string? Summary,
    string? Country,
    string QrToken)
{
    public string Location => $"{HallName} · Stand {StandNumber} · Zone {Zone}";
}

/// <summary>How much of a visitor's day went to one category.</summary>
public sealed record CategoryInterest(
    int CategoryId,
    string CategoryName,
    int TotalDwellSeconds,
    int StandCount,
    DwellLevel BestLevel,
    double SharePct,
    IReadOnlyList<SubCategoryInterest> SubCategories)
{
    public string DwellText => InterestFormatting.Duration(TotalDwellSeconds);
}

public sealed record SubCategoryInterest(
    int SubCategoryId,
    string SubCategoryName,
    int TotalDwellSeconds,
    int StandCount);

/// <summary>One row of "you were here" in the daily report.</summary>
public sealed record VisitedStand(
    KioskFact Kiosk,
    int TotalDwellSeconds,
    int VisitCount,
    DwellLevel Level,
    DateTime FirstSeenUtc,
    bool CatalogueRequested)
{
    public string DwellText => InterestFormatting.Duration(TotalDwellSeconds);
}

/// <summary>One row of "you missed this" in the daily report.</summary>
public sealed record MissedStand(
    KioskFact Kiosk,
    double Score,
    string Reason,
    bool SubCategoryMatch,
    int PeerInterestCount);

/// <summary>The complete picture of one visitor's day.</summary>
public sealed record VisitorDayProfile(
    int VisitorId,
    DateOnly EventDate,
    IReadOnlyList<VisitedStand> Visited,
    IReadOnlyList<CategoryInterest> Categories,
    IReadOnlyList<MissedStand> Missed,
    int TotalDwellSeconds,
    int StandsWithInterest,
    int PassedBy)
{
    public bool HasInterest => StandsWithInterest > 0;
    public string TotalDwellText => InterestFormatting.Duration(TotalDwellSeconds);
}

public static class InterestFormatting
{
    /// <summary>Durations as a person would say them: "4 min 20 s", not "260".</summary>
    public static string Duration(int seconds)
    {
        if (seconds < 60) return $"{seconds} s";
        int m = seconds / 60, s = seconds % 60;
        if (m < 60) return s == 0 ? $"{m} min" : $"{m} min {s} s";
        int h = m / 60;
        m %= 60;
        return m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    public static string LevelText(DwellLevel level) => level switch
    {
        DwellLevel.Strong => "Strong interest",
        DwellLevel.Interested => "Interested",
        DwellLevel.Browsed => "Browsed",
        _ => "Passed by",
    };
}
