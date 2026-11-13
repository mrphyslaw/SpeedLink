using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private int returnView = 2;
    private bool showMapEditTools;
    private bool compactRows;
    private Action? refreshFocusedMapMenu;
    private static readonly Style compactValueStyle = CreateCompactValueStyle();

    private static Style CreateCompactValueStyle()
    {
        var style = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        return style;
    }

    private void ToggleGraph_Click(object sender, RoutedEventArgs e) => CommandMap.ToggleGraphView();

    private void ToggleMapEditTools()
    {
        if (!FinishEdit()) return;
        showMapEditTools = !showMapEditTools;
        ApplyView();
    }

    private void SetCellZoom(double zoom)
    {
        if (!FinishEdit()) return;
        cellZoom = Math.Round(Math.Clamp(zoom, .85, 1.5), 2);
        ApplyGridDensity();
        QueueMapSave();
    }

    private void ApplyGridDensity()
    {
        MapGrid.FontSize = Math.Max(11, (compactRows ? 12 : 13) * cellZoom);
        MapGrid.ColumnHeaderHeight = Math.Ceiling((compactRows ? 19 : 26) * cellZoom);
        foreach (var column in MapGrid.Columns.OfType<DataGridTextColumn>())
            column.ElementStyle = compactRows && !column.IsReadOnly ? compactValueStyle : DataGridTextColumn.DefaultElementStyle;
        measuredCellWidth = double.NaN;
        RefreshAxes();
        if (ViewMode == 1 && CurveList) ApplyView();
        else { UpdateCompactMap(); UpdateTableSizing(); }
        MapRoot.RefreshMapCommandState();
    }

    private void ToggleCompactRows()
    {
        if (!FinishEdit()) return;
        automaticCompactRows = false;
        compactRows = !compactRows;
        ApplyGridDensity();
        QueueMapSave();
    }

    private void ToggleAutoFit()
    {
        if (!FinishEdit()) return;
        autoFitCells = !autoFitCells;
        UpdateTableSizing();
        MapRoot.RefreshMapCommandState();
        QueueMapSave();
    }

    private void ToggleGraphView()
    {
        if (table is not { Count: > 1, IsChoice: false } || !FinishEdit()) return;
        int currentView = ViewMode;
        graphWindow?.Close();
        if (currentView == 3)
        {
            SetViewMode(returnView);
            MapGrid.Focus();
        }
        else
        {
            returnView = currentView;
            SetViewMode(3);
            GraphPanel.Focus();
        }
        QueueMapSave();
    }

    private void CloseOtherMaps_Click(object sender, RoutedEventArgs e) => CommandMap.CloseOtherMaps();

    private void CloseOtherMaps()
    {
        var root = MapRoot;
        if (table is null || root.mapSlots.Count(s => s.Editor.table is not null) < 2 ||
            !root.FinishAllMapEdits()) return;
        foreach (var slot in root.mapSlots.Where(s => !ReferenceEquals(s.Editor, root) && !ReferenceEquals(s.Editor, this)).ToArray())
            root.RemoveMapSlot(slot);
        if (!ReferenceEquals(this, root) && root.table is not null)
        {
            root.CloseMap(root);
            return;
        }
        root.expandedMap = null;
        root.ArrangeMaps();
        ActivateMap();
    }

    private static ContextMenu MapContextMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(BackgroundProperty, "PanelBrush");
        menu.SetResourceReference(ForegroundProperty, "TextBrush");
        menu.SetResourceReference(BorderBrushProperty, "LineBrush");
        return menu;
    }

    private void InitializeMapActions()
    {
        MapGrid.GotKeyboardFocus += (_, _) =>
        {
            if (!ReferenceEquals(MapRoot.activeMap, this)) ActivateMap();
        };
        MapGrid.PreviewMouseRightButtonDown += (_, e) =>
        {
            for (var source = e.OriginalSource as Visual; source is not null && source != MapGrid;
                source = VisualTreeHelper.GetParent(source) as Visual)
            {
                if (source is not DataGridCell cell || cell.Column is null) continue;
                var clicked = new DataGridCellInfo(cell.DataContext, cell.Column);
                if (!MapGrid.SelectedCells.Contains(clicked) && FinishEdit())
                {
                    MapGrid.SelectedCells.Clear();
                    MapGrid.CurrentCell = clicked;
                    MapGrid.SelectedCells.Add(clicked);
                }
                break;
            }
        };
        var actions = MapContextMenu();
        MapGrid.ContextMenu = actions;
        actions.Opened += (_, _) =>
        {
            ActivateMap();
            actions.Items.Clear();
            bool values = HasEditableCells;
            AddAction(actions, "Copy", "Ctrl+C", () => Copy_Click(this, new()), MapGrid.SelectedCells.Count > 0);
            AddAction(actions, "Paste", "Ctrl+V", () => Paste_Click(this, new()), values);
            AddAction(actions, "Reset to baseline", "", () => Reset_Click(this, new()), values);
            actions.Items.Add(new Separator());
            AddAction(actions, "Increase", "Q", () => Nudge(1), values);
            AddAction(actions, "Decrease", "A", () => Nudge(-1), values);
            AddAction(actions, "Increase by 10 steps", "Shift+Q", () => Nudge(10), values);
            AddAction(actions, "Decrease by 10 steps", "Shift+A", () => Nudge(-10), values);
            actions.Items.Add(new Separator());
            AddAction(actions, "Interpolate horizontally", "Shift+H", () => Interpolate("Horizontal"), values && table!.Columns > 1);
            AddAction(actions, "Interpolate vertically", "Shift+V", () => Interpolate("Vertical"), values && table!.Rows > 1);
        };
        var paneMenu = MapContextMenu();
        PaneDragArea.ContextMenu = paneMenu;
        var refresh = AddFocusedMapOptions(paneMenu.Items, () => this, includeViews: true);
        paneMenu.Opened += (_, _) => { ActivateMap(); refresh(); };
    }

    private Action AddFocusedMapOptions(ItemCollection items, Func<EditorWorkspace> target, bool includeViews)
    {
        var update = new List<Action>();
        static bool Loaded(EditorWorkspace map) => map.table is not null;
        static bool Numeric(EditorWorkspace map) => map.table is { Count: > 1, IsChoice: false };
        MenuItem Add(ItemCollection parent, string title, Action<EditorWorkspace> run, Func<EditorWorkspace, bool> enabled,
            Func<EditorWorkspace, bool>? selected = null, string shortcut = "")
        {
            var item = new MenuItem { Header = title, InputGestureText = shortcut };
            item.Click += (_, _) => { var map = target(); if (enabled(map)) run(map); };
            parent.Add(item);
            update.Add(() => { var map = target(); item.IsEnabled = enabled(map); item.IsChecked = selected?.Invoke(map) == true; });
            return item;
        }

        var heading = new MenuItem { IsEnabled = false };
        items.Insert(0, heading);
        update.Add(() => heading.Header = target().table?.Name.Replace("_", "__", StringComparison.Ordinal) ?? "No map selected");
        if (includeViews)
        {
            Add(items, "Toggle table and graph", m => m.ToggleGraphView(), Numeric, shortcut: "F4");
            var views = new MenuItem { Header = "Table and graph layout" };
            items.Add(views);
            string[] names = ["Table above graph", "Table beside graph", "Table only", "Graph only", "Table with preview"];
            for (int index = 0; index < names.Length; index++)
            {
                int mode = index;
                Add(views.Items, names[mode], m => m.ChangeViewMode(mode), Numeric, m => m.ViewMode == mode);
            }
            update.Add(() => views.IsEnabled = Numeric(target()));
            Add(items, "Fit entire map", m => m.FitMap_Click(m, new()), Numeric, shortcut: "Ctrl+0");
            Add(items, "Map information…", m => m.ShowMapInformation(), Loaded, shortcut: "F1");
            Add(items, "Pop out graph", m => m.PopGraph_Click(m, new()), Numeric);
            Add(items, "Dock graph", m => m.graphWindow?.Close(), m => m.graphWindow is not null);
        }

        var display = new MenuItem { Header = "Cell display" };
        items.Insert(1, display);
        var larger = Add(display.Items, "Larger text", m => m.SetCellZoom(m.cellZoom + .05), m => Numeric(m) && m.cellZoom < 1.5);
        Add(display.Items, "Smaller text", m => m.SetCellZoom(m.cellZoom - .05), m => Numeric(m) && m.cellZoom > .85);
        Add(display.Items, "Reset text size", m => m.SetCellZoom(1), m => Numeric(m) && m.cellZoom != 1);
        Add(display.Items, "Auto fit cells", m => m.ToggleAutoFit(), Numeric, m => m.autoFitCells);
        Add(display.Items, "Compact rows", m => m.ToggleCompactRows(), Numeric, m => m.compactRows);
        update.Add(() => { var map = target(); display.IsEnabled = Numeric(map); larger.Header = $"Larger text ({map.cellZoom:P0})"; });

        var compare = new MenuItem { Header = "Comparison baseline" };
        items.Insert(2, compare);
        Add(compare.Items, "Original ROM", m => m.BaselineBox.SelectedIndex = 0, Loaded, m => m.BaselineBox.SelectedIndex == 0);
        Add(compare.Items, "Last save", m => m.BaselineBox.SelectedIndex = 1, Loaded, m => m.BaselineBox.SelectedIndex == 1);
        Add(compare.Items, "Comparison ROM", m => m.BaselineBox.SelectedIndex = 2, m => Loaded(m) && m.document?.HasComparison == true, m => m.BaselineBox.SelectedIndex == 2);
        Add(compare.Items, "Load comparison ROM…", m => m.Compare_Click(m, new()), Loaded);
        update.Add(() => compare.IsEnabled = Loaded(target()));

        Add(items, "Edit tools", m => m.ToggleMapEditTools(), Numeric, m => m.showMapEditTools);
        var dock = Add(items, "Undock map", m => m.UndockMap_Click(m, new()), Loaded);
        var expand = Add(items, "Expand map", m => m.ExpandMap_Click(m, new()), Loaded);
        if (includeViews) Add(items, "Close other maps", m => m.CloseOtherMaps(), m => m.MapRoot.mapSlots.Count(s => s.Editor.table is not null) > 1);
        update.Add(() =>
        {
            var map = target();
            var slot = map.MapRoot.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, map));
            dock.Header = slot?.Window is null ? "Undock map" : "Dock map";
            bool expanded = slot is not null && (ReferenceEquals(slot, map.MapRoot.expandedMap) || slot.Window?.WindowState == WindowState.Maximized);
            expand.Header = expanded ? "Restore map layout" : "Expand map";
        });
        return () => { foreach (var refresh in update) refresh(); };
    }

    private static void AddAction(ContextMenu menu, string name, string shortcut, Action execute, bool enabled)
    {
        var item = new MenuItem { Header = name, InputGestureText = shortcut, IsEnabled = enabled };
        item.Click += (_, _) => execute();
        menu.Items.Add(item);
    }

    private bool HasEditableCells => table is { IsChoice: false } && MapGrid.SelectedCells.Count > 0 &&
        MapGrid.SelectedCells.All(cell => cell.Column is { IsReadOnly: false });

    private bool HandleMapShortcut(KeyEventArgs e)
    {
        var map = CommandMap;
        if (Keyboard.FocusedElement is TextBox or ComboBox) return false;
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.F4 && modifiers == ModifierKeys.None && map.table is { Count: > 1, IsChoice: false })
        {
            if (!e.IsRepeat) map.ToggleGraphView();
        }
        else if (map.MapGrid.IsKeyboardFocusWithin && map.HasEditableCells && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            int steps = modifiers == ModifierKeys.Shift ? 10 : 1;
            switch (e.Key)
            {
                case Key.Q: map.Nudge(steps); break;
                case Key.A: map.Nudge(-steps); break;
                case Key.H when modifiers == ModifierKeys.Shift && map.table!.Columns > 1:
                    map.Interpolate("Horizontal"); break;
                case Key.V when modifiers == ModifierKeys.Shift && map.table!.Rows > 1:
                    map.Interpolate("Vertical"); break;
                default: return false;
            }
            map.MapGrid.Focus();
        }
        else return false;
        e.Handled = true;
        return true;
    }

    private void Interpolate(string direction)
    {
        if (document is null || table is null || !HasEditableCells || !FinishEdit()) return;
        Attempt(() =>
        {
            var r = Rectangle();
            document.Interpolate(table, r.Left, r.Top, r.Right, r.Bottom, direction);
        });
    }

    private void Nudge(int steps)
    {
        if (document is null || table is null || !HasEditableCells || !FinishEdit()) return;
        var map = table;
        var doc = document;
        Attempt(() => doc.Apply(SelectedIndices().Select(index =>
        {
            double value = doc.Read(map, index);
            return new CellEdit(map, index, value + steps * AdjustmentStep(map, value));
        }), (steps > 0 ? "Increase " : "Decrease ") + map.Name));
    }

    private static double AdjustmentStep(TuningTable map, double value)
    {
        int point = map.Format.IndexOf('.');
        int places = point < 0 ? 0 : map.Format[(point + 1)..].TakeWhile(c => c is '0' or '#').Count();
        double displayStep = Math.Pow(10, -Math.Clamp(places, 0, 4));
        double raw = map.Encode(value);
        double previous, next;
        if (map.Storage == "float")
        {
            previous = float.BitDecrement((float)raw);
            next = float.BitIncrement((float)raw);
        }
        else
        {
            raw = Math.Round(raw, MidpointRounding.AwayFromZero);
            previous = raw - 1;
            next = raw + 1;
        }
        foreach (double neighbor in new[] { previous, next })
        {
            if (!double.IsFinite(neighbor)) continue;
            double spacing = Math.Abs(map.Decode(neighbor) - value);
            if (double.IsFinite(spacing)) displayStep = Math.Max(displayStep, spacing);
        }
        return displayStep;
    }
}
