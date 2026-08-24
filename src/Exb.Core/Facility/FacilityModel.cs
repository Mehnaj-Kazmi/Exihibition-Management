using Exb.Core.Configuration;
using Exb.Core.Geometry;
using Exb.Core.Tracking;

namespace Exb.Core.Facility;

public enum AntennaKind
{
    /// <summary>Mounted on an exhibitor's stand. This is what interest is measured with.</summary>
    Kiosk = 0,

    /// <summary>Sparse ceiling grid over the aisles, for walking routes between stands.</summary>
    Aisle = 1,
}

/// <summary>A hall as the admin configured it in Settings.</summary>
public sealed record HallSpec(int Id, string Code, string Name, double WidthM, double DepthM, int DisplayOrder);

/// <summary>An exhibitor's stand footprint on the floor.</summary>
public sealed record KioskSpec(
    int Id,
    int HallId,
    string StandNumber,
    FloorRect Footprint,
    int ExhibitorId,
    string ExhibitorName,
    int? CategoryId,
    int? SubCategoryId);

public sealed record FacilityAntenna(
    string Code,
    string HallCode,
    int HallId,
    AntennaKind Kind,
    int? KioskId,
    double X,
    double Y,
    double HeightM,
    string ReaderCode,
    int Port);

public sealed record FacilityReader(
    string Code,
    string HallCode,
    int HallId,
    AntennaKind Kind,
    IReadOnlyList<string> AntennaCodes);

public sealed class FacilityHall
{
    public required HallSpec Spec { get; init; }
    public required IReadOnlyList<KioskSpec> Kiosks { get; init; }
    public required IReadOnlyList<FacilityAntenna> Antennas { get; init; }
    public required IReadOnlyList<FacilityReader> Readers { get; init; }
    public required double ZoneSizeM { get; init; }

    public int Id => Spec.Id;
    public string Code => Spec.Code;
    public string Name => Spec.Name;
    public double WidthM => Spec.WidthM;
    public double DepthM => Spec.DepthM;
    public double AreaM2 => Spec.WidthM * Spec.DepthM;
    public int ZoneCols => ZoneGrid.ColumnCount(WidthM, ZoneSizeM);
    public int ZoneRows => ZoneGrid.RowCount(DepthM, ZoneSizeM);

    public string ZoneLabel(double x, double y) => ZoneGrid.Label(x, y, WidthM, DepthM, ZoneSizeM);
}

/// <summary>
/// What the geometry actually delivers, measured rather than assumed.
///
/// Two different questions are reported separately because they have different
/// answers and different consequences. Stand coverage is the one that decides
/// whether interest data is trustworthy; whole-floor coverage only decides
/// whether the live map has holes in the aisles.
/// </summary>
public sealed record CoverageReport(
    double KioskReadRadiusM,
    double AisleReadRadiusM,
    int TotalAntennas,
    int KioskAntennas,
    int AisleAntennas,
    int TotalReaders,
    double TotalAreaM2,
    double StandFloorDetectablePct,
    double StandFloorLocalizablePct,
    double WholeFloorDetectablePct,
    double WholeFloorLocalizablePct,
    int KiosksWithNoAntenna,
    int KiosksWithSingleAntenna,
    string? Warning,
    string? Remedy);

/// <summary>
/// The whole physical installation, rebuilt whenever an admin changes halls,
/// stands or antenna rules. Everything downstream reads this snapshot, so a
/// settings change takes effect atomically rather than half-applied.
/// </summary>
public sealed class FacilityModel
{
    public required TrackingSettings Settings { get; init; }
    public required RfModel Rf { get; init; }
    public required IReadOnlyList<FacilityHall> Halls { get; init; }
    public required IReadOnlyList<FacilityAntenna> Antennas { get; init; }
    public required IReadOnlyList<FacilityReader> Readers { get; init; }
    public required CoverageReport Coverage { get; init; }
    public required IReadOnlyDictionary<string, FacilityAntenna> AntennaByCode { get; init; }
    public required IReadOnlyDictionary<string, FacilityHall> HallByCode { get; init; }
    public required IReadOnlyDictionary<int, FacilityHall> HallById { get; init; }
    public required IReadOnlyDictionary<int, KioskSpec> KioskById { get; init; }
    public DateTime BuiltUtc { get; init; } = DateTime.UtcNow;

    public FacilityHall? Hall(string code) => HallByCode.GetValueOrDefault(code);
    public FacilityAntenna? Antenna(string code) => AntennaByCode.GetValueOrDefault(code);

    public static FacilityModel Empty(TrackingSettings settings) => new()
    {
        Settings = settings,
        Rf = new RfModel(settings.Rf),
        Halls = [],
        Antennas = [],
        Readers = [],
        AntennaByCode = new Dictionary<string, FacilityAntenna>(),
        HallByCode = new Dictionary<string, FacilityHall>(),
        HallById = new Dictionary<int, FacilityHall>(),
        KioskById = new Dictionary<int, KioskSpec>(),
        Coverage = new CoverageReport(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            "No halls configured.", "Add a hall in Settings > Halls."),
    };
}
