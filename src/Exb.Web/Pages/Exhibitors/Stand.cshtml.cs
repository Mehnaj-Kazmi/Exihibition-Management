using System.Text.Json;
using Exb.Core.Facility;
using Exb.Core.Geometry;
using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Exhibitors;

/// <summary>
/// Places one exhibitor's stand on a hall floor.
///
/// This screen is where the antenna count is really decided, so it shows the
/// consequence of the size as it is typed rather than leaving it to be
/// discovered later on the coverage report.
/// </summary>
public class StandModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    SettingsStore settings,
    FacilityProvider facility) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    [BindProperty] public int HallId { get; set; }
    [BindProperty] public string StandNumber { get; set; } = "";
    [BindProperty] public double X { get; set; }
    [BindProperty] public double Y { get; set; }
    [BindProperty] public double WidthM { get; set; } = 3;
    [BindProperty] public double DepthM { get; set; } = 3;

    public string CompanyName { get; private set; } = "";
    public int? KioskId { get; private set; }
    public string? QrToken { get; private set; }
    public List<SelectListItem> Halls { get; private set; } = [];
    public string LayoutJson { get; private set; } = "{}";
    public string AntennaRuleJson { get; private set; } = "{}";
    public int AntennaCount { get; private set; }
    public string? Problem { get; private set; }
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return RedirectToPage("/Exhibitors/Index");
        Message = TempData["message"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var hall = await db.Halls.FirstOrDefaultAsync(h => h.Id == HallId, ct);
        if (hall is null)
        {
            Problem = "Choose a hall.";
            await LoadAsync(ct, keepPosted: true);
            return Page();
        }

        var footprint = new FloorRect(X, Y, WidthM, DepthM);

        if (WidthM < 1 || DepthM < 1)
            Problem = "A stand must be at least 1 m on each side.";
        else if (X < 0 || Y < 0 || footprint.Right > hall.WidthM || footprint.Top > hall.DepthM)
            Problem = $"That stand falls outside {hall.Name}, which is {hall.WidthM} × {hall.DepthM} m.";
        else if (string.IsNullOrWhiteSpace(StandNumber))
            Problem = "Give the stand a number; it is what visitors and stewards navigate by.";

        if (Problem is null)
        {
            // Overlapping stands would make dwell attribution meaningless: a badge
            // inside the overlap genuinely belongs to both, and the margin test
            // would silently downgrade every visit on both stands.
            var others = await db.Kiosks
                .Where(k => k.HallId == HallId && k.IsActive && k.ExhibitorId != Id)
                .Select(k => new { k.StandNumber, k.X, k.Y, k.WidthM, k.DepthM })
                .ToListAsync(ct);

            var clash = others.FirstOrDefault(o =>
                footprint.Intersects(new FloorRect(o.X, o.Y, o.WidthM, o.DepthM)));

            if (clash is not null)
                Problem = $"That footprint overlaps stand {clash.StandNumber}. Stands must not overlap.";
        }

        if (Problem is null)
        {
            bool duplicate = await db.Kiosks.AnyAsync(
                k => k.HallId == HallId && k.StandNumber == StandNumber && k.ExhibitorId != Id, ct);
            if (duplicate) Problem = $"Stand number {StandNumber} is already used in {hall.Name}.";
        }

        if (Problem is not null)
        {
            await LoadAsync(ct, keepPosted: true);
            return Page();
        }

        var kiosk = await db.Kiosks.FirstOrDefaultAsync(k => k.ExhibitorId == Id, ct);
        if (kiosk is null)
        {
            kiosk = new Kiosk { ExhibitorId = Id, QrToken = Tokens.New(16) };
            db.Kiosks.Add(kiosk);
        }

        kiosk.HallId = HallId;
        kiosk.StandNumber = StandNumber.Trim();
        kiosk.X = Math.Round(X, 2);
        kiosk.Y = Math.Round(Y, 2);
        kiosk.WidthM = Math.Round(WidthM, 2);
        kiosk.DepthM = Math.Round(DepthM, 2);
        kiosk.IsActive = true;

        db.AuditEntries.Add(new AuditEntry
        {
            Action = "stand.place",
            EntityName = "Kiosk",
            EntityId = kiosk.Id.ToString(),
            User = User.Identity?.Name,
            DetailJson = JsonSerializer.Serialize(new { HallId, StandNumber, X, Y, WidthM, DepthM }),
        });

        await db.SaveChangesAsync(ct);

        // The stand's antennas are derived from its footprint, so the physical
        // model has to be rebuilt before the change means anything.
        await facility.RebuildAsync(ct);

        TempData["message"] = $"Stand {kiosk.StandNumber} placed. It has been issued "
            + $"{FacilityBuilder.AntennaCountFor(kiosk.WidthM * kiosk.DepthM, settings.Current.Tracking.KioskAntennas)} antenna(s).";
        return RedirectToPage(new { id = Id });
    }

    private async Task<bool> LoadAsync(CancellationToken ct, bool keepPosted = false)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var exhibitor = await db.Exhibitors.AsNoTracking().FirstOrDefaultAsync(e => e.Id == Id, ct);
        if (exhibitor is null) return false;
        CompanyName = exhibitor.CompanyName;

        Halls = await db.Halls.Where(h => h.IsActive).OrderBy(h => h.DisplayOrder)
            .Select(h => new SelectListItem($"{h.Name} ({h.WidthM}×{h.DepthM} m)", h.Id.ToString()))
            .ToListAsync(ct);

        var kiosk = await db.Kiosks.AsNoTracking().FirstOrDefaultAsync(k => k.ExhibitorId == Id, ct);
        if (kiosk is not null)
        {
            KioskId = kiosk.Id;
            QrToken = kiosk.QrToken;

            if (!keepPosted)
            {
                HallId = kiosk.HallId;
                StandNumber = kiosk.StandNumber;
                X = kiosk.X;
                Y = kiosk.Y;
                WidthM = kiosk.WidthM;
                DepthM = kiosk.DepthM;
            }
        }
        else if (!keepPosted)
        {
            HallId = Halls.Count > 0 ? int.Parse(Halls[0].Value) : 0;
            StandNumber = await SuggestStandNumberAsync(db, HallId, ct);
        }

        var rule = settings.Current.Tracking.KioskAntennas;
        AntennaCount = FacilityBuilder.AntennaCountFor(WidthM * DepthM, rule);
        AntennaRuleJson = JsonSerializer.Serialize(new
        {
            areaPerAntennaM2 = rule.AreaPerAntennaM2,
            min = rule.MinPerKiosk,
            max = rule.MaxPerKiosk,
            heightM = rule.HeightM,
            readRadiusM = facility.Current.Coverage.KioskReadRadiusM,
        });

        var halls = await db.Halls.AsNoTracking().Where(h => h.IsActive)
            .Select(h => new
            {
                id = h.Id,
                name = h.Name,
                widthM = h.WidthM,
                depthM = h.DepthM,
                stands = h.Kiosks.Where(k => k.IsActive && k.ExhibitorId != Id)
                    .Select(k => new
                    {
                        id = k.Id,
                        stand = k.StandNumber,
                        name = k.Exhibitor.CompanyName,
                        x = k.X,
                        y = k.Y,
                        w = k.WidthM,
                        d = k.DepthM,
                    }),
            })
            .ToListAsync(ct);

        LayoutJson = JsonSerializer.Serialize(halls);
        return true;
    }

    private static async Task<string> SuggestStandNumberAsync(ExhibitionDbContext db, int hallId, CancellationToken ct)
    {
        var hall = await db.Halls.FirstOrDefaultAsync(h => h.Id == hallId, ct);
        if (hall is null) return "";

        int used = await db.Kiosks.CountAsync(k => k.HallId == hallId, ct);
        return $"{hall.Code}-{used + 1:D3}";
    }
}
