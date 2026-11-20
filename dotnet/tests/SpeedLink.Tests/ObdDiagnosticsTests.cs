using System.Text;
using System.Text.Json;
using SpeedLink.Core;
using SpeedLink.J2534;

namespace SpeedLink.Tests;

public sealed class ObdDiagnosticsTests
{
    [Theory]
    [InlineData(0x01, 0x33, "P0133")]
    [InlineData(0x11, 0x31, "P1131")]
    [InlineData(0x52, 0x34, "C1234")]
    [InlineData(0xA0, 0x01, "B2001")]
    [InlineData(0xD0, 0x16, "U1016")]
    public void CanCodeDecodingPreservesSystemAndManufacturerDigits(byte a, byte b, string expected)
        => Assert.Equal(expected, ObdProtocol.DecodeCode(a, b));

    [Fact]
    public void CanCountAndPaddingAreCheckedForEmptyShortAndLongCodeLists()
    {
        Assert.Empty(ObdProtocol.Codes(3, [0x43, 0, 0, 0, 0, 0, 0]));
        Assert.Equal("P0133", Assert.Single(ObdProtocol.Codes(3, [0x43, 1, 1, 0x33])).Code);
        var longReply = new byte[] { 0x47, 50 }.Concat(Enumerable.Range(1, 50).SelectMany(i => new byte[] { 1, (byte)i })).ToArray();
        Assert.Equal(50, ObdProtocol.Codes(7, longReply).Count);
        foreach (byte[] malformed in new byte[][] { [0x43], [0x43, 2, 1, 0x33], [0x43, 1, 0, 0], [0x43, 0, 1], [0x47, 0], [0x43, 0, 0, 0, 0, 0, 0, 0] })
            Assert.Throws<InvalidDataException>(() => ObdProtocol.Codes(3, malformed));
    }

    [Fact]
    public void ReadinessSeparatesSupportFromCompletionAndDecodesBothIgnitionTypes()
    {
        var spark = ObdProtocol.Readiness([0x41, 1, 0x83, 0x27, 0xA5, 0x24]);
        Assert.True(spark.MilOn); Assert.Equal(3, spark.StoredCodeCount);
        Assert.Equal("Spark ignition", spark.IgnitionType);
        Assert.False(spark.Monitors.Single(m => m.Name == "Fuel system").Complete);
        Assert.True(spark.Monitors.Single(m => m.Name == "Catalyst").Complete);
        Assert.False(spark.Monitors.Single(m => m.Name == "Evaporative system").Complete);
        Assert.False(spark.Monitors.Single(m => m.Name == "Heated catalyst").Supported);
        Assert.Null(spark.Monitors.Single(m => m.Name == "Heated catalyst").Complete);
        var diesel = ObdProtocol.Readiness([0x41, 1, 0, 0x0F, 0xEB, 0x40]);
        Assert.Equal("Compression ignition", diesel.IgnitionType);
        Assert.False(diesel.Monitors.Single(m => m.Name == "Particulate filter").Complete);
        Assert.DoesNotContain(diesel.Monitors, m => m.Name == "Evaporative system");
        Assert.Throws<InvalidDataException>(() => ObdProtocol.Readiness([0x41, 2, 0, 0, 0, 0]));
    }

    [Fact]
    public void ScanKeepsStatusesDistinctAndReadsOnlyAdvertisedFreezeFrameValues()
    {
        using var peer = new ObdPeer();
        var scan = new ObdDiagnostics(peer).Scan();
        Assert.True(scan.ConnectionUsable);
        Assert.Equal(2, scan.Stored.Value!.Count); Assert.Single(scan.Pending.Value!);
        Assert.Equal(ObdStatus.Unsupported, scan.Permanent.Status); Assert.Null(scan.Permanent.Value);
        Assert.Equal("P0100", scan.FreezeFrame.Value!.TriggerCode);
        Assert.Equal(3, scan.FreezeFrame.Value.Values.Count);
        Assert.Equal(3000, scan.FreezeFrame.Value.Values.Single(v => v.Pid == 12).Value);
        Assert.Equal(85, scan.FreezeFrame.Value.Values.Single(v => v.Pid == 5).Value);
        Assert.Equal(50.19607843137255, scan.FreezeFrame.Value.Values.Single(v => v.Pid == 4).Value);
        Assert.DoesNotContain(peer.Requests, r => r[0] == 4 || r[0] == 2 && r[1] == 0x10);
    }

