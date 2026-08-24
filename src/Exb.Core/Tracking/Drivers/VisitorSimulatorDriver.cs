using System.Collections.Concurrent;
using Exb.Core.Configuration;
using Exb.Core.Facility;

namespace Exb.Core.Tracking.Drivers;

/// <summary>A badge the simulator should put on the floor, with the interests it will act on.</summary>
public sealed record SimulatedBadge(string Epc, IReadOnlyList<int> PreferredCategoryIds);

/// <summary>Raised when a simulated visitor scans a stand's QR code for its e-catalogue.</summary>
public sealed record SimulatedScan(string Epc, int KioskId, DateTime Utc);

/// <summary>
/// A synthetic exhibition: visitors who walk the halls, stop at stands that
/// match their interests, and occasionally scan a QR code.
///
/// It models the things that actually break a locating system rather than
/// handing the engine the answers. Readers multiplex, so a given antenna only
/// revisits a badge once per port cycle. RSSI comes from the same link model the
/// solver inverts, plus Gaussian noise. Reads drop out at random, as they do on
/// site from badge orientation, RF nulls and collisions during inventory.
///
/// Crucially the engine never sees a visitor's true position or their interests.
/// <see cref="Truth"/> exposes both, but only to the test suite, which is what
/// makes it possible to ask an honest question: given only radio measurements,
/// did the system work out what this person cared about?
/// </summary>
public sealed class VisitorSimulatorDriver(SimulatorSettings settings, IReadOnlyList<SimulatedBadge> badges) : ITagReaderDriver
{
    private readonly Random _random = new(settings.Seed);
    private readonly List<Agent> _agents = [];
    private readonly ConcurrentQueue<SimulatedScan> _scans = new();
    private readonly object _gate = new();

    private FacilityModel? _facility;
    private Dictionary<int, List<Agent>> _agentsByHall = [];
    private Timer? _timer;
    private long _tick;

    public event Action<TagRead>? Read;
    public event Action<ReaderStatus>? StatusChanged;
    public event Action<SimulatedScan>? ScanRequested;

    public string Name => $"Simulator ({_agents.Count} synthetic visitors)";

    public IReadOnlyList<ReaderStatus> ReaderStatuses =>
        _facility?.Readers
            .Select(r => new ReaderStatus(r.Code, ReaderState.Online, "simulated", DateTime.UtcNow))
            .ToList() ?? [];

    private enum Mode { Walking, Dwelling }

    private sealed class Agent
    {
        public required string Epc { get; init; }
        public required HashSet<int> Preferred { get; init; }
        public int HallId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public Mode Mode { get; set; } = Mode.Walking;
        public int TargetKioskId { get; set; }
        public double TargetX { get; set; }
        public double TargetY { get; set; }
        public double DwellRemaining { get; set; }
        public double ScanAt { get; set; } = -1;
        public double Speed { get; set; }
    }

    public Task StartAsync(FacilityModel facility, CancellationToken ct)
    {
        _facility = facility;
        if (facility.Halls.Count == 0) return Task.CompletedTask;

        BuildPopulation(facility);

        int period = Math.Max(20, facility.Settings.KioskAntennas.DwellMs);
        _timer = new Timer(_ => Step(), null, period, period);

        foreach (var reader in facility.Readers)
            StatusChanged?.Invoke(new ReaderStatus(reader.Code, ReaderState.Online, "simulated", DateTime.UtcNow));

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        return ValueTask.CompletedTask;
    }

    // --- population ----------------------------------------------------------

