using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private sealed record SavedBackupRow(JournalInspection Journal, string Folder, bool HasCapture)
    {
        public string Date => Journal.StartedAt?.ToLocalTime().ToString("g") ?? "—";
        public string Calibration => Journal.Calibration ?? "—";
        public string Source => Journal.Mode switch { "hardware" => "ECU", "simulator" => "Simulation", _ => "—" };
        public string Status => !Journal.Valid ? "Damaged journal" : !Journal.Complete ? "Interrupted / stopped" :
            HasCapture ? "Verified read" : "Backup files missing";
        public string RomPath => Path.Combine(Folder, "rom.bin");

        public static SavedBackupRow FromJournal(JournalInspection journal)
        {
            string folder = Path.GetDirectoryName(journal.Path)!;
            bool hasCapture = journal is { Valid: true, Complete: true, Operation: "read_backup" } &&
                new[] { "rom.bin", "backup.json", "pass1.read", "pass2.read" }.All(file => File.Exists(Path.Combine(folder, file)));
            return new(journal, folder, hasCapture);
        }
    }

    private void RefreshHistory()
    {
        var selected = HistoryGrid.SelectedItem as SavedBackupRow;
        try
        {
            var rows = store.Scan(vault).Where(item => item.Operation == "read_backup" || !item.Valid)
                .Select(SavedBackupRow.FromJournal).OrderByDescending(row => row.Journal.StartedAt).ToArray();
            HistoryGrid.ItemsSource = rows;
            HistoryGrid.SelectedItem = rows.FirstOrDefault(row => row.Folder == selected?.Folder) ?? rows.FirstOrDefault();
            HistoryNote.Text = rows.Length == 0 ? "No backups yet. Use ECU → Read backup to create one." :
                $"{rows.Length:N0} saved read(s). Verified backups are checked again before opening.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            HistoryNote.Text = "Could not refresh saved backups. " + error.Message;
        }
        RefreshBackupActions();
    }

    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();

    private void BackupSelection_Changed(object sender, SelectionChangedEventArgs e) => RefreshBackupActions();

    private void RefreshBackupActions(bool? idle = null)
    {
        if (OpenBackupInEditorButton is null || OpenBackupFolderButton is null || HistoryGrid is null) return;
        bool ready = idle ?? (!busy && !closing && !exportingReport && flashWorkspace?.IsWorking != true);
        var selected = HistoryGrid.SelectedItem as SavedBackupRow;
        OpenBackupInEditorButton.IsEnabled = ready && selected?.HasCapture == true;
        OpenBackupFolderButton.IsEnabled = ready && selected is not null;
        OpenBackupFolderButton.ToolTip = selected?.Folder;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (OpenBackupFolderButton.IsEnabled && HistoryGrid.SelectedItem is SavedBackupRow selected)
            TryFile(() => Process.Start(new ProcessStartInfo(selected.Folder) { UseShellExecute = true }));
    }

    private void OpenSavedBackupInEditor_Click(object sender, RoutedEventArgs e)
    {
        if (!OpenBackupInEditorButton.IsEnabled || HistoryGrid.SelectedItem is not SavedBackupRow selected) return;
        TryFile(() =>
        {
            var backup = store.Load(selected.RomPath);
            if (EditorPane.TryOpenRom(selected.RomPath, backup.Data)) Navigate(1);
        });
    }

    private async Task CheckSavedBackupLibrary(string syntheticRom)
    {
        byte[] data = File.ReadAllBytes(syntheticRom);
        var inspection = EditorPane.TestInspector.Inspect(data);
        var ecu = new EcuIdentity("simulator", "Offline backup", null, inspection.Calibration);
        string hash = RomInspector.Hash(data);
        var finished = store.CreateOperation(vault, "simulator", "read_backup");
        string path;
        using (finished.Journal)
        {
            finished.Journal.Append("identity", new() { ["identity"] = ecu.ToManifest() });
            foreach (string filename in new[] { "pass1.read", "pass2.read" })
                DurableStorage.NewFile(store.FileSystem, Path.Combine(finished.Folder, filename), data);
            path = store.Seal(finished.Folder, data, ecu, inspection, hash, hash, "simulator");
            finished.Journal.Append("completed", new() { ["sha256"] = hash });
        }
        var stopped = store.CreateOperation(vault, "simulator", "read_backup");
        using (stopped.Journal)
        {
            stopped.Journal.Append("identity", new() { ["identity"] = ecu.ToManifest() });
            stopped.Journal.RecordStopped(new() { ["reason"] = "Offline interrupted read" });
        }

        Navigate(5);
        RefreshHistory();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var rows = HistoryGrid.Items.Cast<SavedBackupRow>().ToArray();
        var partial = rows.Single(row => row.Folder == stopped.Folder);
        var verified = rows.Single(row => row.Folder == finished.Folder);
        HistoryGrid.SelectedItem = partial;
        if (partial.Status != "Interrupted / stopped" || partial.Date == "—" || partial.Calibration != ecu.Calibration ||
            OpenBackupInEditorButton.IsEnabled || !OpenBackupFolderButton.IsEnabled || !Equals(OpenBackupFolderButton.ToolTip, stopped.Folder))
            throw new InvalidOperationException("The selected interrupted read did not expose its date, calibration and exact folder.");

        HistoryGrid.SelectedItem = verified;
        if (verified.Status != "Verified read" || !OpenBackupInEditorButton.IsEnabled)
            throw new InvalidOperationException("The completed backup could not be opened from its list row.");
        OpenBackupInEditorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (Pages.SelectedIndex != 1 || EditorPane.Document?.SourcePath != path || !File.ReadAllBytes(path).AsSpan().SequenceEqual(data))
            throw new InvalidOperationException("The selected verified backup did not open in Editor with its captured bytes intact.");
    }
}
