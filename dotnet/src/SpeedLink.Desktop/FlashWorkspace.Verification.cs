using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

internal sealed record FlashVerificationRun(string Journal, FlashVerification Verification)
{
    internal string Caption => $"{Verification.ResetAt.ToLocalTime():G} · {Verification.Calibration}";
    public override string ToString() => Caption;

    internal string CompareBackup(string path)
    {
        var current = FlashVerification.Load(Journal, Verification.Mode);
        if (current != Verification)
            throw new InvalidDataException("The selected write has changed. Select it again before verifying.");
        return current.CompareBackup(path)
            ? "Match · Every byte matches the intended image."
            : "Mismatch · The captured ROM differs from the intended image.";
    }

    internal static bool IsFileError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or FormatException;
}

internal sealed partial class FlashWorkspace
{
    private readonly Button reviewInterruptedRun = new() { Content = "Review interrupted run", Visibility = Visibility.Collapsed };
    private readonly Button readForVerification = new() { Content = "Read for verification", Visibility = Visibility.Collapsed };
    private readonly TextBlock verificationStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted, Visibility = Visibility.Collapsed };
    private string? reviewedInterruptedJournal;
    internal event Action<FlashVerificationRun>? ReadVerificationRequested;

    private StackPanel CreateResultActions()
    {
        var panel = new StackPanel();
        var next = new WrapPanel();
        reviewInterruptedRun.Click += (_, _) => ReviewInterruptedResult();
        readForVerification.Click += async (_, _) => await ReadForCurrentResult();
        next.Children.Add(reviewInterruptedRun);
        next.Children.Add(readForVerification);
        panel.Children.Add(next);
        panel.Children.Add(verificationStatus);
        return panel;
    }

    private void RefreshResultActions()
    {
        string? journal = CurrentResultJournal();
        bool show = !active && journal is not null;
        reviewInterruptedRun.Visibility = show && workflow?.Stage == BenchFlashStage.RecoveryRequired &&
            !StringComparer.OrdinalIgnoreCase.Equals(journal, reviewedInterruptedJournal) ? Visibility.Visible : Visibility.Collapsed;
        readForVerification.Visibility = show && workflow?.Stage is BenchFlashStage.VerificationRequired or BenchFlashStage.WrittenUnverified
            ? Visibility.Visible : Visibility.Collapsed;
        reviewInterruptedRun.IsEnabled = readForVerification.IsEnabled = !IsWorking;
        verificationStatus.Visibility = string.IsNullOrWhiteSpace(verificationStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private string? CurrentResultJournal() => workflow?.RunFolder is string folder ? Path.Combine(folder, "journal.jsonl") : null;

    private void ReviewInterruptedResult()
    {
        if (IsWorking || workflow?.Stage != BenchFlashStage.RecoveryRequired || CurrentResultJournal() is not string journal) return;
        InterruptedRun interrupted;
        try { interrupted = DescribeRun(journal); }
        catch (Exception error) when (FlashVerificationRun.IsFileError(error))
        {
            SetVerificationMessage("Could not review this interrupted run · " + error.Message);
            return;
        }

        ResetProbe();
        selectedJournal = journal;
        if (IsRecovery) RefreshInterruptedRuns();
        else recoverMode.IsChecked = true;
        var saved = savedRuns.Items.OfType<InterruptedRun>().FirstOrDefault(run => StringComparer.OrdinalIgnoreCase.Equals(run.Journal, journal));
        if (saved is null)
        {
            savedRuns.ItemsSource = savedRuns.Items.OfType<InterruptedRun>().Prepend(interrupted).ToArray();
            saved = interrupted;
        }
        savedRuns.SelectedItem = saved;
        reviewedInterruptedJournal = journal;
        InvalidatePlan();
        SetVerificationMessage("Interrupted run selected. When the same ECU is ready, choose Check ECU state.");
        savedRuns.BringIntoView();
    }

    private async Task ReadForCurrentResult()
    {
        if (IsWorking || workflow?.Stage is not (BenchFlashStage.WrittenUnverified or BenchFlashStage.VerificationRequired) ||
            CurrentResultJournal() is not string journal) return;
        FlashVerificationRun? run = null;
        loading = true; Refresh(); HardwareUseChanged?.Invoke();
        try { run = await Task.Run(() => LoadVerificationRun(journal)); }
        catch (Exception error) when (FlashVerificationRun.IsFileError(error))
        { SetVerificationMessage("Could not open this write for verification · " + error.Message); }
        finally { loading = false; HardwareUseChanged?.Invoke(); Refresh(); }
        if (run is not null && IsVisible) ReadVerificationRequested?.Invoke(run);
    }

    private FlashVerificationRun LoadVerificationRun(string journal) => new(journal, FlashVerification.Load(journal, evidenceMode));

    private IReadOnlyList<FlashVerificationRun> FindVerificationRuns()
    {
        var runs = new List<FlashVerificationRun>();
        foreach (string journal in library.FlashJournals(vault, evidenceMode).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { runs.Add(LoadVerificationRun(journal)); }
            catch (Exception error) when (FlashVerificationRun.IsFileError(error)) { }
        }
        return runs.OrderByDescending(run => run.Verification.ResetAt).ToArray();
    }

    private async Task BrowseVerificationRuns()
    {
        if (IsWorking) return;
        FlashVerificationRun? selected = null;
        bool useReader = false;
        loading = true; Refresh(); HardwareUseChanged?.Invoke();
        SetVerificationMessage("Checking saved flash runs…");
        try
        {
            var runs = await Task.Run(FindVerificationRuns);
            if (!IsVisible)
            {
                SetVerificationMessage("");
                return;
            }
            if (runs.Count == 0)
            {
                SetVerificationMessage("No saved write with an acknowledged reset could be verified.");
                return;
            }
            (selected, useReader) = ChooseVerificationRun(runs);
            SetVerificationMessage("");
            if (selected is not null && !useReader)
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Choose a verified backup captured after the selected write",
                    Filter = "Verified backup|rom.bin"
                };
                if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                    await VerifySelectedBackup(selected, dialog.FileName);
            }
        }
        catch (Exception error) when (FlashVerificationRun.IsFileError(error))
        { SetVerificationMessage("Could not check saved writes · " + error.Message); }
        finally { loading = false; HardwareUseChanged?.Invoke(); Refresh(); }
        if (selected is not null && useReader) ReadVerificationRequested?.Invoke(selected);
    }

    private (FlashVerificationRun? Run, bool UseReader) ChooseVerificationRun(IReadOnlyList<FlashVerificationRun> runs)
    {
        var content = new StackPanel { Margin = new(18) };
        var window = new Window
        {
            Owner = Window.GetWindow(this), Title = "SpeedLink · Verify saved write", WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 650, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Background = UiTheme.Panel, Foreground = UiTheme.Text, Content = content
        };
        content.Children.Add(new TextBlock
        {
            Text = "Choose a saved write",
            FontSize = 20, FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "Select the write by date and calibration. Use a verified backup from the same ECU, captured after that write.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 12)
        });
        var choice = new ComboBox { ItemsSource = runs, Margin = new(0, 0, 0, 12), SelectedIndex = runs.Count == 1 ? 0 : -1 };
        System.Windows.Automation.AutomationProperties.SetName(choice, "Saved writes");
        content.Children.Add(choice);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancelChoice = new Button { Content = "Cancel", IsCancel = true };
        var browse = new Button { Content = "Choose verified backup…" };
        var read = new Button { Content = "Read for verification" };
        buttons.Children.Add(cancelChoice); buttons.Children.Add(browse); buttons.Children.Add(read);
        content.Children.Add(buttons);
        bool useReader = false;
        browse.Click += (_, _) => window.DialogResult = true;
        read.Click += (_, _) => { useReader = true; window.DialogResult = true; };
        choice.SelectionChanged += (_, _) => browse.IsEnabled = read.IsEnabled = choice.SelectedItem is FlashVerificationRun;
        browse.IsEnabled = read.IsEnabled = choice.SelectedItem is FlashVerificationRun;
        return window.ShowDialog() == true ? (choice.SelectedItem as FlashVerificationRun, useReader) : (null, false);
    }

    private async Task VerifySelectedBackup(FlashVerificationRun run, string path)
    {
        SetVerificationMessage($"Checking the verified backup for {run.Caption}…");
        try { ShowVerificationResult(run, await Task.Run(() => run.CompareBackup(path))); }
        catch (Exception error) when (FlashVerificationRun.IsFileError(error))
        { ShowVerificationResult(run, "Could not verify this write · " + error.Message); }
    }

    internal void ShowVerificationResult(FlashVerificationRun run, string message) => SetVerificationMessage($"{run.Caption} · {message}");

    private void SetVerificationMessage(string message)
    {
        verificationStatus.Text = message;
        RefreshResultActions();
        if (verificationStatus.IsVisible) verificationStatus.BringIntoView();
    }
}
