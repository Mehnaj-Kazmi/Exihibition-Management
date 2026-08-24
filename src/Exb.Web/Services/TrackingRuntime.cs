using Exb.Core.Dwell;
using Exb.Core.Facility;
using Exb.Core.Tracking;
using Exb.Core.Tracking.Drivers;
using Exb.Data;
using Exb.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Services;

/// <summary>
/// The live tracking stack: the reader driver, the locating engine and the dwell
/// engine, held together so they can be started, stopped and rebuilt as one.
///
/// They have to move together. Changing a hall size changes the antenna layout,
/// which changes which antenna codes the driver will report, which changes what
/// the locating engine can resolve. Restarting the three separately would leave
/// a window where reads arrive for antennas that no longer exist.
/// </summary>
public sealed class TrackingRuntime(
    FacilityProvider facility,
    SettingsStore settings,
    BadgeDirectory badges,
    VisitRepository visits,
    IDbContextFactory<ExhibitionDbContext> factory,
    CatalogueRequestService catalogueRequests,
    ILogger<TrackingRuntime> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    private ITagReaderDriver? _driver;
    private DateTime _lastVisitFlush = DateTime.MinValue;
    private DateTime _lastSnapshot = DateTime.MinValue;

    public LocatingEngine? Engine { get; private set; }
    public DwellEngine? Dwell { get; private set; }
    public string DriverName => _driver?.Name ?? "not started";
    public bool IsRunning => _driver is not null;
    public DateTime? StartedUtc { get; private set; }

    /// <summary>Total stand visits opened since start, for the health panel.</summary>
    public long SessionsOpened { get; private set; }
    public long SessionsClosed { get; private set; }

    public IReadOnlyList<ReaderStatus> ReaderStatuses => _driver?.ReaderStatuses ?? [];

    public async Task RestartAsync(CancellationToken ct = default)
    {
        await _restartGate.WaitAsync(ct);
        try
        {
            await StopInternalAsync();

            var model = await facility.RebuildAsync(ct);
            if (model.Halls.Count == 0)
            {
                logger.LogWarning("No active halls configured; tracking not started.");
                return;
            }

            await badges.RefreshAsync(ct);

            var engine = new LocatingEngine(model);
            var dwell = new DwellEngine(badges);
            dwell.Restore(await visits.LoadOpenAsync(settings.Current.Dwell, ct));

            var driver = await CreateDriverAsync(model, ct);
            driver.Read += engine.Ingest;

            if (driver is VisitorSimulatorDriver simulator)
                simulator.ScanRequested += OnSimulatedScan;

            Engine = engine;
            Dwell = dwell;
            _driver = driver;
            StartedUtc = DateTime.UtcNow;

            await driver.StartAsync(model, ct);
            logger.LogInformation("Tracking started with {Driver}.", driver.Name);
        }
        finally
        {
            _restartGate.Release();
        }
    }

    /// <summary>
    /// Choose between real readers and the simulator.
    ///
    /// Real hardware wins whenever any reader endpoint is configured and
    /// enabled, regardless of the simulator setting: a venue that has cabled its
    /// readers should never find synthetic visitors mixed into its live floor
    /// because a checkbox was left on from the demo.
    /// </summary>
    private async Task<ITagReaderDriver> CreateDriverAsync(FacilityModel model, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var endpoints = await db.ReaderEndpoints
            .AsNoTracking()
            .Where(r => r.IsEnabled && r.Host != "")
            .Select(r => new ReaderEndpointInfo(r.ReaderCode, r.Host, r.Port, r.IsEnabled))
            .ToListAsync(ct);

        if (endpoints.Count > 0)
        {
            logger.LogInformation("Using LLRP readers: {Count} endpoint(s) configured.", endpoints.Count);
            return new LlrpDriver(endpoints);
        }

        if (!settings.Current.Simulator.Enabled)
        {
            logger.LogWarning(
                "No reader endpoints are configured and the simulator is off. " +
                "Nothing will be tracked until readers are added in Settings > Readers.");
            return new LlrpDriver([]);
        }

        var badgeList = await db.Visitors
            .AsNoTracking()
            .Where(v => v.IsActive && v.BadgeEpc != "")
            .Select(v => v.BadgeEpc)
            .ToListAsync(ct);

        var categories = model.Halls
            .SelectMany(h => h.Kiosks)
            .Where(k => k.CategoryId is not null)
            .Select(k => k.CategoryId!.Value)
            .Distinct()
            .ToList();

        // Give each real registered badge a plausible interest, so the demo
        // exercises the same code path a live show would.
        var random = new Random(settings.Current.Simulator.Seed);
        var simulated = badgeList
            .Select(epc => new SimulatedBadge(
                epc,
                categories.Count == 0 ? [] : [categories[random.Next(categories.Count)]]))
            .ToList();

        return new VisitorSimulatorDriver(settings.Current.Simulator, simulated);
    }

    /// <summary>One pass: solve positions, attribute dwell, and persist what changed.</summary>
    public async Task<int> TickAsync(CancellationToken ct = default)
    {
        var engine = Engine;
        var dwell = Dwell;
        if (engine is null || dwell is null) return 0;

        var now = DateTime.UtcNow;
        var updated = engine.SolveTick(now);

        var app = settings.Current;
        var eventDate = LocalDate(app.Exhibition);
        var changes = dwell.Tick(engine.Facility, app.Dwell, updated, now, eventDate);

        SessionsOpened += changes.Count(c => c.Kind == SessionChangeKind.Opened);
        SessionsClosed += changes.Count(c => c.Kind == SessionChangeKind.Closed);

        // Opens and closes are written at once; running updates are throttled,
        // since a row saying "still here, now 43 seconds" does not need to be
        // written ten times a second for every visitor in the building.
        bool flushUpdates = (now - _lastVisitFlush).TotalSeconds >= 15;
        var toPersist = flushUpdates
            ? changes
            : changes.Where(c => c.Kind != SessionChangeKind.Updated).ToList();

        if (toPersist.Count > 0)
        {
            await visits.ApplyAsync(toPersist, app.Dwell, ct);
            if (flushUpdates) _lastVisitFlush = now;
        }

        if ((now - _lastSnapshot).TotalSeconds >= 30)
        {
            _lastSnapshot = now;
            await SnapshotPositionsAsync(engine, ct);
        }

        return updated.Count;
    }

    /// <summary>
    /// Periodically record last-known positions, so a restart does not lose the
    /// floor picture and a lost badge can still be traced to where it was last
    /// heard.
    /// </summary>
    private async Task SnapshotPositionsAsync(LocatingEngine engine, CancellationToken ct)
    {
        var live = engine.LiveTags.ToList();
        if (live.Count == 0) return;

        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.TagPositions.ToDictionaryAsync(p => p.Epc, ct);

        foreach (var tag in live)
        {
            if (!existing.TryGetValue(tag.Epc, out var row))
            {
                row = new Data.Entities.TagPositionSnapshot { Epc = tag.Epc, FirstSeenUtc = tag.FirstSeenUtc };
                db.TagPositions.Add(row);
            }

            row.HallId = tag.HallId;
            row.X = tag.X;
            row.Y = tag.Y;
            row.Zone = tag.Zone;
            row.KioskId = tag.AttributedKioskId;
            row.Confidence = tag.Confidence;
            row.UncertaintyM = tag.UncertaintyM;
            row.BestRssi = tag.BestRssi;
            row.AntennaCount = tag.AntennaCount;
            row.LastSeenUtc = tag.LastSeenUtc;
            row.ReadCount = tag.ReadCount;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>A simulated visitor scanning a stand's QR code becomes a real catalogue request.</summary>
    private void OnSimulatedScan(SimulatedScan scan)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var holder = badges.Resolve(scan.Epc);
                if (holder is null) return;   // a synthetic badge with no registration behind it

                await using var db = await factory.CreateDbContextAsync();
                string? token = await db.Kiosks
                    .Where(k => k.Id == scan.KioskId)
                    .Select(k => k.QrToken)
                    .FirstOrDefaultAsync();

                if (token is null) return;

                await catalogueRequests.RecordAsync(
                    token, holder.VisitorId, LocalDate(settings.Current.Exhibition), "qr");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Simulated scan could not be recorded.");
            }
        });
    }

    /// <summary>
    /// Today, in the venue's own time zone. The server may well be in a
    /// different one, and a visit at 21:00 local must land on the day the
    /// visitor was actually there.
    /// </summary>
    public static DateOnly LocalDate(Core.Configuration.ExhibitionSettings exhibition)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(exhibition.TimeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(DateTime.Now);
        }
    }

    public static DateTime LocalNow(Core.Configuration.ExhibitionSettings exhibition)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(exhibition.TimeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return DateTime.Now;
        }
    }

    public async Task StopAsync()
    {
        await _restartGate.WaitAsync();
        try { await StopInternalAsync(); }
        finally { _restartGate.Release(); }
    }

    private async Task StopInternalAsync()
    {
        if (_driver is null) return;

        if (Engine is not null) _driver.Read -= Engine.Ingest;
        if (_driver is VisitorSimulatorDriver simulator) simulator.ScanRequested -= OnSimulatedScan;

        await _driver.StopAsync();
        await _driver.DisposeAsync();
        _driver = null;
        StartedUtc = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _restartGate.Dispose();
    }
}
