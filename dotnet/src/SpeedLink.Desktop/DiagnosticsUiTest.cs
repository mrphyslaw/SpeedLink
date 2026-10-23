using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    internal async Task<Dictionary<string, object?>> ExerciseDiagnosticsOffline(string output)
    {
        if (!Resource("SpeedLink.SupportHelp.md").Contains("Factory security-access", StringComparison.Ordinal)) throw new InvalidDataException("Reporting help is missing.");
        Show(); UpdateLayout(); await BeginDiagnostics();
        if (diagnosticSession is null) throw new IOException("Diagnostic storage did not open.");
        diagnosticSession.Record("connection.opening", new { adapter = "Synthetic test adapter" });
        diagnosticSession.Record("adapter.ready", new { adapter = "Synthetic test adapter" });
        diagnosticSession.Record("exchange.started", new { stage = "Request security seed", requestHex = "2701" });
        diagnosticSession.Record("exchange.completed", new { responseHex = "6701123456" });
        diagnosticSession.Error("operation.error", new TransportException("Synthetic response timeout. No request was resent."));
        await diagnosticSession.DisposeAsync(); diagnosticSession = null;
        Controls(); if (!ExportReportButton.IsEnabled || transport is not null) throw new InvalidOperationException("Saved session export is unavailable offline.");
        var finished = new TaskCompletionSource<Dictionary<string, object?>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += async (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<SupportReportWindow>().SingleOrDefault();
            if (dialog is null) return;
            timer.Stop();
            try
            {
                if (ConnectButton.IsEnabled || ExportReportButton.IsEnabled) throw new InvalidOperationException("Connection controls were not locked during export.");
                string zipPath = Path.Combine(output, "SupportReport.zip"); await dialog.ExportTo(zipPath);
                using var zip = ZipFile.OpenRead(zipPath);
                using var summary = new StreamReader(zip.GetEntry("summary.txt")!.Open()); string text = summary.ReadToEnd();
                if (!text.Contains("Failed", StringComparison.Ordinal) || !text.Contains("Request security seed", StringComparison.Ordinal)) throw new InvalidDataException("Report lost failure context.");
                var content = (FrameworkElement)dialog.Content;
                dialog.UpdateLayout();
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                    drawing.DrawRectangle(UiTheme.Panel, null, bounds);
                    drawing.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.Fill }, null, bounds);
                }
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var png = File.Create(Path.Combine(output, "SupportReport.png"))) encoder.Save(png);
                finished.TrySetResult(new() { ["passed"] = true, ["hardwareAccessed"] = false, ["savedSessionAvailableOffline"] = true, ["actualExportButton"] = true, ["controlsLockedDuringExport"] = true, ["exportedZip"] = zipPath, ["entries"] = zip.Entries.Select(e => e.FullName).ToArray() });
            }
            catch (Exception error) { finished.TrySetException(error); }
            finally { dialog.Close(); }
        };
        timer.Start();
        try
        {
            ExportReportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (!ConnectButton.IsEnabled || !ExportReportButton.IsEnabled) throw new InvalidOperationException("Connection controls did not recover after export.");
            return result;
        }
        finally { timer.Stop(); }
    }
}
