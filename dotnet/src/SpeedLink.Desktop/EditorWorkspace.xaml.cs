using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public sealed record EditorRow(int Index, string[] Values);
public partial class EditorWorkspace : UserControl
{
    private GridLength browserWidth = new(245);
    private bool focusedLayout;
    internal RomEditor? Document => document;
    internal void RequestOpen() => Open_Click(this, new());
    private void RefreshChanges()
    {
        if (ChangesList is null) return;
        var changed = document?.Definition.Tables.Where(t => document.TableChanged(t, Baseline)).ToArray() ?? [];
        ChangesList.ItemsSource = changed; ChangesButton.Content = $"Changes ({changed.Length})";
    }
    private void Changes_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ChangesList.SelectedItem is TuningTable changed) OpenTable(changed);
    }
    private void Layout_Changed(object sender, RoutedEventArgs e)
    {
        if (BrowserColumn is null || FocusButton is null || DetailsPanel is null || ChangesPanel is null) return;
        bool focused = FocusButton.IsChecked == true, changes = ChangesButton.IsChecked == true;
        if (focused != focusedLayout)
        {
            if (focused) browserWidth = BrowserColumn.Width;
            BrowserColumn.Width = focused ? new(0) : browserWidth; focusedLayout = focused;
        }
        BrowserDivider.Width = new(focused ? 0 : 6);
        BrowserPanel.Visibility = BrowserSplitter.Visibility = focused ? Visibility.Collapsed : Visibility.Visible;
        DetailsPanel.Visibility = DetailsButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ChangesColumn.Width = new(changes ? 250 : 0); ChangesDivider.Width = new(changes ? 6 : 0);
        ChangesPanel.Visibility = ChangesSplitter.Visibility = changes ? Visibility.Visible : Visibility.Collapsed;
    }
    private void View_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DataLayout is null || GraphPanel is null) return;
        int view = ViewBox.SelectedIndex; bool beside = view == 1, graphOnly = view == 3;
        Grid.SetRow(GraphPanel, beside || graphOnly ? 0 : 2); Grid.SetColumn(GraphPanel, beside ? 2 : 0);
        TableRow.MinHeight = view == 0 ? 45 : 0; GraphRow.MinHeight = view == 0 ? 120 : 0;
        TableRow.Height = new(2, GridUnitType.Star); GraphRow.Height = view == 0 ? new(1, GridUnitType.Star) : new(0);
        DataDividerRow.Height = new(view == 0 ? 6 : 0); DataDividerColumn.Width = new(beside ? 6 : 0);
        TableColumn.Width = new(1, GridUnitType.Star); GraphColumn.Width = beside ? new(1, GridUnitType.Star) : new(0);
        MapGrid.Visibility = graphOnly ? Visibility.Collapsed : Visibility.Visible; GraphPanel.Visibility = view == 2 ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(DataSplitter, beside ? 0 : 1); Grid.SetColumn(DataSplitter, beside ? 1 : 0);
        DataSplitter.Width = beside ? 6 : double.NaN; DataSplitter.Height = beside ? double.NaN : 6;
        DataSplitter.HorizontalAlignment = beside ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        DataSplitter.VerticalAlignment = beside ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        DataSplitter.ResizeDirection = beside ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        DataSplitter.Visibility = view is 0 or 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private readonly RomInspector inspector;
    private readonly TuningCodeGuard codeGuard;
    private RomEditor? document;
    private TuningTable? table;
    private bool refreshing, closingForTest;
    private TuningBaseline Baseline => (TuningBaseline)Math.Max(0, BaselineBox.SelectedIndex);
    public EditorWorkspace() : this(new(), new()) { }
    internal EditorWorkspace(RomInspector inspector, TuningCodeGuard codeGuard)
    {
        this.inspector = inspector; this.codeGuard = codeGuard;
        UiTheme.Ensure(); InitializeComponent(); BaselineBox.ItemsSource = new[] { "Original ROM", "Last save", "Comparison ROM" }; BaselineBox.SelectedIndex = 0;
        Graph.CellPicked += index => SelectCell(index); RefreshControls();
    }
    public void OpenRom(string path)
    {
        var opened = new RomEditor(File.ReadAllBytes(path), path, inspector, codeGuard);
        document = opened; document.Changed += Document_Changed; table = null;
        MapTabs.Items.Clear(); BaselineBox.SelectedIndex = 0; SearchBox.Text = ""; ChangedOnly.IsChecked = false;
        FileText.Text = Path.GetFileName(path); FileText.ToolTip = path;
        RefreshBrowser(); RefreshTable(); RefreshControls();
        EditorStatus.Text = "ROM loaded. Choose a map to edit. Static axes are read-only.";
    }
    private void Attempt(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.Xml.XmlException or FormatException or OverflowException)
        { EditorStatus.Text = error.Message; MessageBox.Show(Window.GetWindow(this), error.Message, "SpeedLink ROM editor", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private bool FinishEdit() => MapGrid.CommitEdit(DataGridEditingUnit.Row, true);
    private bool MayReplace() => document?.IsDirty != true || MessageBox.Show(Window.GetWindow(this), "Discard unsaved changes? Saved copies and the original ROM are kept.", "Unsaved ROM edits", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishEdit() || !MayReplace()) return;
        var dialog = new OpenFileDialog { Title = "Open ROM for editing", Filter = "ROM files|*.bin;*.rom|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Attempt(() => OpenRom(dialog.FileName));
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (document is null || !FinishEdit()) return;
        string stem = Path.GetFileNameWithoutExtension(document.SourcePath) + "_edited";
        string folder = Path.GetDirectoryName(document.LastSavePath ?? document.SourcePath)!;
        string suggestion = stem + ".bin"; int revision = 2;
        while (File.Exists(Path.Combine(folder, suggestion))) suggestion = stem + "_" + revision++ + ".bin";
        var dialog = new SaveFileDialog { Title = "Save a new edited ROM copy", Filter = "Binary ROM|*.bin", FileName = suggestion, InitialDirectory = folder, OverwritePrompt = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Attempt(() => { document.SaveNew(dialog.FileName); EditorStatus.Text = "Saved · bytes and checksum verified: " + dialog.FileName; });
    }
    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (document is null || !FinishEdit()) return;
        var dialog = new OpenFileDialog { Title = "Compare with the same ROM calibration", Filter = "ROM files|*.bin;*.rom|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Attempt(() => { document.LoadComparison(File.ReadAllBytes(dialog.FileName), dialog.FileName); BaselineBox.SelectedIndex = 2; });
    }
    private void Document_Changed(object? sender, EventArgs e)
    {
        if (refreshing) return;
        RefreshControls(); RefreshTable(); RefreshBrowser();
    }
    private void RefreshControls()
    {
        SaveButton.IsEnabled = document is not null; CompareButton.IsEnabled = document is not null;
        UndoButton.IsEnabled = document?.CanUndo == true; RedoButton.IsEnabled = document?.CanRedo == true;
        UndoButton.ToolTip = document?.UndoDescription; RedoButton.ToolTip = document?.RedoDescription;
        EditTools.IsEnabled = table is not null && !table.IsChoice;
        RefreshChanges();
        if (document is not null) ProfileText.Text = document.Definition.Calibration + " · " + document.Definition.Model + (document.IsDirty ? " · Unsaved changes" : " · Saved state");
    }
    private void RefreshBrowser()
    {
        if (MapTree is null) return;
        var expanded = MapTree.Items.OfType<TreeViewItem>().Where(i => i.IsExpanded).Select(i => i.Tag as string).ToHashSet();
        MapTree.Items.Clear(); if (document is null) return;
        string query = SearchBox.Text.Trim();
        var filtered = document.Definition.Tables.Where(t => (query.Length == 0 || (t.Name + " " + t.Category + " " + t.Description).Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (ChangedOnly.IsChecked != true || document.TableChanged(t, Baseline))).ToArray();
        foreach (var group in filtered.GroupBy(t => t.Category))
        {
            var category = new TreeViewItem { Header = group.Key + "  (" + group.Count() + ")", Tag = group.Key, IsExpanded = query.Length > 0 || ChangedOnly.IsChecked == true || expanded.Contains(group.Key), Padding = new(2, 5, 0, 5) };
            foreach (var item in group) category.Items.Add(new TreeViewItem { Header = (document.TableChanged(item, Baseline) ? "● " : "") + item.Name, Tag = item, ToolTip = item.Name, Padding = new(2, 4, 0, 4) });
            MapTree.Items.Add(category);
        }
        MapCountText.Text = $"{filtered.Length:N0} / {document.Definition.Tables.Count:N0} controls";
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshBrowser();
    private void Filter_Changed(object sender, RoutedEventArgs e) => RefreshBrowser();
    private void Baseline_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (BaselineBox.SelectedIndex == 2 && document?.HasComparison != true) { BaselineBox.SelectedIndex = 0; if (EditorStatus is not null) EditorStatus.Text = "Load a comparison ROM first."; return; }
        if (MapTree is not null) { RefreshBrowser(); RefreshTable(); RefreshChanges(); }
    }
    private void Map_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: TuningTable selected }) OpenTable(selected);
    }
    private void OpenTable(TuningTable selected)
    {
        var tab = MapTabs.Items.OfType<TabItem>().FirstOrDefault(t => ReferenceEquals(t.Tag, selected));
        if (tab is null)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal }; header.Children.Add(new TextBlock { Text = selected.Name, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            tab = new TabItem { Header = header, Tag = selected, ToolTip = selected.Name };
            var close = new Button { Content = "×", Padding = new(3, 0, 3, 0), MinHeight = 18, Margin = new(9, 0, 0, 0) }; var closing = tab;
            close.Click += (_, _) => { if (FinishEdit()) MapTabs.Items.Remove(closing); }; header.Children.Add(close); MapTabs.Items.Add(tab);
        }
        MapTabs.SelectedItem = tab;
    }
    private void Tab_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MapTabs)) return;
        refreshing = true; MapGrid.SelectedCells.Clear(); refreshing = false;
        table = (MapTabs.SelectedItem as TabItem)?.Tag as TuningTable; RefreshTable(); RefreshControls();
    }
    private int[] SelectedIndices() => table is null ? [] : MapGrid.SelectedCells.Where(c => c.Item is EditorRow && c.Column.DisplayIndex >= 0).Select(c => ((EditorRow)c.Item).Index * table.Columns + c.Column.DisplayIndex).Distinct().Order().ToArray();
    private void RefreshTable()
    {
        if (MapGrid is null || refreshing) return;
        refreshing = true;
        try
        {
            int[] selection = SelectedIndices().Where(i => table is not null && i >= 0 && i < table.Count).ToArray();
            MapGrid.Columns.Clear(); MapGrid.ItemsSource = null; ChoiceBox.Visibility = Visibility.Collapsed;
            if (document is null || table is null)
            {
                MapTitle.Text = "Select a map"; AxesText.Text = ""; HelpText.Text = "Choose a map from the list.";
                StorageText.Text = ""; CellText.Text = ""; GraphTitle.Text = "2D curve / 3D surface"; Graph.SetData(null, [], -1); return;
            }
            MapTitle.Text = table.Name;
            HelpText.Text = table.Description;
            AxesText.Text = table.Columns == 1 && table.Rows == 1 ? table.Units : $"X: {table.X.Name}" + (table.Rows > 1 ? $"   ·   Y: {table.Y.Name}" : "") + $"   ·   Values: {table.Units}";
            StorageText.Text = $"{table.Units}\n{table.Columns} × {table.Rows} · {table.Storage} · {(table.BigEndian ? "big endian" : "little endian")}\nAddress 0x{table.Address:X}" + (table.Bit is int b ? $" · bit {b}" : "") + "\nStock definition · " + document.Definition.Version + "\nAxes: static display values";
            var values = Enumerable.Range(0, table.Count).Select(i => document.Read(table, i)).ToArray();
            var finite = values.Where(double.IsFinite).ToArray(); double min = finite.DefaultIfEmpty(0).Min(), max = finite.DefaultIfEmpty(1).Max();
            for (int col = 0; col < table.Columns; col++)
            {
                var converter = new EditorCellConverter(document, table, col, Baseline, min, max);
                var style = new Style(typeof(DataGridCell));
                style.Setters.Add(new Setter(BackgroundProperty, new Binding { Converter = converter, ConverterParameter = "color" }));
                style.Setters.Add(new Setter(ToolTipProperty, new Binding { Converter = converter, ConverterParameter = "tip" }));
                style.Setters.Add(new Setter(BorderBrushProperty, new Binding { Converter = converter, ConverterParameter = "border" }));
                style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
                var trigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true }; trigger.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(93, 70, 36)))); trigger.Setters.Add(new Setter(ForegroundProperty, UiTheme.Text)); style.Triggers.Add(trigger);
                MapGrid.Columns.Add(new DataGridTextColumn { Header = table.Columns == 1 ? "Value" : table.X.Values[col].ToString("G6", CultureInfo.InvariantCulture), Width = table.Columns == 1 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(82), Binding = new Binding($"Values[{col}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit }, IsReadOnly = table.IsChoice, CellStyle = style });
            }
            MapGrid.ItemsSource = Enumerable.Range(0, table.Rows).Select(row => new EditorRow(row, Enumerable.Range(0, table.Columns).Select(col => table.Display(values[row * table.Columns + col])).ToArray())).ToArray();
            if (table.IsChoice)
            {
                double value = document.Read(table, selection.FirstOrDefault(0));
                var options = table.Bit is not null ? new[] { new TuningState("Disabled / 0", 0), new TuningState("Enabled / 1", 1) } : table.States;
                ChoiceBox.ItemsSource = options; ChoiceBox.DisplayMemberPath = "Name"; ChoiceBox.SelectedItem = options.FirstOrDefault(s => s.Value == value);
                ChoiceBox.Visibility = Visibility.Visible;
            }
            foreach (int index in selection.Where(i => i < table.Count)) MapGrid.SelectedCells.Add(new(MapGrid.Items[index / table.Columns], MapGrid.Columns[index % table.Columns]));
            MapGrid.RowHeaderWidth = table.Bits.Length > 0 ? 300 : 105;
            Graph.SetData(table.IsChoice ? null : table, values, selection.FirstOrDefault(-1));
            GraphTitle.Text = table.IsChoice ? "Switch controls · choose a row, then use the named selector above" : table.Rows > 1 ? "3D surface · drag to rotate · wheel to zoom · click to select" : table.Columns > 1 ? "2D curve · click a point to select" : "Scalar value";
            UpdateCellText();
        }
        finally { refreshing = false; }
    }
    private void Grid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (table is not null && e.Row.Item is EditorRow row) e.Row.Header = table.Bits.Length > 0 ? table.BitNames[row.Index] : table.Rows > 1 ? table.Y.Values[row.Index].ToString("G6", CultureInfo.InvariantCulture) : "Value";
    }
    private void Grid_EditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (refreshing || document is null || table is null || e.EditAction != DataGridEditAction.Commit || e.EditingElement is not TextBox input) return;
        var row = (EditorRow)e.Row.Item; var selected = table; int index = row.Index * table.Columns + e.Column.DisplayIndex;
        if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) { e.Cancel = true; EditorStatus.Text = "Enter a finite numeric value using a decimal point."; return; }
        try { refreshing = true; document.Apply([new(selected, index, value)], "Edit " + selected.Name); }
        catch (ArgumentException error) { e.Cancel = true; EditorStatus.Text = error.Message; }
        finally { refreshing = false; }
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { RefreshTable(); RefreshBrowser(); RefreshControls(); }));
    }
    private void Choice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing && document is not null && table is not null && ChoiceBox.SelectedItem is TuningState state) Attempt(() => document.Apply([new(table, SelectedIndices().FirstOrDefault(0), state.Value)], "Switch " + table.Name));
    }
    private void Grid_SelectionChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (refreshing) return; UpdateCellText(); Graph.Select(SelectedIndices().FirstOrDefault(-1));
        if (table?.IsChoice == true && document is not null)
        {
            refreshing = true; double value = document.Read(table, SelectedIndices().FirstOrDefault(0)); ChoiceBox.SelectedItem = ChoiceBox.Items.Cast<TuningState>().FirstOrDefault(s => s.Value == value); refreshing = false;
        }
    }
    private void UpdateCellText()
    {
        if (document is null || table is null) return; var selected = SelectedIndices();
        if (selected.Length == 0) { CellText.Text = "Select a cell to see its reference value."; return; }
        int index = selected[0]; double value = document.Read(table, index), reference = document.Read(table, index, Baseline), delta = value - reference;
        string percent = reference == 0 ? "n/a" : (delta / reference * 100).ToString("G6", CultureInfo.InvariantCulture) + "%";
        CellText.Text = $"{selected.Length} selected\nCurrent: {table.Display(value)} {table.Units}\nBaseline: {table.Display(reference)}\nDifference: {table.Display(delta)} ({percent})";
    }
    private void SelectCell(int index)
    {
        if (table is null || index < 0 || index >= table.Count) return;
        MapGrid.SelectedCells.Clear(); var item = MapGrid.Items[index / table.Columns]; var column = MapGrid.Columns[index % table.Columns];
        MapGrid.SelectedCells.Add(new(item, column)); MapGrid.CurrentCell = new(item, column); MapGrid.ScrollIntoView(item, column);
    }
    private (int Left, int Top, int Right, int Bottom) Rectangle()
    {
        var selected = SelectedIndices(); if (table is null || selected.Length == 0) throw new InvalidOperationException("Select cells first.");
        int left = selected.Min(i => i % table.Columns), right = selected.Max(i => i % table.Columns), top = selected.Min(i => i / table.Columns), bottom = selected.Max(i => i / table.Columns);
        if (selected.Length != (right - left + 1) * (bottom - top + 1)) throw new InvalidOperationException("Select a contiguous rectangle.");
        return (left, top, right, bottom);
    }
    private void Transform(TuningOperation operation)
    {
        if (document is null || table is null) return;
        Attempt(() => { if (!double.TryParse(OperandBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double operand)) throw new ArgumentException("Enter a numeric amount."); var selected = SelectedIndices(); if (selected.Length == 0) throw new InvalidOperationException("Select cells first."); document.Transform(table, selected, operation, operand); });
    }
    private void Set_Click(object sender, RoutedEventArgs e) => Transform(TuningOperation.Set);
    private void Add_Click(object sender, RoutedEventArgs e) => Transform(TuningOperation.Add);
    private void Multiply_Click(object sender, RoutedEventArgs e) => Transform(TuningOperation.Multiply);
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (document is null || table is null) return; Attempt(() => { var r = Rectangle(); Clipboard.SetText(string.Join('\n', Enumerable.Range(r.Top, r.Bottom - r.Top + 1).Select(y => string.Join('\t', Enumerable.Range(r.Left, r.Right - r.Left + 1).Select(x => document.Read(table, y * table.Columns + x).ToString("R", CultureInfo.InvariantCulture)))))); EditorStatus.Text = "Selected values copied without headers."; });
    }
    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (document is null || table is null) return; Attempt(() => { var r = Rectangle(); document.Paste(table, r.Top, r.Left, Clipboard.GetText()); });
    }
    private void Interpolate_Click(object sender, RoutedEventArgs e)
    {
        if (document is null || table is null) return; Attempt(() => { var r = Rectangle(); document.Interpolate(table, r.Left, r.Top, r.Right, r.Bottom, ((ComboBoxItem)InterpolationBox.SelectedItem).Content.ToString()!); });
    }
    private void Reset_Click(object sender, RoutedEventArgs e) { if (document is not null && table is not null) Attempt(() => document.Reset(table, SelectedIndices(), Baseline)); }
    private void Undo_Click(object sender, RoutedEventArgs e) { MapGrid.CancelEdit(); document?.Undo(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { MapGrid.CancelEdit(); document?.Redo(); }
    private void ResetView_Click(object sender, RoutedEventArgs e) => Graph.ResetView();
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        using var stream = typeof(EditorWindow).Assembly.GetManifestResourceStream("SpeedLink.EditorHelp.md")!; using var reader = new StreamReader(stream);
        new Window { Owner = Window.GetWindow(this), Title = "SpeedLink · Editor guide", Width = 830, Height = 650, Content = new TextBox { Text = reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(20) } }.Show();
    }
    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        bool editingText = Keyboard.FocusedElement is TextBox;
        if (e.Key == Key.S) Save_Click(sender, e);
        else if (!editingText && e.Key == Key.Z) Undo_Click(sender, e);
        else if (!editingText && e.Key == Key.Y) Redo_Click(sender, e);
        else if (!editingText && e.Key == Key.C) Copy_Click(sender, e);
        else if (!editingText && e.Key == Key.V) Paste_Click(sender, e);
        else return; e.Handled = true;
    }
    internal bool MayClose() => closingForTest || (FinishEdit() && MayReplace());
    internal void CloseOfflineTest() => closingForTest = true;
    internal async Task<object> ExerciseOffline(string path, string folder, Action? switchWorkspaces = null)
    {
        Directory.CreateDirectory(folder); OpenRom(path); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var doc = document!; var scalar = doc.Definition.Tables.First(t => t.Kind == "1D" && t.Storage == "float");
        OpenTable(scalar); SelectCell(0); UpdateLayout();
        if (MapGrid.Columns[0].IsReadOnly || !MapGrid.BeginEdit()) throw new InvalidOperationException("Numeric grid is not editable.");
        UpdateLayout();
        if (MapGrid.Columns[0].GetCellContent(MapGrid.Items[0]) is not TextBox entry) throw new InvalidOperationException("Numeric editing textbox missing.");
        entry.Text = "8123";
        if (!FinishEdit() || doc.Read(scalar, 0) != 8123) throw new InvalidOperationException("Direct cell commit did not edit ROM bytes.");
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var dirtySnapshot = doc.Snapshot(); switchWorkspaces?.Invoke();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (!doc.IsDirty || !doc.Snapshot().AsSpan().SequenceEqual(dirtySnapshot)) throw new InvalidOperationException("Workspace navigation lost edits.");
        ChangesButton.IsChecked = true; if (!ChangesList.Items.Cast<TuningTable>().Contains(scalar)) throw new InvalidOperationException("Changes drawer missed the edited map.");
        ChangesList.SelectedItem = scalar; if (!ReferenceEquals(table, scalar)) throw new InvalidOperationException("Changed-map navigation failed.");
        doc.LoadComparison(doc.Snapshot(), path); BaselineBox.SelectedIndex = 2;
        if (ChangesList.Items.Count != 0) throw new InvalidOperationException("Changes drawer ignored comparison baseline.");
        BaselineBox.SelectedIndex = 0; if (!ChangesList.Items.Cast<TuningTable>().Contains(scalar)) throw new InvalidOperationException("Changes drawer did not restore original baseline.");
        ChangesButton.IsChecked = false;
        doc.Undo(); if (doc.IsDirty) throw new InvalidOperationException("Direct cell edit undo failed.");
        SelectCell(0); OperandBox.Text = "1"; Add_Click(this, new()); if (!doc.IsDirty || !UndoButton.IsEnabled) throw new InvalidOperationException("Scalar UI edit failed.");
        Undo_Click(this, new()); if (doc.IsDirty) throw new InvalidOperationException("UI undo failed."); Redo_Click(this, new());
        var curve = doc.Definition.Tables.First(t => t.Kind == "2D"); OpenTable(curve); SelectCell(1); UpdateLayout(); Capture(Path.Combine(folder, "curve.png"));
        var surface = doc.Definition.Tables.First(t => t.Kind == "3D"); OpenTable(surface); SelectCell(surface.Columns + 2); UpdateLayout(); Capture(Path.Combine(folder, "surface.png"));
        var bit = doc.Definition.Tables.First(t => t.Bit is not null); OpenTable(bit); double before = doc.Read(bit, 0); ChoiceBox.SelectedItem = ChoiceBox.Items.Cast<TuningState>().Single(s => s.Value != before); if (doc.Read(bit, 0) == before) throw new InvalidOperationException("Bit selector did not edit.");
        doc.Undo(); doc.Undo(); if (doc.IsDirty) throw new InvalidOperationException("Undo failed to restore original bytes.");
        doc.LoadComparison(doc.Snapshot(), path); BaselineBox.SelectedIndex = 2; ChangedOnly.IsChecked = true; if (MapTree.Items.Count != 0) throw new InvalidOperationException("Changed filter failed.");
        ChangedOnly.IsChecked = false; BaselineBox.SelectedIndex = 0; OpenTable(surface); SelectCell(surface.Columns + 2);
        string export = Path.Combine(folder, "UI_UNEDITED_COPY.bin"); doc.SaveNew(export); if (!File.ReadAllBytes(export).AsSpan().SequenceEqual(File.ReadAllBytes(path))) throw new InvalidOperationException("No-edit roundtrip differs.");
        FocusButton.IsChecked = true; DetailsButton.IsChecked = false; ViewBox.SelectedIndex = 1; UpdateLayout();
        if (BrowserPanel.IsVisible || Grid.GetColumn(GraphPanel) != 2 || MapGrid.ActualWidth < 100 || GraphPanel.ActualWidth < 100) throw new InvalidOperationException("Focused split layout failed.");
        Capture(Path.Combine(folder, "focused.png"));
        ViewBox.SelectedIndex = 2; UpdateLayout(); if (GraphPanel.IsVisible) throw new InvalidOperationException("Table-only view failed.");
        ViewBox.SelectedIndex = 3; UpdateLayout(); if (MapGrid.IsVisible || !GraphPanel.IsVisible) throw new InvalidOperationException("Graph-only view failed.");
        ViewBox.SelectedIndex = 0; FocusButton.IsChecked = false; DetailsButton.IsChecked = true; UpdateLayout();
        if (!BrowserPanel.IsVisible || !DetailsPanel.IsVisible || Grid.GetRow(GraphPanel) != 2) throw new InvalidOperationException("Workbench layout did not restore.");
        Capture(Path.Combine(folder, "editor.png")); closingForTest = true;
        MapTabs.Items.Clear();
        var documentationCurve = doc.Definition.Tables.FirstOrDefault(t => t.Name.Contains("Injector Base Pulsewidth", StringComparison.OrdinalIgnoreCase) && t.Kind == "2D") ?? curve;
        OpenTable(documentationCurve); SelectCell(0);
        foreach (var category in MapTree.Items.OfType<TreeViewItem>()) category.IsExpanded = (string?)category.Tag == documentationCurve.Category;
        UpdateLayout(); Capture(Path.Combine(folder, "CurveEditor.png"));
        OpenTable(surface); SelectCell(surface.Columns + 2); UpdateLayout(); Capture(Path.Combine(folder, "SurfaceEditor.png"));
        return new { passed = true, calibration = doc.Definition.Calibration, controls = doc.Definition.Tables.Count, grid = true, directCellCommit = true, curve = true, surface = true, bitSelector = true, undoRedo = true, comparison = true, changedFilter = true, noEditExportIdentical = true, focusedAndAllViews = true, changesDrawer = true, workspaceEditsPreserved = true };
    }
    private void Capture(string path)
    {
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}

internal sealed class EditorCellConverter(RomEditor document, TuningTable table, int column, TuningBaseline baseline, double min, double max) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not EditorRow row) return UiTheme.Panel;
        int index = row.Index * table.Columns + column; double current = document.Read(table, index);
        if ((string)parameter == "border") return document.CellChanged(table, index, baseline) ? Brushes.DarkOrange : Brushes.Transparent;
        if ((string)parameter == "tip") { double reference = document.Read(table, index, baseline); return $"Baseline: {table.Display(reference)}\nDifference: {table.Display(current - reference)} {table.Units}"; }
        if (!double.IsFinite(current)) return Brushes.LightCoral;
        double scale = Math.Max(1, Math.Max(Math.Abs(min), Math.Abs(max))), span = max / scale - min / scale;
        double fraction = span < 1e-15 ? .5 : Math.Clamp((current / scale - min / scale) / span, 0, 1);
        return new SolidColorBrush(Color.FromRgb((byte)(39 + 48 * fraction), (byte)(43 + 23 * fraction), (byte)(46 - 7 * fraction)));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
