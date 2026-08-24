using Exb.Core.Facility;

namespace Exb.Core.Tracking;

/// <summary>A single observation of one badge by one antenna.</summary>
public readonly record struct TagRead(
    string ReaderCode,
    string AntennaCode,
    string Epc,
    double Rssi,
    DateTime Utc);

public enum ReaderState
{
    Offline = 0,
    Connecting = 1,
    Online = 2,
    Error = 3,
}

public sealed record ReaderStatus(string ReaderCode, ReaderState State, string? Detail, DateTime UpdatedUtc);

/// <summary>
/// Reader driver contract.
///
/// Everything above this line — the locating engine, dwell attribution, the
/// reports — consumes badge reads through this interface and nothing else. That
/// keeps the physics and the exhibition logic independent of whether the reads
/// came off real LLRP hardware or the simulator, and it is what lets the whole
/// product be tested without a hall full of readers.
/// </summary>
public interface ITagReaderDriver : IAsyncDisposable
{
    string Name { get; }

    event Action<TagRead>? Read;
    event Action<ReaderStatus>? StatusChanged;

    Task StartAsync(FacilityModel facility, CancellationToken ct);
    Task StopAsync();

    IReadOnlyList<ReaderStatus> ReaderStatuses { get; }
}