    [Fact]
    public void MalformedOrMissingRepliesStopFurtherRequestsAndNeverBecomeNoCodes()
    {
        using var peer = new ObdPeer { BreakStored = true };
        var scan = new ObdDiagnostics(peer).Scan();
        Assert.False(scan.ConnectionUsable);
        Assert.Equal(ObdStatus.CommunicationError, scan.Stored.Status);
        Assert.Equal(ObdStatus.NotAttempted, scan.Pending.Status);
        Assert.Equal(2, peer.Requests.Count);
    }

    [Fact]
    public void ClearCapturesBeforeRequiresEngineStoppedSendsOnceAndRescans()
    {
        using var folder = new TempFolder(); using var peer = new ObdPeer();
        var result = new ObdDiagnostics(peer).Clear(folder.Path, true);
        Assert.True(result.Acknowledged); Assert.False(result.OutcomeUncertain); Assert.True(result.ConnectionUsable);
        Assert.Equal(1, peer.Clears); Assert.Empty(result.After!.Stored.Value!);
        using var saved = JsonDocument.Parse(File.ReadAllText(result.BeforeReport));
        Assert.Equal(2, saved.RootElement.GetProperty("scan").GetProperty("Stored").GetProperty("Value").GetArrayLength());
        string journal = File.ReadAllText(result.Journal);
        Assert.True(journal.IndexOf("clear.prepared", StringComparison.Ordinal) < journal.IndexOf("clear.acknowledged", StringComparison.Ordinal));
        Assert.Contains("rescan.saved", journal);
        Assert.Equal(ObdStatus.Unsupported, result.After.Permanent.Status);
        Assert.Equal(2, Directory.GetFiles(folder.Path, "*.json").Length);
    }

    [Fact]
    public void UnconfirmedRunningEngineAndFailedCaptureNeverDispatchClear()
    {
        using var folder = new TempFolder(); using var peer = new ObdPeer();
        var workflow = new ObdDiagnostics(peer);
        Assert.Throws<InvalidOperationException>(() => workflow.Clear(folder.Path, false));
        Assert.Empty(peer.Requests);
        peer.Rpm = 1000;
        Assert.True(Assert.Throws<ObdPreflightException>(() => workflow.Clear(folder.Path, true)).ConnectionUsable);
        Assert.Equal(0, peer.Clears);
        peer.Rpm = 0; string file = Path.Combine(folder.Path, "occupied"); File.WriteAllText(file, "keep");
        Assert.Throws<IOException>(() => workflow.Clear(file, true));
        Assert.Equal(0, peer.Clears); Assert.Equal("keep", File.ReadAllText(file));
    }

    [Fact]
    public void LostAcknowledgementIsUnknownWithoutRetryAndRejectedClearIsDistinct()
    {
        using var folder = new TempFolder(); using var peer = new ObdPeer { LoseAck = true };
        var result = new ObdDiagnostics(peer).Clear(folder.Path, true);
        Assert.False(result.Acknowledged); Assert.True(result.OutcomeUncertain); Assert.False(result.ConnectionUsable);
        Assert.Equal(1, peer.Clears); Assert.Null(result.After); Assert.True(File.Exists(result.BeforeReport));
        peer.LoseAck = false; peer.RejectClear = true;
        result = new ObdDiagnostics(peer).Clear(folder.Path, true);
        Assert.False(result.Acknowledged); Assert.False(result.OutcomeUncertain); Assert.True(result.ConnectionUsable);
        Assert.Equal(2, peer.Clears);
    }

    [Fact]
    public void FailedPreclearScanRequiresReconnectAndNeverSendsClear()
    {
        using var folder = new TempFolder(); using var peer = new ObdPeer { BreakStored = true };
        var error = Assert.Throws<ObdPreflightException>(() => new ObdDiagnostics(peer).Clear(folder.Path, true));
        Assert.False(error.ConnectionUsable); Assert.Equal(0, peer.Clears);
        Assert.Contains("No clear command was sent", error.Message);
    }

    [Fact]
    public void PermanentCodesRemainAfterAcknowledgedClearAndCancellationKeepsAcknowledgement()
    {
        using var folder = new TempFolder(); using var peer = new ObdPeer { PermanentSupported = true };
        var result = new ObdDiagnostics(peer).Clear(folder.Path, true);
        Assert.Equal("P0100", Assert.Single(result.After!.Permanent.Value!).Code);
        using var cancel = new CancellationTokenSource(); peer.AfterClear = cancel.Cancel;
        result = new ObdDiagnostics(peer).Clear(folder.Path, true, cancel.Token);
        Assert.True(result.Acknowledged); Assert.Null(result.After); Assert.Contains("stopped", result.Detail);
    }

