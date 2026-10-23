using System.Diagnostics;
using SpeedLink.Core;

namespace SpeedLink.J2534;

public sealed record J2534Version(string Firmware, string Driver, string Api);
public sealed record J2534Frame(uint Id, uint Status, byte[] Data);

/// <summary>A single physical ISO 15765 channel; implementations must not retry requests.</summary>
public interface IJ2534Channel : IDisposable
{
    string AdapterName => "J2534 adapter";
    bool Experimental => false;
    J2534Version Open();
    void Connect(uint bitrate, uint requestId, uint responseId);
    void ClearReceive();
    void Write(byte[] request);
    J2534Frame? Read(int timeoutMilliseconds);
    string? CleanupWarning { get; }
    object? ReadDiagnostics => null;
}

/// <summary>Read-only Mazda diagnostics through an installed J2534 driver.</summary>
public sealed class J2534ReadTransport : IReadTransport
{
    private readonly IJ2534Channel channel;
    private readonly TimeSpan responseTimeout;
    private readonly TimeSpan pendingTimeout;
    private bool initialized, disposed, faulted;
    private readonly IDiagnosticSink? diagnostics;
    public string Mode => "hardware";
    public string Adapter { get; private set; }
    public int ReadChunkSize => 3968;
    public bool ReadAttempted { get; set; }
    public string? CloseWarning => channel.CleanupWarning;

    public J2534ReadTransport(IJ2534Channel channel, TimeSpan? responseTimeout = null, TimeSpan? pendingTimeout = null, IDiagnosticSink? diagnostics = null)
    {
        this.channel = channel;
        this.diagnostics = diagnostics;
        Adapter = channel.AdapterName;
        this.responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(10);
        this.pendingTimeout = pendingTimeout ?? TimeSpan.FromSeconds(30);
        if (this.responseTimeout <= TimeSpan.Zero || this.pendingTimeout < this.responseTimeout)
            throw new ArgumentOutOfRangeException(nameof(responseTimeout));
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (initialized || faulted)
            throw new TransportException("Reconnect the J2534 adapter before initializing again.");
        try
        {
            diagnostics?.Record("adapter.opening", new { adapter = channel.AdapterName, backend = "J2534", channel.Experimental });
            var version = channel.Open();
            diagnostics?.Record("adapter.version", new { version.Firmware, version.Driver, version.Api });
            if (channel.Experimental && !version.Api.StartsWith("04.04", StringComparison.Ordinal))
                throw new TransportException("This experimental adapter requires a J2534-1 04.04 driver. No CAN connection was opened.");
            channel.Connect(500000, 0x7E0, 0x7E8);
            Adapter = $"{channel.AdapterName} · firmware {version.Firmware} · driver {version.Driver} · API {version.Api}";
            initialized = true;
            diagnostics?.Record("adapter.ready", new { adapter = Adapter, readChunkBytes = ReadChunkSize, responseTimeoutMs = responseTimeout.TotalMilliseconds, pendingTimeoutMs = pendingTimeout.TotalMilliseconds });
        }
        catch (Exception error) { diagnostics?.Record("adapter.error", new { stage = "Open/configure J2534", error = error.Message }); faulted = true; Dispose(); throw; }
    }

    public byte[] Exchange(byte[] request)
    {
        // Validate before touching the driver, including clearing its receive queue.
        DiagnosticProtocol.CheckReadOnly(request, ReadChunkSize);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!initialized || faulted)
            throw new TransportException("Reconnect and identify before continuing.");
        var total = Stopwatch.StartNew();
        var watch = new Stopwatch();
        int polls = 0, emptyPolls = 0, notifications = 0, foreignMessages = 0, completedReplies = 0, pendingReplies = 0;
        int txEchoes = 0, startNotifications = 0, txIndications = 0;
        try
        {
            channel.ClearReceive();
            channel.Write(request);
            watch.Start(); // Keep the ECU response deadline independent of driver queue/write time.
            var limit = responseTimeout;
            var packets = new List<byte[]>();
            while (watch.Elapsed < limit)
            {
                var remaining = Math.Max(1, (int)(limit - watch.Elapsed).TotalMilliseconds);
                var frame = channel.Read(Math.Min(25, remaining));
                polls++;
                if (frame is null)
                { emptyPolls++; continue; }
                // J2534 Tx echoes, start notifications and Tx indications are not PDUs.
                if ((frame.Status & 0x0B) != 0)
                {
                    notifications++;
                    if ((frame.Status & 1) != 0) txEchoes++;
                    if ((frame.Status & 2) != 0) startNotifications++;
                    if ((frame.Status & 8) != 0) txIndications++;
                    continue;
                }
                if (frame.Id != 0x7E8) { foreignMessages++; continue; }
                if ((frame.Status & 0x10) != 0)
                    throw new TransportException("J2534 adapter reported invalid ISO-TP frame padding.");
                // Preserve unexpected PDU headers for diagnosis without exporting identity or ROM bytes.
                bool omitPayload = request[0] is 9 or 0x23 || frame.Data.Length > 0 && frame.Data[0] == 0x63;
                diagnostics?.Record("j2534.response", new { canId = $"0x{frame.Id:X}", rxStatus = $"0x{frame.Status:X}", bytes = frame.Data.Length,
                    responseHex = omitPayload ? "[payload omitted]" : Convert.ToHexString(frame.Data.AsSpan(0, Math.Min(request[0] == 0x27 ? 4095 : 64, frame.Data.Length))) });
                if (frame.Data.Length is < 1 or > 4095)
                    throw new TransportException("Invalid J2534 response size.");
                packets.Add(frame.Data);
                if (packets.Count > 32)
                    throw new TransportException("Too many pending ECU replies. Reconnect before continuing.");
                if (frame.Data.AsSpan().SequenceEqual(new byte[] { 0x7F, request[0], 0x78 }))
                {
                    // A bounded pending interval extends this exchange, never resends it.
                    limit = pendingTimeout;
                    pendingReplies++;
                    continue;
                }
                completedReplies++;
                return DiagnosticProtocol.FinalResponse(request, packets);
            }
            throw new TransportException($"J2534 response timed out after {watch.Elapsed.TotalMilliseconds:0} ms ({completedReplies} completed replies, {notifications} notifications, {pendingReplies} pending replies). No request was resent; reconnect before continuing.");
        }
        catch (NegativeResponseException) { throw; }
        catch { faulted = true; throw; }
        finally { diagnostics?.Record("j2534.exchange", new { stage = DiagnosticTransport.Describe(request), durationMs = total.Elapsed.TotalMilliseconds, responseWaitMs = watch.Elapsed.TotalMilliseconds,
            polls, emptyPolls, notifications, txEchoes, startNotifications, txIndications, foreignMessages, completedReplies, pendingReplies, driver = channel.ReadDiagnostics }); }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        channel.Dispose();
    }
}
