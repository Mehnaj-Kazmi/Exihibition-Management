using System.Collections.Concurrent;
using Exb.Core.Facility;

namespace Exb.Core.Tracking;

public enum TagStatus
{
    /// <summary>Heard within the stale window.</summary>
    Live = 0,

    /// <summary>Not heard recently; last position still shown.</summary>
    Stale = 1,

    /// <summary>Silent long enough that the badge has presumably left the building.</summary>
    Gone = 2,
}

/// <summary>Live state of one badge on the floor.</summary>
public sealed class TrackedTag
{
    public required string Epc { get; init; }

    public int HallId { get; set; }
    public string HallCode { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public string Zone { get; set; } = "";

    public double UncertaintyM { get; set; }
    public double Confidence { get; set; }
    public int AntennaCount { get; set; }
    public double BestRssi { get; set; }
    public string Method { get; set; } = "";
    public double? ResidualRms { get; set; }
    public double SpeedMps { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public long ReadCount { get; set; }
    public TagStatus Status { get; set; }

    /// <summary>Stand this badge is currently attributed to, set by the dwell engine.</summary>
    public int? AttributedKioskId { get; set; }
    public double AttributionMarginM { get; set; }

    public TrackedTag Snapshot() => (TrackedTag)MemberwiseClone();
}

/// <summary>
/// Locating engine.
///
/// Consumes raw badge reads, buffers them per badge over a sliding window, and
/// periodically resolves each badge to a floor position.
///
/// Buffering matters because readers multiplex: a reader dwells on one antenna
/// port at a time, so a given antenna only revisits a badge about once per
/// port cycle. Solving on a single instant's reads would almost always leave one
/// antenna and no usable fix, which on a stand-antenna layout would mean no
/// interest data at all.
/// </summary>
public sealed class LocatingEngine
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, RssiSample>> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TrackedTag> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _antennaActivity = new(StringComparer.OrdinalIgnoreCase);

    private FacilityModel _facility;
    private long _readCount;
    private long _readsSinceTick;

    public LocatingEngine(FacilityModel facility)
    {
        _facility = facility;
        StartedUtc = DateTime.UtcNow;
    }

    private readonly record struct RssiSample(double Rssi, DateTime Utc);

    public DateTime StartedUtc { get; }
    public long TotalReads => Interlocked.Read(ref _readCount);
    public int ReadRateHz { get; private set; }
    public double LastSolveMs { get; private set; }
    public int TrackedCount => _tags.Count;
    public FacilityModel Facility => _facility;

    /// <summary>Swap in a rebuilt facility model after an admin changes halls or stands.</summary>
    public void UpdateFacility(FacilityModel facility) => _facility = facility;

    public TrackedTag? Get(string epc) => _tags.GetValueOrDefault(epc);

    public IReadOnlyCollection<TrackedTag> Tags => _tags.Values.ToList();

    public IEnumerable<TrackedTag> LiveTags => _tags.Values.Where(t => t.Status != TagStatus.Gone);

    /// <summary>Hot path: called for every read off every antenna. Keep it cheap.</summary>
    public void Ingest(TagRead read)
    {
        Interlocked.Increment(ref _readCount);
        Interlocked.Increment(ref _readsSinceTick);
        _antennaActivity[read.AntennaCode] = read.Utc;

        var buffer = _buffers.GetOrAdd(read.Epc, _ => new ConcurrentDictionary<string, RssiSample>(StringComparer.OrdinalIgnoreCase));

        // Keep the strongest read from this antenna within the window. Peak rather
        // than latest, because backscatter fluctuates with how the badge happens
        // to be hanging, and the peak is the closest thing to a clean
        // line-of-sight measurement.
        // Copied out of the `in` parameter so the update lambda can capture them.
        double rssi = read.Rssi;
        DateTime utc = read.Utc;
        int halfWindow = _facility.Settings.Locator.WindowMs / 2;

        buffer.AddOrUpdate(
            read.AntennaCode,
            new RssiSample(rssi, utc),
            (_, prev) => rssi >= prev.Rssi || (utc - prev.Utc).TotalMilliseconds > halfWindow
                ? new RssiSample(rssi, utc)
                : prev);
    }

    /// <summary>
    /// Re-solve every badge that has fresh reads. Returns the tags whose position
    /// was updated this tick, which is what the dwell engine then attributes.
    /// </summary>
    public IReadOnlyList<TrackedTag> SolveTick(DateTime nowUtc)
    {
        var started = DateTime.UtcNow;
        var settings = _facility.Settings.Locator;
        var cutoff = nowUtc.AddMilliseconds(-settings.WindowMs);

        ReadRateHz = (int)Math.Round(Interlocked.Exchange(ref _readsSinceTick, 0) * 1000.0 / Math.Max(1, settings.IntervalMs));

        var updated = new List<TrackedTag>();

        foreach (var (epc, buffer) in _buffers)
        {
            // Drop reads that have aged out of the window.
            foreach (var (antennaCode, sample) in buffer)
                if (sample.Utc < cutoff) buffer.TryRemove(antennaCode, out _);

            if (buffer.IsEmpty)
            {
                _buffers.TryRemove(epc, out _);
                continue;
            }

            // Reads should all come from one hall, but a badge near a hall
            // boundary can be heard through it. Keep the hall with the strongest
            // signal and solve using only that hall's antennas.
            var byHall = new Dictionary<int, (List<AntennaFix> Fixes, double Best)>();
            DateTime lastSeen = DateTime.MinValue;

            foreach (var (antennaCode, sample) in buffer)
            {
                var antenna = _facility.Antenna(antennaCode);
                if (antenna is null) continue;

                if (!byHall.TryGetValue(antenna.HallId, out var group))
                    byHall[antenna.HallId] = group = ([], double.NegativeInfinity);

                group.Fixes.Add(new AntennaFix(antenna.Code, antenna.X, antenna.Y, antenna.HeightM, sample.Rssi));
                if (sample.Rssi > group.Best)
                    byHall[antenna.HallId] = (group.Fixes, sample.Rssi);

                if (sample.Utc > lastSeen) lastSeen = sample.Utc;
            }

            if (byHall.Count == 0) continue;

            var winner = byHall.OrderByDescending(kv => kv.Value.Best).First();
            var hall = _facility.HallById.GetValueOrDefault(winner.Key);
            if (hall is null) continue;

            var fix = Locator.Solve(_facility.Rf, settings, hall.WidthM, hall.DepthM, winner.Value.Fixes);
            if (fix is null) continue;

            var existing = _tags.GetValueOrDefault(epc);
            var (sx, sy) = Locator.Smooth(
                existing?.X, existing?.Y, existing?.HallCode,
                fix.X, fix.Y, hall.Code,
                settings.SmoothingAlpha);

            var tag = existing ?? new TrackedTag { Epc = epc, FirstSeenUtc = lastSeen };
            double speed = existing is null ? 0 : EstimateSpeed(existing, sx, sy, lastSeen);

            tag.HallId = hall.Id;
            tag.HallCode = hall.Code;
            tag.X = sx;
            tag.Y = sy;
            tag.Zone = hall.ZoneLabel(sx, sy);
            tag.UncertaintyM = fix.UncertaintyM;
            tag.Confidence = fix.Confidence;
            tag.AntennaCount = fix.AntennaCount;
            tag.BestRssi = fix.BestRssi;
            tag.Method = fix.Method;
            tag.ResidualRms = fix.ResidualRms;
            tag.LastSeenUtc = lastSeen;
            tag.ReadCount += buffer.Count;
            tag.Status = TagStatus.Live;
            tag.SpeedMps = speed;

            _tags[epc] = tag;
            updated.Add(tag);
        }

        // Age out badges we have stopped hearing from.
        foreach (var tag in _tags.Values)
        {
            double ageMs = (nowUtc - tag.LastSeenUtc).TotalMilliseconds;
            if (ageMs > settings.GoneMs) tag.Status = TagStatus.Gone;
            else if (ageMs > settings.StaleMs) tag.Status = TagStatus.Stale;
        }

        LastSolveMs = (DateTime.UtcNow - started).TotalMilliseconds;
        return updated;
    }

    /// <summary>Drop badges that have been gone long enough to stop costing memory.</summary>
    public int Evict(DateTime nowUtc, TimeSpan olderThan)
    {
        int removed = 0;
        foreach (var (epc, tag) in _tags)
        {
            if (tag.Status == TagStatus.Gone && nowUtc - tag.LastSeenUtc > olderThan && _tags.TryRemove(epc, out _))
                removed++;
        }
        return removed;
    }

    public int ActiveAntennaCount(DateTime nowUtc)
        => _antennaActivity.Count(kv => (nowUtc - kv.Value).TotalSeconds < 5);

    private static double EstimateSpeed(TrackedTag prev, double nx, double ny, DateTime lastSeen)
    {
        double dt = (lastSeen - prev.LastSeenUtc).TotalSeconds;
        if (dt <= 0.05) return prev.SpeedMps;
        double d = Math.Sqrt(Math.Pow(nx - prev.X, 2) + Math.Pow(ny - prev.Y, 2));
        return Math.Round(0.7 * prev.SpeedMps + 0.3 * (d / dt), 2);
    }
}
