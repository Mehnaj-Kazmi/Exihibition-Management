using System.Text.Json;
using Exb.Core.Facility;
using Exb.Data.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Exb.Web.Pages;

/// <summary>
/// The live floor plan. The stand layout is serialised into the page once and
/// the badge positions arrive over SignalR, because the layout is static for the
/// session and is far bigger than a frame of badge coordinates.
/// </summary>
public class LiveModel(FacilityProvider facility) : PageModel
{
    public string LayoutJson { get; private set; } = "[]";
    public bool HasHalls { get; private set; }

    public void OnGet()
    {
        var model = facility.Current;
        HasHalls = model.Halls.Count > 0;

        var halls = model.Halls.Select(hall => new
        {
            id = hall.Id,
            code = hall.Code,
            name = hall.Name,
            widthM = hall.WidthM,
            depthM = hall.DepthM,
            stands = hall.Kiosks.Select(k => new
            {
                id = k.Id,
                stand = k.StandNumber,
                name = k.ExhibitorName,
                x = k.Footprint.X,
                y = k.Footprint.Y,
                w = k.Footprint.Width,
                d = k.Footprint.Depth,
                antennas = hall.Antennas.Count(a => a.KioskId == k.Id),
            }),
            antennas = hall.Antennas.Select(a => new
            {
                x = a.X,
                y = a.Y,
                kind = (int)a.Kind,
            }),
        });

        LayoutJson = JsonSerializer.Serialize(halls);
    }
}
