using Exb.Core.Configuration;
using Exb.Core.Facility;
using Exb.Core.Geometry;

namespace Exb.Tests;

/// <summary>
/// A realistic exhibition floor for the tests: rows of stands of mixed sizes
/// separated by aisles, which is what an actual floor plan looks like and what
/// makes the attribution tests meaningful. A hall of identical stands would
/// flatter the margin logic.
/// </summary>
internal static class TestFacility
{
    public static FacilityModel Build(
        int halls = 1,
        double widthM = 60,
        double depthM = 40,
        TrackingSettings? settings = null)
    {
        settings ??= new TrackingSettings();

        var hallSpecs = Enumerable.Range(1, halls)
            .Select(i => new HallSpec(i, $"H{i}", $"Hall {i}", widthM, depthM, i))
            .ToList();

        var kiosks = new List<KioskSpec>();
        int id = 1;

        foreach (var hall in hallSpecs)
        {
            const double margin = 3.0, aisle = 3.5, rowDepth = 6.0;
            double[] widths = [3, 6, 6, 9, 12];
            int widthIndex = 0;
            int standNumber = 1;

            for (double y = margin; y + rowDepth <= hall.DepthM - margin; y += rowDepth + aisle)
            {
                double x = margin;
                while (x < hall.WidthM - margin - 2.5)
                {
                    double w = widths[widthIndex++ % widths.Length];
                    if (x + w > hall.WidthM - margin) w = hall.WidthM - margin - x;
                    if (w < 2.5) break;

                    // Categories cycle so that neighbouring stands differ, which is
                    // what the missed-stand tests need.
                    int category = (id % 4) + 1;

                    kiosks.Add(new KioskSpec(
                        id,
                        hall.Id,
                        $"{hall.Code}-{standNumber:D3}",
                        new FloorRect(x, y, w, rowDepth),
                        ExhibitorId: id,
                        ExhibitorName: $"Exhibitor {id}",
                        CategoryId: category,
                        SubCategoryId: 100 + category * 10 + (id % 3)));

                    id++;
                    standNumber++;
                    x += w;
                }
            }
        }

        return FacilityBuilder.Build(settings, hallSpecs, kiosks);
    }
}
