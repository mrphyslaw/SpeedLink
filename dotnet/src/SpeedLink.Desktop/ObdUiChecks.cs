using System.IO;
using System.Text.Json;
using System.Windows;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    internal async Task<object> ExerciseObdOffline(string output)
    {
        Show(); Navigate(ObdPage); UpdateLayout();
        var page = ShowObdWorkspace();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        Check(Resource("SpeedLink.ObdHelp.md").Contains("Ctrl+8", StringComparison.Ordinal), "Bundled diagnostics guide is missing.");
        Check(page.ScanButton.IsEnabled && !page.ClearButton.IsEnabled && !page.SaveButton.IsEnabled && image is null, "Offline diagnostics controls are wrong.");
        using var peer = new ObdUiPeer();
        bool locked = false;
        peer.CheckBusy = () => Dispatcher.Invoke(() => { Check(busy && !page.ScanButton.IsEnabled && !page.ClearButton.IsEnabled && !ConnectButton.IsEnabled && !ReadButton.IsEnabled, "Diagnostics did not lock competing operations."); locked = true; });
        AdapterBox.SelectedIndex = 1; UpdateLayout();
        Check(AdapterBox.SelectedIndex == 1, "Diagnostics adapter selection is not shared.");
        AdapterBox.SelectedIndex = 0; PortBox.Text = "COM_SYNTHETIC"; BaudBox.SelectedItem = 115200; UpdateLayout();
        Check(PortBox.Text == "COM_SYNTHETIC" && (int)BaudBox.SelectedItem == 115200, "Diagnostics serial settings are not shared.");
        offlineConnectionFactory = () => (peer, null, new("synthetic", peer.Adapter, "JM1GG000000000001", "L3M6EG000L3T60A1"));
        page.ScanButton.RaiseEvent(new(System.Windows.Controls.Button.ClickEvent));
        await (obdConnectWork ?? throw new InvalidOperationException("Connect & read codes did not start.")); UpdateLayout();
        Check(Pages.SelectedIndex == ObdPage && page.ScanButton.IsEnabled && DisconnectButton.IsEnabled && page.ClearButton.IsEnabled && image is null, "Diagnostics connection left the page or incorrectly required a ROM.");
        Check(page.LastScan?.Stored.Value?.Single().Code == "U0121" && page.CodesGrid.Items.Count == 2 && page.FrameGrid.Items.Count == 1, "Fault or frame results were lost.");
        var firstScan = page.LastScan;
        DisconnectButton.RaiseEvent(new(System.Windows.Controls.Button.ClickEvent)); await activeWork!; UpdateLayout();
        Check(transport is null && Pages.SelectedIndex == ObdPage && page.ScanButton.IsEnabled && !DisconnectButton.IsEnabled, "Disconnect left Diagnostics or failed to restore connection controls.");
        page.ScanButton.RaiseEvent(new(System.Windows.Controls.Button.ClickEvent)); await obdConnectWork!; UpdateLayout();
        Check(Pages.SelectedIndex == ObdPage && page.LastScan is not null && !ReferenceEquals(page.LastScan, firstScan), "Reconnect did not replace the previous scan on Diagnostics.");
        using var readSession = new ObdUiPeer { ReadAttempted = true };
        transport = readSession;
        Controls();
        Check(page.ScanButton.IsEnabled && Equals(page.ScanButton.Content, "Reconnect & read codes") &&
            page.ConnectionText.Text.Contains("Reconnect after a ROM read", StringComparison.Ordinal) && !page.ClearButton.IsEnabled,
            "Diagnostics did not offer an in-place action for the ROM-read session.");
        firstScan = page.LastScan;
        page.ScanButton.RaiseEvent(new(System.Windows.Controls.Button.ClickEvent));
        await obdConnectWork!;
        Check(readSession.Disposals == 1 && ReferenceEquals(transport, peer) && Pages.SelectedIndex == ObdPage &&
            page.LastScan is not null && !ReferenceEquals(firstScan, page.LastScan),
            "Reconnect and read did not close the prior session and refresh diagnostics in place.");
        offlineConnectionFactory = null;
        Check(page.LastScan!.Permanent.Status == ObdStatus.Unsupported && page.ServiceText.Text.Contains("Not supported", StringComparison.Ordinal), "Unsupported service became an empty success.");
        ObdReports.SaveNew(Path.Combine(output, "synthetic-report.json"), page.LastScan);
        var originalAppearance = appearance;
        foreach (var theme in new[] { Appearance.Light, Appearance.Dark })
        {
            UiTheme.Apply(theme, greenForHighValues); Width = 1200; Height = 780; UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            CaptureWorkbench(Path.Combine(output, "Diagnostics-" + theme + ".png"));
        }
        Width = MinWidth; Height = MinHeight; UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
        Check(page.CodesGrid.ActualWidth > 500 && page.CodesGrid.ActualHeight > 140 && page.ScanButton.IsVisible && page.SaveButton.IsVisible, "Minimum diagnostics layout is clipped.");
        Check(page.CodesGrid.Columns.Sum(c => c.ActualWidth) <= page.CodesGrid.ActualWidth, "Diagnostic columns did not fit the smaller window.");
        CaptureWorkbench(Path.Combine(output, "Diagnostics-Minimum.png"));
        UiTheme.Apply(originalAppearance, greenForHighValues);
        peer.PermanentSupported = true;
        await ClearObd(); UpdateLayout();
        Check(peer.Clears == 1 && page.LastScan!.Stored.Value!.Count == 0 && page.LastScan.Permanent.Value!.Count == 1, "Clear/rescan did not preserve permanent codes.");
        var before = Directory.GetFiles(Path.Combine(vault, "obd"), "*-before.json");
        Check(before.Length == 1 && File.ReadAllText(before[0]).Contains("U0121", StringComparison.Ordinal), "Before-clear report was not retained.");
        Check(page.StatusText.Text.Contains("acknowledged", StringComparison.OrdinalIgnoreCase), "Clear acknowledgement is missing.");
        peer.LoseAcknowledgement = true;
        await ClearObd(); UpdateLayout();
        Check(peer.Clears == 2 && transport is null && !page.ClearButton.IsEnabled && page.StatusText.Text.Contains("unknown", StringComparison.OrdinalIgnoreCase), "Unknown clear result did not require reconnect.");
        Check(HandleApplicationShortcut(ShortcutAction.Diagnostics, new()) && Pages.SelectedIndex == ObdPage, "Diagnostics navigation shortcut failed.");
        var custom = EditorPane.Shortcuts.Copy();
        Check(custom.TryAssign(ShortcutAction.Diagnostics, new(System.Windows.Input.Key.D8, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift), out _), "Diagnostics shortcut is not assignable.");
        var legacy = EditorPane.Shortcuts.Copy(); legacy.TryAssign(ShortcutAction.Diagnostics, null, out _);
        var oldChord = new ShortcutKey(System.Windows.Input.Key.D8, System.Windows.Input.ModifierKeys.Control);
        Check(legacy.TryAssign(ShortcutAction.FindMap, oldChord, out _), "Could not prepare the shortcut migration check.");
        string legacyPath = Path.Combine(output, "legacy-shortcuts.json"); legacy.Save(legacyPath);
        var savedKeys = JsonSerializer.Deserialize<Dictionary<string, ShortcutKey?>>(File.ReadAllText(legacyPath))!;
        savedKeys.Remove(nameof(ShortcutAction.Diagnostics)); File.WriteAllText(legacyPath, JsonSerializer.Serialize(savedKeys));
        var migrated = ShortcutPreferences.Load(legacyPath);
        Check(migrated[ShortcutAction.FindMap] == oldChord && migrated[ShortcutAction.Diagnostics] is null && migrated[ShortcutAction.SaveRom] == EditorPane.Shortcuts[ShortcutAction.SaveRom], "New diagnostics shortcut overwrote existing user bindings.");
        page.ResetForConnection();
        Check(page.LastScan is null && page.CodesGrid.Items.Count == 0 && !page.SaveButton.IsEnabled, "Previous ECU results survived a new connection.");
        return new { passed = true, hardwareAccessed = false, readCodes = true, clearAndRescan = true, beforeReportRetained = true,
            permanentCodesRemain = true, controlsLocked = locked, romNotRequired = image is null, unknownOutcome = true,
            navigationShortcut = true, shortcutMigration = true, newConnectionClearsResults = true, samePageConnectReadDisconnect = true, sharedAdapterSettings = true, capturedThemes = new[] { "Light", "Dark" }, pages = Pages.Items.Count };
    }
    private sealed class ObdUiPeer : IObdTransport
    {
        public string Mode => "synthetic"; public string Adapter => "Synthetic OBD ECU · no hardware"; public int ReadChunkSize => 256;
        public bool ReadAttempted { get; set; } public int Clears, Disposals; public bool PermanentSupported, LoseAcknowledgement; public Action? CheckBusy;
        public byte[] Exchange(byte[] request) => throw new InvalidOperationException("Synthetic OBD check used the ROM reader.");
        public byte[] ExchangeObd(byte[] request, bool clearConfirmed = false)
        {
            ObdProtocol.CheckAllowed(request, clearConfirmed); CheckBusy?.Invoke();
            if (request.AsSpan().SequenceEqual(new byte[] { 1, 1 })) return [0x41, 1, (byte)(Clears == 0 ? 0x81 : 0), 7, 0xE1, (byte)(Clears == 0 ? 0 : 0xE1)];
            if (request.AsSpan().SequenceEqual(new byte[] { 1, 12 })) return [0x41, 12, 0, 0];
            if (request[0] == 3) return Clears == 0 ? [0x43, 1, 0xC1, 0x21] : [0x43, 0];
            if (request[0] == 7) return Clears == 0 ? [0x47, 1, 0xC1, 0x21] : [0x47, 0];
            if (request[0] == 10) { if (!PermanentSupported) throw new NegativeResponseException(10, 0x11); return [0x4A, 1, 1, 0x33]; }
            if (request[0] == 2) return request[1] switch { 0 => [0x42, 0, 0, 0x48, 0, 0, 0], 2 => Clears == 0 ? [0x42, 2, 0, 0xC1, 0x21] : [0x42, 2, 0, 0, 0], 5 => [0x42, 5, 0, 130], _ => throw new NegativeResponseException(2, 0x31) };
            if (request[0] == 4) { Clears++; if (LoseAcknowledgement) throw new TransportException("Synthetic lost acknowledgement"); return [0x44]; }
            throw new InvalidOperationException("Unexpected synthetic OBD request.");
        }
        public void Dispose() { Disposals++; }
    }
}