    [Fact]
    public void ReadAndLoggerAllowListsStayReadOnlyAndObdAllowsOnlyItsExactCommands()
    {
        foreach (byte[] request in new byte[][] { [4], [3], [7], [0x0A], [1, 1], [2, 2, 0] })
            Assert.Throws<TransportException>(() => DiagnosticProtocol.CheckReadOnly(request));
        Assert.Throws<TransportException>(() => ObdProtocol.CheckAllowed([4]));
        foreach (byte[] request in new byte[][] { [4, 0], [0x14, 0xFF, 0xFF, 0xFF], [0x10, 2], [0x34], [0x36, 1], [0x23, 0, 0, 0, 0, 0, 1], [2, 2, 1], [1, 0xFF] })
            Assert.Throws<TransportException>(() => ObdProtocol.CheckAllowed(request, true));
        ObdProtocol.CheckAllowed([4], true);
    }

    [Fact]
    public void ReportExportPreservesExistingFilesAndNeedsNoRom()
    {
        using var folder = new TempFolder(); using var peer = new ObdPeer();
        string path = Path.Combine(folder.Path, "report.json");
        ObdReports.SaveNew(path, new ObdDiagnostics(peer).Scan()); byte[] before = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => ObdReports.SaveNew(path, new ObdDiagnostics(peer).Scan()));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.DoesNotContain("rom.bin", Encoding.UTF8.GetString(before), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescriptionsUseMatchingDefinitionsAndPreserveUnknownCodeNumbers()
    {
        var describe = ObdDescriptions.From(TuningDefinition.Known, "L38KEG000L3T6021");
        Assert.NotNull(describe("P0012")); Assert.DoesNotContain("DTC Reporting", describe("P0012")!);
        Assert.Null(describe("P3FFF"));
        Assert.Equal("P3FFF", Assert.Single(ObdProtocol.Codes(3, [0x43, 1, 0x3F, 0xFF], describe)).Code);
    }

    [Fact]
    public void J2534ObdHandlesPendingAndLongRepliesWithExplicitClearPermission()
    {
        using var channel = new ObdChannel(); using var transport = new J2534ReadTransport(channel);
        transport.Initialize(); int before = channel.Writes;
        Assert.Throws<TransportException>(() => transport.Exchange([4]));
        Assert.Throws<TransportException>(() => transport.ExchangeObd([4])); Assert.Equal(before, channel.Writes);
        Assert.Equal(50, ObdProtocol.Codes(3, transport.ExchangeObd([3])).Count);
        Assert.Equal(new byte[] { 0x44 }, transport.ExchangeObd([4], true));
        Assert.Equal(2, channel.Writes);
    }

    [Fact]
    public void VLinkerObdHandlesLongCodesAndKeepsOrdinaryExchangeRestrictions()
    {
        var clock = new TestClock(); using var serial = new ObdSerial(clock);
        using var transport = new VLinkerReadTransport(serial, clock, VLinkerReadTransport.DefaultBaud);
        transport.Initialize(); int before = serial.Commands.Count;
        Assert.Throws<TransportException>(() => transport.ExchangeObd([4]));
        Assert.Throws<TransportException>(() => transport.Exchange([3])); Assert.Equal(before, serial.Commands.Count);
        Assert.Equal(50, ObdProtocol.Codes(3, transport.ExchangeObd([3])).Count);
        Assert.Equal(new byte[] { 0x44 }, transport.ExchangeObd([4], true));
        Assert.Equal(1, serial.Commands.Count(c => c == "04"));
    }

    [Fact]
    public void NativeDriverHasAnIndependentObdGateBeforeAnyDriverIo()
    {
        using var native = (NativeJ2534Channel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(NativeJ2534Channel));
        Assert.Throws<TransportException>(() => native.Write([1, 1]));
        Assert.Throws<TransportException>(() => native.WriteObd([4], false));
        Assert.Throws<TransportException>(() => native.WriteObd([0x34], true));
        Assert.Contains("not ready", Assert.Throws<TransportException>(() => native.WriteObd([1, 1], false)).Message);
        Assert.Contains("not ready", Assert.Throws<TransportException>(() => native.WriteObd([4], true)).Message);
        Assert.Equal(0, native.WriteRequests);
    }

    public sealed class ObdPeer : IObdTransport
    {
        public string Mode => "synthetic"; public string Adapter => "Synthetic OBD ECU";
        public int ReadChunkSize => 256; public bool ReadAttempted { get; set; }
        public List<byte[]> Requests { get; } = []; public int Clears { get; private set; }
        public bool BreakStored, LoseAck, RejectClear, PermanentSupported; public int Rpm; public Action? AfterClear;
        public byte[] Exchange(byte[] request) { DiagnosticProtocol.CheckReadOnly(request); throw new NotSupportedException(); }
        public byte[] ExchangeObd(byte[] request, bool clearConfirmed = false)
        {
            ObdProtocol.CheckAllowed(request, clearConfirmed); Requests.Add(request.ToArray());
            if (request.AsSpan().SequenceEqual(new byte[] { 1, 1 })) return [0x41, 1, (byte)(Clears == 0 ? 0x82 : 0), 7, 0xA5, (byte)(Clears == 0 ? 0 : 0xA5)];
            if (request.AsSpan().SequenceEqual(new byte[] { 1, 12 })) return [0x41, 12, (byte)(Rpm * 4 >> 8), (byte)(Rpm * 4)];
            if (request[0] == 3) { if (BreakStored) return [0x43, 4, 1]; return Clears == 0 ? [0x43, 2, 1, 0, 1, 0x22] : [0x43, 0]; }
            if (request[0] == 7) return Clears == 0 ? [0x47, 1, 1, 0x22] : [0x47, 0];
            if (request[0] == 10) { if (!PermanentSupported) throw new NegativeResponseException(10, 0x11); return [0x4A, 1, 1, 0]; }
            if (request[0] == 2) return request[1] switch { 0 => [0x42, 0, 0, 0x58, 0x10, 0, 0], 2 => Clears == 0 ? [0x42, 2, 0, 1, 0] : [0x42, 2, 0, 0, 0], 4 => [0x42, 4, 0, 128], 5 => [0x42, 5, 0, 125], 12 => [0x42, 12, 0, 0x2E, 0xE0], _ => throw new NegativeResponseException(2, 0x31) };
            if (request[0] == 4) { Clears++; if (RejectClear) throw new NegativeResponseException(4, 0x22); if (LoseAck) throw new TransportException("Acknowledgement lost"); AfterClear?.Invoke(); return [0x44]; }
            throw new NotSupportedException();
        }
        public void Dispose() { }
    }
    private sealed class ObdChannel : IJ2534Channel
    {
        private readonly Queue<byte[]> replies = []; public int Writes; public string? CleanupWarning => null;
        public J2534Version Open() => new("synthetic", "synthetic", "04.04");
        public void Connect(uint bitrate, uint requestId, uint responseId) { Assert.Equal(0x7E0u, requestId); Assert.Equal(0x7E8u, responseId); }
        public void ClearReceive() => replies.Clear();
        public void Write(byte[] request)
        {
            Writes++;
            if (request[0] == 3) replies.Enqueue(new byte[] { 0x43, 50 }.Concat(Enumerable.Range(1, 50).SelectMany(i => new byte[] { 1, (byte)i })).ToArray());
            else { replies.Enqueue([0x7F, 4, 0x78]); replies.Enqueue([0x44]); }
        }
        public J2534Frame? Read(int timeoutMilliseconds) => replies.TryDequeue(out var p) ? new(0x7E8, 0, p) : null;
        public void Dispose() { }
    }
    private sealed class TestClock : IMonotonicClock
    { public TimeSpan Elapsed { get; private set; } public void Sleep(int milliseconds) => Elapsed += TimeSpan.FromMilliseconds(milliseconds); }
    private sealed class ObdSerial(TestClock clock) : ISerialConnection
    {
        private readonly Queue<byte> received = []; public List<string> Commands { get; } = [];
        public bool IsOpen => true; public int BaudRate { get; set; } = 115200; public int BytesToRead => received.Count;
        public int Read(byte[] buffer, int offset, int count) { clock.Sleep(1); int n = Math.Min(count, received.Count); for (int i = 0; i < n; i++) buffer[offset + i] = received.Dequeue(); return n; }
        public int Write(byte[] data)
        {
            string command = Encoding.ASCII.GetString(data).Trim(); Commands.Add(command);
            string reply = command switch { "ATI" => "ELM327 v2.2", "VTI" => "vLinker FS v2.2", "03" => Convert.ToHexString(new byte[] { 0x43, 50 }.Concat(Enumerable.Range(1, 50).SelectMany(i => new byte[] { 1, (byte)i })).ToArray()), "04" => "44", _ => command.StartsWith("STPX", StringComparison.Ordinal) ? "DATA" : "OK" };
            foreach (byte b in Encoding.ASCII.GetBytes(reply + "\r>")) received.Enqueue(b); return data.Length;
        }
        public void Flush() { } public void DiscardInput() => received.Clear(); public void Dispose() { }
    }
}
