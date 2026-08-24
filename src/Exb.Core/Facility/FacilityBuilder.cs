using Exb.Core.Configuration;
using Exb.Core.Geometry;
using Exb.Core.Tracking;

namespace Exb.Core.Facility;

/// <summary>
/// Turns the admin's configuration — halls, stands, antenna rules — into the
/// physical model the tracking stack runs on, and then measures what that model
/// actually delivers instead of assuming it works.
///
/// The provisioning rule is the heart of it: an exhibitor's stand gets antennas
/// in proportion to its floor area, mounted on the stand itself. A 9 m² shell
/// scheme gets one; a 72 m² island gets six, spread over the footprint. That is
/// how the hardware is really quoted and installed, and it means sensing density
/// automatically follows the stands rather than the building.
/// </summary>
public static class FacilityBuilder
{
    /// <summary>Floor sampling pitch for the coverage measurement, in metres.</summary>
    private const double SamplePitchM = 1.0;

    public static FacilityModel Build(
        TrackingSettings settings,
        IReadOnlyList<HallSpec> halls,
        IReadOnlyList<KioskSpec> kiosks)
    {
        settings = settings.Clone();
        var rf = new RfModel(settings.Rf);

        if (halls.Count == 0) return FacilityModel.Empty(settings);

        var builtHalls = new List<FacilityHall>();
        var allAntennas = new List<FacilityAntenna>();
        var allReaders = new List<FacilityReader>();

        foreach (var hall in halls.OrderBy(h => h.DisplayOrder).ThenBy(h => h.Code))
        {
            var hallKiosks = kiosks.Where(k => k.HallId == hall.Id)
                                   .OrderBy(k => k.StandNumber, StringComparer.OrdinalIgnoreCase)
                                   .ToList();

            var antennas = new List<FacilityAntenna>();
            antennas.AddRange(ProvisionKioskAntennas(hall, hallKiosks, settings.KioskAntennas));
            if (settings.AisleGrid.Enabled)
                antennas.AddRange(ProvisionAisleAntennas(hall, hallKiosks, settings.AisleGrid));

            var readers = WireReaders(hall, antennas, settings);

            // WireReaders assigns the definitive reader/port for each antenna.
            var wired = antennas
                .Select(a => a with
                {
                    ReaderCode = readers.Assignment[a.Code].ReaderCode,
                    Port = readers.Assignment[a.Code].Port,
                })
                .ToList();

            builtHalls.Add(new FacilityHall
            {
                Spec = hall,
                Kiosks = hallKiosks,
                Antennas = wired,
                Readers = readers.Readers,
                ZoneSizeM = settings.ZoneSizeM,
            });

            allAntennas.AddRange(wired);
            allReaders.AddRange(readers.Readers);
        }

        var coverage = MeasureCoverage(builtHalls, settings, rf);

        return new FacilityModel
        {
            Settings = settings,
            Rf = rf,
            Halls = builtHalls,
            Antennas = allAntennas,
            Readers = allReaders,
            Coverage = coverage,
            AntennaByCode = allAntennas.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase),
            HallByCode = builtHalls.ToDictionary(h => h.Code, StringComparer.OrdinalIgnoreCase),
            HallById = builtHalls.ToDictionary(h => h.Id),
            KioskById = builtHalls.SelectMany(h => h.Kiosks).ToDictionary(k => k.Id),
        };
    }

    // --- provisioning --------------------------------------------------------

    /// <summary>How many antennas a stand of this size gets. Public so the stand
    /// editor can show the admin the count before they save.</summary>
    public static int AntennaCountFor(double areaM2, KioskAntennaSettings s)
    {
        if (s.AreaPerAntennaM2 <= 0) return s.MinPerKiosk;
        int n = (int)Math.Ceiling(areaM2 / s.AreaPerAntennaM2);
        return Math.Clamp(n, Math.Max(1, s.MinPerKiosk), Math.Max(1, s.MaxPerKiosk));
    }

    /// <summary>
    /// Spread <paramref name="count"/> antennas over a stand footprint. The grid
    /// is shaped to the stand's aspect ratio so a long narrow row stand gets a
    /// line of antennas along its length rather than a clump in the middle.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> LayOutOnKiosk(FloorRect footprint, int count)
    {
        if (count <= 1) return [(footprint.CentreX, footprint.CentreY)];

        double aspect = footprint.Width / Math.Max(0.01, footprint.Depth);
        int cols = Math.Clamp((int)Math.Round(Math.Sqrt(count * aspect)), 1, count);
        int rows = (int)Math.Ceiling(count / (double)cols);
        // Re-tighten: a 5-antenna stand should be 3x2, not 3x2 with a wasted row.
        cols = (int)Math.Ceiling(count / (double)rows);

        var points = new List<(double, double)>(count);
        for (int r = 0; r < rows && points.Count < count; r++)
        {
            for (int c = 0; c < cols && points.Count < count; c++)
            {
                points.Add((
                    footprint.X + (c + 0.5) * footprint.Width / cols,
                    footprint.Y + (r + 0.5) * footprint.Depth / rows));
            }
        }
        return points;
    }

    private static IEnumerable<FacilityAntenna> ProvisionKioskAntennas(
        HallSpec hall, IReadOnlyList<KioskSpec> kiosks, KioskAntennaSettings s)
    {
        foreach (var k in kiosks)
        {
            int n = AntennaCountFor(k.Footprint.Area, s);
            var points = LayOutOnKiosk(k.Footprint, n);
            for (int i = 0; i < points.Count; i++)
            {
                yield return new FacilityAntenna(
                    Code: $"{hall.Code}-{k.StandNumber}-A{i + 1}",
                    HallCode: hall.Code,
                    HallId: hall.Id,
                    Kind: AntennaKind.Kiosk,
                    KioskId: k.Id,
                    X: Math.Round(points[i].X, 3),
                    Y: Math.Round(points[i].Y, 3),
                    HeightM: s.HeightM,
                    ReaderCode: "",   // assigned by WireReaders
                    Port: 0);
            }
        }
    }

    /// <summary>
    /// Sparse ceiling grid over the aisles. Grid points that land on a stand are
    /// skipped: that floor is already covered by the stand's own antennas, and a
    /// ceiling antenna above a built stand with rigging and a roof would be
    /// shadowed anyway, so counting it would overstate coverage.
    /// </summary>
    private static IEnumerable<FacilityAntenna> ProvisionAisleAntennas(
        HallSpec hall, IReadOnlyList<KioskSpec> kiosks, AisleGridSettings s)
    {
        if (s.PitchM <= 0) yield break;

        int cols = Math.Max(1, (int)Math.Round(hall.WidthM / s.PitchM));
        int rows = Math.Max(1, (int)Math.Round(hall.DepthM / s.PitchM));
        double stepX = hall.WidthM / cols;
        double stepY = hall.DepthM / rows;

        int seq = 0;
        for (int r = 0; r <= rows; r++)
        {
            for (int c = 0; c <= cols; c++)
            {
                double x = c * stepX;
                double y = r * stepY;
                if (kiosks.Any(k => k.Footprint.Contains(x, y))) continue;

                seq++;
                yield return new FacilityAntenna(
                    Code: $"{hall.Code}-AISLE-A{seq:D3}",
                    HallCode: hall.Code,
                    HallId: hall.Id,
                    Kind: AntennaKind.Aisle,
                    KioskId: null,
                    X: Math.Round(x, 3),
                    Y: Math.Round(y, 3),
                    HeightM: s.HeightM,
                    ReaderCode: "",
                    Port: 0);
            }
        }
    }

    /// <summary>
    /// Wire antennas into multi-port readers. Stand antennas and aisle antennas
    /// go on separate readers because they are physically separate cable runs —
    /// one in the stand's own rack, one in the ceiling — and a shared reader
    /// would mean an exhibitor's stand build could take out aisle coverage.
    /// </summary>
    private static (List<FacilityReader> Readers, Dictionary<string, (string ReaderCode, int Port)> Assignment)
        WireReaders(HallSpec hall, List<FacilityAntenna> antennas, TrackingSettings settings)
    {
        var readers = new List<FacilityReader>();
        var assignment = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);

        foreach (var kind in new[] { AntennaKind.Kiosk, AntennaKind.Aisle })
        {
            var group = antennas.Where(a => a.Kind == kind).ToList();
            if (group.Count == 0) continue;

            int ports = Math.Max(1, kind == AntennaKind.Kiosk
                ? settings.KioskAntennas.PortsPerReader
                : settings.AisleGrid.PortsPerReader);
            string prefix = kind == AntennaKind.Kiosk ? "KR" : "AR";

            for (int i = 0; i < group.Count; i += ports)
            {
                string readerCode = $"{hall.Code}-{prefix}{i / ports + 1:D2}";
                var codes = new List<string>();
                for (int j = 0; j < ports && i + j < group.Count; j++)
                {
                    var a = group[i + j];
                    assignment[a.Code] = (readerCode, j + 1);
                    codes.Add(a.Code);
                }
                readers.Add(new FacilityReader(readerCode, hall.Code, hall.Id, kind, codes));
            }
        }

        return (readers, assignment);
    }

    // --- coverage measurement ------------------------------------------------

    /// <summary>
    /// Sample the floor and count how many antennas can hear a badge at each
    /// point. One antenna means the badge is detected; three means the geometry
    /// supports a real multilateration fix. Measured separately over stand
    /// footprints and over the whole hall, because only the first one governs
    /// whether interest data can be trusted.
    /// </summary>
    private static CoverageReport MeasureCoverage(
        IReadOnlyList<FacilityHall> halls, TrackingSettings settings, RfModel rf)
    {
        double kioskRadius = rf.MaxLateralRange(settings.KioskAntennas.HeightM);
        double aisleRadius = settings.AisleGrid.Enabled
            ? rf.MaxLateralRange(settings.AisleGrid.HeightM)
            : 0.0;

        long standTotal = 0, standDetect = 0, standLocal = 0;
        long floorTotal = 0, floorDetect = 0, floorLocal = 0;
        int noAntenna = 0, singleAntenna = 0;

        foreach (var hall in halls)
        {
            // Pre-square the radii so the inner loop avoids a sqrt.
            var pts = hall.Antennas
                .Select(a => (a.X, a.Y, R2: Sq(a.Kind == AntennaKind.Kiosk ? kioskRadius : aisleRadius)))
                .ToArray();

            foreach (var k in hall.Kiosks)
            {
                int n = hall.Antennas.Count(a => a.KioskId == k.Id);
                if (n == 0) noAntenna++;
                else if (n == 1) singleAntenna++;
            }

            for (double x = 0; x <= hall.WidthM; x += SamplePitchM)
            {
                for (double y = 0; y <= hall.DepthM; y += SamplePitchM)
                {
                    int heard = 0;
                    foreach (var p in pts)
                    {
                        double dx = p.X - x, dy = p.Y - y;
                        if (dx * dx + dy * dy <= p.R2 && ++heard >= 3) break;
                    }

                    floorTotal++;
                    if (heard >= 1) floorDetect++;
                    if (heard >= 3) floorLocal++;

                    if (hall.Kiosks.Any(kk => kk.Footprint.Contains(x, y)))
                    {
                        standTotal++;
                        if (heard >= 1) standDetect++;
                        if (heard >= 3) standLocal++;
                    }
                }
            }
        }

        double standDetectPct = Pct(standDetect, standTotal);
        double standLocalPct = Pct(standLocal, standTotal);
        double floorDetectPct = Pct(floorDetect, floorTotal);
        double floorLocalPct = Pct(floorLocal, floorTotal);

        string? warning = null;
        string? remedy = null;

        if (noAntenna > 0)
        {
            warning = $"{noAntenna} stand(s) have no antenna at all and cannot register any visitor interest.";
            remedy = "Check those stands have a size set in Exhibitors > Stand.";
        }
        else if (standLocalPct < 90)
        {
            warning = $"Only {standLocalPct:F1}% of stand floor has the three antennas a full position fix needs.";
            remedy = "Lower Settings > Tracking > area per antenna, so each stand is issued more antennas.";
        }
        else if (settings.AisleGrid.Enabled && floorDetectPct < 95)
        {
            warning = $"{100 - floorDetectPct:F1}% of the aisles are unheard, so walking routes between stands will break up.";
            remedy = "Reduce Settings > Tracking > aisle grid pitch for a denser ceiling grid.";
        }

        return new CoverageReport(
            KioskReadRadiusM: Math.Round(kioskRadius, 2),
            AisleReadRadiusM: Math.Round(aisleRadius, 2),
            TotalAntennas: halls.Sum(h => h.Antennas.Count),
            KioskAntennas: halls.Sum(h => h.Antennas.Count(a => a.Kind == AntennaKind.Kiosk)),
            AisleAntennas: halls.Sum(h => h.Antennas.Count(a => a.Kind == AntennaKind.Aisle)),
            TotalReaders: halls.Sum(h => h.Readers.Count),
            TotalAreaM2: Math.Round(halls.Sum(h => h.AreaM2), 1),
            StandFloorDetectablePct: standDetectPct,
            StandFloorLocalizablePct: standLocalPct,
            WholeFloorDetectablePct: floorDetectPct,
            WholeFloorLocalizablePct: floorLocalPct,
            KiosksWithNoAntenna: noAntenna,
            KiosksWithSingleAntenna: singleAntenna,
            Warning: warning,
            Remedy: remedy);
    }

    private static double Sq(double v) => v * v;

    private static double Pct(long part, long total)
        => total == 0 ? 0 : Math.Round(100.0 * part / total, 1);
}
