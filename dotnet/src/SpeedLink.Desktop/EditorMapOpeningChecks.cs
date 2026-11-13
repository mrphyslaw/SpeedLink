using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    internal async Task<object> ExerciseMapOpening(string folder, string? name)
    {
        var doc = document!;
        var bytes = doc.Snapshot();
        var host = Window.GetWindow(this);
        host.Width = 1540; host.Height = 980;
        browserWidth = 350;
        ApplyLayout();
        var pinnedMap = doc.Definition.Tables.First(t => t.Count == 1 && !t.IsChoice);
        var pinned = OpenMap(pinnedMap);
        await Settle();
        var pinnedBounds = DockedBounds(pinned);
        var results = new List<object>();
        var failures = new List<string>();
        int complete = 0, scrolling = 0;
        var tables = doc.Definition.Tables.Where(t => name is null || t.Name == name).ToArray();
        if (tables.Length == 0) throw new InvalidOperationException("No matching map to check.");
        foreach (var selected in tables)
        {
            BrowseMap(selected);
            await Settle();
            var editor = CommandMap;
            if (!ReferenceEquals(editor.table, selected) || !SameBounds(pinnedBounds, DockedBounds(pinned)))
                throw new InvalidOperationException($"Browsing {selected.Name} changed the pinned map or did not open the requested control.");
            bool fits = editor.ScalarMap;
            if (!editor.ScalarMap)
            {
                var grid = editor.MapGrid;
                var scroll = Descendants(grid).OfType<ScrollViewer>().First();
                var rows = scroll.Template.FindName("PART_ScrollContentPresenter", scroll) as ScrollContentPresenter;
                double visibleHeight = rows?.ActualHeight ?? 0;
                bool horizontal = scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible;
                bool vertical = scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible;
                double columnWidth = grid.Columns.Sum(c => c.ActualWidth);
                int rowCount = editor.CurveList ? selected.Count : selected.Rows;
                bool needsWidth = columnWidth + grid.RowHeaderWidth > grid.ActualWidth + 1;
                double rowHeight = Descendants(grid).OfType<DataGridRow>().FirstOrDefault(r => r.IsVisible)?.ActualHeight ?? grid.RowHeight;
                double allRows = rowHeight * rowCount;
                fits = !horizontal && !vertical && !needsWidth && visibleHeight + 1 >= allRows;
                var minimum = editor.MinimumMapPaneSize();
                bool enoughRoom = editor.ViewMode == 2 && minimum.Width <= mapDockScroll.ViewportWidth &&
                    minimum.Height <= mapDockScroll.ViewportHeight - 2;
                if (visibleHeight + 1 < rowHeight || enoughRoom && !fits)
                {
                    if (failures.Count == 0) CaptureHost(Path.Combine(folder, "MapOpeningFailure.png"));
                    failures.Add($"{selected.Name} ({selected.Columns}x{selected.Rows}): pane {DockedBounds(editor)}, minimum {minimum}, " +
                        $"grid {grid.ActualWidth}x{grid.ActualHeight}, data height {visibleHeight}/{allRows}; scroll {horizontal}/{vertical}.");
                }
            }
            if (fits) complete++; else scrolling++;
            results.Add(new { selected.Name, selected.Columns, selected.Rows, fits });
            if (doc.Definition.Calibration == "L3P4EM000" &&
                selected.Name is "BCS DC Adjustment IAT" or "Open Loop Part Throttle AFR Target Knock Present")
                CaptureHost(Path.Combine(folder, selected.Name == "BCS DC Adjustment IAT" ? "BCS-IAT.png" : "AFR-Knock.png"));
        }
        if (!bytes.AsSpan().SequenceEqual(doc.Snapshot())) throw new InvalidOperationException("Opening maps changed the ROM bytes.");
        foreach (var selected in tables.Where(t => t.Name is "BCS DC Adjustment IAT" or "Open Loop Part Throttle AFR Target Knock Present"))
        {
            if (doc.Definition.Calibration != "L3P4EM000") continue;
            var prior = doc.Definition.Tables.First(t => t.Count == 1 && !t.IsChoice && t != pinnedMap);
            BrowseMap(prior);
            await Settle();
            var reusable = CommandMap;
            var slot = mapSlots.Single(s => ReferenceEquals(s.Editor, reusable));
            slot.DockBounds = new(380, 65, 300, 260);
            slot.PreserveBoundsOnOpen = true;
            ArrangeMaps();
            await Settle();
            BrowseMap(selected);
            await Settle();
            var scroll = Descendants(reusable.MapGrid).OfType<ScrollViewer>().First();
            if (!ReferenceEquals(CommandMap, reusable) || !SameBounds(pinnedBounds, DockedBounds(pinned)) ||
                scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible ||
                scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                failures.Add($"The saved 300×260 preview did not fit {selected.Name}.");
        }
        return new { passed = failures.Count == 0, checkedMaps = results.Count, complete, scrolling, failures, results };
    }

    private async Task<string[]> ExerciseEdgeDocking()
    {
        var first = document!.Definition.Tables.First(t => t.Count == 1 && !t.IsChoice);
        var second = document.Definition.Tables.First(t => t.Rows > 1 && t.Columns > 1 && !t.IsChoice);
        var pinned = OpenMap(first);
        var editor = OpenMap(second);
        await Settle();
        FreezeMapPositions();
        var fixedBounds = DockedBounds(pinned);
        var slot = mapSlots.Single(s => ReferenceEquals(s.Editor, editor));
        foreach (var side in new[] { MapResizeEdge.Left, MapResizeEdge.Top, MapResizeEdge.Right, MapResizeEdge.Bottom })
        {
            DetachMap(editor);
            await Settle();
            double width = mapDockScroll.ViewportWidth, height = mapDockScroll.ViewportHeight;
            var point = side switch
            {
                MapResizeEdge.Left => new Point(-5, height / 2),
                MapResizeEdge.Top => new Point(width / 2, 5),
                MapResizeEdge.Right => new Point(width + 5, height / 2),
                _ => new Point(width / 2, height - 5)
            };
            if (DockEdgeAt(new(width / 2, height / 2)) is not null ||
                DockEdgeAt(new(-70, height / 2)) is not null || DockEdgeAt(point) != side)
                throw new InvalidOperationException($"The dock target did not recognize the {side} edge.");
            var edge = DockEdgeAt(point);
            ShowDockTarget(edge);
            if (!dockTarget.IsVisible || dockTarget.IsHitTestVisible)
                throw new InvalidOperationException("Dock preview was absent or blocked pointer input.");
            ShowDockTarget(null);
            DockDetachedMap(slot, edge);
            await Settle();
            var bounds = DockedBounds(editor);
            double gap = side switch
            {
                MapResizeEdge.Left => bounds.Left - mapDockScroll.HorizontalOffset,
                MapResizeEdge.Top => bounds.Top - mapDockScroll.VerticalOffset,
                MapResizeEdge.Right => mapDockScroll.HorizontalOffset + width - bounds.Left - bounds.Width,
                _ => mapDockScroll.VerticalOffset + height - bounds.Top - bounds.Height
            };
            editor.SelectCell(0);
            if (slot.Window is not null || !editor.MapGrid.IsVisible || !editor.HasEditableCells || Math.Abs(gap) > 3 ||
                !slot.Pinned || !SameBounds(fixedBounds, DockedBounds(pinned)) || dockTarget.IsVisible)
                throw new InvalidOperationException($"Docking to {side} did not preserve the map and its neighbor. " +
                    $"Gap {gap}, floating {slot.Window is not null}, visible {editor.MapGrid.IsVisible}, pinned {slot.Pinned}, " +
                    $"neighbor {fixedBounds} -> {DockedBounds(pinned)}, target {dockTarget.IsVisible}.");
        }
        ClearMapWindows(); MapTabs.Items.Clear(); await Settle();
        return ["Detached maps dock to every edge without shifting the pinned map"];
    }

}

public partial class MainWindow
{
    internal async Task<object> ExerciseMapOpeningOffline(string path, string folder, string? name)
    {
        Show(); Navigate(1); EditorPane.OpenRom(path); UpdateLayout();
        return await EditorPane.ExerciseMapOpening(folder, name);
    }
}
