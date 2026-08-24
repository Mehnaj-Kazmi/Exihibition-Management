using Exb.Core.Facility;
using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Settings;

/// <summary>
/// Add, remove and resize halls while the exhibition is running.
///
/// Resizing is the operation that needs care: stands are positioned in
/// hall-local metres, so shrinking a hall can leave stands hanging outside the
/// building. Rather than silently clamping them — which would move exhibitors
/// without telling anyone — the save is refused and the offending stands are
/// named.
/// </summary>
public class HallsModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    FacilityProvider facility,
    SettingsStore settings) : PageModel
{
    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public CoverageReport Coverage => facility.Current.Coverage;
    public double AreaPerAntenna => settings.Current.Tracking.KioskAntennas.AreaPerAntennaM2;
    public bool AisleGridOn => settings.Current.Tracking.AisleGrid.Enabled;
    public string? Message { get; private set; }
    public string? Problem { get; private set; }

    public record Row(
        int Id, string Code, string Name, double WidthM, double DepthM, int DisplayOrder,
        int Stands, int KioskAntennas, int AisleAntennas, int Readers, double StandAreaM2);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostSaveAsync(
        int? id, string code, string name, double widthM, double depthM, int displayOrder, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        code = (code ?? "").Trim().ToUpperInvariant();
        name = (name ?? "").Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            return await FailAsync("A hall needs both a code and a name.", ct);

        if (widthM < 5 || depthM < 5)
            return await FailAsync("A hall must be at least 5 m on each side.", ct);

        if (widthM > 500 || depthM > 500)
            return await FailAsync("That hall is over 500 m on a side. Check the units — these are metres.", ct);

        if (await db.Halls.AnyAsync(h => h.Code == code && h.Id != id, ct))
            return await FailAsync($"Hall code '{code}' is already in use.", ct);

        Hall hall;
        if (id is null)
        {
            hall = new Hall();
            db.Halls.Add(hall);
        }
        else
        {
            hall = await db.Halls.FirstOrDefaultAsync(h => h.Id == id, ct)
                   ?? throw new InvalidOperationException($"no hall {id}");

            // Shrinking must not silently strand an exhibitor outside the building.
            var stranded = await db.Kiosks
                .Where(k => k.HallId == hall.Id && k.IsActive
                            && (k.X + k.WidthM > widthM || k.Y + k.DepthM > depthM))
                .Select(k => k.StandNumber)
                .Take(6)
                .ToListAsync(ct);

            if (stranded.Count > 0)
                return await FailAsync(
                    $"{hall.Name} cannot shrink to {widthM} × {depthM} m: stand(s) {string.Join(", ", stranded)} " +
                    "would fall outside it. Move those stands first.", ct);
        }

        hall.Code = code;
        hall.Name = name;
        hall.WidthM = Math.Round(widthM, 2);
        hall.DepthM = Math.Round(depthM, 2);
        hall.DisplayOrder = displayOrder;
        hall.IsActive = true;

        db.AuditEntries.Add(new AuditEntry
        {
            Action = id is null ? "hall.create" : "hall.update",
            EntityName = "Hall",
            EntityId = hall.Id.ToString(),
            User = User.Identity?.Name,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { code, name, widthM, depthM }),
        });

        await db.SaveChangesAsync(ct);

        // The antenna layout is derived from hall and stand geometry, so the
        // physical model has to be rebuilt for the change to take effect.
        var model = await facility.RebuildAsync(ct);

        TempData["message"] = $"{hall.Name} saved. The floor now has {model.Coverage.TotalAntennas} antenna(s) "
            + $"on {model.Coverage.TotalReaders} reader(s), with {model.Coverage.StandFloorLocalizablePct}% of stand floor fully covered.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var hall = await db.Halls.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hall is null) return RedirectToPage();

        int stands = await db.Kiosks.CountAsync(k => k.HallId == id && k.IsActive, ct);
        if (stands > 0)
        {
            TempData["problem"] =
                $"{hall.Name} still has {stands} stand(s) in it. Move or remove them before deleting the hall, " +
                "so no exhibitor is left without a location.";
            return RedirectToPage();
        }

        // Retired rather than deleted: visit history references the hall id, and
        // that history is what the reports are built from.
        hall.IsActive = false;

        db.AuditEntries.Add(new AuditEntry
        {
            Action = "hall.retire",
            EntityName = "Hall",
            EntityId = hall.Id.ToString(),
            User = User.Identity?.Name,
        });

        await db.SaveChangesAsync(ct);
        await facility.RebuildAsync(ct);

        TempData["message"] = $"{hall.Name} removed from the floor plan.";
        return RedirectToPage();
    }

    private async Task<IActionResult> FailAsync(string message, CancellationToken ct)
    {
        Problem = message;
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Message ??= TempData["message"] as string;
        Problem ??= TempData["problem"] as string;

        await using var db = await factory.CreateDbContextAsync(ct);
        var halls = await db.Halls.AsNoTracking().Where(h => h.IsActive)
            .OrderBy(h => h.DisplayOrder).ThenBy(h => h.Code).ToListAsync(ct);

        var standStats = await db.Kiosks.AsNoTracking().Where(k => k.IsActive)
            .GroupBy(k => k.HallId)
            .Select(g => new { g.Key, Count = g.Count(), Area = g.Sum(k => k.WidthM * k.DepthM) })
            .ToDictionaryAsync(x => x.Key, x => x, ct);

        var model = facility.Current;

        Rows = halls.Select(h =>
        {
            var stats = standStats.GetValueOrDefault(h.Id);
            var built = model.HallById.GetValueOrDefault(h.Id);
            return new Row(
                h.Id, h.Code, h.Name, h.WidthM, h.DepthM, h.DisplayOrder,
                stats?.Count ?? 0,
                built?.Antennas.Count(a => a.Kind == AntennaKind.Kiosk) ?? 0,
                built?.Antennas.Count(a => a.Kind == AntennaKind.Aisle) ?? 0,
                built?.Readers.Count ?? 0,
                Math.Round(stats?.Area ?? 0, 1));
        }).ToList();
    }
}
