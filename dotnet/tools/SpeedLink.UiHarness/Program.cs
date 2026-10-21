using System.IO;
using System.Text.Json;
using System.Windows;
using SpeedLink.Desktop;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string Get(string key)
        {
            int at = Array.IndexOf(args, key);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : throw new ArgumentException("Missing " + key);
        }
        if (args.Contains("--replay-offline", StringComparer.Ordinal))
        {
            var replayApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int resultCode = 1;
            replayApp.Startup += async (_, _) =>
            {
                LogViewerWindow? window = null;
                try
                {
                    window = new(); replayApp.MainWindow = window;
                    var report = await window.ExerciseOffline(Get("--file"), Get("--screenshot"));
                    File.WriteAllText(Get("--report"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); resultCode = 0;
                }
                catch (Exception error) { File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
                finally { window?.Close(); replayApp.Shutdown(resultCode); }
            };
            replayApp.Run(); return resultCode;
        }
        if (args.Contains("--editor-offline", StringComparer.Ordinal) || args.Contains("--workbench-offline", StringComparer.Ordinal) || args.Contains("--definitions-offline", StringComparer.Ordinal) || args.Contains("--layout-offline", StringComparer.Ordinal))
        {
            var editorApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int resultCode = 1;
            editorApp.Startup += async (_, _) =>
            {
                EditorWindow? window = null; MainWindow? workbench = null;
                try
                {
                    var profile = SpeedLink.Core.RomInspector.KnownProfiles.Single(p => p.Calibration == Get("--calibration"));
                    var definition = SpeedLink.Core.TuningDefinition.Known.Single(d => d.Calibration == profile.Calibration);
                    var image = new byte[SpeedLink.Core.RomInspector.RomSize];
                    "SYNTHETIC UI TEST - NOT ECU FIRMWARE"u8.CopyTo(image.AsSpan(0x100));
                    System.Text.Encoding.ASCII.GetBytes(profile.Identity).CopyTo(image, profile.IdentityOffset);
                    image[0x8002] = 0x5A; image[0x8003] = 0xA5; image[^2] = 0xA5; image[^1] = 0x5A;
                    foreach (var word in profile.LayoutWords) System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(word.Address, 4), word.Value);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(profile.CorrectionAddress, 4), unchecked(SpeedLink.Core.RomInspector.ChecksumTarget - SpeedLink.Core.RomInspector.Sum(image, profile)));
                    profile = profile with { BootSha256 = SpeedLink.Core.RomInspector.Hash(image.AsSpan(0, 0x8000)), LoopSha256 = profile.LoopAddress is int at ? SpeedLink.Core.RomInspector.Hash(image.AsSpan(at, profile.LoopSize!.Value)) : null };
                    var guard = new SpeedLink.Core.TuningCodeGuard([new(profile.Calibration, profile.ChecksumStart, SpeedLink.Core.TuningCodeGuard.Hash(image, definition, profile))]);
                    string output = Get("--output"); Directory.CreateDirectory(output);
                    string path = Path.Combine(output, "SYNTHETIC_NOT_ECU_FIRMWARE_" + profile.Calibration + ".bin"); File.WriteAllBytes(path, image);
                    object workflow;
                    if (args.Contains("--layout-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile]), guard); editorApp.MainWindow = workbench;
                        workflow = await workbench.ExerciseLayoutOffline(path, output);
                    }
                    else if (args.Contains("--definitions-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile]), guard); editorApp.MainWindow = workbench;
                        workflow = await workbench.ExerciseDefinitionsOffline(path, output, profile.Calibration);
                    }
                    else if (args.Contains("--workbench-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile]), guard); editorApp.MainWindow = workbench;
                        workflow = await workbench.ExerciseWorkbenchOffline(path, output, args.Contains("--recording", StringComparer.Ordinal) ? Get("--recording") : null);
                    }
                    else
                    {
                        window = new(new SpeedLink.Core.RomInspector([profile]), guard); editorApp.MainWindow = window;
                        workflow = await window.ExerciseOffline(path, output);
                    }
                    File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = true, synthetic = true, hardwareAccessed = false, calibration = profile.Calibration, workflow }, new JsonSerializerOptions { WriteIndented = true })); resultCode = 0;
                }
                catch (Exception error) { File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
                finally { window?.CloseOfflineTest(); workbench?.CloseWorkbenchTest(); editorApp.Shutdown(resultCode); }
            };
            editorApp.Run(); return resultCode;
        }
        if (args.Contains("--logger-offline", StringComparer.Ordinal))
        {
            var loggerApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int resultCode = 1;
            loggerApp.Startup += async (_, _) =>
            {
                MainWindow? loggerWindow = null;
                try
                {
                    bool fault = args.Contains("--fault", StringComparer.Ordinal);
                    var profile = SpeedLink.Core.LoggerDefinition.Load(Get("--definition"));
                    using var vectors = JsonDocument.Parse(File.ReadAllBytes(Get("--vectors")));
                    var values = vectors.RootElement.GetProperty("calibration");
                    var calibration = SpeedLink.Core.LoggerCalibration.ExplicitMap(values.GetProperty("map_gain_kpa_per_v").GetDouble(), values.GetProperty("map_offset_kpa").GetDouble());
                    using var peer = new LoggerPeer(profile, Get("--vectors"), fault);
                    loggerWindow = new MainWindow(Get("--vault")); loggerApp.MainWindow = loggerWindow;
                    var result = await loggerWindow.ExerciseLoggerOffline(peer, Get("--screenshot"), fault, profile, calibration, peer.AllowTraffic);
                    result["requests"] = peer.Requests; result["faultRequests"] = peer.FaultRequests;
                    if (fault && peer.FaultRequests != 1) throw new InvalidOperationException("Faulted request was retried.");
                    File.WriteAllText(Get("--report"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })); resultCode = 0;
                }
                catch (Exception error) { File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
                finally { loggerWindow?.Close(); loggerApp.Shutdown(resultCode); }
            };
            loggerApp.Run(); return resultCode;
        }
        if (!args.Contains("--isolated-bench", StringComparer.Ordinal))
            throw new ArgumentException("Confirmed isolated bench is required.");
        var report = Get("--report");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int code = 1;
        app.Startup += async (_, _) =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow(Get("--vault"));
                app.MainWindow = window;
                int baudAt = Array.IndexOf(args, "--baud");
                int baud = baudAt < 0 ? 115200 : int.Parse(args[baudAt + 1]);
                int adapterAt = Array.IndexOf(args, "--adapter");
                bool tactrix = adapterAt >= 0 && args[adapterAt + 1] == "tactrix";
                var result = args.Contains("--logger-hardware", StringComparer.Ordinal)
                    ? await window.ExerciseLoggerHardware(Get("--port"), Get("--expected"), Get("--screenshot"), tactrix)
                    : await window.ExerciseHardware(Get("--port"), File.ReadAllBytes(Get("--expected")), baud, tactrix);
                File.WriteAllText(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                code = 0;
            }
            catch (Exception error) { File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true })); }
            finally { window?.Close(); app.Shutdown(code); }
        };
        app.Run();
        return code;
    }
}

