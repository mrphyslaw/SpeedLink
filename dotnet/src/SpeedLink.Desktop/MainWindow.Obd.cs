using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SpeedLink.Core;
using SpeedLink.Hardware;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private const int ObdPage = 7;
    private ObdWorkspace? obdWorkspace;
    private Task? obdConnectWork;
    private Func<(IReadTransport Link, FtdiSerialConnection? Connection, EcuIdentity Identity)>? offlineConnectionFactory;
    private bool ObdAvailable => transport is IObdTransport && (transport is not DiagnosticTransport traced || traced.Inner is IObdTransport);
    private ObdWorkspace ShowObdWorkspace()
    {
        if (obdWorkspace is null)
        {
            obdWorkspace = new(); ObdTab.Content = obdWorkspace;
            obdWorkspace.ConnectRequested += () => obdConnectWork = ConnectObd();
            obdWorkspace.ScanRequested += async () => await ScanObd();
            obdWorkspace.ClearRequested += async () =>
            {
                if (MessageBox.Show(this, "Ignition must be on and the engine stopped.\n\nClearing removes stored/pending emissions faults and freeze-frame data and resets readiness monitors. Faults can return. Permanent codes may remain.\n\nSpeedLink will first read and save a before-clear report, check engine speed, send Clear once, then scan again.\n\nClear engine ECU codes?", "Clear diagnostic codes", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                    await ClearObd();
            };
            obdWorkspace.StopRequested += () => stop?.Cancel();
            obdWorkspace.SaveRequested += SaveObdReport;
            obdWorkspace.OpenReportsRequested += () => { try { string folder = Path.Combine(vault, "obd"); Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); } catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { StatusText.Text = e.Message; } };
        }
        ObdControls(!busy && !closing && !exportingReport && flashWorkspace?.HardwareInUse != true);
        return obdWorkspace;
    }
    private async Task ConnectObd()
    {
        var page = ShowObdWorkspace(); page.StatusText.Text = "Connecting and identifying the engine ECU…";
        await ConnectAdapter();
        if (ObdAvailable) await ScanObd();
        else page.StatusText.Text = StatusText.Text;
    }
    private void ObdControls(bool idle) => obdWorkspace?.SetAvailability(idle, ObdAvailable, transport?.ReadAttempted == true, identity, ConnectButton.IsEnabled);
    private ObdDiagnostics ObdWorkflow() => ObdAvailable && transport is IObdTransport link && !link.ReadAttempted
        ? new(link, identity, ObdDescriptions.From(definitionSnapshot.Editors, identity?.Calibration))
        : throw new InvalidOperationException("Connect an adapter, and reconnect after any ROM read, before using engine diagnostics.");
    private Task ScanObd() => Run(async () =>
    {
        var page = ShowObdWorkspace(); page.StatusText.Text = "Reading engine ECU faults, readiness and available freeze frame…";
        try
        {
        var workflow = ObdWorkflow(); var scan = await Task.Run(() => workflow.Scan(stop!.Token));
        page.ShowScan(scan);
        if (!scan.ConnectionUsable) await Disconnect();
        StatusText.Text = scan.ConnectionUsable ? "Diagnostic scan complete · review each service status" : "Diagnostic scan incomplete · reconnect before retrying";
        Log(StatusText.Text);
        }
        catch (OperationCanceledException) { page.StatusText.Text = "Scan stopped. Any previously displayed results are from the last completed scan."; throw; }
        catch (Exception e) { page.StatusText.Text = "Scan stopped · " + e.Message; throw; }
    });
    private Task ClearObd() => Run(async () =>
    {
        var page = ShowObdWorkspace(); page.StatusText.Text = "Reading and retaining current fault information before clearing…";
        try
        {
        var workflow = ObdWorkflow(); var result = await Task.Run(() => workflow.Clear(Path.Combine(vault, "obd"), true, stop!.Token));
        page.ShowScan(result.After ?? result.Before);
        page.StatusText.Text = result.Detail + "\nBefore-clear report retained. Use Open reports to find it.";
        if (!result.ConnectionUsable) await Disconnect();
        StatusText.Text = result.Detail; Log(result.Detail);
        }
        catch (OperationCanceledException) { page.StatusText.Text = "Clear workflow stopped before a clear request was sent."; throw; }
        catch (ObdPreflightException e) { page.StatusText.Text = e.Message; if (!e.ConnectionUsable) await Disconnect(); throw; }
        catch (Exception e) { page.StatusText.Text = "Clear workflow stopped · " + e.Message; throw; }
    });
    private void SaveObdReport()
    {
        if (busy || obdWorkspace?.LastScan is not { } scan) return;
        var dialog = new SaveFileDialog { Title = "Save engine diagnostic report (includes vehicle identity)", Filter = "Diagnostic report (*.json)|*.json", FileName = "SpeedLink-diagnostics-" + scan.CapturedUtc.ToString("yyyyMMdd-HHmmss") + ".json", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try { ObdReports.SaveNew(dialog.FileName, scan); StatusText.Text = "Diagnostic report saved · " + dialog.FileName; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { StatusText.Text = "Report could not be saved · " + e.Message; }
    }
}
