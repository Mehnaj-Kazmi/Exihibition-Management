using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using Exb.Core.Facility;

namespace Exb.Core.Tracking.Drivers;

/// <summary>Network address of one physical reader, from the ReaderEndpoints table.</summary>
public sealed record ReaderEndpointInfo(string ReaderCode, string Host, int Port, bool IsEnabled);

/// <summary>
/// LLRP (EPCglobal Low Level Reader Protocol, ISO 24791-5) client.
///
/// This is the protocol Impinj Speedway/R700, Zebra FX7500/FX9600 and most other
/// fixed UHF readers speak on TCP 5084. It connects to each reader that has an
/// endpoint configured, enables an ROSpec that reports EPC, antenna id and peak
/// RSSI, and republishes those reports through the same Read event the simulator
/// raises, so nothing downstream knows or cares which one is running.
///
/// Scope note: message framing, connection management, keepalives, reconnection
/// and decoding of RO_ACCESS_REPORT are implemented here, which is what the
/// locating engine needs. The ROSpec itself is built from a template, because
/// the full LLRP parameter set is large and genuinely site-specific — transmit
/// power, Gen2 session and search mode must be tuned per venue before going
/// live, and are called out in the README rather than guessed at here.
/// </summary>
public sealed class LlrpDriver(IReadOnlyList<ReaderEndpointInfo> endpoints) : ITagReaderDriver
{
    private const int HeaderLength = 10;
    private const int ReconnectMs = 5000;

    private readonly ConcurrentDictionary<string, ReaderStatus> _status = new();
    private readonly ConcurrentDictionary<string, Connection> _connections = new();
    private CancellationTokenSource? _cts;
    private FacilityModel? _facility;
    private int _messageId;

    public event Action<TagRead>? Read;
    public event Action<ReaderStatus>? StatusChanged;

    public string Name => $"LLRP ({endpoints.Count(e => e.IsEnabled)} reader(s) configured)";

    public IReadOnlyList<ReaderStatus> ReaderStatuses => _status.Values.ToList();

    private sealed class Connection
    {
        public required ReaderEndpointInfo Endpoint { get; init; }
        public required FacilityReader Reader { get; init; }
        public TcpClient? Client { get; set; }
    }

    public Task StartAsync(FacilityModel facility, CancellationToken ct)
    {
        _facility = facility;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var byCode = facility.Readers.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in endpoints.Where(e => e.IsEnabled))
        {
            if (!byCode.TryGetValue(endpoint.ReaderCode, out var reader))
            {
                SetStatus(endpoint.ReaderCode, ReaderState.Error,
                    "configured, but no such reader exists in the current hall layout");
                continue;
            }

            var connection = new Connection { Endpoint = endpoint, Reader = reader };
            _connections[endpoint.ReaderCode] = connection;
            _ = Task.Run(() => RunReaderAsync(connection, _cts.Token), _cts.Token);
        }

