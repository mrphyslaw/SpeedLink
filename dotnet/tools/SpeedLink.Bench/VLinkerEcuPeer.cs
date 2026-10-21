using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SpeedLink.Core;
using SpeedLink.Hardware;

// Developer bench only. This is not linked into the desktop application.
// The only transmitted CAN ID is 7E8; all ROM bytes live in host RAM.
sealed class VLinkerEcuPeer(string port, byte[] memory, string reportPath, string fault = "", LoggerBenchData? logger = null)
{
    private readonly StringBuilder input = new();
    private readonly byte[] buffer = new byte[4096];
    private FtdiSerialConnection? serial;
    private VLinkerReadTransport? setup;
    private bool monitoring, session, unlocked;
    private int requests, responses, memoryReads;
    private bool injected;
    private byte[]? nextRequest;
    private byte[]? currentRequest;
    private int discardedMonitorEchoes;
    private readonly byte[] seed = [0x12, 0x34, 0x56];
    private string? stopFile;
    private StreamWriter? trace;
    private string? error;
    private bool Stopping => stopFile is not null && File.Exists(stopFile);

    public object Run(string stopPath)
    {
        if (string.IsNullOrWhiteSpace(stopPath) || File.Exists(stopPath))
            throw new ArgumentException("Specify a fresh --stop-file for the independent vLinker peer.");
        if (memory.Length != RomInspector.RomSize)
            throw new ArgumentException("The bench peer requires exactly 1 MiB of RAM data.");
        stopFile = Path.GetFullPath(stopPath);
        trace = new StreamWriter(reportPath + ".requests.jsonl", append: false);
        try
        {
            serial = new(port, 115200);
            Thread.Sleep(150);
            serial.DiscardInput();
            setup = new(serial);
            setup.Initialize();
            foreach (var command in new[] { "ATSH7E8", "ATCRA7E0", "ATCAF0", "ATH1", "ATCFC0", "ATCSM0", "STCMM1", "STCSEGT0", "STCSEGR0", "ATR0", "ATST32" })
                Command(command, "OK");
            Arm();
            File.WriteAllText(reportPath + ".ready.json", JsonSerializer.Serialize(new
            {
                ready = true, role = "ECU RAM peer", port, baud = serial.BaudRate, latencyMs = 1,
                requestID = "7E0", responseID = "7E8", sha256 = RomInspector.Hash(memory),
                realEcuAttached = false, fault
            }));
            while (!Stopping)
            {
                var request = WaitRequest();
                if (request is null)
                    break;
                requests++;
                currentRequest = request;
                var response = Handle(request);
                if (response is not null)
                {
                    if (fault == "pending-read" && request[0] == 0x23 && !injected)
                    {
                        injected = true;
                        Send([0x7F, 0x23, 0x78]);
                        Thread.Sleep(100);
                    }
                    Send(response);
                    responses++;
                }
                trace.WriteLine(JsonSerializer.Serialize(new
                {
                    request = Convert.ToHexStringLower(request), responseBytes = response?.Length ?? 0,
                    responseSHA256 = response is null ? null : RomInspector.Hash(response), time = DateTimeOffset.UtcNow
                }));
                trace.Flush();
                Arm();
            }
        }
        catch (Exception exception) { error = exception.Message; throw; }
        finally
        {
            try
            {
                if (monitoring && serial?.IsOpen == true)
                {
                    Write("\r");
                    Prompt(2);
                    monitoring = false;
                }
            }
            catch (Exception exception) { error ??= exception.Message; }
            setup?.Dispose();
            serial?.Dispose();
            trace?.Dispose();
            File.WriteAllText(reportPath + ".peer.json", JsonSerializer.Serialize(Summary(), new JsonSerializerOptions { WriteIndented = true }));
        }
        return Summary();
    }
    private object Summary() => new
    {
        requests, responses, memoryReads, error, discardedMonitorEchoes, programmingRequestsSent = 0,
        finalBaud = serial?.BaudRate, originalLatency = serial?.OriginalLatency,
        latencyRestored = serial?.LatencyRestored, driverErrors = serial?.DriverErrors,
        cleanupWarning = serial?.CleanupWarning ?? setup?.CloseWarning,
        handleClosed = serial is null || !serial.IsOpen
    };
    private void Write(string text)
    {
        var data = Encoding.ASCII.GetBytes(text);
        if (serial!.Write(data) != data.Length)
            throw new IOException("Incomplete bench peer serial write.");
        serial.Flush();
    }
    private void ReadAvailable()
    {
        int available = serial!.BytesToRead;
        // A one-byte bounded D2XX read waits for the USB receive event without
        // relying on Windows' coarse Thread.Sleep timer between CAN frames.
        int count = serial.Read(buffer, 0, Math.Max(1, Math.Min(buffer.Length, available)));
        if (buffer.AsSpan(0, count).ContainsAnyInRange((byte)128, byte.MaxValue))
            throw new IOException("Non-ASCII bench serial output.");
        input.Append(Encoding.ASCII.GetString(buffer, 0, count));
        if (input.Length > 65536)
            throw new IOException("Bench peer serial output exceeded its bound.");
    }
    private string Prompt(int seconds = 3)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < seconds)
        {
            int at = input.ToString().IndexOf('>');
            if (at >= 0)
            {
                var result = input.ToString(0, at);
                input.Remove(0, at + 1);
                if (result.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || result.Contains('?') || result.Contains("BUFFER FULL", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("vLinker ECU peer: " + result.Trim());
                return result;
            }
            ReadAvailable();
        }
        throw new TimeoutException("vLinker ECU peer prompt timed out.");
    }
    private string Command(string text, string? expected = null)
    {
        Write(text + "\r");
        string reply;
        try { reply = Prompt(); }
        catch (TimeoutException exception) { throw new TimeoutException("vLinker peer timed out during " + text[..Math.Min(48, text.Length)] + ".", exception); }
        if (expected is not null && reply.Trim() != expected)
            throw new IOException(text + " was not acknowledged: " + reply.Trim());
        return reply;
    }
    private void Arm()
    {
        if (nextRequest is not null)
            return;
        Write("ATMA\r");
        monitoring = true;
    }
    private byte[]? WaitRequest()
    {
        if (nextRequest is not null)
        {
            var captured = nextRequest;
            nextRequest = null;
            return captured;
        }
        while (!Stopping)
        {
            var current = input.ToString();
            int end = current.IndexOfAny(['\r', '\n']);
            if (end < 0)
            {
                if (current.Contains('>'))
                    throw new IOException("The bench monitor stopped unexpectedly: " + current.Trim());
                ReadAvailable();
                continue;
            }
            var line = string.Concat(current[..end].Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
            input.Remove(0, end + 1);
            if (!line.StartsWith("7E0", StringComparison.Ordinal) || line.Length < 7)
                continue;
            var frame = Convert.FromHexString(line[3..]);
            if (frame.Length is < 2 or > 8 || frame[0] is < 1 or > 7 || frame.Length < frame[0] + 1)
                throw new IOException("Unexpected tester frame on the isolated bench.");
            Write("\r"); // Exit only the known active monitor; never repeat a command.
            Prompt(2);
            monitoring = false;
            return frame.AsSpan(1, frame[0]).ToArray();
        }
        return null;
    }
    private void CaptureNext(string reply)
    {
        File.AppendAllText(reportPath + ".raw-can.jsonl", JsonSerializer.Serialize(new { reply, queued = nextRequest is null ? null : Convert.ToHexString(nextRequest) }) + "\n");
        var parsed = ParseNextRequest(reply, currentRequest ?? []);
        if (parsed.DiscardedEcho) discardedMonitorEchoes++;
        if (parsed.Request is null) return;
        if (nextRequest is not null) throw new IOException("Multiple requests arrived during one bench response.");
        nextRequest = parsed.Request;
    }
    internal static (byte[]? Request, bool DiscardedEcho) ParseNextRequest(string reply, byte[] current)
    {
        var frames = new List<byte[]>();
        foreach (var text in reply.Replace('\n', '\r').Split('\r'))
        {
            var line = string.Concat(text.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
            if (!line.StartsWith("7E0", StringComparison.Ordinal) || line.Length < 7)
                continue;
            var frame = Convert.FromHexString(line[3..]);
            if (frame.Length is < 2 or > 8 || frame[0] is < 1 or > 7 || frame.Length < frame[0] + 1)
                throw new IOException("Unexpected frame while waiting for the next bench request.");
            frames.Add(frame.AsSpan(1, frame[0]).ToArray());
        }
        // This firmware can replay the request already consumed by ATMA before
        // the next request in the same UART reply. Only remove that exact leading
        // copy when another frame follows. A single identical next request is valid.
        bool echo = frames.Count == 2 && frames[0].AsSpan().SequenceEqual(current);
        if (echo) frames.RemoveAt(0);
        if (frames.Count > 1) throw new IOException("Multiple requests arrived during one bench response.");
        return (frames.SingleOrDefault(), echo);
    }
    private void Send(byte[] response)
    {
        if (response.Length is < 1 or > 4095)
            throw new IOException("Invalid bench response length.");
        if (response.Length <= 7)
        {
            var frame = new byte[8];
            frame[0] = (byte)response.Length;
            response.CopyTo(frame, 1);
            Command("ATR1", "OK");
            CaptureNext(Command(Convert.ToHexString(frame) + "1"));
            return;
        }
        Command("ATR1", "OK");
        var first = new byte[8];
        first[0] = (byte)(0x10 | (response.Length >> 8));
        first[1] = (byte)response.Length;
        response.AsSpan(0, 6).CopyTo(first.AsSpan(2));
        var raw = Command(Convert.ToHexString(first) + "1");
        var line = raw.Replace('\n', '\r').Split('\r').Select(text => string.Concat(text.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant())
            .FirstOrDefault(text => text.StartsWith("7E030", StringComparison.Ordinal));
        if (line is null)
            throw new IOException("Tactrix flow-control reply missing: " + raw.Trim());
        var fc = Convert.FromHexString(line[3..]);
        if (fc.Length < 3 || fc[1] != 0 || (fc[2] > 127 && fc[2] is not (>= 0xF1 and <= 0xF9)))
            throw new IOException("Unsupported flow-control parameters on this bench peer.");
        double separationMs = fc[2] <= 127 ? fc[2] : (fc[2] - 0xF0) / 10.0;
        Command("ATR0", "OK");
        var frames = new List<string>();
        for (int offset = 6, index = 1; offset < response.Length; offset += 7, index++)
        {
            var frame = new byte[8];
            frame[0] = (byte)(0x20 | (index & 15));
            response.AsSpan(offset, Math.Min(7, response.Length - offset)).CopyTo(frame.AsSpan(1));
            frames.Add(Convert.ToHexString(frame));
        }
        // The tested firmware requires one command per prompt. UART command
        // pipelining is not assumed even when the bytes fit its receive buffer.
        for (int index = 0; index < frames.Count;)
        {
            if (index == frames.Count - 1)
            {
                // Remain in receive mode after the final frame. A fast tester
                // can issue its next request before UART returns to ATMA.
                Command("ATR1", "OK");
                CaptureNext(Command(frames[index] + "1"));
            }
            else
                Command(frames[index]);
            index++;
            if (separationMs > 0)
                Thread.Sleep(TimeSpan.FromMilliseconds(separationMs));
        }
    }
    private byte[]? Handle(byte[] request)
    {
        if (request.Length == 3 && request[0] == 0x22 && logger is not null) return logger.Handle(request, fault);
        if (request.AsSpan().SequenceEqual(new byte[] { 9, 2 }))
            return [0x49, 2, 1, .. "SLBENCH0000000001"u8.ToArray()];
        if (request.AsSpan().SequenceEqual(new byte[] { 9, 4 }))
        {
            var calibration = new RomInspector().Inspect(memory).Calibration ?? "UNKNOWN";
            return [0x49, 4, 1, .. Encoding.ASCII.GetBytes(calibration)];
        }
        if (request.AsSpan().SequenceEqual(new byte[] { 1, 12 }))
            return [0x41, 12, 0, 0];
        if (request.AsSpan().SequenceEqual(new byte[] { 0x10, 0x87 }))
        {
            session = true;
            unlocked = false;
            return [0x50, 0x87];
        }
        if (request.AsSpan().SequenceEqual(new byte[] { 0x27, 1 }))
            return [0x67, 1, .. seed];
        if (request.Length == 5 && request[0] == 0x27 && request[1] == 2)
        {
            unlocked = session && request.AsSpan(2).SequenceEqual(SecurityAccess.SeedKey(seed));
            return unlocked ? [0x67, 2] : [0x7F, 0x27, 0x35];
        }
        if (request.Length == 7 && request[0] == 0x23)
        {
            if (!session || !unlocked)
                return [0x7F, 0x23, 0x33];
            uint address = BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(1, 4));
            int count = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(5, 2));
            if (count < 1 || count > 4094 || (ulong)address + (uint)count > (uint)memory.Length)
                return [0x7F, 0x23, 0x31];
            memoryReads++;
            if (fault == "drop-read")
                return null;
            if (fault == "negative-read")
                return [0x7F, 0x23, 0x31];
            var result = new byte[count + 1];
            result[0] = 0x63;
            memory.AsSpan((int)address, count).CopyTo(result.AsSpan(1));
            if (fault == "short-read")
                return result[..^1];
            if (fault == "pass-mismatch" && memoryReads > (RomInspector.RomSize + 3967) / 3968)
                result[1] ^= 1;
            return result;
        }
        throw new IOException("Unexpected tester service reached the read-only bench peer: " + Convert.ToHexString(request));
    }
}
