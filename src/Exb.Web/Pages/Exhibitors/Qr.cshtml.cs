using Exb.Data;
using Exb.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Exhibitors;

/// <summary>
/// Printable QR signage. One sheet per stand, or the whole hall at once for the
/// build crew.
/// </summary>
public class QrModel(IDbContextFactory<ExhibitionDbContext> factory, SettingsStore settings) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true)] public int? HallId { get; set; }

    public IReadOnlyList<Sheet> Sheets { get; private set; } = [];
    public string BaseUrl { get; private set; } = "";
    public bool BaseUrlLooksLocal { get; private set; }
    public IReadOnlyList<(int Id, string Name)> Halls { get; private set; } = [];

    public record Sheet(string StandNumber, string Company, string HallName, string QrToken, string? CategoryName);

    public async Task OnGetAsync(CancellationToken ct)
    {
        BaseUrl = settings.Current.Exhibition.PublicBaseUrl.TrimEnd('/');
        BaseUrlLooksLocal = BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                            || BaseUrl.Contains("127.0.0.1");

        await using var db = await factory.CreateDbContextAsync(ct);

        Halls = await db.Halls.Where(h => h.IsActive).OrderBy(h => h.DisplayOrder)
            .Select(h => new ValueTuple<int, string>(h.Id, h.Name)).ToListAsync(ct);

        var query = db.Kiosks.AsNoTracking().Where(k => k.IsActive && k.Exhibitor.IsActive);

        if (Id is not null) query = query.Where(k => k.ExhibitorId == Id);
        else if (HallId is not null) query = query.Where(k => k.HallId == HallId);

        Sheets = await query
            .OrderBy(k => k.Hall.DisplayOrder).ThenBy(k => k.StandNumber)
            .Take(500)
            .Select(k => new Sheet(
                k.StandNumber,
                k.Exhibitor.CompanyName,
                k.Hall.Name,
                k.QrToken,
                k.Exhibitor.Category != null ? k.Exhibitor.Category.Name : null))
            .ToListAsync(ct);
    }
}
