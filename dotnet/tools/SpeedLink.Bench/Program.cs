using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using SpeedLink.Core;
using SpeedLink.Hardware;
using SpeedLink.J2534;

var values = new Dictionary<string, string>(StringComparer.Ordinal);
var flags = new HashSet<string>(StringComparer.Ordinal);
var flagNames = new HashSet<string> { "--isolated-bench", "--expect-error" };
for (int index = 0; index < args.Length; index++)
{
    if (flagNames.Contains(args[index]))
        flags.Add(args[index]);
    else if (args[index].StartsWith("--", StringComparison.Ordinal) && index + 1 < args.Length)
        values.Add(args[index], args[++index]);
    else
        throw new ArgumentException("Invalid command arguments.");
}
string Get(string name, string fallback = "") => values.GetValueOrDefault(name, fallback);
int Number(string name, int fallback) => int.TryParse(Get(name), out var value) ? value : fallback;
var mode = Get("--mode");
var reportPath = Get("--report");
if (reportPath.Length == 0 || mode.Length == 0) throw new ArgumentException("Specify --mode and --report. Hardware modes also require --port and --isolated-bench.");
reportPath = Path.GetFullPath(reportPath);
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
string build = typeof(TraceTransport).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
    .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
var report = new Dictionary<string, object?> { ["mode"] = mode, ["version"] = build.Split('+')[0], ["build"] = build, ["tool"] = "SpeedLink.Bench", ["started"] = DateTimeOffset.UtcNow, ["liveProgrammingEnabled"] = false };
VLinkerReadTransport? link = null;
J2534ReadTransport? passThru = null;
NativeJ2534Channel? native = null;
IReadTransport? activeLink = null;
ISerialConnection? serial = null;
SerialConnection? win32 = null;
FtdiSerialConnection? ftdi = null;
TraceTransport? traced = null;
DiagnosticSession? diagnostics = null;
var watch = Stopwatch.StartNew();
int exit = 0;
try
{
    var inspector = new RomInspector();
    var store = new BackupStore();
    if (mode == "diagnostics-self-test")
    {
        diagnostics = new(Get("--diagnostics"), "bench", build);
        for (int i = 0; i < 20000; i++)
        {
            diagnostics.Record("test.short", new { index = i });
            if (i % 250 == 249) await diagnostics.FlushAsync();
        }
        report["hardwareAccessed"] = false;
        report["diagnosticEvents"] = 20000;
    }
    else if (mode == "peer-parser-self-test")
    {
        byte[] current = [0x22, 9, 0], next = [0x22, 0, 0x33];
        foreach (var (raw, expected, echo) in new[] {
            ("7E00322090000000000\r7E00322003300000000\r", next, true),
            ("7E00322090000000000\r", current, false),
            ("7E00322090000000000\r7E00322090000000000\r", current, true),
            ("7E00322003300000000\r", next, false) })
        {
            var parsed = VLinkerEcuPeer.ParseNextRequest(raw, current);
            if (parsed.Request is null || !parsed.Request.AsSpan().SequenceEqual(expected) || parsed.DiscardedEcho != echo) throw new InvalidDataException("Peer parser regression.");
        }
        if (VLinkerEcuPeer.ParseNextRequest("NO DATA\r\r", current).Request is not null) throw new InvalidDataException("Peer parser fabricated a request.");
        foreach (string raw in new[] { "7E00322003300000000\r7E00322000C00000000\r", "7E00900000000000000\r" })
        {
            bool rejected = false;
            try { VLinkerEcuPeer.ParseNextRequest(raw, current); } catch (IOException) { rejected = true; }
            if (!rejected) throw new InvalidDataException("Peer parser accepted invalid frames.");
        }
        report["offlineParserCases"] = 7; report["hardwareAccessed"] = false;
    }
    else if (mode == "inspect")
    {
        var path = Get("--file");
        var data = File.ReadAllBytes(path);
        var original = RomInspector.Hash(data);
        var info = inspector.Inspect(data);
        report["inspection"] = info.ToManifest();
        if (info.CanCorrectChecksum)
        {
            var edited = data.ToArray();
            edited[0x60000] ^= 1;
            var corrected = inspector.CorrectedCopy(edited);
            int at = info.CorrectionAddress!.Value;
            if (!corrected.AsSpan(0, at).SequenceEqual(edited.AsSpan(0, at)) || !corrected.AsSpan(at + 4).SequenceEqual(edited.AsSpan(at + 4)))
                throw new InvalidDataException("Correction changed bytes outside its word.");
            report["checksumCorrectionPassed"] = inspector.Inspect(corrected).ProfileValid;
        }
        if (RomInspector.Hash(File.ReadAllBytes(path)) != original)
            throw new InvalidDataException("Source file changed.");
        report["originalUnchanged"] = true;
    }
    else if (mode == "open-backup")
    {
        var loaded = store.Load(Get("--file"));
        report["sha256"] = RomInspector.Hash(loaded.Data);
        report["backupMode"] = loaded.Manifest["mode"]!.GetValue<string>();
    }
    else if (mode == "scan")
    {
        report["operations"] = store.Scan(Get("--vault"));
    }
    else if (mode == "serve-vlinker")
    {
        if (!flags.Contains("--isolated-bench"))
            throw new InvalidOperationException("Confirm isolated bench with --isolated-bench.");
        var peer = new VLinkerEcuPeer(Get("--port"), File.ReadAllBytes(Get("--expected")), reportPath, Get("--fault"),
            Get("--logger-vectors").Length == 0 ? null : new LoggerBenchData(Get("--logger-vectors")));
        report["peer"] = peer.Run(Get("--stop-file"));
    }
    else
    {
        if (!flags.Contains("--isolated-bench"))
            throw new InvalidOperationException("Confirm isolated bench with --isolated-bench.");
        var port = Get("--port");
        if (port.Length == 0)
            throw new ArgumentException("Explicit COM port is required.");
        if (Get("--diagnostics").Length > 0) diagnostics = new(Get("--diagnostics"), "bench", build);
        if (Get("--adapter") == "tactrix")
        {
            var drivers = J2534Drivers.Discover();
            var matching = drivers.Where(item => Path.GetFileName(item.LibraryPath).Equals(Get("--driver-library", "op20pt32.dll"), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length != 1)
                throw new InvalidOperationException("Select one registered bench driver with --driver-library.");
            native = new(matching[0], diagnostics);
            passThru = new(native, diagnostics: diagnostics);
            passThru.Initialize();
            activeLink = passThru;
            report["driver"] = matching[0].Name;
        }
        else
        {
            var backend = Get("--backend", "win32");
            serial = backend switch
            {
                "ftdi" => ftdi = new(port, Number("--baud", 115200)),
                "win32" => win32 = new(port, Number("--baud", 115200)),
                _ => throw new ArgumentException("Select --backend ftdi or win32.")
            };
            link = new(serial, transferBaud: Number("--transfer-baud", VLinkerReadTransport.TransferBaud), diagnostics: diagnostics);
            report["transferBaud"] = link.ConfiguredTransferBaud;
            link.Initialize();
            activeLink = link;
        }
        if (diagnostics is not null) activeLink = new DiagnosticTransport(activeLink, diagnostics);
        traced = new(activeLink, reportPath + ".requests.jsonl", Number("--delay-ms", 0), Number("--pause-after", 0), Get("--resume-file"), reportPath + ".paused.json");
        report["adapter"] = activeLink.Adapter;
        report["port"] = port;
        report["isolatedBench"] = true;
        if (mode == "blocked")
        {
            var before = native?.WriteRequests ?? ftdi?.Writes ?? win32!.Writes;
            int count = 0;
            foreach (var hex in new[] { "1085", "B100B200", "3400008000000F8000", "36616263", "37", "1101", "04", "3B00", "22F1A0", "23001000000001", "23000FFFFF0002", "23000080000FFF" })
            {
                bool blocked = false;
                try
                {
                    activeLink.Exchange(Convert.FromHexString(hex));
                }
                catch (TransportException) { blocked = true; }
                if (!blocked)
                    throw new InvalidOperationException("Forbidden request passed the allowlist: " + hex);
                count++;
            }
            if ((native?.WriteRequests ?? ftdi?.Writes ?? win32!.Writes) != before)
                throw new InvalidOperationException("A blocked request reached serial output.");
            report["blockedRequests"] = count;
            report["adapterWritesForBlockedRequests"] = 0;
        }
        else
        {
            var identity = ReadWorkflow.Identify(traced);
            report["identity"] = identity.ToManifest();
            if (mode == "logger-probe")
            {
                if (link is null) throw new ArgumentException("The direct ELM probe requires vLinker.");
                DiagnosticProtocol.CheckReadOnly([0x22, 0x00, 0x0C]);
                var checks = new List<object>();
                foreach (string command in new[] { "22000C1", "22000C1", "22000C1" })
                {
                    var timer = Stopwatch.StartNew(); string reply = link.Command(command, 5);
                    checks.Add(new { command, reply, milliseconds = timer.Elapsed.TotalMilliseconds });
                }
                report["directLoggerProbe"] = checks;
            }
            else if (mode == "logger")
            {
                var profile = LoggerDefinition.ForCalibration(identity.Calibration) ?? throw new InvalidDataException("No fixture logger profile matches the peer identity.");
                var calibration = profile.BindRom(File.ReadAllBytes(Get("--expected")));
                var selection = Get("--channels").Length == 0 ? profile.ChannelIds.ToArray() : Get("--channels").Split(',');
                var logger = new LiveLogger(profile, calibration, traced, identity, selection);
                using var recording = new LoggerRecording(Get("--vault"), profile, calibration, identity, selection,
                    profile.Channels.Where(c => selection.Contains(c.Id)).ToDictionary(c => c.Id, c => c.Unit), null, "bench", Number("--scans-per-second", 5));
                report["recording"] = recording.Folder; report["loggerProfile"] = profile.Id;
                report["loggerChannels"] = selection.Length; report["loggerQueries"] = logger.QueryCount;
                int scans = Number("--count", 10); string status = "Stopped";
                try
                {
                    for (int i = 0; i < scans; i++)
                    {
                        var round = Stopwatch.StartNew(); var frame = logger.ReadFrame(); recording.Append(frame);
                        if (Get("--cancel-file").Length > 0 && File.Exists(Get("--cancel-file"))) break;
                        int delay = (int)Math.Max(0, 1000.0 / Number("--scans-per-second", 5) - round.Elapsed.TotalMilliseconds);
                        if (delay > 0) Thread.Sleep(delay);
                    }
                }
                catch (Exception error)
                {
                    status = "Stopped after error: " + error.Message;
                    if (logger.LastFrame is { } frame && frame.Scan > recording.Rows) recording.Append(frame);
                    throw;
                }
                finally
                {
                    recording.Complete(status); report["loggerScans"] = recording.Rows;
                    report["loggerReadSessionEntered"] = traced.ReadAttempted;
                }
            }
            else if (mode == "read")
            {
                using var cancellation = new CancellationTokenSource();
                int cancelAfter = Number("--cancel-after", 0);
                using var progressLog = new StreamWriter(reportPath + ".progress.jsonl", append: false);
                var progress = new InlineProgress(value =>
                {
                    progressLog.WriteLine(JsonSerializer.Serialize(new
                    {
                        fraction = value.Fraction,
                        message = value.Message,
                        memoryReads = traced.MemoryReads
                    }));
                    progressLog.Flush();
                    if (cancelAfter > 0 && traced.MemoryReads >= cancelAfter)
                        cancellation.Cancel();
                });
                try
                {
                    var result = new ReadWorkflow(store, inspector).ReadBackup(traced, identity, Get("--vault"), progress, cancellation.Token);
                    var backup = store.Load(result.Path, "hardware");
                    report["backupPath"] = result.Path;
                    report["sha256"] = RomInspector.Hash(backup.Data);
                    report["inspection"] = result.Inspection.ToManifest();
                    if (Get("--expected").Length > 0 && !File.ReadAllBytes(Get("--expected")).AsSpan().SequenceEqual(backup.Data))
                        throw new InvalidDataException("Wire backup differs from expected image.");
                    if (cancelAfter > 0)
                        throw new InvalidOperationException("Cancellation did not stop the read.");
                }
                catch (OperationCanceledException) when (cancelAfter > 0) { report["cancelled"] = true; report["noVerifiedBackup"] = !Directory.EnumerateFiles(Get("--vault"), "rom.bin", SearchOption.AllDirectories).Any(); }
            }
            else if (mode == "endurance")
            {
                ReadWorkflow.CheckEngineStopped(traced);
                int count = Number("--count", 1000);
                for (int i = 0; i < count; i++)
                    if (!traced.Exchange([0x01, 0x0C]).AsSpan().SequenceEqual(new byte[] { 0x41, 0x0C, 0, 0 }))
                        throw new InvalidDataException("RPM response mismatch.");
                report["exchanges"] = count;
            }
            else if (mode == "lengths" || mode == "soak")
            {
                ReadWorkflow.CheckEngineStopped(traced);
                traced.Exchange([0x10, 0x87]);
                var seed = traced.Exchange([0x27, 0x01]);
                if (seed.Length != 5 || seed[0] != 0x67 || seed[1] != 1)
                    throw new InvalidDataException("Invalid seed response.");
                if (seed.AsSpan(2).ToArray().Any(x => x != 0))
                    traced.Exchange([0x27, 0x02, .. SecurityAccess.SeedKey(seed.AsSpan(2))]);
                var expected = File.ReadAllBytes(Get("--expected"));
                var checks = new List<object>();
                var random = new Random(0x534C);
                var spans = mode == "lengths"
                    ? new[] { 1, 6, 7, 8, 16, 120, 128, 255, 256, 512, 1024, 2048, 3968 }.Select(n => (0x48000, n)).Concat(new[] { (0xFFFFF, 1), (RomInspector.RomSize - 3968, 3968) })
                    : Enumerable.Range(0, Number("--count", 1000)).Select(index => (index % 125 == 0 ? 0 : index % 125 == 1 ? RomInspector.RomSize - 3968 : random.Next(RomInspector.RomSize - 3968 + 1), 3968));
                foreach (var (at, count) in spans)
                {
                    var request = new byte[7];
                    request[0] = 0x23;
                    BinaryPrimitives.WriteInt32BigEndian(request.AsSpan(1, 4), at);
                    BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(5, 2), (ushort)count);
                    var result = traced.Exchange(request);
                    if (result.Length != count + 1 || !result.AsSpan(1).SequenceEqual(expected.AsSpan(at, count)))
                        throw new InvalidDataException("Length/boundary read mismatch.");
                    checks.Add(new
                    {
                        address = at,
                        bytes = count,
                        sha256 = RomInspector.Hash(result.AsSpan(1))
                    });
                }
                report["checks"] = checks;
            }
            else if (mode != "identify")
                throw new ArgumentException("Unknown mode.");
        }
    }
    report["passed"] = !flags.Contains("--expect-error");
    if (flags.Contains("--expect-error")) { report["expectedErrorMissing"] = true; exit = 1; }
}
catch (Exception error)
{
    diagnostics?.Error("operation.error", error);
    report["error"] = error.Message;
    report["errorType"] = error.GetType().FullName;
    bool expected = flags.Contains("--expect-error") && error.Message.Contains(Get("--expect-message"), StringComparison.OrdinalIgnoreCase);
    report["passed"] = expected;
    report["expectedError"] = expected;
    exit = expected ? 0 : 1;
}
finally
{
    if (passThru is not null)
    {
        passThru.Dispose();
        report["j2534HandleClosed"] = native is null || !native.IsOpen;
        report["j2534WriteRequests"] = native?.WriteRequests;
        report["j2534CleanupWarning"] = passThru.CloseWarning;
        if (passThru.CloseWarning is not null)
        {
            report["passed"] = false;
            exit = 1;
        }
    }
    if (link is not null)
    {
        report["failedConnection"] = link.Failed;
        link.Dispose();
        report["finalBaud"] = link.Baud;
        report["baudRestoreWarning"] = link.CloseWarning;
        if (link.CloseWarning is not null)
        {
            report["passed"] = false;
            exit = 1;
        }
    }
    if (serial is not null)
    {
        serial.Dispose();
        report["serialBackend"] = ftdi is not null ? FtdiSerialConnection.Backend : SerialConnection.Backend;
        report["serialDriverErrors"] = ftdi?.DriverErrors ?? win32!.DriverErrors;
        report["serialReadBufferSize"] = win32?.ReadBufferSize;
        report["serialWrites"] = ftdi?.Writes ?? win32!.Writes;
        report["serialHandleClosed"] = !serial.IsOpen;
        if (ftdi is not null)
        {
            report["originalLatencyMs"] = ftdi.OriginalLatency;
            report["latencyRestored"] = ftdi.LatencyRestored;
            report["serialCleanupWarning"] = ftdi.CleanupWarning;
            if (ftdi.CleanupWarning is not null || !ftdi.LatencyRestored)
            {
                report["passed"] = false;
                exit = 1;
            }
        }
    }
    if (traced is not null)
    {
        report["memoryReads"] = traced.MemoryReads;
        report["exchangeCalls"] = traced.Requests;
        traced.Dispose();
    }
    if (diagnostics is not null)
    {
        await diagnostics.DisposeAsync();
        report["operationPassed"] = report.GetValueOrDefault("passed");
        try
        {
            SupportReport.Export(diagnostics.Folder, reportPath + ".support.zip");
            report["supportReport"] = reportPath + ".support.zip";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            report["supportReportError"] = error.Message;
            report["passed"] = false; exit = 1;
        }
        report["diagnosticStorageError"] = diagnostics.StorageError;
        report["diagnosticDroppedEvents"] = diagnostics.DroppedEvents;
    }
    report["seconds"] = watch.Elapsed.TotalSeconds;
    File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(report));
}
return exit;

