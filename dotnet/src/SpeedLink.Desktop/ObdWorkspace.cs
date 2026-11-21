using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

internal sealed class ObdWorkspace : UserControl
{
    internal event Action? ScanRequested, ClearRequested, StopRequested, SaveRequested, ConnectRequested, OpenReportsRequested;
    internal ObdScan? LastScan { get; private set; }
    internal readonly Button ScanButton = new() { Content = "Read codes" }, ClearButton = new() { Content = "Clear codes…" },
        StopButton = new() { Content = "Stop" }, SaveButton = new() { Content = "Save report…" };
    private bool connectedHere;
    internal readonly TextBlock ConnectionText = new(), StatusText = new() { Text = "Select the shared adapter above and choose Connect & read codes. No ROM file is required." },
        SummaryText = new(), ServiceText = new(), FreezeText = new();
    internal readonly DataGrid CodesGrid = Grid(), ReadinessGrid = Grid(), FrameGrid = Grid();
    internal ObdWorkspace()
    {
        var root = new DockPanel { Margin = new(20), LastChildFill = true };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Engine diagnostics", FontSize = 25, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = "Engine fault codes, readiness and freeze-frame data · no ROM required", Margin = new(0, 5, 0, 10), TextWrapping = TextWrapping.Wrap });
        ConnectionText.TextWrapping = StatusText.TextWrapping = SummaryText.TextWrapping = ServiceText.TextWrapping = FreezeText.TextWrapping = TextWrapping.Wrap;
        ConnectionText.Margin = new(0, 0, 0, 10); top.Children.Add(ConnectionText);
        var actions = new WrapPanel { Margin = new(0, 0, 0, 10) };
        ScanButton.SetResourceReference(StyleProperty, "PrimaryButton");
        foreach (var button in new[] { ScanButton, ClearButton, StopButton, SaveButton }) actions.Children.Add(button);
        var reports = new Button { Content = "Open reports" }; reports.Click += (_, _) => OpenReportsRequested?.Invoke(); actions.Children.Add(reports);
        ScanButton.Click += (_, _) => { if (connectedHere) ScanRequested?.Invoke(); else ConnectRequested?.Invoke(); }; ClearButton.Click += (_, _) => ClearRequested?.Invoke();
        StopButton.Click += (_, _) => StopRequested?.Invoke(); SaveButton.Click += (_, _) => SaveRequested?.Invoke();
        top.Children.Add(actions);
        StatusText.Margin = new(0, 0, 0, 8); top.Children.Add(StatusText);
        SummaryText.FontWeight = FontWeights.SemiBold; SummaryText.Margin = new(0, 0, 0, 6); top.Children.Add(SummaryText);
        ServiceText.Margin = new(0, 0, 0, 12); top.Children.Add(ServiceText);
        var tabs = new TabControl();
        tabs.SetResourceReference(BackgroundProperty, "PanelBrush"); tabs.SetResourceReference(BorderBrushProperty, "LineBrush");
        Columns(CodesGrid, ("Code", "Code", 90), ("Description", "Description", 0), ("Status", "Status", 135));
        Columns(ReadinessGrid, ("Monitor", "Monitor", 0), ("Status", "Status", 180));
        Columns(FrameGrid, ("Parameter", "Name", 0), ("Value", "Value", 135), ("Unit", "Unit", 90), ("Status", "Status", 180));
        tabs.Items.Add(new TabItem { Header = "Fault codes", Content = CodesGrid });
        tabs.Items.Add(new TabItem { Header = "Readiness", Content = ReadinessGrid });
        var frame = new DockPanel(); DockPanel.SetDock(FreezeText, Dock.Top); FreezeText.Margin = new(8); frame.Children.Add(FreezeText); frame.Children.Add(FrameGrid);
        tabs.Items.Add(new TabItem { Header = "Freeze frame", Content = frame }); root.Children.Add(tabs); Content = root;
        SetAvailability(true, false, false, null);
    }
    private static DataGrid Grid() => new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new(0, 8, 0, 0), MinHeight = 100, RowHeight = double.NaN };
    private static void Columns(DataGrid grid, params (string Header, string Path, int Width)[] columns)
    {
        foreach (var c in columns)
        {
            var text = new Style(typeof(TextBlock)); text.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)); text.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4)));
            grid.Columns.Add(new DataGridTextColumn { Header = c.Header, Binding = new Binding(c.Path), ElementStyle = text,
                Width = c.Width == 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new(c.Width), MinWidth = c.Width == 0 ? 190 : c.Width });
        }
    }
    internal void SetAvailability(bool idle, bool connected, bool readSession, EcuIdentity? identity, bool canConnect = false)
    {
        connectedHere = connected;
        ScanButton.Content = connected ? "Read codes" : "Connect & read codes";
        ScanButton.IsEnabled = idle && (connected ? !readSession : canConnect);
        ClearButton.IsEnabled = idle && connected && !readSession;
        StopButton.IsEnabled = !idle && connected; SaveButton.IsEnabled = idle && LastScan is not null;
        ConnectionText.Text = connected ? $"Connected · {identity?.Calibration ?? "Calibration unavailable"}" : "Adapter disconnected · use the shared adapter controls above.";
        ConnectionText.ToolTip = identity?.Adapter;
        ScanButton.ToolTip = ClearButton.ToolTip = readSession ? "Disconnect and reconnect after a ROM read before using OBD diagnostics." : "Use the connected engine ECU. Stop logging or other adapter operations first.";
    }
    internal void ShowScan(ObdScan scan)
    {
        LastScan = scan;
        var rows = new List<object>();
        void Add(ObdResult<IReadOnlyList<ObdCode>> section, string status)
        { if (section.Value is not null) rows.AddRange(section.Value.Select(c => (object)new { c.Code, c.Description, Status = status })); }
        Add(scan.Stored, "Stored"); Add(scan.Pending, "Pending"); Add(scan.Permanent, "Permanent"); CodesGrid.ItemsSource = rows;
        string Count(ObdResult<IReadOnlyList<ObdCode>> s) => s.Status == ObdStatus.Supported ? (s.Value?.Count ?? 0).ToString(CultureInfo.InvariantCulture) : Label(s.Status);
        ServiceText.Text = $"Stored: {Count(scan.Stored)} · Pending: {Count(scan.Pending)} · Permanent: {Count(scan.Permanent)}";
        var issues = new[] { ("Readiness", scan.Readiness.Status, scan.Readiness.Detail), ("Stored", scan.Stored.Status, scan.Stored.Detail),
            ("Pending", scan.Pending.Status, scan.Pending.Detail), ("Permanent", scan.Permanent.Status, scan.Permanent.Detail), ("Freeze frame", scan.FreezeFrame.Status, scan.FreezeFrame.Detail) }
            .Where(s => s.Item2 != ObdStatus.Supported).Select(s => $"{s.Item1}: {s.Item2} · {s.Item3}");
        string details = string.Join("\n", issues);
        ServiceText.ToolTip = string.IsNullOrEmpty(details) ? "All requested services returned valid responses." : details;
        if (!scan.ConnectionUsable) ServiceText.Text += "\nThe scan was interrupted. Reconnect before reading again.";
        if (scan.Readiness.Value is { } readiness)
        {
            SummaryText.Text = $"Check-engine light: {(readiness.MilOn ? "ON" : "OFF")} · ECU-reported stored count: {readiness.StoredCodeCount} · {readiness.IgnitionType}";
            ReadinessGrid.ItemsSource = readiness.Monitors.Select(m => new { Monitor = m.Name, Status = !m.Supported ? "Not supported" : m.Complete == true ? "Complete" : "Incomplete" });
        }
        else { SummaryText.Text = "Check-engine light and readiness: " + Label(scan.Readiness.Status); ReadinessGrid.ItemsSource = null; }
        if (scan.FreezeFrame.Value is { } freeze)
        {
            FreezeText.Text = (freeze.TriggerCode is null ? "" : "Trigger: " + freeze.TriggerCode + " · ") + freeze.Detail;
            FrameGrid.ItemsSource = freeze.Values.Select(v => new { v.Name, Value = v.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—", v.Unit,
                Status = v.Status == ObdStatus.Supported ? "Captured" : Label(v.Status) });
        }
        else { FreezeText.Text = "Freeze frame: " + Label(scan.FreezeFrame.Status); FreezeText.ToolTip = scan.FreezeFrame.Detail; FrameGrid.ItemsSource = null; }
        StatusText.Text = $"Scan captured {scan.CapturedUtc.ToLocalTime():g}. " + (scan.ConnectionUsable ? "Permanent codes clear only after the ECU confirms the repair." : "Scan incomplete. Reconnect before trying again.");
    }
    internal void ResetForConnection()
    {
        LastScan = null; CodesGrid.ItemsSource = ReadinessGrid.ItemsSource = FrameGrid.ItemsSource = null;
        SummaryText.Text = ServiceText.Text = FreezeText.Text = ""; ServiceText.ToolTip = FreezeText.ToolTip = null;
        StatusText.Text = "Connected to a new adapter session. Choose Read codes to capture this ECU's current results.";
        SaveButton.IsEnabled = false;
    }
    internal void ShowDisconnected()
    {
        if (LastScan is null) StatusText.Text = "Adapter disconnected. Use the shared controls above to connect and read codes.";
    }
    private static string Label(ObdStatus status) => status switch { ObdStatus.Unsupported => "Not supported", ObdStatus.Rejected => "ECU declined request", ObdStatus.CommunicationError => "Connection error", ObdStatus.NotAttempted => "Not read", _ => "Available" };
}