        foreach (var reader in facility.Readers)
            if (!_connections.ContainsKey(reader.Code))
                SetStatus(reader.Code, ReaderState.Offline, "no network endpoint configured");

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is not null) await _cts.CancelAsync();
        foreach (var connection in _connections.Values) connection.Client?.Close();
        _connections.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }

    /// <summary>Connect, configure, read, and reconnect for as long as we are running.</summary>
    private async Task RunReaderAsync(Connection connection, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetStatus(connection.Endpoint.ReaderCode, ReaderState.Connecting,
                    $"{connection.Endpoint.Host}:{connection.Endpoint.Port}");

                using var client = new TcpClient { NoDelay = true };
                connection.Client = client;
                await client.ConnectAsync(connection.Endpoint.Host, connection.Endpoint.Port, ct);

                var stream = client.GetStream();
                SetStatus(connection.Endpoint.ReaderCode, ReaderState.Online,
                    $"{connection.Endpoint.Host}:{connection.Endpoint.Port}");

                await ConfigureAsync(stream, ct);
                await ReadLoopAsync(connection, stream, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                SetStatus(connection.Endpoint.ReaderCode, ReaderState.Error, ex.Message);
            }

            if (ct.IsCancellationRequested) return;
            SetStatus(connection.Endpoint.ReaderCode, ReaderState.Offline, "reconnecting");
            try { await Task.Delay(ReconnectMs, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task ConfigureAsync(NetworkStream stream, CancellationToken ct)
    {
        await SendAsync(stream, LlrpMessage.AddRoSpec, BuildRoSpecTemplate(), ct);
        await SendAsync(stream, LlrpMessage.EnableRoSpec, U32(1), ct);
        await SendAsync(stream, LlrpMessage.StartRoSpec, U32(1), ct);
    }

    private async Task ReadLoopAsync(Connection connection, NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var pending = new List<byte>(64 * 1024);

        while (!ct.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buffer, ct);
            if (read == 0) return;   // reader closed the connection

            pending.AddRange(buffer.AsSpan(0, read).ToArray());

            // Pull whole LLRP messages out of the stream.
            while (pending.Count >= HeaderLength)
            {
                var header = pending.GetRange(0, HeaderLength).ToArray();
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(2));

                if (length < HeaderLength || length > 8 * 1024 * 1024)
                    throw new InvalidDataException("LLRP framing lost; dropping the connection to resynchronise");

                if (pending.Count < length) break;

                var message = pending.GetRange(0, length).ToArray();
                pending.RemoveRange(0, length);

                // Spans cannot live across an await, so the decode is done in a
                // synchronous helper and only the keepalive reply is awaited.
                (int type, uint messageId) = Dispatch(connection, message);

                if (type == LlrpMessage.KeepAlive)
                    await SendAsync(stream, LlrpMessage.KeepAliveAck, [], ct, messageId);
            }
        }
    }

    /// <summary>Decode one framed message, returning its type and id for the caller to act on.</summary>
    private (int Type, uint MessageId) Dispatch(Connection connection, byte[] message)
    {
        int type = BinaryPrimitives.ReadUInt16BigEndian(message) & 0x03FF;
        uint messageId = BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(6));

        if (type == LlrpMessage.RoAccessReport)
            ParseReport(connection, message.AsSpan(HeaderLength));

        return (type, messageId);
    }

    // --- report decoding -----------------------------------------------------

    /// <summary>
    /// An RO_ACCESS_REPORT is a sequence of TagReportData parameters, each a TLV
    /// holding sub-parameters. Walk the lengths and pick out the three things
    /// that matter: EPC, antenna id and peak RSSI.
    /// </summary>
    private void ParseReport(Connection connection, ReadOnlySpan<byte> body)
    {
        int offset = 0;
        while (offset + 4 <= body.Length)
        {
            int type = BinaryPrimitives.ReadUInt16BigEndian(body[offset..]) & 0x03FF;
            int length = BinaryPrimitives.ReadUInt16BigEndian(body[(offset + 2)..]);
            if (length <= 0 || offset + length > body.Length) return;   // malformed; stop here

            if (type == LlrpParameter.TagReportData)
                ParseTagReportData(connection, body.Slice(offset + 4, length - 4));

            offset += length;
        }
    }

    private void ParseTagReportData(Connection connection, ReadOnlySpan<byte> data)
    {
        string? epc = null;
        int? antennaId = null;
        double? rssi = null;
        int offset = 0;

        while (offset < data.Length)
        {
            // TV-encoded (short) parameters set the high bit of the first byte.
            if ((data[offset] & 0x80) != 0)
            {
                int tvType = data[offset] & 0x7F;
                int size;
                switch (tvType)
                {
                    case LlrpParameter.Epc96:
                        if (offset + 13 > data.Length) return;
                        epc = Convert.ToHexString(data.Slice(offset + 1, 12));
                        size = 13;
                        break;
                    case LlrpParameter.AntennaId:
                        if (offset + 3 > data.Length) return;
                        antennaId = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 1)..]);
                        size = 3;
                        break;
                    case LlrpParameter.PeakRssi:
                        if (offset + 2 > data.Length) return;
                        rssi = (sbyte)data[offset + 1];
                        size = 2;
                        break;
                    case LlrpParameter.TagSeenCount:
                        size = 3;
                        break;
                    case LlrpParameter.FirstSeenUtc:
                    case LlrpParameter.LastSeenUtc:
                        size = 9;
                        break;
                    default:
                        // TV lengths are implicit, so an unknown one cannot be skipped safely.
                        return;
                }
                offset += size;
            }
            else
            {
                if (offset + 4 > data.Length) return;
                int type = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]) & 0x03FF;
                int length = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
                if (length <= 0 || offset + length > data.Length) return;

                if (type == LlrpParameter.EpcData && offset + 6 <= data.Length)
                {
                    int bitLength = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 4)..]);
                    int byteLength = (bitLength + 7) / 8;
                    if (offset + 6 + byteLength <= data.Length)
                        epc = Convert.ToHexString(data.Slice(offset + 6, byteLength));
                }
                offset += length;
            }
        }

        if (epc is null || antennaId is null || rssi is null) return;

        // Map the reader-local antenna port back to our own antenna code.
        int port = antennaId.Value - 1;
        if (port < 0 || port >= connection.Reader.AntennaCodes.Count) return;

        Read?.Invoke(new TagRead(
            connection.Reader.Code,
            connection.Reader.AntennaCodes[port],
            epc,
            rssi.Value,
            DateTime.UtcNow));
    }

    // --- LLRP encoding -------------------------------------------------------

    private async Task SendAsync(NetworkStream stream, int type, byte[] payload, CancellationToken ct, uint? id = null)
    {
        uint messageId = id ?? (uint)Interlocked.Increment(ref _messageId);
        var message = new byte[HeaderLength + payload.Length];

        // Reserved = 0, Version = 1, then the type in the low ten bits.
        BinaryPrimitives.WriteUInt16BigEndian(message, (ushort)(1 << 10 | type & 0x03FF));
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(2), (uint)message.Length);
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(6), messageId);
        payload.CopyTo(message.AsSpan(HeaderLength));

        await stream.WriteAsync(message, ct);
    }

    /// <summary>
    /// A minimal ROSpec: every antenna, immediate start, and a report for each
    /// tag with the antenna id and peak RSSI enabled.
    ///
    /// Transmit power, Gen2 session (S2 suits dense fixed installs) and search
    /// mode live in AntennaConfiguration and are vendor-specific; set them to
    /// match the site survey before commissioning.
    /// </summary>
    private static byte[] BuildRoSpecTemplate()
    {
        byte[] tagContent = Param(238, U16(0x2040));                       // EnableAntennaID | EnablePeakRSSI
        byte[] roReport = Param(237, [.. new byte[] { 2 }, .. U16(1), .. tagContent]);

        byte[] inventory = Param(186, [.. U16(1), 1]);                     // EPCGlobal Class-1 Gen-2
        byte[] aiStop = Param(184, [.. new byte[] { 0 }, .. U32(0)]);
        byte[] aiSpec = Param(183, [.. U16(1), .. U16(0), .. aiStop, .. inventory]);

        byte[] startTrigger = Param(179, [0]);
        byte[] stopTrigger = Param(182, [.. new byte[] { 0 }, .. U32(0)]);
        byte[] boundary = Param(178, [.. startTrigger, .. stopTrigger]);

        byte[] body =
        [
            .. U32(1),           // ROSpecID
            0,                   // priority
            0,                   // current state: disabled
            .. boundary,
            .. aiSpec,
            .. roReport,
        ];

        return Param(177, body);
    }

    private static byte[] Param(int type, byte[] value)
    {
        var result = new byte[4 + value.Length];
        BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)(type & 0x03FF));
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2), (ushort)result.Length);
        value.CopyTo(result.AsSpan(4));
        return result;
    }

    private static byte[] U16(int value)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)value);
        return b;
    }

    private static byte[] U32(uint value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        return b;
    }

    private void SetStatus(string readerCode, ReaderState state, string? detail)
    {
        var status = new ReaderStatus(readerCode, state, detail, DateTime.UtcNow);
        _status[readerCode] = status;
        StatusChanged?.Invoke(status);
    }
}

internal static class LlrpMessage
{
    public const int AddRoSpec = 20;
    public const int StartRoSpec = 22;
    public const int EnableRoSpec = 24;
    public const int RoAccessReport = 61;
    public const int KeepAlive = 62;
    public const int KeepAliveAck = 72;
    public const int CloseConnection = 14;
}

internal static class LlrpParameter
{
    public const int AntennaId = 1;
    public const int FirstSeenUtc = 2;
    public const int LastSeenUtc = 4;
    public const int PeakRssi = 6;
    public const int TagSeenCount = 8;
    public const int Epc96 = 13;
    public const int TagReportData = 240;
    public const int EpcData = 241;
}
