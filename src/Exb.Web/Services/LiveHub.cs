using Exb.Core.Tracking;
using Microsoft.AspNetCore.SignalR;

namespace Exb.Web.Services;

/// <summary>Push channel for the live floor plan. Clients only listen.</summary>
public sealed class LiveHub : Hub;

public sealed record LiveBadge(int H, double X, double Y, int K, int S, double U);

public sealed record LiveHallSummary(int Id, string Code, string Name, int Live, int OnStands);

public sealed record LiveStand(int KioskId, string StandNumber, string Exhibitor, int Here, int InterestedToday);

public sealed record LiveFrame(
    long Ts,
    int Tracked,
    int ReadRateHz,
    double SolveMs,
    int ActiveAntennas,
    int TotalAntennas,
    int ReadersOnline,
    int TotalReaders,
    string Driver,
    long SessionsOpened,
    long SessionsClosed,
    IReadOnlyList<LiveHallSummary> Halls,
    IReadOnlyList<LiveBadge> Badges,
    IReadOnlyList<LiveStand> BusiestStands);

/// <summary>
/// Builds the snapshot pushed to every connected display each second.
///
/// One frame is built per tick and sent to everyone, rather than a tailored
/// frame per client. Badge positions are rounded to a tenth of a metre and sent
/// as short field names, because on a busy floor this goes out sixty times a
/// minute to every wall display and tablet in the building, and the full records
/// would dominate the bandwidth for no visible benefit.
/// </summary>
public static class LiveFrameBuilder
{
    public static LiveFrame Build(
        TrackingRuntime runtime,
        IReadOnlyDictionary<int, int> interestedTodayByKiosk)
    {
        var engine = runtime.Engine;
        if (engine is null)
            return new LiveFrame(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                0, 0, 0, 0, 0, 0, 0, runtime.DriverName, 0, 0, [], [], []);

        var facility = engine.Facility;
        var now = DateTime.UtcNow;

        var badges = new List<LiveBadge>();
        var perHall = facility.Halls.ToDictionary(h => h.Id, _ => (Live: 0, OnStands: 0));
        var occupancy = new Dictionary<int, int>();

        foreach (var tag in engine.Tags)
        {
            if (tag.Status == TagStatus.Gone) continue;

            badges.Add(new LiveBadge(
                tag.HallId,
                Math.Round(tag.X, 1),
                Math.Round(tag.Y, 1),
                tag.AttributedKioskId ?? 0,
                (int)tag.Status,
                Math.Round(tag.UncertaintyM, 1)));

            if (perHall.TryGetValue(tag.HallId, out var counts))
                perHall[tag.HallId] = (counts.Live + 1, counts.OnStands + (tag.AttributedKioskId is null ? 0 : 1));

            if (tag.AttributedKioskId is { } kioskId)
                occupancy[kioskId] = occupancy.GetValueOrDefault(kioskId) + 1;
        }

        var busiest = occupancy
            .OrderByDescending(kv => kv.Value)
            .Take(12)
            .Select(kv =>
            {
                var kiosk = facility.KioskById.GetValueOrDefault(kv.Key);
                return new LiveStand(
                    kv.Key,
                    kiosk?.StandNumber ?? "?",
                    kiosk?.ExhibitorName ?? "?",
                    kv.Value,
                    interestedTodayByKiosk.GetValueOrDefault(kv.Key));
            })
            .ToList();

        var readers = runtime.ReaderStatuses;

        return new LiveFrame(
            Ts: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Tracked: engine.TrackedCount,
            ReadRateHz: engine.ReadRateHz,
            SolveMs: Math.Round(engine.LastSolveMs, 1),
            ActiveAntennas: engine.ActiveAntennaCount(now),
            TotalAntennas: facility.Antennas.Count,
            ReadersOnline: readers.Count(r => r.State == ReaderState.Online),
            TotalReaders: facility.Readers.Count,
            Driver: runtime.DriverName,
            SessionsOpened: runtime.SessionsOpened,
            SessionsClosed: runtime.SessionsClosed,
            Halls: facility.Halls
                .Select(h => new LiveHallSummary(h.Id, h.Code, h.Name,
                    perHall.GetValueOrDefault(h.Id).Live,
                    perHall.GetValueOrDefault(h.Id).OnStands))
                .ToList(),
            Badges: badges,
            BusiestStands: busiest);
    }
}
