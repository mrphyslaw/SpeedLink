using System.Diagnostics;
using SpeedLink.Core;

namespace SpeedLink.J2534;

public sealed record J2534Version(string Firmware, string Driver, string Api);
public sealed record J2534Frame(uint Id, uint Status, byte[] Data);

/// <summary>A single physical ISO 15765 channel; implementations must not retry requests.</summary>
public interface IJ2534Channel : IDisposable
{
    J2534Version Open();
    void Connect(uint bitrate, uint requestId, uint responseId);
    void ClearReceive();
    void Write(byte[] request);
    J2534Frame? Read(int timeoutMilliseconds);
    string? CleanupWarning { get; }
}

/// <summary>Read-only Mazda diagnostics through an installed J2534 driver.</summary>
public sealed class J2534ReadTransport : IReadTransport
{
    private readonly IJ2534Channel channel;
    private readonly TimeSpan responseTimeout;
    private readonly TimeSpan pendingTimeout;
    private bool initialized, disposed, faulted;
    public string Mode => "hardware";
    public string Adapter { get; private set; } = "Tactrix Openport 2.0";
    public int ReadChunkSize => 3968;
    public bool ReadAttempted { get; set; }
    public string? CloseWarning => channel.CleanupWarning;

    public J2534ReadTransport(IJ2534Channel channel, TimeSpan? responseTimeout = null, TimeSpan? pendingTimeout = null)
    {
        this.channel = channel;
        this.responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(10);
        this.pendingTimeout = pendingTimeout ?? TimeSpan.FromSeconds(30);
        if (this.responseTimeout <= TimeSpan.Zero || this.pendingTimeout < this.responseTimeout)
            throw new ArgumentOutOfRangeException(nameof(responseTimeout));
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (initialized || faulted)
            throw new TransportException("Reconnect the Tactrix before initializing again.");
        try
        {
            var version = channel.Open();
            channel.Connect(500000, 0x7E0, 0x7E8);
            Adapter = $"Tactrix Openport 2.0 · firmware {version.Firmware}";
            initialized = true;
        }
        catch { faulted = true; Dispose(); throw; }
    }

    public byte[] Exchange(byte[] request)
    {
        // Validate before touching the driver, including clearing its receive queue.
        DiagnosticProtocol.CheckReadOnly(request, ReadChunkSize);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!initialized || faulted)
            throw new TransportException("Reconnect and identify before continuing.");
        try
        {
            channel.ClearReceive();
            channel.Write(request);
            var watch = Stopwatch.StartNew();
            var limit = responseTimeout;
            var packets = new List<byte[]>();
            while (watch.Elapsed < limit)
            {
                var remaining = Math.Max(1, (int)(limit - watch.Elapsed).TotalMilliseconds);
                var frame = channel.Read(Math.Min(25, remaining));
                if (frame is null)
                    continue;
                // J2534 Tx echoes, start notifications and Tx indications are not PDUs.
                if ((frame.Status & 0x0B) != 0 || frame.Id != 0x7E8)
                    continue;
                if ((frame.Status & 0x10) != 0)
                    throw new TransportException("Tactrix reported invalid ISO-TP frame padding.");
                if (frame.Data.Length is < 1 or > 4095)
                    throw new TransportException("Invalid J2534 response size.");
                packets.Add(frame.Data);
                if (packets.Count > 32)
                    throw new TransportException("Too many pending ECU replies. Reconnect before continuing.");
                if (frame.Data.AsSpan().SequenceEqual(new byte[] { 0x7F, request[0], 0x78 }))
                {
                    // A bounded pending interval extends this exchange, never resends it.
                    limit = pendingTimeout;
                    continue;
                }
                return DiagnosticProtocol.FinalResponse(request, packets);
            }
            throw new TransportException("Tactrix response timed out. No request was resent; reconnect before continuing.");
        }
        catch (NegativeResponseException) { throw; }
        catch { faulted = true; throw; }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        channel.Dispose();
    }
}
