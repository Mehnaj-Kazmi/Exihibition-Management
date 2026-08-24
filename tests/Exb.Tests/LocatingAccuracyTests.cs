using Exb.Core.Configuration;
using Exb.Core.Dwell;
using Exb.Core.Facility;
using Exb.Core.Tracking;
using Xunit;
using Xunit.Abstractions;

namespace Exb.Tests;

/// <summary>
/// End-to-end locating accuracy, measured the honest way: true positions are
/// chosen privately, reads are synthesised from the link model with noise and
/// dropouts, and the engine solves from those reads alone. The two are only
/// compared afterwards.
///
/// This is the test that would catch the locating maths quietly breaking when
/// the antenna geometry moved from a ceiling grid to stand-mounted antennas.
/// </summary>
public class LocatingAccuracyTests(ITestOutputHelper output)
{
    private const int Population = 400;

    [Fact]
    public void SolvesPositionsAccuratelyFromStandAntennasAlone()
    {
        var model = TestFacility.Build();
        var settings = model.Settings;
        var engine = new LocatingEngine(model);
        var random = new Random(20260817);
        var now = DateTime.UtcNow;

        var truth = PlaceBadges(model, random);
        EmitReads(model, engine, truth, random, now, dropoutProbability: 0.06, noiseDb: 2.5);

        var solved = engine.SolveTick(now);
        Assert.NotEmpty(solved);

        var errors = new List<double>();
        int correctHall = 0, insideUncertainty = 0;

        foreach (var tag in solved)
        {
            var actual = truth[tag.Epc];
            if (tag.HallId == actual.HallId) correctHall++;

            double error = Math.Sqrt(Math.Pow(tag.X - actual.X, 2) + Math.Pow(tag.Y - actual.Y, 2));
            errors.Add(error);
            if (error <= tag.UncertaintyM) insideUncertainty++;
        }

        errors.Sort();
        double mean = errors.Average();
        double p95 = errors[(int)(errors.Count * 0.95)];
        double insidePct = 100.0 * insideUncertainty / errors.Count;

        output.WriteLine($"badges solved        {solved.Count} of {truth.Count}");
        output.WriteLine($"mean error           {mean:0.00} m");
        output.WriteLine($"95th percentile      {p95:0.00} m");
        output.WriteLine($"correct hall         {100.0 * correctHall / solved.Count:0.0}%");
        output.WriteLine($"inside its own circle {insidePct:0.0}%");

        Assert.True(solved.Count >= truth.Count * 0.97, $"only {solved.Count} of {truth.Count} badges were located at all");
        Assert.Equal(solved.Count, correctHall);
        Assert.True(mean < 1.2, $"mean position error {mean:0.00} m is too high");
        Assert.True(p95 < 3.0, $"95th percentile error {p95:0.00} m is too high");

        // The uncertainty circle is meant to be calibrated, not decorative: it
        // should contain the true position most of the time. Too low means the
        // system is overconfident; far too high means it is uselessly vague.
        Assert.InRange(insidePct, 55, 99);
    }

    [Fact]
    public void AttributesBadgesToTheStandTheyAreStandingAt()
    {
        var model = TestFacility.Build();
        var dwellSettings = new DwellSettings();
        var engine = new LocatingEngine(model);
        var random = new Random(4242);
        var now = DateTime.UtcNow;

        var truth = PlaceBadges(model, random);
        EmitReads(model, engine, truth, random, now, dropoutProbability: 0.06, noiseDb: 2.5);

        var solved = engine.SolveTick(now);

        int correct = 0, attributed = 0;
        int confidentTotal = 0, confidentCorrect = 0;
        int ambiguousTotal = 0, ambiguousCorrect = 0;

        foreach (var tag in solved)
        {
            var actual = truth[tag.Epc];
            var hall = model.HallById[tag.HallId];
            var attribution = KioskAttributor.Attribute(hall, tag.X, tag.Y, dwellSettings.AttachRadiusM);

            if (attribution is null) continue;
            attributed++;

            bool right = attribution.Value.KioskId == actual.KioskId;
            if (right) correct++;

            // The margin is what the dwell engine uses to decide whether it
            // genuinely knows which stand this was.
            if (attribution.Value.MarginM >= dwellSettings.MinMarginM)
            {
                confidentTotal++;
                if (right) confidentCorrect++;
            }
            else
            {
                ambiguousTotal++;
                if (right) ambiguousCorrect++;
            }
        }

        double attributedPct = 100.0 * attributed / solved.Count;
        double accuracy = 100.0 * correct / Math.Max(1, attributed);
        double confidentAccuracy = 100.0 * confidentCorrect / Math.Max(1, confidentTotal);
        double ambiguousAccuracy = 100.0 * ambiguousCorrect / Math.Max(1, ambiguousTotal);

        output.WriteLine($"attributed to a stand   {attributedPct:0.0}% of badges");
        output.WriteLine($"attributed correctly    {accuracy:0.0}%  ({correct} of {attributed})");
        output.WriteLine($"where the margin was good ({confidentTotal,3} badges): {confidentAccuracy:0.0}% correct");
        output.WriteLine($"where it was ambiguous    ({ambiguousTotal,3} badges): {ambiguousAccuracy:0.0}% correct");

        Assert.True(attributedPct > 90, $"only {attributedPct:0.0}% of badges were attributed to any stand");
        Assert.True(accuracy >= 90, $"stand attribution was only {accuracy:0.0}% accurate");

        // The whole point of the margin: it must actually predict when the
        // attribution is unreliable, or downgrading on a small margin is
        // superstition rather than engineering.
        if (ambiguousTotal >= 10)
            Assert.True(confidentAccuracy > ambiguousAccuracy,
                $"the margin does not separate reliable attributions ({confidentAccuracy:0.0}%) "
                + $"from unreliable ones ({ambiguousAccuracy:0.0}%)");
    }