internal sealed class LoggerPeer : SpeedLink.Core.IReadTransport
{
    private readonly System.Threading.ManualResetEventSlim ready = new(false);
    public void AllowTraffic() => ready.Set();
    private readonly Dictionary<string, byte[]> replies = [];
    private readonly bool fault;
    public List<string> Requests { get; } = [];
    public int FaultRequests { get; private set; }
    public string Mode => "test";
    public string Adapter => "Offline logger UI fixture";
    public int ReadChunkSize => 3968;
    public bool ReadAttempted { get; set; }
    public LoggerPeer(SpeedLink.Core.LoggerDefinition definition, string vectors, bool fault)
    {
        this.fault = fault;
        using var data = JsonDocument.Parse(File.ReadAllBytes(vectors));
        foreach (var vector in data.RootElement.GetProperty("vectors").EnumerateArray())
            if (vector.GetProperty("type").GetString() == "decode")
                replies.TryAdd(Convert.ToHexString(definition.RequestFor(vector.GetProperty("channel").GetString()!)),
                    Convert.FromHexString(vector.GetProperty("responseHex").GetString()!));
    }
    public byte[] Exchange(byte[] request)
    {
        if (!ready.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("Offline UI did not enable fixture traffic.");
        SpeedLink.Core.DiagnosticProtocol.CheckReadOnly(request);
        if (request.Length != 3 || request[0] != 0x22) throw new InvalidOperationException("Logger attempted a non-logging request.");
        string key = Convert.ToHexString(request); Requests.Add(key);
        if (fault && Requests.Count >= 4) { FaultRequests++; throw new SpeedLink.Core.TransportException("Offline injected USB removal"); }
        return replies[key];
    }
    public void Dispose() => ready.Dispose();
}
