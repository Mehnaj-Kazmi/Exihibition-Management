using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Exhibitors;

public class IndexModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    FacilityProvider facility,
    SettingsStore settings) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? HallId { get; set; }
    [BindProperty(SupportsGet = true)] public int? CategoryId { get; set; }
    [BindProperty(SupportsGet = true)] public bool Unplaced { get; set; }

    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public List<SelectListItem> Halls { get; private set; } = [];
    public List<SelectListItem> Categories { get; private set; } = [];
    public int TotalExhibitors { get; private set; }
    public int PlacedStands { get; private set; }

    public record Row(
        int ExhibitorId, string Code, string Company, string? CategoryName, string? SubCategoryName,
        int? KioskId, string? StandNumber, string? HallName, double AreaM2, int Antennas,
        int CatalogueFiles, int InterestedToday, string? QrToken);

    public async Task OnGetAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = TrackingRuntime.LocalDate(settings.Current.Exhibition);

        Halls = await db.Halls.Where(h => h.IsActive).OrderBy(h => h.DisplayOrder)
            .Select(h => new SelectListItem(h.Name, h.Id.ToString())).ToListAsync(ct);

        Categories = await db.Categories.Where(c => c.ParentId == null).OrderBy(c => c.DisplayOrder)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(ct);

        TotalExhibitors = await db.Exhibitors.CountAsync(e => e.IsActive, ct);
        PlacedStands = await db.Kiosks.CountAsync(k => k.IsActive, ct);

        var interested = await db.Visits
            .Where(v => v.EventDate == today && v.Level >= InterestLevel.Interested)
            .GroupBy(v => v.ExhibitorId)
            .Select(g => new { g.Key, Count = g.Select(v => v.VisitorId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var query = db.Exhibitors.AsNoTracking().Where(e => e.IsActive);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            string term = Q.Trim();
            query = query.Where(e =>
                EF.Functions.Like(e.CompanyName, $"%{term}%") ||
                EF.Functions.Like(e.Code, $"%{term}%") ||
                e.Kiosks.Any(k => EF.Functions.Like(k.StandNumber, $"%{term}%")));
        }

        if (CategoryId is not null)
            query = query.Where(e => e.CategoryId == CategoryId || e.SubCategoryId == CategoryId);

        if (HallId is not null)
            query = query.Where(e => e.Kiosks.Any(k => k.HallId == HallId));

        if (Unplaced)
            query = query.Where(e => !e.Kiosks.Any(k => k.IsActive));

        var rows = await query
            .OrderBy(e => e.CompanyName)
            .Take(600)
            .Select(e => new
            {
                e.Id,
                e.Code,
                e.CompanyName,
                CategoryName = e.Category != null ? e.Category.Name : null,
                SubCategoryName = e.SubCategory != null ? e.SubCategory.Name : null,
                Kiosk = e.Kiosks.Where(k => k.IsActive)
                    .Select(k => new { k.Id, k.StandNumber, HallName = k.Hall.Name, k.WidthM, k.DepthM, k.QrToken })
                    .FirstOrDefault(),
                Files = e.Catalogues.Count(c => c.IsActive),
            })
            .ToListAsync(ct);

        var model = facility.Current;

        Rows = rows.Select(r => new Row(
            r.Id, r.Code, r.CompanyName, r.CategoryName, r.SubCategoryName,
            r.Kiosk?.Id, r.Kiosk?.StandNumber, r.Kiosk?.HallName,
            r.Kiosk is null ? 0 : Math.Round(r.Kiosk.WidthM * r.Kiosk.DepthM, 1),
            r.Kiosk is null ? 0 : model.Antennas.Count(a => a.KioskId == r.Kiosk.Id),
            r.Files,
            interested.GetValueOrDefault(r.Id),
            r.Kiosk?.QrToken)).ToList();
    }
}
