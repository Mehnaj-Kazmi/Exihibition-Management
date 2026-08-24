using Exb.Core.Configuration;
using Exb.Core.Facility;
using Exb.Core.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace Exb.Tests;

public class FacilityBuilderTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(9, 12, 1)]     // a 3x3 shell scheme
    [InlineData(18, 12, 2)]
    [InlineData(36, 12, 3)]
    [InlineData(72, 12, 6)]
    [InlineData(9, 6, 2)]      // a denser rule issues more
    public void AntennaCountFollowsStandArea(double areaM2, double areaPerAntenna, int expected)
    {
        var rule = new KioskAntennaSettings { AreaPerAntennaM2 = areaPerAntenna, MinPerKiosk = 1, MaxPerKiosk = 8 };
        Assert.Equal(expected, FacilityBuilder.AntennaCountFor(areaM2, rule));
    }

    [Fact]
    public void AntennaCountRespectsTheConfiguredBounds()
    {
        var rule = new KioskAntennaSettings { AreaPerAntennaM2 = 1, MinPerKiosk = 2, MaxPerKiosk = 4 };

        Assert.Equal(2, FacilityBuilder.AntennaCountFor(0.5, rule));   // tiny stand still gets the minimum
        Assert.Equal(4, FacilityBuilder.AntennaCountFor(500, rule));   // huge stand is capped
    }

    [Fact]
    public void AntennasAreSpreadOverTheStandNotClumpedInTheMiddle()
    {
        // A long, narrow row stand should get a line of antennas along its length.
        var footprint = new FloorRect(0, 0, 12, 3);
        var points = FacilityBuilder.LayOutOnKiosk(footprint, 4);

        Assert.Equal(4, points.Count);
        Assert.All(points, p => Assert.True(footprint.Contains(p.X, p.Y), $"({p.X},{p.Y}) is off the stand"));

        double spreadX = points.Max(p => p.X) - points.Min(p => p.X);
        double spreadY = points.Max(p => p.Y) - points.Min(p => p.Y);
        Assert.True(spreadX > spreadY, "a 12x3 m stand should spread its antennas along its length");
    }

    [Fact]
    public void EveryStandGetsAtLeastOneAntennaAndEveryAntennaGetsAReaderPort()
    {
        var model = TestFacility.Build();

        Assert.NotEmpty(model.Halls);
        Assert.Equal(0, model.Coverage.KiosksWithNoAntenna);

        foreach (var kiosk in model.Halls.SelectMany(h => h.Kiosks))
            Assert.True(model.Antennas.Any(a => a.KioskId == kiosk.Id), $"stand {kiosk.StandNumber} has no antenna");

        foreach (var antenna in model.Antennas)
        {
            Assert.False(string.IsNullOrEmpty(antenna.ReaderCode), $"{antenna.Code} is not wired to a reader");
            Assert.InRange(antenna.Port, 1, 32);
        }

        // Every antenna appears on exactly one reader, at one port.
        var wired = model.Readers.SelectMany(r => r.AntennaCodes).ToList();
        Assert.Equal(model.Antennas.Count, wired.Count);
        Assert.Equal(model.Antennas.Count, wired.Distinct().Count());
    }

    [Fact]
    public void StandAndAisleAntennasAreNeverOnTheSameReader()
    {
        var model = TestFacility.Build();
        var byCode = model.AntennaByCode;

        foreach (var reader in model.Readers)
        {
            var kinds = reader.AntennaCodes.Select(c => byCode[c].Kind).Distinct().ToList();
            Assert.True(kinds.Count == 1,
                $"reader {reader.Code} mixes stand and aisle antennas, so a stand build could take out aisle coverage");
        }
    }

    [Fact]
    public void CoverageOverStandFloorIsMeasuredAndGood()
    {
        var model = TestFacility.Build();
        var coverage = model.Coverage;

        output.WriteLine($"read radius   {coverage.KioskReadRadiusM} m from stand antennas, {coverage.AisleReadRadiusM} m from aisle");
        output.WriteLine($"antennas      {coverage.KioskAntennas} on stands + {coverage.AisleAntennas} in aisles on {coverage.TotalReaders} readers");
        output.WriteLine($"stand floor   {coverage.StandFloorDetectablePct}% heard, {coverage.StandFloorLocalizablePct}% with a full fix");
        output.WriteLine($"whole floor   {coverage.WholeFloorDetectablePct}% heard, {coverage.WholeFloorLocalizablePct}% with a full fix");

        // Stand floor is what interest data depends on, so it must be complete.
        // The full-fix threshold is set from the density sweep in CoverageSweep:
        // the shipped default measures 98.5% here, and a regression below 95%
        // means the provisioning rule or the link budget has drifted.
        Assert.Equal(100.0, coverage.StandFloorDetectablePct);
        Assert.True(coverage.StandFloorLocalizablePct >= 95,
            $"only {coverage.StandFloorLocalizablePct}% of stand floor supports a full position fix");
        Assert.True(coverage.KioskReadRadiusM > 2.0, "stand antennas should reach past the edge of their own stand");
    }

    [Fact]
    public void AisleGridSkipsPointsThatFallOnAStand()
    {
        var model = TestFacility.Build();
        var hall = model.Halls[0];

        foreach (var antenna in hall.Antennas.Where(a => a.Kind == AntennaKind.Aisle))
        {
            bool onStand = hall.Kiosks.Any(k => k.Footprint.Contains(antenna.X, antenna.Y));
            Assert.False(onStand, $"aisle antenna {antenna.Code} was placed above a built stand");
        }
    }

    [Fact]
    public void TurningOffTheAisleGridLeavesStandCoverageIntact()
    {
        var withAisles = TestFacility.Build();
        var settings = new TrackingSettings();
        settings.AisleGrid.Enabled = false;
        var without = TestFacility.Build(settings: settings);

        Assert.Equal(0, without.Coverage.AisleAntennas);
        Assert.Equal(withAisles.Coverage.KioskAntennas, without.Coverage.KioskAntennas);

        // The point of the separation: interest data does not depend on the aisle grid.
        Assert.Equal(100.0, without.Coverage.StandFloorDetectablePct);
        Assert.True(without.Coverage.WholeFloorDetectablePct < withAisles.Coverage.WholeFloorDetectablePct,
            "removing the aisle grid should visibly cost aisle coverage");
    }

    [Fact]
    public void ResizingAHallRebuildsTheAisleGridButKeepsStandAntennas()
    {
        var small = TestFacility.Build(widthM: 40, depthM: 30);
        var large = TestFacility.Build(widthM: 80, depthM: 60);

        Assert.True(large.Coverage.AisleAntennas > small.Coverage.AisleAntennas);
        Assert.True(large.Coverage.TotalAreaM2 > small.Coverage.TotalAreaM2);
    }

    [Fact]
    public void AnEmptyFloorPlanIsReportedRatherThanCrashing()
    {
        var model = FacilityBuilder.Build(new TrackingSettings(), [], []);

        Assert.Empty(model.Halls);
        Assert.NotNull(model.Coverage.Warning);
    }
}
