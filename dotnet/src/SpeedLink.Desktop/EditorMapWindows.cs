using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private sealed class MapSlot(EditorWorkspace editor, Border frame)
    {
        internal readonly EditorWorkspace Editor = editor;
        internal readonly Border Frame = frame;
        internal Window? Window;
        internal bool DockOnClose;
        internal PanelBounds Bounds = new(Width: 840, Height: 650);
    }
    private EditorWorkspace? mapOwner, activeMap;
    private readonly List<MapSlot> mapSlots = [];
    private readonly Grid mapDock = new();
    private readonly ScrollViewer mapDockScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private MapSlot? expandedMap;
    private double dockColumnFraction = .5, dockRowFraction = .5;
    private int dockColumns = 2;
    private double[] dockRowWeights = [];
    private double cellZoom = 1;
    private bool autoFitCells = true, updatingMapControls, rebuildingDock, restoringMaps, disposingMaps, gridDraft;
    private double measuredCellWidth = double.NaN;
    private string? mapsPath;
    private MapWorkspacePreferences[] mapProfiles = [];
    private DispatcherTimer? mapSaveTimer;
    private EditorWorkspace MapRoot => mapOwner ?? this;
    private EditorWorkspace CommandMap => mapOwner is null ? activeMap ?? this : this;
    private static string MapKey(TuningTable t) => t.Address.ToString("X", CultureInfo.InvariantCulture) + ":" + t.Name;

    private void InitializeMapWindows(EditorWorkspace? owner)
    {
        mapOwner = owner;
        MapGrid.FontSize = 13;
        MapTabs.Visibility = Visibility.Collapsed;
        MapWorkspace.PreviewMouseDown += (_, _) => ActivateMap();
        MapWorkspace.GotKeyboardFocus += (_, _) => ActivateMap();
        Point? dragStart = null;
        PaneCaption.Cursor = Cursors.SizeAll;
        PaneCaption.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { dragStart = null; ExpandMap_Click(this, new()); e.Handled = true; }
            else { dragStart = e.GetPosition(PaneCaption); PaneCaption.CaptureMouse(); }
        };
        PaneCaption.PreviewMouseLeftButtonUp += (_, _) => { dragStart = null; PaneCaption.ReleaseMouseCapture(); };
        PaneCaption.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) { dragStart = null; return; }
            if (dragStart is not Point start || table is null || (e.GetPosition(PaneCaption) - start).Length < 8) return;
            dragStart = null; PaneCaption.ReleaseMouseCapture(); var selected = table; var root = MapRoot;
            root.DetachMap(this);
            var moved = root.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor.table, selected));
            if (moved?.Window is not Window window) return;
            try { window.DragMove(); } catch (InvalidOperationException) { return; }
            var origin = root.mapDockScroll.PointToScreen(new Point());
            var corner = root.mapDockScroll.PointToScreen(new Point(root.mapDockScroll.ActualWidth, root.mapDockScroll.ActualHeight));
            if (new Rect(origin, corner).Contains(window.PointToScreen(Mouse.GetPosition(window)))) { moved.DockOnClose = true; window.Close(); }
        };
        if (owner is not null)
        {
            EditorToolbar.Visibility = Visibility.Collapsed;
            WorkspaceRoot.RowDefinitions[0].Height = new(0);
            EditorLayout.ColumnDefinitions[2].MinWidth = 0;
            WorkspaceRoot.Margin = new(3);
            FocusButton.IsChecked = true;
            MapsButton.Visibility = Visibility.Collapsed;
            FileText.Visibility = Visibility.Collapsed;
            ExportIntegrityText.Visibility = EditorStatus.Visibility = Visibility.Collapsed;
            return;
        }
        Grid.SetColumn(mapDockScroll, 2); mapDockScroll.Content = mapDock;
        EditorLayout.Children.Remove(MapWorkspace);
        var frame = MapFrame(MapWorkspace);
        mapSlots.Add(new(this, frame));
        EditorLayout.Children.Add(mapDockScroll);
        mapDockScroll.SizeChanged += (_, _) => UpdateDockExtent();
        mapDockScroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) UpdateDockExtent(); };
        ArrangeMaps();
    }
    private Border MapFrame(UIElement content)
    {
        var border = new Border { Child = content, BorderThickness = new(1), Padding = new(3), CornerRadius = new(3), ClipToBounds = true };
        border.SetResourceReference(Border.BackgroundProperty, "ShellBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        border.SizeChanged += (_, _) =>
        {
            if (content is FrameworkElement element && border.ActualWidth > 8)
                element.MaxWidth = border.ActualWidth - 8;
        };
        return border;
    }
    private void ActivateMap()
    {
        var root = MapRoot;
        root.activeMap = this;
        foreach (var slot in root.mapSlots)
            slot.Frame.SetResourceReference(Border.BorderBrushProperty, ReferenceEquals(slot.Editor, this) ? "AmberBrush" : "LineBrush");
        root.RefreshMapCommandState();
        root.QueueMapSave();
    }
    private void RefreshMapCommandState()
    {
        if (mapOwner is not null) return;
        var current = CommandMap; bool graphable = current.table is { Count: > 1, IsChoice: false };
        FitMapButton.IsEnabled = DetailsButton.IsEnabled = current.table is not null;
        PreviewItem.IsEnabled = BelowItem.IsEnabled = BesideItem.IsEnabled = GraphItem.IsEnabled = graphable;
        PreviewItem.IsChecked = graphable && current.ViewMode == 4; BelowItem.IsChecked = current.ViewMode == 0;
        BesideItem.IsChecked = current.ViewMode == 1; TableItem.IsChecked = current.ViewMode == 2; GraphItem.IsChecked = current.ViewMode == 3;
    }
    private void MenuMapInfo_Click(object sender, RoutedEventArgs e) => CommandMap.ShowMapInformation();
    /// <summary>Each map is a view over the same document, not a second ROM copy.</summary>
    internal EditorWorkspace OpenMap(TuningTable selected)
    {
        var root = MapRoot;
        if (!ReferenceEquals(root, this)) return root.OpenMap(selected);
        var existing = mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor.table, selected));
        if (existing is not null)
        {
            if (expandedMap is not null && !ReferenceEquals(expandedMap, existing)) { expandedMap = null; ArrangeMaps(); }
            existing.Window?.Activate(); existing.Editor.ActivateMap(); existing.Editor.MapGrid.Focus();
            return existing.Editor;
        }
        if (mapSlots.Count(s => s.Editor.table is not null) >= 24) throw new InvalidOperationException("Close a map before opening another; up to 24 maps can remain open.");
        if (table is null)
        {
            OpenTable(selected); ActivateMap(); ArrangeMaps(); QueueMapSave(); return this;
        }
        if (document is null) throw new InvalidOperationException("Open a ROM first.");
        var editor = new EditorWorkspace(inspector, codeGuard, this) { document = document, Definitions = Definitions };
        document.Changed += editor.Document_Changed;
        editor.FocusButton.IsChecked = true;
        var slot = new MapSlot(editor, MapFrame(editor)); mapSlots.Add(slot);
        editor.OpenTable(selected); editor.SetViewMode(selected is { Rows: 1, Columns: > 16 } ? 1 : 4);
        expandedMap = null; ArrangeMaps(); editor.ActivateMap(); QueueMapSave();
        return editor;
    }
    private void ArrangeMaps()
    {
        if (mapOwner is not null || rebuildingDock) return;
        rebuildingDock = true;
        try
        {
            // Keep existing views parented: rebuilding the container would end
            // keyboard focus and could discard an unfinished cell edit.
            foreach (var splitter in mapDock.Children.OfType<GridSplitter>().ToArray()) mapDock.Children.Remove(splitter);
            mapDock.ColumnDefinitions.Clear(); mapDock.RowDefinitions.Clear();
            bool emptyDock = !mapSlots.Any(s => s.Window is null && s.Editor.table is not null);
            var docked = mapSlots.Where(s => s.Window is null && (s.Editor.table is not null || emptyDock && ReferenceEquals(s.Editor, this))).ToArray();
            var visible = expandedMap is not null ? docked.Where(s => ReferenceEquals(s, expandedMap)).ToArray() : docked;
            foreach (var slot in mapSlots.Where(s => s.Window is null)) slot.Frame.Visibility = visible.Contains(slot) ? Visibility.Visible : Visibility.Collapsed;
            int columns = visible.Length <= 1 || mapDockScroll.ActualWidth is > 0 and < 540 ? 1 : dockColumns;
            int rows = Math.Max(1, (visible.Length + columns - 1) / columns);
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) mapDock.ColumnDefinitions.Add(new() { Width = new(7) });
                mapDock.ColumnDefinitions.Add(new() { Width = new(columns == 1 ? 1 : c == 0 ? dockColumnFraction : 1 - dockColumnFraction, GridUnitType.Star), MinWidth = 260 });
            }
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) mapDock.RowDefinitions.Add(new() { Height = new(7) });
                double weight = dockRowWeights.Length == rows ? dockRowWeights[r] : rows == 2 ? r == 0 ? dockRowFraction : 1 - dockRowFraction : 1;
                mapDock.RowDefinitions.Add(new() { Height = new(weight, GridUnitType.Star), MinHeight = 260 });
            }
            for (int i = 0; i < visible.Length; i++)
            {
                var slot = visible[i]; Grid.SetRow(slot.Frame, (i / columns) * 2); Grid.SetColumn(slot.Frame, (i % columns) * 2);
                if (!mapDock.Children.Contains(slot.Frame)) mapDock.Children.Add(slot.Frame);
                slot.Editor.ExpandMapButton.Content = ReferenceEquals(slot, expandedMap) ? "Restore" : "Expand";
            }
            if (columns == 2) AddDockSplitter(true, 1, rows * 2 - 1);
            for (int r = 1; r < rows * 2 - 1; r += 2) AddDockSplitter(false, r, columns * 2 - 1);
            UpdateDockExtent();
        }
        finally { rebuildingDock = false; }
    }
    private void UpdateDockExtent()
    {
        int rows = Math.Max(1, (mapDock.RowDefinitions.Count + 1) / 2);
        if (mapDockScroll.ViewportWidth is > 0 and < 100000) mapDock.Width = mapDockScroll.ViewportWidth;
        mapDock.Height = Math.Max(0, Math.Max(mapDockScroll.ActualHeight - 2, rows * 260 + (rows - 1) * 7));
        if (mapSlots.Count(s => s.Window is null && s.Editor.table is not null) > 1 && expandedMap is null && !rebuildingDock)
        {
            bool narrow = mapDockScroll.ActualWidth < 540;
            if (narrow && mapDock.ColumnDefinitions.Count == 3 || !narrow && mapDock.ColumnDefinitions.Count == 1 && dockColumns == 2) ArrangeMaps();
        }
    }
    private void AddDockSplitter(bool vertical, int position, int span)
    {
        var splitter = new GridSplitter { Background = UiTheme.Panel, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = vertical ? GridResizeDirection.Columns : GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ToolTip = vertical ? "Resize map widths" : "Resize map heights", ShowsPreview = false };
        System.Windows.Automation.AutomationProperties.SetName(splitter, vertical ? "Resize map widths" : "Resize map heights");
        if (vertical) { Grid.SetColumn(splitter, position); Grid.SetRowSpan(splitter, span); }
        else { Grid.SetRow(splitter, position); Grid.SetColumnSpan(splitter, span); }
        splitter.DragCompleted += (_, e) =>
        {
            if (e.Canceled) { ArrangeMaps(); return; }
            if (vertical) dockColumnFraction = Math.Clamp(mapDock.ColumnDefinitions[0].ActualWidth / Math.Max(1, mapDock.ActualWidth - 7), .2, .8);
            else if (mapDock.RowDefinitions.Count == 3) dockRowFraction = Math.Clamp(mapDock.RowDefinitions[0].ActualHeight / Math.Max(1, mapDock.ActualHeight - 7), .2, .8);
            if (!vertical)
            {
                var heights = mapDock.RowDefinitions.Where((_, i) => i % 2 == 0).Select(r => r.ActualHeight).ToArray();
                double total = Math.Max(1, heights.Sum()); dockRowWeights = heights.Select(h => h / total).ToArray();
            }
            QueueMapSave();
        };
        mapDock.Children.Add(splitter);
    }
    private void ArrangeMaps_Click(object sender, RoutedEventArgs e)
    {
        var root = MapRoot; root.expandedMap = null;
        root.dockColumns = sender is MenuItem { Tag: "1" } ? 1 : 2;
        root.dockColumnFraction = root.dockRowFraction = .5;
        root.dockRowWeights = [];
        root.ArrangeMaps(); root.QueueMapSave();
    }
    private void ExpandMap_Click(object sender, RoutedEventArgs e)
    {
        var root = MapRoot; var slot = root.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, this));
        if (slot is null) return;
        if (slot.Window is not null) { slot.Window.WindowState = slot.Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; return; }
        root.expandedMap = ReferenceEquals(root.expandedMap, slot) ? null : slot;
        root.ArrangeMaps(); ActivateMap();
    }
    private void UndockMap_Click(object sender, RoutedEventArgs e)
    {
        var slot = MapRoot.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, this));
        if (slot?.Window is not null) { slot.DockOnClose = true; slot.Window.Close(); } else MapRoot.DetachMap(this);
    }
    private void DetachMap(EditorWorkspace editor)
    {
        if (!editor.FinishEdit()) return;
        var slot = mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, editor));
        if (slot is null || editor.table is null) return;
        if (slot.Window is not null) { slot.Window.Activate(); return; }
        // The primary view stays in the main editor; move its state to an identical
        // shared-document view before detaching, so closing it cannot close the ROM.
        if (ReferenceEquals(editor, this))
        {
            var state = CaptureMap(this);
            var tableToMove = table!;
            MapTabs.Items.Clear();
            var moved = OpenMap(tableToMove);
            if (ReferenceEquals(moved, this))
            {
                // Reserve the primary view for the next map while this view floats.
                moved = new EditorWorkspace(inspector, codeGuard, this) { document = document, Definitions = Definitions };
                document!.Changed += moved.Document_Changed;
                var movedSlot = new MapSlot(moved, MapFrame(moved)); mapSlots.Add(movedSlot);
                moved.OpenTable(tableToMove); RestoreMap(moved, state);
                MapTabs.Items.Clear(); slot = movedSlot; editor = moved;
            }
        }
        mapDock.Children.Remove(slot.Frame);
        var window = Floating("SpeedLink · " + editor.table!.Name, slot.Bounds.Width, slot.Bounds.Height, slot.Bounds);
        window.MinWidth = 300; window.MinHeight = 330; window.ShowInTaskbar = true;
        slot.Window = window; window.Content = slot.Frame; editor.UndockMapButton.Content = "Dock";
        window.LocationChanged += (_, _) => QueueMapSave();
        window.SizeChanged += (_, _) => QueueMapSave();
        window.Closing += (_, args) =>
        {
            if (!disposingMaps && !editor.FinishEdit()) args.Cancel = true;
        };
        window.Closed += (_, _) =>
        {
            slot.Bounds = Bounds(window, slot.Bounds); window.Content = null; slot.Window = null;
            editor.UndockMapButton.Content = "Undock";
            if (!disposingMaps)
            {
                expandedMap = null;
                if (!slot.DockOnClose) { RemoveMapSlot(slot); activeMap = mapSlots.Select(s => s.Editor).FirstOrDefault(p => p.table is not null) ?? this; activeMap.ActivateMap(); }
                slot.DockOnClose = false;
                // Leave the visuals disconnected until input teardown dismisses
                // tooltip owners from the old native window. Reparenting inside
                // Closed can leave a popup referring to its destroyed handle.
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    if (layoutClosing || disposingMaps || !mapSlots.Contains(slot) || slot.Window is not null) return;
                    ArrangeMaps(); editor.ActivateMap(); QueueMapSave();
                }));
            }
        };
        expandedMap = null; ArrangeMaps(); editor.ActivateMap(); window.Show(); QueueMapSave();
    }
    private void CloseMap_Click(object sender, RoutedEventArgs e) => MapRoot.CloseMap(this);
    private bool CloseMap(EditorWorkspace editor)
    {
        if (!editor.FinishEdit()) return false;
        var slot = mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, editor)); if (slot is null) return true;
        if (ReferenceEquals(editor, this))
        {
            var next = mapSlots.FirstOrDefault(s => !ReferenceEquals(s.Editor, this) && s.Window is null);
            if (next is not null)
            {
                if (!next.Editor.FinishEdit()) return false;
                var state = CaptureMap(next.Editor); var selected = next.Editor.table;
                RemoveMapSlot(next); MapTabs.Items.Clear();
                if (selected is not null) { OpenTable(selected); RestoreMap(this, state with { Floating = false }); }
            }
            else MapTabs.Items.Clear();
        }
        else RemoveMapSlot(slot);
        expandedMap = null; activeMap = mapSlots.Select(s => s.Editor).FirstOrDefault(p => p.table is not null) ?? this;
        ArrangeMaps(); activeMap.ActivateMap(); QueueMapSave(); return true;
    }
    private void RemoveMapSlot(MapSlot slot)
    {
        bool wasDisposing = disposingMaps; disposingMaps = true;
        slot.Editor.PrepareHostClose(); slot.Window?.Close(); mapDock.Children.Remove(slot.Frame);
        slot.Frame.Child = null; slot.Editor.document!.Changed -= slot.Editor.Document_Changed; mapSlots.Remove(slot);
        disposingMaps = wasDisposing;
    }
    private void ClearMapWindows()
    {
        if (mapOwner is not null) return;
        disposingMaps = true;
        foreach (var slot in mapSlots.Where(s => !ReferenceEquals(s.Editor, this)).ToArray()) RemoveMapSlot(slot);
        disposingMaps = false; expandedMap = null; activeMap = this; ArrangeMaps();
    }
    private bool FinishAllMapEdits()
    {
        var root = MapRoot;
        foreach (var slot in root.mapSlots.ToArray())
            if (!slot.Editor.FinishEdit())
            {
                if (Window.GetWindow(root) is MainWindow main) main.OpenEditorFile();
                root.SetWorkspaceActive(true);
                if (root.expandedMap is not null && !ReferenceEquals(root.expandedMap, slot)) { root.expandedMap = null; root.ArrangeMaps(); }
                root.EditorStatus.Text = "Correct or cancel the pending entry in " + slot.Editor.table?.Name + " before continuing.";
                slot.Editor.ActivateMap(); slot.Window?.Activate(); return false;
            }
        return true;
    }
    private void RefreshMapControls()
    {
        if (PaneCaption is null) return;
        PaneCaption.Text = table?.Name ?? "Choose a map"; PaneCaption.ToolTip = PaneCaption.Text;
        CloseMapButton.IsEnabled = UndockMapButton.IsEnabled = ExpandMapButton.IsEnabled = table is not null;
        updatingMapControls = true;
        AutoFitBox.IsChecked = autoFitCells; CellZoomText.Text = cellZoom.ToString("P0", CultureInfo.InvariantCulture);
        PaneViewBox.SelectedIndex = ViewMode; CellZoomOut.IsEnabled = cellZoom > .85; CellZoomIn.IsEnabled = cellZoom < 1.5;
        var controls = table is { Count: > 1 } ? Visibility.Visible : Visibility.Collapsed;
        AutoFitBox.Visibility = CellZoomOut.Visibility = CellZoomIn.Visibility = CellZoomText.Visibility = PaneViewBox.Visibility = controls;
        PaneViewBox.IsEnabled = table is { IsChoice: false };
        updatingMapControls = false;
    }
    private void CellFit_Changed(object sender, RoutedEventArgs e)
    {
        if (updatingMapControls || AutoFitBox is null) return;
        autoFitCells = AutoFitBox.IsChecked == true; UpdateTableSizing(); QueueMapSave();
    }
    private void CellZoom_Click(object sender, RoutedEventArgs e)
    {
        cellZoom = Math.Round(Math.Clamp(cellZoom + (ReferenceEquals(sender, CellZoomOut) ? -.05 : .05), .85, 1.5), 2);
        MapGrid.FontSize = 13 * cellZoom; MapGrid.ColumnHeaderHeight = Math.Ceiling(26 * cellZoom);
        measuredCellWidth = double.NaN;
        RefreshAxes(); RefreshMapControls(); UpdateCompactMap(); UpdateTableSizing(); QueueMapSave();
    }
    private void PaneView_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (updatingMapControls || PaneViewBox.SelectedIndex < 0) return;
        if (!FinishEdit()) { RefreshMapControls(); return; }
        SetViewMode(PaneViewBox.SelectedIndex); QueueMapSave();
    }

    private void QueueMapSave()
    {
        var root = MapRoot;
        if (root.restoringMaps || root.disposingMaps || root.layoutClosing || root.mapsPath is null) return;
        root.mapSaveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => { root.mapSaveTimer!.Stop(); root.SaveMapWorkspace(); }, root.Dispatcher);
        root.mapSaveTimer.Stop(); root.mapSaveTimer.Start();
    }
    private MapPanePreferences CaptureMap(EditorWorkspace editor)
    {
        var slot = mapSlots.First(s => ReferenceEquals(s.Editor, editor));
        var scroll = Descendants(editor.MapGrid).OfType<ScrollViewer>().FirstOrDefault();
        return editor.Graph.CaptureView(new MapPanePreferences
        {
            Key = editor.table is null ? "" : MapKey(editor.table), Zoom = editor.cellZoom, AutoFit = editor.autoFitCells,
            View = editor.ViewMode, Baseline = Math.Min(1, editor.BaselineBox.SelectedIndex), Selection = editor.SelectedIndices(),
            HorizontalOffset = scroll?.HorizontalOffset ?? 0, VerticalOffset = scroll?.VerticalOffset ?? 0,
            PreviewWidth = editor.previewWidth, TableFraction = editor.tableFraction, Floating = slot.Window is not null,
            Bounds = Bounds(slot.Window, slot.Bounds)
        });
    }
    private void SaveMapWorkspace()
    {
        if (mapOwner is not null) { MapRoot.SaveMapWorkspace(); return; }
        if (mapsPath is null || document is null || restoringMaps || disposingMaps) return;
        mapSaveTimer?.Stop();
        var state = new MapWorkspacePreferences { Calibration = document.Definition.Calibration,
            Maps = mapSlots.Where(s => s.Editor.table is not null).Select(s => CaptureMap(s.Editor)).ToArray(),
            Active = activeMap?.table is { } t ? MapKey(t) : "", Columns = dockColumns, ColumnFraction = dockColumnFraction, RowFraction = dockRowFraction, RowWeights = dockRowWeights };
        mapProfiles = mapProfiles.Where(p => p.Calibration != state.Calibration).Append(state).TakeLast(32).ToArray();
        try { MapWorkspacePreferences.Save(mapsPath, mapProfiles); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { EditorStatus.ToolTip = "Map layout could not be saved: " + error.Message; }
    }
    private void RestoreMap(EditorWorkspace editor, MapPanePreferences state)
    {
        editor.cellZoom = state.Zoom; editor.autoFitCells = state.AutoFit; editor.previewWidth = state.PreviewWidth; editor.tableFraction = state.TableFraction;
        editor.MapGrid.FontSize = 13 * state.Zoom; editor.MapGrid.ColumnHeaderHeight = Math.Ceiling(26 * state.Zoom);
        editor.measuredCellWidth = double.NaN;
        editor.BaselineBox.SelectedIndex = state.Baseline; editor.SetViewMode(state.View); editor.RefreshAxes(); editor.RefreshMapControls();
        void ApplySelection()
        {
            if (editor.layoutClosing || editor.table is null || MapKey(editor.table) != state.Key) return;
            editor.MapGrid.UpdateLayout();
            editor.refreshing = true;
            editor.MapGrid.SelectedCells.Clear();
            foreach (int index in state.Selection.Where(i => i < editor.table.Count))
                if (!editor.ScalarMap) editor.MapGrid.SelectedCells.Add(new(editor.MapGrid.Items[editor.CurveList ? index : index / editor.table.Columns], editor.MapGrid.Columns[editor.CurveList ? 1 : index % editor.table.Columns]));
            editor.refreshing = false;
            editor.UpdateSelectedAxes(); editor.UpdateCellText(); editor.Graph.Select(state.Selection.FirstOrDefault(-1)); editor.Graph.RestoreView(state);
            var scroll = Descendants(editor.MapGrid).OfType<ScrollViewer>().FirstOrDefault();
            scroll?.ScrollToHorizontalOffset(state.HorizontalOffset); scroll?.ScrollToVerticalOffset(state.VerticalOffset);
        }
        if (editor.MapGrid.IsLoaded) ApplySelection();
        // WPF assigns column display indices when the view is loaded. Restoring
        // cells earlier creates invalid DataGridCellInfo entries after startup.
        editor.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplySelection));
    }
    private void RestoreMapWorkspace()
    {
        if (mapOwner is not null || document is null) return;
        var saved = mapProfiles.FirstOrDefault(p => p.Calibration == document.Definition.Calibration); if (saved is null) return;
        restoringMaps = true;
        try
        {
            dockColumns = saved.Columns; dockColumnFraction = saved.ColumnFraction; dockRowFraction = saved.RowFraction;
            dockRowWeights = saved.RowWeights;
            foreach (var state in saved.Maps)
            {
                var selected = document.Definition.Tables.FirstOrDefault(t => MapKey(t) == state.Key); if (selected is null) continue;
                var editor = OpenMap(selected); RestoreMap(editor, state);
                var slot = mapSlots.First(s => ReferenceEquals(s.Editor, editor)); slot.Bounds = state.Bounds;
                if (state.Floating) DetachMap(editor);
            }
            var active = mapSlots.FirstOrDefault(s => s.Editor.table is { } t && MapKey(t) == saved.Active);
            active?.Editor.ActivateMap(); ArrangeMaps();
        }
        finally { restoringMaps = false; }
    }
}
