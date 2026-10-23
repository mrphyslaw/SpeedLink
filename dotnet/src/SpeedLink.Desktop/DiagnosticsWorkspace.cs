using System.IO;
using System.Reflection;
using System.Windows;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private DiagnosticSession? diagnosticSession;
    private bool exportingReport;
    private string? diagnosticStorageWarning;
    private string DiagnosticRoot => Path.Combine(vault, "diagnostics");
    private async Task BeginDiagnostics()
    {
        if (diagnosticSession is not null) { await diagnosticSession.DisposeAsync(); diagnosticSession = null; }
        diagnosticStorageWarning = null;
        try
        {
            string build = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Version;
            diagnosticSession = new(DiagnosticRoot, Version, build);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            diagnosticStorageWarning = "Diagnostics could not be saved: " + new DiagnosticRedactor().Text(error.Message);
            Log(diagnosticStorageWarning);
        }
    }
    private void DiagnosticControls(bool idle)
    {
        try { ExportReportButton.IsEnabled = idle && SupportReport.Sessions(DiagnosticRoot).Count > 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ExportReportButton.IsEnabled = false; diagnosticStorageWarning = new DiagnosticRedactor().Text(error.Message); }
        string? warning = diagnosticStorageWarning ?? diagnosticSession?.StorageError;
        long dropped = diagnosticSession?.DroppedEvents ?? 0;
        DiagnosticStatusText.Text = warning is not null ? "Diagnostic storage warning: " + warning : dropped > 0
            ? $"Diagnostic capture could not keep up · {dropped:N0} events omitted. Include the report when requesting help."
            : "Diagnostics are captured automatically · export a support report after a problem, even after restarting.";
        DiagnosticStatusText.Foreground = warning is not null || dropped > 0 ? UiTheme.Amber : UiTheme.Muted;
    }
    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closing || exportingReport) return;
        exportingReport = true; Controls();
        try
        {
            try { if (diagnosticSession is not null) await diagnosticSession.FlushAsync(); }
            catch (Exception error) when (error is IOException or TimeoutException) { Log("Diagnostic storage warning: " + error.Message); }
            if (!closing) new SupportReportWindow(DiagnosticRoot) { Owner = this }.ShowDialog();
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Cannot open support report", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { exportingReport = false; Controls(); }
    }
}