sealed class InlineProgress(Action<ReadProgress> report) : IProgress<ReadProgress>
{
    public void Report(ReadProgress value) => report(value);
}
sealed class TraceTransport(IReadTransport inner, string path, int delay, int pauseAfter, string resumeFile, string pauseFile) : IReadTransport
{
    private readonly StreamWriter trace = new(path, append: false);
    public string Mode => inner.Mode; public string Adapter => inner.Adapter; public int ReadChunkSize => inner.ReadChunkSize;
    public IDiagnosticSink? Diagnostics => inner.Diagnostics;
    public bool ReadAttempted
    {
        get => inner.ReadAttempted; set => inner.ReadAttempted = value;
    }
    public int MemoryReads
    {
        get; private set;
    }
    public int Requests
    {
        get; private set;
    }
    public int LoggerReads { get; private set; }
    public byte[] Exchange(byte[] request)
    {
        if (pauseAfter > 0 && (request[0] == 0x23 && MemoryReads == pauseAfter || request[0] == 0x22 && LoggerReads == pauseAfter))
        {
            if (resumeFile.Length == 0)
                throw new ArgumentException("A bench pause requires --resume-file.");
            if (File.Exists(resumeFile))
                throw new InvalidOperationException("Bench release file must not exist before the pause.");
            File.WriteAllText(pauseFile, JsonSerializer.Serialize(new
            {
                paused = true,
                acknowledgedMemoryReads = MemoryReads,
                acknowledgedLoggerQueries = LoggerReads,
                time = DateTimeOffset.UtcNow
            }));
            var timer = Stopwatch.StartNew();
            while (!File.Exists(resumeFile))
            {
                if (timer.Elapsed > TimeSpan.FromHours(8))
                    throw new TimeoutException("Bench pause was not released; no next request was sent.");
                Thread.Sleep(50);
            }
        }
        Requests++;
        if (request[0] == 0x22) LoggerReads++;
        if (request[0] == 0x23)
        {
            MemoryReads++;
            if (delay > 0)
                Thread.Sleep(delay);
        }
        trace.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "issued",
            time = DateTimeOffset.UtcNow,
            request = Convert.ToHexStringLower(request)
        }));
        trace.Flush();
        try
        {
            var response = inner.Exchange(request);
            trace.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "ack",
                request = Convert.ToHexStringLower(request),
                bytes = response.Length,
                sha256 = RomInspector.Hash(response)
            }));
            trace.Flush();
            return response;
        }
        catch (Exception error) { trace.WriteLine(JsonSerializer.Serialize(new { kind = "error", request = Convert.ToHexStringLower(request), error = error.Message })); trace.Flush(); throw; }
    }
    public void Dispose() => trace.Dispose();
}
