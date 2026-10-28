using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using SpeedLink.Core;
using SpeedLink.J2534;

namespace SpeedLink.Tests;

public sealed class DiagnosticsTests : IDisposable
{
    [Fact]
    public async Task Full_default_segments_stay_within_encoded_byte_limit_and_export()
    {
        await using var session = Session();
        for (int i = 0; i < 20000; i++)
        {
            session.Record("test.short", new { counter = i });
            if (i % 250 == 0) await session.FlushAsync();
        }
        await session.FlushAsync();
        var files = Directory.GetFiles(session.Folder, "events-*.jsonl");
        Assert.True(files.Length >= 2); Assert.Equal(0, session.DroppedEvents); Assert.Null(session.StorageError);
        Assert.All(files, p => { using var input = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); Assert.InRange(input.Length, 1, 2 * 1024 * 1024); });
        Assert.Contains("test.short", Export(session));
    }

    private readonly string vault = Path.Combine(Path.GetTempPath(), "speedlink-diagnostics-test-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(vault, "diagnostics");
    private DiagnosticSession Session(DiagnosticLimits? limits = null) => new(Root, "test-alpha", "test-build", limits);
    private static string Events(DiagnosticSession session) => string.Join('\n', Directory.GetFiles(session.Folder, "events-*.jsonl").Order().Select(path => { using var text = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)); return text.ReadToEnd(); }));
    private string Export(DiagnosticSession session)
    {
        string path = Path.Combine(vault, Guid.NewGuid() + ".zip"); SupportReport.Export(session.Folder, path);
        using var zip = ZipFile.OpenRead(path);
        return string.Join('\n', zip.Entries.Select(e => { using var text = new StreamReader(e.Open()); return text.ReadToEnd(); }));
    }
    [Fact]
    public async Task seed_key_requests_and_replies_survive_capture_and_export()
    {
        await using var session = Session();
        using var peer = new Peer(); using var link = new DiagnosticTransport(peer, session);
        Assert.Equal(new byte[] { 0x67, 1, 0x12, 0x34, 0x56 }, link.Exchange([0x27, 1]));
        link.Exchange([0x27, 2, 0xAB, 0xCD, 0xEF]);
        session.Record("security.context", new { seed = "123456", key = "ABCDEF", securityKey = "ABCDEF" });
        await session.FlushAsync();
        string report = Export(session);
        Assert.Contains("2702ABCDEF", report); Assert.Contains("6701123456", report);
        Assert.Contains("ABCDEF", report); Assert.Contains("securityAccessIncluded", report);
        Assert.Equal(2, peer.Requests.Count);
    }
    [Fact]
    public async Task rom_payload_and_personal_information_stay_out_of_report()
    {
        await using var session = Session(); using var peer = new Peer(); using var link = new DiagnosticTransport(peer, session);
        link.Exchange([9, 2]);
        session.Record("identity", new { vin = Peer.Vin, adapter = "test SN:ABC123456", message = "Owner at C:\\Users\\PrivateOwner\\backup.bin", raw = Convert.ToHexString(Encoding.ASCII.GetBytes(Peer.Vin)) });
        link.Exchange([0x23, 0, 0, 0x80, 0, 0, 16]);
        await session.FlushAsync(); string report = Export(session);
        Assert.DoesNotContain(Peer.Vin, report); Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(Peer.Vin)), report);
        Assert.DoesNotContain("PrivateOwner", report); Assert.DoesNotContain("ABC123456", report);
        Assert.DoesNotContain(Convert.ToHexString(Peer.Payload), report); Assert.DoesNotContain("63DE", report);
        Assert.Contains("23000080000010", report); Assert.Contains("responseSha256", report); Assert.Contains("payload omitted", report);
    }
    [Fact]
    public async Task timeout_is_durable_after_restart_and_has_stage_and_no_resend()
    {
        var session = Session(); using var peer = new Peer { Fail = true }; using var link = new DiagnosticTransport(peer, session);
        Assert.Contains("Request security seed", Assert.Throws<TransportException>(() => link.Exchange([0x27, 1])).Message);
        await session.DisposeAsync();
        string folder = Assert.Single(SupportReport.Sessions(Root)); var meta = SupportReport.Metadata(folder);
        Assert.Equal("Failed", meta["status"]!.GetValue<string>());
        Assert.Contains("Request security seed", SupportReport.Summary(meta));
        Assert.Contains("requestSentAgain\":false", Events(session)); Assert.Single(peer.Requests);
        Assert.Contains("Test timeout", Export(session));
    }
    [Fact]
    public async Task negative_security_reply_retains_bytes_and_original_exception()
    {
        await using var session = Session(); using var peer = new Peer { Negative = true }; using var link = new DiagnosticTransport(peer, session);
        var error = Assert.Throws<NegativeResponseException>(() => link.Exchange([0x27, 2, 1, 2, 3]));
        Assert.Equal(0x35, error.Code); await session.FlushAsync(); Assert.Contains("7F2735", Events(session)); Assert.Single(peer.Requests);
        Assert.Contains("exchange.negative_response", Events(session)); Assert.NotEqual("Failed", SupportReport.Metadata(session.Folder)["status"]!.GetValue<string>());
        session.Error("operation.error", error); await session.FlushAsync(); Assert.Equal("Failed", SupportReport.Metadata(session.Folder)["status"]!.GetValue<string>());
    }
    [Fact]
    public async Task exporter_only_adds_known_files_and_does_not_overwrite()
    {
        await using var session = Session(); File.WriteAllText(Path.Combine(session.Folder, "rom.bin"), "PRIVATE ROM");
        File.WriteAllText(Path.Combine(session.Folder, "log.csv"), "PRIVATE RECORDING"); await session.FlushAsync();
        string path = Path.Combine(vault, "report.zip"); SupportReport.Export(session.Folder, path); byte[] first = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => SupportReport.Export(session.Folder, path)); Assert.Equal(first, File.ReadAllBytes(path));
        using var zip = ZipFile.OpenRead(path); Assert.DoesNotContain(zip.Entries, e => e.Name is "rom.bin" or "log.csv");
        Assert.Contains(zip.Entries, e => e.Name == "summary.txt");
    }
    [Fact]
    public async Task journal_export_is_separate_and_original_integrity_is_preserved()
    {
        await using var session = Session(); var store = new BackupStore(); var (_, journal) = store.CreateOperation(vault, "hardware", "read_backup");
        using (journal) { journal.Append("identity", new() { ["vin"] = Peer.Vin }); }
        byte[] original = File.ReadAllBytes(journal.Path); session.AttachJournal(journal.Path); await session.FlushAsync();
        string report = Export(session); Assert.DoesNotContain(Peer.Vin, report); Assert.DoesNotContain("\"previous\":", report);
        Assert.Equal(original, File.ReadAllBytes(journal.Path)); Assert.True(store.InspectJournal(journal.Path).Valid);
    }
    [Fact]
    public async Task outside_journal_is_not_attached()
    {
        await using var session = Session(); session.AttachJournal(Path.Combine(vault, "outside", "journal.jsonl")); await session.FlushAsync();
        Assert.Null(SupportReport.Metadata(session.Folder)["readJournal"]);
    }
    [Fact]
    public async Task rotation_is_bounded_and_preserves_latest_failure()
    {
        await using var session = Session(new(SegmentBytes: 1024, Segments: 2));
        for (int i = 0; i < 30; i++) { session.Record("test", new { i, text = new string('a', 100) }); await session.FlushAsync(); }
        session.Record("operation.error", new { message = "last failure retained" }); await session.FlushAsync();
        Assert.InRange(Directory.GetFiles(session.Folder, "events-*.jsonl").Length, 1, 2);
        Assert.Contains("last failure retained", Events(session)); Assert.Contains("last failure retained", SupportReport.Summary(SupportReport.Metadata(session.Folder)));
    }
    [Fact]
    public async Task oversized_events_are_counted_and_do_not_break_following_events()
    {
        await using var session = Session(new(SegmentBytes: 1024)); session.Record("test", new { text = new string('a', 5000) });
        session.Record("test.after", new { ok = true }); await session.FlushAsync(); Assert.Equal(1, session.DroppedEvents); Assert.Contains("test.after", Events(session));
    }
    [Fact]
    public async Task storage_failure_never_changes_transport_requests_or_leaves_export_partial()
    {
        await using var session = Session(); await session.FlushAsync();
        File.Delete(Path.Combine(session.Folder, "session.json")); Directory.CreateDirectory(Path.Combine(session.Folder, "session.json"));
        using var peer = new Peer(); using var link = new DiagnosticTransport(peer, session); Assert.Equal(new byte[] { 0x67, 2 }, link.Exchange([0x27, 2, 1, 2, 3]));
        await Assert.ThrowsAsync<IOException>(session.FlushAsync); Assert.NotNull(session.StorageError); Assert.Single(peer.Requests);
        string path = Path.Combine(vault, "failed.zip"); Assert.ThrowsAny<Exception>(() => SupportReport.Export(session.Folder, path));
        Assert.False(File.Exists(path)); Assert.Empty(Directory.GetFiles(vault, "*.partial-*"));
    }
    [Fact]
    public async Task retention_keeps_only_latest_sessions_and_preserves_unrecognized_files()
    {
        string? first = null;
        for (int i = 0; i < 4; i++)
        {
            await using var session = Session(new(Sessions: 2));
            if (i == 0) { first = session.Folder; File.WriteAllText(Path.Combine(first, "keep.txt"), "user file"); }
            await session.FlushAsync();
        }
        Assert.Equal(2, SupportReport.Sessions(Root).Count); Assert.True(File.Exists(Path.Combine(first!, "keep.txt")));
    }
    [Fact]
    public async Task incomplete_last_event_is_reported_without_copying_raw_input()
    {
        var session = Session(); await session.DisposeAsync();
        File.AppendAllText(Directory.GetFiles(session.Folder, "events-*.jsonl").Single(), "{\"bad input");
        Assert.Contains("incomplete_record", Export(session));
    }
    [Fact]
    public async Task driver_reply_trace_excludes_identity_and_memory_bytes()
    {
        await using var session = Session(); var channel = new Channel();
        channel.Replies.Enqueue(new(0x7E8, 0, [0x49, 2, 1, .. Encoding.ASCII.GetBytes(Peer.Vin)]));
        channel.Replies.Enqueue(new(0x7E8, 0, [0x63, .. Peer.Payload]));
        using var device = new J2534ReadTransport(channel, diagnostics: session); device.Initialize(); using var link = new DiagnosticTransport(device, session);
        link.Exchange([9, 2]); link.Exchange([0x23, 0, 0, 0x80, 0, 0, 16]); await session.FlushAsync(); string report = Export(session);
        Assert.Contains("j2534.response", report); Assert.DoesNotContain(Peer.Vin, report); Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(Peer.Vin)), report);
        Assert.DoesNotContain(Convert.ToHexString(Peer.Payload), report); Assert.Equal(2, channel.Requests.Count);
    }
    [Fact]
    public async Task gen2_support_report_preserves_read_context_without_rom_payloads()
    {
        await using var session = Session(); using var peer = new Peer(); using var link = new DiagnosticTransport(peer, session);
        link.Exchange(Convert.FromHexString("270486CA06"));
        link.Exchange(Convert.FromHexString("2324000FFFF00010"));
        await session.FlushAsync(); string report = Export(session);
        Assert.Contains("270486CA06", report); Assert.Contains("Submit security key", report);
        Assert.Contains("Read Gen 2 ROM at 0x0FFFF0 (16 bytes)", report);
        Assert.Contains("payload omitted", report); Assert.DoesNotContain(Convert.ToHexString(Peer.Payload), report);
        Assert.Equal(2, peer.Requests.Count);
    }
    [Fact]
    public async Task damaged_journal_line_does_not_prevent_support_export()
    {
        await using var session = Session(); var (_, journal) = new BackupStore().CreateOperation(vault, "hardware", "read_backup");
        journal.Dispose(); File.AppendAllText(journal.Path, "null\n[1,2]\n{bad json\n"); session.AttachJournal(journal.Path); await session.FlushAsync();
        Assert.Contains("incomplete_record", Export(session));
    }
    [Fact]
    public async Task unexpected_long_security_reply_is_retained_for_different_seed_methods()
    {
        await using var session = Session(); using var peer = new Peer { LongSeed = true }; using var link = new DiagnosticTransport(peer, session);
        var response = link.Exchange([0x27, 1]); await session.FlushAsync(); Assert.Contains(Convert.ToHexString(response), Export(session));
    }
    [Fact]
    public async Task driver_open_failure_is_captured_before_any_request()
    {
        await using var session = Session(); var channel = new Channel { FailOpen = true };
        using var device = new J2534ReadTransport(channel, diagnostics: session);
        Assert.Throws<TransportException>(device.Initialize); await session.FlushAsync();
        Assert.Contains("Test driver open failure", Export(session)); Assert.Equal("Failed", SupportReport.Metadata(session.Folder)["status"]!.GetValue<string>()); Assert.Empty(channel.Requests);
    }
    [Fact]
    public async Task j2534_notifications_and_pending_are_counted_without_extra_requests()
    {
        await using var session = Session(); var channel = new Channel();
        channel.Replies.Enqueue(new(0x7E0, 1, [])); channel.Replies.Enqueue(new(0x7E8, 2, [])); channel.Replies.Enqueue(new(0x7E0, 8, []));
        channel.Replies.Enqueue(new(0x7E8, 0, [0x7F, 0x27, 0x78])); channel.Replies.Enqueue(new(0x7E8, 0, [0x67, 1, 0x12, 0x34, 0x56]));
        using var device = new J2534ReadTransport(channel, diagnostics: session); device.Initialize(); using var link = new DiagnosticTransport(device, session);
        link.Exchange([0x27, 1]); await session.FlushAsync(); string events = Events(session);
        Assert.Contains("\"notifications\":3", events); Assert.Contains("\"pendingReplies\":1", events); Assert.Contains("\"startNotifications\":1", events); Assert.Single(channel.Requests);
    }
    public void Dispose() { if (Directory.Exists(vault)) Directory.Delete(vault, true); }
    private sealed class Peer : IReadTransport
    {
        public const string Vin = "TESTVIN1234567890";
        public static readonly byte[] Payload = [0xDE, 0xAD, 0xBE, 0xEF, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        public string Mode => "hardware"; public string Adapter => "test"; public int ReadChunkSize => 3968; public bool ReadAttempted { get; set; }
        public bool Fail, Negative, LongSeed; public List<byte[]> Requests = [];
        public byte[] Exchange(byte[] request)
        {
            Requests.Add(request.ToArray()); if (Fail) throw new TransportException("Test timeout"); if (Negative) throw new NegativeResponseException(0x27, 0x35);
            if (LongSeed) return [0x67, 1, .. Enumerable.Repeat((byte)0xAB, 128)];
            return request[0] switch { 9 => [0x49, 2, 1, .. Encoding.ASCII.GetBytes(Vin)], 0x23 => [0x63, .. Payload], _ => request[1] == 1 ? [0x67, 1, 0x12, 0x34, 0x56] : [0x67, 2] };
        }
        public void Dispose() { }
    }
    private sealed class Channel : IJ2534Channel
    {
        public Queue<J2534Frame> Replies = new(); public List<byte[]> Requests = [];
        public bool FailOpen;
        public string? CleanupWarning => null;
        public J2534Version Open() => FailOpen ? throw new TransportException("Test driver open failure") : new("test", "test", "04.04"); public void Connect(uint bitrate, uint requestId, uint responseId) { }
        public void ClearReceive() { } public void Write(byte[] request) => Requests.Add(request.ToArray());
        public J2534Frame? Read(int timeoutMilliseconds) => Replies.TryDequeue(out var result) ? result : null;
        public void Dispose() { }
    }
}