    private void BuildPopulation(FacilityModel facility)
    {
        var categories = facility.Halls
            .SelectMany(h => h.Kiosks)
            .Where(k => k.CategoryId is not null)
            .Select(k => k.CategoryId!.Value)
            .Distinct()
            .ToList();

        var population = new List<SimulatedBadge>(badges);

        // Top up with invented badges so the floor looks like an exhibition even
        // before many visitors have registered.
        for (int i = population.Count; i < settings.VisitorCount; i++)
        {
            var prefs = new List<int>();
            if (categories.Count > 0)
            {
                prefs.Add(categories[_random.Next(categories.Count)]);
                if (categories.Count > 1 && _random.NextDouble() < 0.45)
                    prefs.Add(categories[_random.Next(categories.Count)]);
            }
            population.Add(new SimulatedBadge(SyntheticEpc(i + 1), prefs));
        }

        foreach (var badge in population.Take(Math.Max(settings.VisitorCount, badges.Count)))
        {
            var prefs = badge.PreferredCategoryIds.Count > 0 || categories.Count == 0
                ? badge.PreferredCategoryIds.ToHashSet()
                : new HashSet<int> { categories[_random.Next(categories.Count)] };

            var hall = facility.Halls[_random.Next(facility.Halls.Count)];
            var agent = new Agent
            {
                Epc = badge.Epc,
                Preferred = prefs,
                HallId = hall.Id,
                X = _random.NextDouble() * hall.WidthM,
                Y = _random.NextDouble() * hall.DepthM,
                Speed = settings.WalkSpeedMps * (0.75 + _random.NextDouble() * 0.5),
            };
            ChooseTarget(agent, facility);
            _agents.Add(agent);
        }

        _agentsByHall = _agents.GroupBy(a => a.HallId).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>An SGTIN-96 shaped EPC, so synthetic badges look like the real thing.</summary>
    private static string SyntheticEpc(int serial)
    {
        unchecked
        {
            uint body = (uint)(serial * 2654435761);
            uint tail = (uint)(serial * 40503) & 0xFFFFFF;
            return ("3034257BF4" + body.ToString("X8") + tail.ToString("X6"))[..24];
        }
    }

    // --- behaviour -----------------------------------------------------------

    /// <summary>
    /// Pick the next stand to walk to.
    ///
    /// Weighting is interest first and distance second: a visitor will cross a
    /// hall for something in their field, but will not walk past ten relevant
    /// stands to reach an eleventh. Without the distance term the simulated
    /// floor turns into everyone criss-crossing at once, which is not what an
    /// exhibition looks like and would make aisle coverage look better than it is.
    /// </summary>
    private void ChooseTarget(Agent agent, FacilityModel facility)
    {
        // Visitors change halls now and then, through the doors rather than the walls.
        if (_random.NextDouble() < 0.04 && facility.Halls.Count > 1)
        {
            var next = facility.Halls[_random.Next(facility.Halls.Count)];
            if (next.Id != agent.HallId)
            {
                agent.HallId = next.Id;
                agent.X = 1.0;
                agent.Y = 1.0;
            }
        }

        var hall = facility.HallById.GetValueOrDefault(agent.HallId);
        if (hall is null || hall.Kiosks.Count == 0)
        {
            agent.Mode = Mode.Walking;
            agent.TargetX = _random.NextDouble() * (hall?.WidthM ?? 10);
            agent.TargetY = _random.NextDouble() * (hall?.DepthM ?? 10);
            agent.TargetKioskId = 0;
            return;
        }

        double totalWeight = 0;
        var weights = new double[hall.Kiosks.Count];

        for (int i = 0; i < hall.Kiosks.Count; i++)
        {
            var kiosk = hall.Kiosks[i];
            double interest = kiosk.CategoryId is not null && agent.Preferred.Contains(kiosk.CategoryId.Value) ? 12.0 : 1.0;
            double distance = kiosk.Footprint.DistanceTo(agent.X, agent.Y);
            weights[i] = interest / (1.0 + distance / 25.0);
            totalWeight += weights[i];
        }

        double roll = _random.NextDouble() * totalWeight;
        int chosen = hall.Kiosks.Count - 1;
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll <= 0) { chosen = i; break; }
        }

        var target = hall.Kiosks[chosen];
        agent.TargetKioskId = target.Id;
        agent.Mode = Mode.Walking;

