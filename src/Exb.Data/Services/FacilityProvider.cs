using Exb.Core.Facility;
using Exb.Core.Geometry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Exb.Data.Services;

/// <summary>
/// Holds the current physical model of the exhibition and rebuilds it whenever
/// the admin changes something that would alter it.
///
/// Rebuild is a whole-model swap rather than an in-place edit. Halls, stands and
/// antenna rules are all interdependent — resizing a hall moves stands, moving a
/// stand moves its antennas, and both change the coverage figures — so a partial
/// update would leave the locating engine solving against a floor plan that no
/// longer exists.
/// </summary>
public sealed class FacilityProvider(
    IDbContextFactory<ExhibitionDbContext> factory,
    SettingsStore settings,
    ILogger<FacilityProvider> logger)
{
    private FacilityModel _current = FacilityModel.Empty(new Core.Configuration.TrackingSettings());

    public FacilityModel Current => _current;

    /// <summary>Raised after a rebuild so drivers and the live map can re-attach.</summary>
    public event Action<FacilityModel>? Rebuilt;

    public async Task<FacilityModel> RebuildAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var halls = await db.Halls
            .AsNoTracking()
            .Where(h => h.IsActive)
            .OrderBy(h => h.DisplayOrder).ThenBy(h => h.Code)
            .Select(h => new HallSpec(h.Id, h.Code, h.Name, h.WidthM, h.DepthM, h.DisplayOrder))
            .ToListAsync(ct);

        var kiosks = await db.Kiosks
            .AsNoTracking()
            .Where(k => k.IsActive && k.Exhibitor.IsActive)
            .Select(k => new
            {
                k.Id,
                k.HallId,
                k.StandNumber,
                k.X,
                k.Y,
                k.WidthM,
                k.DepthM,
                k.ExhibitorId,
                k.Exhibitor.CompanyName,
                k.Exhibitor.CategoryId,
                k.Exhibitor.SubCategoryId,
            })
            .ToListAsync(ct);

        var specs = kiosks
            .Select(k => new KioskSpec(
                k.Id,
                k.HallId,
                k.StandNumber,
                new FloorRect(k.X, k.Y, k.WidthM, k.DepthM),
                k.ExhibitorId,
                k.CompanyName,
                k.CategoryId,
                k.SubCategoryId))
            .ToList();

        var model = FacilityBuilder.Build(settings.Current.Tracking, halls, specs);
        _current = model;

        logger.LogInformation(
            "Facility rebuilt: {Halls} hall(s), {Kiosks} stand(s), {Antennas} antennas on {Readers} readers. " +
            "Stand floor localizable {StandPct}%, whole floor detectable {FloorPct}%.",
            model.Halls.Count, specs.Count, model.Coverage.TotalAntennas, model.Coverage.TotalReaders,
            model.Coverage.StandFloorLocalizablePct, model.Coverage.WholeFloorDetectablePct);

        if (model.Coverage.Warning is not null)
            logger.LogWarning("Coverage: {Warning} {Remedy}", model.Coverage.Warning, model.Coverage.Remedy);

        Rebuilt?.Invoke(model);
        return model;
    }
}
