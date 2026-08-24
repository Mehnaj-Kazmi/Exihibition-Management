namespace Exb.Core.Configuration;

/// <summary>
/// Every knob that governs how tags are heard and turned into positions.
/// Held in SQL Server rather than appsettings.json, because the settings screen
/// has to be able to change halls, kiosk antenna density and dwell thresholds
/// while the exhibition is running.
/// </summary>
public sealed class TrackingSettings
{
    public RfSettings Rf { get; set; } = new();
    public LocatorSettings Locator { get; set; } = new();
    public KioskAntennaSettings KioskAntennas { get; set; } = new();
    public AisleGridSettings AisleGrid { get; set; } = new();

    /// <summary>Side length of the wayfinding zone squares (A1, D7...) in metres.</summary>
    public double ZoneSizeM { get; set; } = 5.0;

    public TrackingSettings Clone() => new()
    {
        Rf = Rf.Clone(),
        Locator = Locator.Clone(),
        KioskAntennas = KioskAntennas.Clone(),
        AisleGrid = AisleGrid.Clone(),
        ZoneSizeM = ZoneSizeM,
    };
}

/// <summary>
/// UHF link budget. These are the numbers that must be calibrated on site;
/// everything downstream inherits them, so this is what turns good antenna
/// geometry into good position accuracy.
/// </summary>
public sealed class RfSettings
{
    /// <summary>Reference RSSI (dBm) at one metre from the antenna, on boresight.</summary>
    public double RefRssiAt1M { get; set; } = -40.0;

    /// <summary>Path-loss exponent. 2.0 is free space; raise it for crowded halls.</summary>
    public double PathLossExponent { get; set; } = 2.2;

    /// <summary>Reader sensitivity floor (dBm). Sets the effective read radius.</summary>
    public double SensitivityDbm { get; set; } = -75.0;

    /// <summary>Antenna beam roll-off exponent in the cos^k sense, one way.</summary>
    public double BeamExponent { get; set; } = 3.6;

    /// <summary>Height of a badge tag above the floor, in metres (lanyard height).</summary>
    public double TagHeightM { get; set; } = 1.2;

    public RfSettings Clone() => (RfSettings)MemberwiseClone();
}

public sealed class LocatorSettings
{
    /// <summary>Sliding window over which reads from different antennas are pooled.</summary>
    public int WindowMs { get; set; } = 2500;

    /// <summary>How often positions are re-solved.</summary>
    public int IntervalMs { get; set; } = 500;

    /// <summary>Silence after which a badge is shown as stale rather than live.</summary>
    public int StaleMs { get; set; } = 12_000;

    /// <summary>Silence after which a badge is presumed to have left the building.</summary>
    public int GoneMs { get; set; } = 300_000;

    /// <summary>0 = never move, 1 = no smoothing at all.</summary>
    public double SmoothingAlpha { get; set; } = 0.35;

    public int MaxIterations { get; set; } = 6;

    /// <summary>Measured RSSI noise (dB). Drives the uncertainty circles.</summary>
    public double RssiNoiseDb { get; set; } = 2.5;

    public LocatorSettings Clone() => (LocatorSettings)MemberwiseClone();
}

/// <summary>
/// Rule that turns a stand's floor area into a set of antennas mounted on that
/// stand. This is the core difference from a warehouse install: sensing is
/// attached to the exhibitors, not spread evenly over the ceiling.
/// </summary>
public sealed class KioskAntennaSettings
{
    /// <summary>
    /// One antenna per this many square metres of stand.
    ///
    /// Ten is not a round number chosen for looks: measured against a realistic
    /// floor of mixed 3 m to 12 m stands, 12 m² leaves 13% of stand floor without
    /// the three antennas a full position fix needs, and 10 m² closes that to
    /// 1.5% for about a quarter more hardware. Below 8 m² the return flattens out
    /// and you are buying antennas for nothing.
    /// </summary>
    public double AreaPerAntennaM2 { get; set; } = 10.0;

    public int MinPerKiosk { get; set; } = 1;
    public int MaxPerKiosk { get; set; } = 8;

    /// <summary>
    /// Mounting height on the stand, in metres.
    ///
    /// 3.2 m is what a shell-scheme stand will take. If the venue is rigging a
    /// gantry anyway, 4.0 m is worth having: it widens the read radius from
    /// 3.47 m to 4.37 m, which reaches the same coverage with roughly 20% fewer
    /// antennas. Raise this only if the stands can really carry it — an antenna
    /// specified at a height it cannot be mounted at is worse than a denser grid.
    /// </summary>
    public double HeightM { get; set; } = 3.2;

    /// <summary>Antenna ports on each reader.</summary>
    public int PortsPerReader { get; set; } = 8;

    /// <summary>Reader port dwell time; a port revisits a tag every dwell x ports.</summary>
    public int DwellMs { get; set; } = 125;

    public KioskAntennaSettings Clone() => (KioskAntennaSettings)MemberwiseClone();
}

/// <summary>
/// Optional sparse ceiling grid over the aisles. Kiosk antennas alone answer
/// "which stand is this visitor at", which is what interest is built from; the
/// aisle grid is what lets you also see the walking routes between stands.
/// </summary>
public sealed class AisleGridSettings
{
    public bool Enabled { get; set; } = true;
    public double PitchM { get; set; } = 9.0;
    public double HeightM { get; set; } = 6.0;
    public int PortsPerReader { get; set; } = 8;

    public AisleGridSettings Clone() => (AisleGridSettings)MemberwiseClone();
}
