using Exb.Core.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Exb.Tests;

/// <summary>
/// Not an assertion so much as a measurement: what each antenna density
/// actually buys, so the shipped default is chosen from numbers rather than
/// from a guess. Run it with `dotnet test --filter CoverageSweep -v n`.
/// </summary>
public class CoverageSweep(ITestOutputHelper output)
{
    [Fact]
    public void MeasureDensityAgainstCoverage()
    {
        output.WriteLine("m2/antenna  height  radius   antennas  stand-heard  stand-fix  floor-heard");
        output.WriteLine(new string('-', 76));

        foreach (double height in new[] { 3.2, 4.0 })
        {
            foreach (double area in new[] { 16.0, 12.0, 10.0, 9.0, 8.0, 6.0, 4.5 })
            {
                var settings = new TrackingSettings();
                settings.KioskAntennas.AreaPerAntennaM2 = area;
                settings.KioskAntennas.HeightM = height;
                settings.KioskAntennas.MaxPerKiosk = 16;

                var model = TestFacility.Build(settings: settings);
                var c = model.Coverage;

                output.WriteLine(
                    $"{area,10:0.0}  {height,6:0.0}  {c.KioskReadRadiusM,6:0.00}  {c.KioskAntennas,8}  "
                    + $"{c.StandFloorDetectablePct,10:0.0}%  {c.StandFloorLocalizablePct,8:0.0}%  {c.WholeFloorDetectablePct,10:0.0}%");
            }
            output.WriteLine("");
        }
    }
}
