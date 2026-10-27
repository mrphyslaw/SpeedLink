using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class App
{
    private async Task<bool> RunCrashChecks(string[] args)
    {
        if (!args.Contains("--crash-probe", StringComparer.Ordinal) && !args.Contains("--crash-review", StringComparer.Ordinal)) return false;
        string Get(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException(key); }
        string output = Path.GetFullPath(Get("--output")); Directory.CreateDirectory(output);
        crashTestRoot = Path.Combine(output, "crashes");
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (args.Contains("--crash-probe", StringComparer.Ordinal))
        {
            // Exercise the real dispatcher boundary in a disposable process. Never opens an adapter or ROM.
            crashProbe = true;
            _ = Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("Synthetic unexpected UI failure")), DispatcherPriority.Normal);
            return true;
        }
        var reports = new CrashReports(crashTestRoot);
        var pending = reports.List(true);
        if (pending.Count != 1) throw new InvalidOperationException("Expected one report from the previous process.");
        var dialog = new CrashReportWindow(reports, true); MainWindow = dialog;
        dialog.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
        string zipPath = Path.Combine(output, "CrashSupport.zip"); dialog.ExportTo(pending[0], zipPath);
        var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var png = File.Create(Path.Combine(output, "CrashReport.png"))) encoder.Save(png);
        dialog.Close();
        using var zip = ZipFile.OpenRead(zipPath);
        if (reports.List(true).Count != 0 || reports.List().Count != 1 || zip.Entries.Count != 2 || zip.GetEntry("crash.json") is null)
            throw new InvalidOperationException("Crash review, acknowledgment or export failed.");
        File.WriteAllText(Get("--smoke-report"), JsonSerializer.Serialize(new { passed = true, hardwareAccessed = false, priorProcessReport = true, acknowledged = true, exported = true }));
        Shutdown(0); return true;
    }
}
