using System.IO;
using System.Windows;
using System.Windows.Controls;
using SpeedLink.Core;
using SpeedLink.J2534;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private const int FlashPage = 6;
    private FlashBackupLibrary flashBackups = null!;
    private FlashWorkspace? flashWorkspace;
    private string? handoffBackup;
    private FlashVerificationRun? pendingFlashRead;
    private FlashVerificationRun? completedFlashRead;
    private string? flashReadResult;
    private StackPanel? flashReadBanner;
    private TextBlock? flashReadMessage;
    private Button? returnToFlashResult;

    private FlashWorkspace ShowFlashWorkspace(FlashWorkspace? workspace = null)
    {
        if (flashWorkspace is null)
        {
            RememberFlashLocation();
            flashWorkspace = workspace ?? new(Path.Combine(vault, "flashing"), flashBackups);
            flashWorkspace.HardwareUseChanged += () => { if (!busy) StatusText.Text = flashWorkspace.ActivityMessage; Controls(); };
            flashWorkspace.ConnectionRequested += OpenSharedConnection;
            flashWorkspace.ReadVerificationRequested += ShowReadForVerification;
            if (workspace is null)
            {
                flashWorkspace.AttachSharedAdapter(PrepareSharedProgramming);
                SharedAdapterChanged();
            }
            flashWorkspace.ProgressChanged += value => { StatusText.Text = value.Message; ProgressBar.Value = value.Fraction * 100; };
            FlashTab.Content = flashWorkspace;
            flashWorkspace.SetConnectionUnavailable(busy || closing || exportingReport);
        }
        return flashWorkspace;
    }

    private void Gen1Flash_Click(object sender, RoutedEventArgs e) => Navigate(FlashPage);

    private void ShowReadForVerification(FlashVerificationRun run)
    {
        EnsureFlashReadBanner();
        pendingFlashRead = run;
        completedFlashRead = null;
        flashReadResult = null;
        returnToFlashResult!.Visibility = Visibility.Collapsed;
        flashReadMessage!.Text = $"Verify the write from {run.Caption}. Connect to the same ECU and choose Read & verify backup. SpeedLink will compare the new backup with this write.";
        flashReadBanner!.Visibility = Visibility.Visible;
        Navigate(0);
    }

    private void EnsureFlashReadBanner()
    {
        if (flashReadBanner is not null) return;
        flashReadBanner = new StackPanel { Margin = new(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        flashReadMessage = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        flashReadBanner.Children.Add(flashReadMessage);
        var actions = new WrapPanel { Margin = new(0, 4, 0, 0) };
        returnToFlashResult = new Button { Content = "Show flash result", Visibility = Visibility.Collapsed };
        returnToFlashResult.Click += (_, _) =>
        {
            if (completedFlashRead is not { } run || flashReadResult is not { } result) return;
            Navigate(FlashPage);
            ShowFlashWorkspace().ShowVerificationResult(run, result);
            DismissFlashRead();
        };
        var dismiss = new Button { Content = "Dismiss", Margin = new(6, 0, 0, 0) };
        dismiss.Click += (_, _) => DismissFlashRead();
        actions.Children.Add(returnToFlashResult);
        actions.Children.Add(dismiss);
        flashReadBanner.Children.Add(actions);
        ((Panel)DiagnosticStatusText.Parent).Children.Insert(0, flashReadBanner);
    }

    private void DismissFlashRead()
    {
        pendingFlashRead = completedFlashRead = null;
        flashReadResult = null;
        if (flashReadBanner is not null) flashReadBanner.Visibility = Visibility.Collapsed;
    }

    private async Task VerifyPendingFlashRead(string path)
    {
        if (pendingFlashRead is not { } run || flashReadMessage is null || returnToFlashResult is null) return;
        flashReadMessage.Text = $"Comparing this backup with the write from {run.Caption}…";
        try
        {
            string result = await Task.Run(() => run.CompareBackup(path));
            if (!ReferenceEquals(pendingFlashRead, run)) return;
            pendingFlashRead = null;
            completedFlashRead = run;
            flashReadResult = result;
            flashReadMessage.Text = $"{run.Caption} · {result}";
            returnToFlashResult.Visibility = Visibility.Visible;
        }
        catch (Exception error) when (FlashVerificationRun.IsFileError(error))
        {
            if (ReferenceEquals(pendingFlashRead, run))
                flashReadMessage.Text = $"Backup saved. Could not verify the write from {run.Caption} · {error.Message}";
        }
    }

    private void OpenBackupInFlash_Click(object sender, RoutedEventArgs e)
    {
        if (handoffBackup is null || !FlashBackupButton.IsEnabled) return;
        Navigate(FlashPage);
        var passThru = AdapterBox.SelectedIndex > 0;
        ShowFlashWorkspace().UseBackup(handoffBackup, passThru ? null : PortBox.Text.Trim(),
            passThru ? DriverBox.SelectedItem as J2534Driver : null);
    }

    private void OfferBackupForFlash(BackupResult result)
    {
        if (result.Inspection.ProfileValid && result.Inspection.ChecksumValid == true &&
            result.Inspection.Calibration is string id && BenchFlashWorkflow.IsNativeWriterVerified(id))
        {
            handoffBackup = result.Path;
            FlashBackupButton.Visibility = Visibility.Visible;
            RememberFlashLocation();
        }
        else
        {
            handoffBackup = null;
            FlashBackupButton.Visibility = Visibility.Collapsed;
        }
    }

    private void RememberFlashLocation()
    {
        try { flashBackups.Remember(vault); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Log("Could not share this data location across SpeedLink versions: " + error.Message); }
    }

    internal async Task<object> SmokeFlashWorkspace(string output, BenchFlashUiPeerState peer, string backup,
        RomInspector inspector, TuningCodeGuard guard)
    {
        // Register the old installation's operation folder, then let the new
        // workspace resolve the moved target and the interrupted run itself.
        flashBackups.Remember(Path.GetDirectoryName(backup)!);
        var wrongChassis = new EcuIdentity("simulator", "fixture", null, "L3M6EG000") { ChassisId = "GG3P999999" };
        var wrongCalibration = wrongChassis with { Calibration = "L38KEH000", ChassisId = "GG3P123456" };
        if (flashBackups.Candidates("simulator", wrongChassis, null).Any() ||
            flashBackups.Candidates("simulator", wrongCalibration, null).Any())
            throw new InvalidOperationException("A different chassis or calibration matched the saved backup.");
        var page = ShowFlashWorkspace(new(Path.Combine(vault, "flashing"), flashBackups, peer.Open, (inspector, guard)));
        Show();
        Navigation.Children.OfType<RadioButton>().Single(button => (string)button.Tag == "flash").IsChecked = true;
        UpdateLayout();
        if (Pages.SelectedIndex != FlashPage || !page.IsVisible)
            throw new InvalidOperationException("The top navigation did not open Flash & Recovery.");
        page.HardwareUseChanged += () =>
        {
            if (page.IsWorking && (ConnectionSettingsButton.IsEnabled || ConnectButton.IsEnabled || DisconnectButton.IsEnabled))
                throw new InvalidOperationException("Shared connection controls were enabled during flash preparation or programming.");
        };
        var report = await page.Smoke(backup, output, peer, async (journal, freshBackup) =>
        {
            if (pendingFlashRead?.Journal != journal || Pages.SelectedIndex != 0 || flashReadBanner?.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Read for verification did not open the reader with the selected write.");
            Navigate(1);
            Navigate(0);
            await VerifyPendingFlashRead(freshBackup);
            if (completedFlashRead?.Journal != journal || pendingFlashRead is not null ||
                flashReadResult?.StartsWith("Match ·", StringComparison.Ordinal) != true ||
                returnToFlashResult?.Visibility != Visibility.Visible)
                throw new InvalidOperationException("The fresh backup was not compared with the retained write.");
            returnToFlashResult.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (Pages.SelectedIndex != FlashPage || flashReadBanner.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Could not return to the saved flash result from the reader.");
        });
        Navigate(0);
        OfferBackupForFlash(new(backup, inspector.Inspect(File.ReadAllBytes(backup))));
        Controls();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        if (!FlashBackupButton.IsEnabled || !FlashBackupButton.IsVisible)
            throw new InvalidOperationException("Connection did not offer its verified Gen 1 backup.");
        FlashBackupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (Pages.SelectedIndex != FlashPage || page.PreferredBackup != backup)
            throw new InvalidOperationException("Connection did not pass its backup into Flash & Recover.");
        return report;
    }
}
