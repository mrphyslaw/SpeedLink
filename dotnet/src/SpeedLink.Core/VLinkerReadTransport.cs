using System.Diagnostics;
using System.Text;

namespace SpeedLink.Core;

public interface ISerialConnection : IDisposable
{
    bool IsOpen
    {
        get;
    }
    int BaudRate
    {
        get; set;
    }
    int BytesToRead
    {
        get;
    }
    int Read(byte[] buffer, int offset, int count);
    int Write(byte[] data);
    void Flush();
    void DiscardInput();
}
public interface IMonotonicClock
{
    TimeSpan Elapsed
    {
        get;
    }
    void Sleep(int milliseconds);
}
public sealed class MonotonicClock : IMonotonicClock
{
    private readonly Stopwatch timer = Stopwatch.StartNew();
    public TimeSpan Elapsed => timer.Elapsed;
    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
}
public class ElmTransport : IReadTransport
{
    protected readonly ISerialConnection Serial;
    protected readonly IMonotonicClock Clock;
    public string Mode => "hardware";
    public string Adapter { get; protected set; } = "";
    public virtual int ReadChunkSize => 256;
    public bool ReadAttempted
    {
        get; set;
    }
    public bool Ready
    {
        get; protected set;
    }
    public bool Failed
    {
        get; protected set;
    }
    public ElmTransport(ISerialConnection serial, IMonotonicClock? clock = null)
    {
        Serial = serial;
        Clock = clock ?? new MonotonicClock();
    }
    protected void WriteExact(byte[] data)
    {
        if (Serial.Write(data) != data.Length)
            throw new TransportException("Incomplete serial write.");
        Serial.Flush();
    }
    protected string Prompt(double seconds, int maxBytes = 32768)
    {
        using var response = new MemoryStream();
        var deadline = Clock.Elapsed + TimeSpan.FromSeconds(seconds);
        var buffer = new byte[16384];
        while (Clock.Elapsed < deadline)
        {
            var count = Serial.Read(buffer, 0, Math.Max(1, Math.Min(Serial.BytesToRead, buffer.Length)));
            response.Write(buffer, 0, count);
            if (response.Length > maxBytes)
                throw new TransportException("Adapter response exceeded the receive limit.");
            var raw = response.GetBuffer().AsSpan(0, (int)response.Length);
            var at = raw.IndexOf((byte)'>');
            if (at >= 0)
            {
                if (raw[(at + 1)..].ToArray().Any(c => !char.IsWhiteSpace((char)c)))
                    throw new TransportException("Unexpected data after adapter prompt.");
                if (raw[..at].ToArray().Any(c => c > 127))
                    throw new TransportException("Non-ASCII adapter reply.");
                return Encoding.ASCII.GetString(raw[..at]);
            }
        }
        throw new TransportException("Adapter timeout. Command was sent once; reconnect before continuing.");
    }
    public string Command(string command, double timeout = 4)
    {
        if (Failed)
            throw new TransportException("Connection is no longer synchronized. Disconnect and reconnect.");
        try
        {
            WriteExact(Encoding.ASCII.GetBytes(command + "\r"));
            return Prompt(timeout);
        }
        catch { Failed = true; throw; }
    }
    public virtual void Initialize()
    {
        Command("ATZ", 5);
        Adapter = Command("ATI").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (Adapter.Length == 0 || Adapter.Contains('?'))
            throw new TransportException("Adapter identification failed.");
        foreach (var command in new[] { "ATE0", "ATL0", "ATS0", "ATH0", "ATSP6", "ATCAF1", "ATCFC1", "ATSH7E0", "ATCRA7E8", "ATAT0", "ATSTFF" })
        {
            var lines = Command(command).Replace('\n', '\r').Split('\r', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x != command).ToArray();
            if (lines.Length != 1 || lines[0].ToUpperInvariant() != "OK")
            {
                Failed = true;
                throw new TransportException(command + " was not accepted.");
            }
        }
        Ready = true;
    }
    public virtual byte[] Exchange(byte[] request)
    {
        DiagnosticProtocol.CheckReadOnly(request);
        if (!Ready)
            throw new TransportException("Initialize the adapter first.");
        try
        {
            var command = Convert.ToHexString(request);
            return DiagnosticProtocol.FinalResponse(request, DiagnosticProtocol.ParseElm(Command(command, 5), command));
        }
        catch (NegativeResponseException) { throw; }
        catch { Failed = true; throw; }
    }
    public virtual void Dispose()
    {
        Ready = false;
        Serial.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed class VLinkerReadTransport : ElmTransport
{
    // Host USB-UART rate. ATSP6 configures the separate 500 kbit/s CAN link.
    public const int TransferBaud = 2000000;
    public int ConfiguredTransferBaud
    {
        get;
    }
    public override int ReadChunkSize => 3968;
    public int Baud => Serial.BaudRate;
    public string? CloseWarning
    {
        get; private set;
    }
    public VLinkerReadTransport(ISerialConnection serial, IMonotonicClock? clock = null, int transferBaud = TransferBaud) : base(serial, clock)
    {
        if (transferBaud is not (115200 or 230400 or 500000 or 1000000 or 2000000))
            throw new ArgumentOutOfRangeException(nameof(transferBaud), "Select a supported serial transfer rate.");
        ConfiguredTransferBaud = transferBaud;
    }
    public override void Initialize()
    {
        try
        {
            Serial.DiscardInput();
            Command("ATI");
            Command("ATE0");
            var firmware = Command("VTI").Trim();
            if (!firmware.StartsWith("vLinker FS ", StringComparison.Ordinal))
                throw new TransportException("SpeedLink requires a Vgate vLinker FS.");
            foreach (var command in new[] { "ATL0", "ATS0", "ATH0", "ATSP6", "ATCAF1", "ATCFC1", "ATSH7E0", "ATCRA7E8", "ATAT0", "ATST32", "ATFCSM0", "STCSEGT1", "STCSEGR1" })
                if (Command(command).Trim() != "OK")
                    throw new TransportException(command + " was not accepted.");
            SetBaud(ConfiguredTransferBaud);
            Adapter = firmware + " · segmented read";
            Ready = true;
        }
        catch { Failed = true; Ready = false; throw; }
    }
    private void SetBaud(int baud)
    {
        if (baud == Serial.BaudRate)
            return;
        if (Command("STBRT100").Trim() != "OK")
            throw new TransportException("Adapter rejected baud-switch timeout.");
        WriteExact(Encoding.ASCII.GetBytes("STSBR " + baud + "\r"));
        using var response = new MemoryStream();
        var buffer = new byte[4096];
        var deadline = Clock.Elapsed + TimeSpan.FromSeconds(2);
        while (Clock.Elapsed < deadline)
        {
            var count = Serial.Read(buffer, 0, Math.Max(1, Math.Min(Serial.BytesToRead, buffer.Length)));
            response.Write(buffer, 0, count);
            var text = Encoding.ASCII.GetString(response.GetBuffer(), 0, (int)response.Length);
            if (text.Contains("OK", StringComparison.Ordinal) || text.Contains('?'))
                break;
        }
        if (!Encoding.ASCII.GetString(response.ToArray()).Contains("OK", StringComparison.Ordinal))
            throw new TransportException("Adapter rejected temporary baud switch.");
        Serial.BaudRate = baud;
        Clock.Sleep(250);
        Serial.DiscardInput();
        WriteExact("STI\r"u8.ToArray());
        // The baud transition can emit an extra adapter prompt before STI.
        // Diagnostic exchanges retain the strict single-prompt parser above.
        using var identity = new MemoryStream();
        deadline = Clock.Elapsed + TimeSpan.FromSeconds(3);
        while (Clock.Elapsed < deadline)
        {
            var count = Serial.Read(buffer, 0, Math.Max(1, Math.Min(Serial.BytesToRead, buffer.Length)));
            identity.Write(buffer, 0, count);
            if (identity.Length > 4096)
                throw new TransportException("Baud identity reply exceeded the receive limit.");
            var raw = identity.ToArray();
            if (raw.Any(value => value > 127))
                throw new TransportException("Non-ASCII baud identity reply.");
            var reply = Encoding.ASCII.GetString(raw);
            if (reply.Contains("STN", StringComparison.Ordinal) && reply.TrimEnd().EndsWith('>'))
            {
                var lines = reply.Replace('>', '\r').Replace('\n', '\r').Split('\r', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (lines.Count(line => line.StartsWith("STN", StringComparison.Ordinal)) != 1 || lines.Any(line => line != "STI" && !line.StartsWith("STN", StringComparison.Ordinal)))
                    throw new TransportException("Unexpected baud identity content.");
                return;
            }
        }
        throw new TransportException("Baud switch did not return a synchronized identity reply.");
    }
    public override byte[] Exchange(byte[] request)
    {
        DiagnosticProtocol.CheckReadOnly(request, ReadChunkSize);
        if (!Ready || Failed)
            throw new TransportException("Initialize a fresh connection before reading.");
        try
        {
            WriteExact(Encoding.ASCII.GetBytes($"STPX L:{request.Length},T:200,R:1\r"));
            if (Prompt(3, 65536).Trim().ToUpperInvariant() != "DATA")
                throw new TransportException("Adapter did not request the read payload.");
            WriteExact(Encoding.ASCII.GetBytes(Convert.ToHexString(request) + "\r"));
            return DiagnosticProtocol.FinalResponse(request, DiagnosticProtocol.ParseSegmented(Prompt(6, 65536)));
        }
        catch (NegativeResponseException) { throw; } // A complete NRC reply retains framing synchronization.
        catch { Failed = true; throw; }
    }
    public override void Dispose()
    {
        try
        {
            if (Serial.IsOpen && !Failed)
                try
                {
                    SetBaud(115200);
                }
                catch (Exception error) { CloseWarning = error.Message; }
        }
        finally { base.Dispose(); }
    }
}