        // Stand at the edge of the stand, where visitors actually stand, rather
        // than in the middle of the exhibitor's carpet.
        double angle = _random.NextDouble() * Math.PI * 2;
        agent.TargetX = Math.Clamp(target.Footprint.CentreX + Math.Cos(angle) * (target.Footprint.Width / 2 + 0.6), 0, hall.WidthM);
        agent.TargetY = Math.Clamp(target.Footprint.CentreY + Math.Sin(angle) * (target.Footprint.Depth / 2 + 0.6), 0, hall.DepthM);
    }

    private void Step()
    {
        var facility = _facility;
        if (facility is null) return;
        if (!Monitor.TryEnter(_gate)) return;   // a slow tick must not pile up behind itself

        try
        {
            long tick = _tick++;
            double dt = Math.Max(20, facility.Settings.KioskAntennas.DwellMs) / 1000.0;
            var now = DateTime.UtcNow;

            MoveAgents(facility, dt, now);
            EmitReads(facility, tick, now);

            while (_scans.TryDequeue(out var scan)) ScanRequested?.Invoke(scan);
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void MoveAgents(FacilityModel facility, double dt, DateTime now)
    {
        bool hallsChanged = false;

        foreach (var agent in _agents)
        {
            if (agent.Mode == Mode.Dwelling)
            {
                agent.DwellRemaining -= dt;

                if (agent.ScanAt > 0 && agent.DwellRemaining <= agent.ScanAt)
                {
                    agent.ScanAt = -1;
                    _scans.Enqueue(new SimulatedScan(agent.Epc, agent.TargetKioskId, now));
                }

                if (agent.DwellRemaining <= 0)
                {
                    int previousHall = agent.HallId;
                    ChooseTarget(agent, facility);
                    if (agent.HallId != previousHall) hallsChanged = true;
                }
                continue;
            }

            double dx = agent.TargetX - agent.X;
            double dy = agent.TargetY - agent.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double stride = agent.Speed * dt;

            if (distance <= stride)
            {
                agent.X = agent.TargetX;
                agent.Y = agent.TargetY;
                StartDwelling(agent, facility);
            }
            else
            {
                agent.X += dx / distance * stride;
                agent.Y += dy / distance * stride;
            }
        }

        if (hallsChanged)
            _agentsByHall = _agents.GroupBy(a => a.HallId).ToDictionary(g => g.Key, g => g.ToList());
    }

    private void StartDwelling(Agent agent, FacilityModel facility)
    {
        agent.Mode = Mode.Dwelling;

        var kiosk = facility.KioskById.GetValueOrDefault(agent.TargetKioskId);
        bool interesting = kiosk?.CategoryId is not null && agent.Preferred.Contains(kiosk.CategoryId.Value);

        // Interested visitors stop and talk; everyone else glances and moves on.
        double seconds = interesting
            ? 70 + _random.NextDouble() * 320
            : 3 + _random.NextDouble() * 45;

        agent.DwellRemaining = seconds * Math.Max(0.05, settings.DwellScale);

        agent.ScanAt = interesting && _random.NextDouble() < settings.ScanProbability
            ? agent.DwellRemaining * 0.5
            : -1;
    }

    // --- read generation -----------------------------------------------------

    private void EmitReads(FacilityModel facility, long tick, DateTime now)
    {
        var handler = Read;
        if (handler is null) return;

        double sensitivity = facility.Settings.Rf.SensitivityDbm;

        foreach (var reader in facility.Readers)
        {
            if (reader.AntennaCodes.Count == 0) continue;

            // A reader dwells on one port at a time, so only one of its antennas
            // is transmitting on any given tick.
            string antennaCode = reader.AntennaCodes[(int)(tick % reader.AntennaCodes.Count)];
            var antenna = facility.Antenna(antennaCode);
            if (antenna is null) continue;

            if (!_agentsByHall.TryGetValue(antenna.HallId, out var agents)) continue;

            double maxLateral = facility.Rf.MaxLateralRange(antenna.HeightM);

            foreach (var agent in agents)
            {
                double dx = agent.X - antenna.X;
                if (dx > maxLateral || dx < -maxLateral) continue;   // cheap reject before the sqrt
                double dy = agent.Y - antenna.Y;
                if (dy > maxLateral || dy < -maxLateral) continue;

                double lateral = Math.Sqrt(dx * dx + dy * dy);
                if (lateral > maxLateral) continue;
                if (_random.NextDouble() < settings.DropoutProbability) continue;

                double rssi = facility.Rf.ExpectedRssi(lateral, antenna.HeightM) + Gaussian(settings.RssiNoiseDb);
                if (rssi < sensitivity) continue;

                handler(new TagRead(reader.Code, antennaCode, agent.Epc, rssi, now));
            }
        }
    }

    private double Gaussian(double sigma)
    {
        if (sigma <= 0) return 0;
        double u1 = 1.0 - _random.NextDouble();
        double u2 = _random.NextDouble();
        return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    // --- ground truth, for the test suite only -------------------------------

    public sealed record AgentTruth(string Epc, int HallId, double X, double Y, IReadOnlyList<int> PreferredCategoryIds, int? DwellingAtKioskId);

    /// <summary>
    /// Where every simulated visitor really is and what they really care about.
    /// Never consumed by the tracking stack; it exists so the tests can compare
    /// what the system inferred against what was true.
    /// </summary>
    public IReadOnlyList<AgentTruth> Truth()
    {
        lock (_gate)
        {
            return _agents.Select(a => new AgentTruth(
                a.Epc, a.HallId, a.X, a.Y,
                a.Preferred.ToList(),
                a.Mode == Mode.Dwelling ? a.TargetKioskId : null)).ToList();
        }
    }

    /// <summary>Run the simulation forward deterministically, without a timer, for tests.</summary>
    public void StepForTest() => Step();
}