    [Fact]
    public void HarsherRadioConditionsDegradeGracefullyRatherThanSilently()
    {
        // Double the noise and quadruple the dropouts: a crowded hall on the
        // busiest afternoon. Accuracy should fall, and the reported uncertainty
        // should widen to match rather than staying confidently wrong.
        var model = TestFacility.Build();
        var random = new Random(99);
        var now = DateTime.UtcNow;

        var clean = Measure(model, random, noiseDb: 2.5, dropout: 0.06, now);
        var harsh = Measure(model, new Random(99), noiseDb: 5.0, dropout: 0.25, now);

        output.WriteLine($"clean  mean {clean.Mean:0.00} m, reported uncertainty {clean.Uncertainty:0.00} m");
        output.WriteLine($"harsh  mean {harsh.Mean:0.00} m, reported uncertainty {harsh.Uncertainty:0.00} m");

        Assert.True(harsh.Mean > clean.Mean, "harsher conditions should measurably reduce accuracy");
        Assert.True(harsh.Uncertainty > clean.Uncertainty,
            "the reported uncertainty must widen when conditions worsen, or it is not telling the truth");
    }

    private (double Mean, double Uncertainty) Measure(
        FacilityModel model, Random random, double noiseDb, double dropout, DateTime now)
    {
        var engine = new LocatingEngine(model);
        var truth = PlaceBadges(model, random);
        EmitReads(model, engine, truth, random, now, dropout, noiseDb);

        var solved = engine.SolveTick(now);
        var errors = solved.Select(t =>
        {
            var actual = truth[t.Epc];
            return Math.Sqrt(Math.Pow(t.X - actual.X, 2) + Math.Pow(t.Y - actual.Y, 2));
        }).ToList();

        return (errors.Average(), solved.Average(t => t.UncertaintyM));
    }

    // --- the private truth ---------------------------------------------------

    private readonly record struct TrueBadge(int HallId, int KioskId, double X, double Y);

    /// <summary>
    /// Put badges where visitors really stand: on the stand itself, or in the
    /// aisle immediately in front of it.
    ///
    /// Note what is deliberately not done here. Stands in a row share edges, so
    /// scattering badges into a uniformly expanded footprint would place a good
    /// share of them physically inside the neighbouring stand — and then score
    /// the system wrong for correctly saying so. Standing in the aisle in front
    /// of a stand is both realistic and unambiguous, which is what makes the
    /// attribution figure mean something.
    /// </summary>
    private static Dictionary<string, TrueBadge> PlaceBadges(FacilityModel model, Random random)
    {
        var truth = new Dictionary<string, TrueBadge>(StringComparer.OrdinalIgnoreCase);
        var kiosks = model.Halls.SelectMany(h => h.Kiosks).ToList();

        for (int i = 0; i < Population; i++)
        {
            var kiosk = kiosks[random.Next(kiosks.Count)];
            var footprint = kiosk.Footprint;
            double x, y;

            if (random.NextDouble() < 0.6)
            {
                // On the stand, just inside its edge.
                x = footprint.X + 0.3 + random.NextDouble() * Math.Max(0.1, footprint.Width - 0.6);
                y = footprint.Y + 0.3 + random.NextDouble() * Math.Max(0.1, footprint.Depth - 0.6);
            }
            else
            {
                // In the aisle at the stand's open edge, within its own width.
                x = footprint.X + 0.3 + random.NextDouble() * Math.Max(0.1, footprint.Width - 0.6);
                double standOff = 0.3 + random.NextDouble() * 1.1;
                y = random.NextDouble() < 0.5 ? footprint.Y - standOff : footprint.Top + standOff;
            }

            var hall = model.HallById[kiosk.HallId];
            truth[$"BADGE{i:D5}"] = new TrueBadge(
                kiosk.HallId,
                kiosk.Id,
                Math.Clamp(x, 0, hall.WidthM),
                Math.Clamp(y, 0, hall.DepthM));
        }

        return truth;
    }

    /// <summary>
    /// One full reader port cycle: each antenna in range reports each badge it
    /// can hear, once, with Gaussian noise and random dropouts.
    /// </summary>
    private static void EmitReads(
        FacilityModel model,
        LocatingEngine engine,
        Dictionary<string, TrueBadge> truth,
        Random random,
        DateTime now,
        double dropoutProbability,
        double noiseDb)
    {
        double sensitivity = model.Settings.Rf.SensitivityDbm;

        foreach (var antenna in model.Antennas)
        {
            double maxLateral = model.Rf.MaxLateralRange(antenna.HeightM);

            foreach (var (epc, badge) in truth)
            {
                if (badge.HallId != antenna.HallId) continue;

                double dx = badge.X - antenna.X, dy = badge.Y - antenna.Y;
                double lateral = Math.Sqrt(dx * dx + dy * dy);
                if (lateral > maxLateral) continue;
                if (random.NextDouble() < dropoutProbability) continue;

                double rssi = model.Rf.ExpectedRssi(lateral, antenna.HeightM) + Gaussian(random, noiseDb);
                if (rssi < sensitivity) continue;

                engine.Ingest(new TagRead(antenna.ReaderCode, antenna.Code, epc, rssi, now));
            }
        }
    }

    private static double Gaussian(Random random, double sigma)
    {
        if (sigma <= 0) return 0;
        double u1 = 1.0 - random.NextDouble();
        double u2 = random.NextDouble();
        return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
