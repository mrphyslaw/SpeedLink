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
        var entries = MapTreeEntries(MapTree).Where(row => row.Tag is TuningTable).ToDictionary(row => (TuningTable)row.Tag);
        foreach (var group in MapTreeEntries(MapTree).Where(row => row.Tag is string)) group.IsExpanded = true;
        await Settle();
        var browser = Descendants(MapTree).OfType<ScrollViewer>().First();
        int stableBrowserSelections = 0, namedRowsChecked = 0;
        foreach (var selected in tables)
        {
            var entry = entries[selected];
            // A folder can expand its extent after virtualized rows are laid out.
            // Start at the left edge and request the same reveal as keyboard focus.
            browser.ScrollToLeftEnd();
            entry.IsSelected = false;
            entry.IsSelected = true;
            entry.BringIntoView();
            await Settle();
            if (browser.HorizontalOffset > 1)
                failures.Add($"Selecting {selected.Name} moved the map browser horizontally ({browser.HorizontalOffset:F0}).");
            else stableBrowserSelections++;
            var editor = CommandMap;
            if (!ReferenceEquals(editor.table, selected) || !SameBounds(pinnedBounds, DockedBounds(pinned)))
                throw new InvalidOperationException($"Browsing {selected.Name} changed the pinned map or did not open the requested control.");
            bool fits = editor.ScalarMap;
            if (!editor.ScalarMap)
            {
                if (editor.HasNamedRows)
                {
                    editor.CheckNamedRowLabels(requireUnwrapped: true);
                    namedRowsChecked++;
                }
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
                double graphSpace = editor.CurveList ? CurveGraphSpace : 0;
                bool enoughRoom = minimum.Width + graphSpace <= mapDockScroll.ViewportWidth &&
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
        foreach (var selected in tables.Where(t => doc.Definition.Calibration == "L3P4EM000" &&
            t.Name is "BCS DC Adjustment IAT" or "Open Loop Part Throttle AFR Target Knock Present"))
        foreach (var size in new[] { new Size(1540, 980), new Size(1044, 760) })
        {
            host.Width = size.Width; host.Height = size.Height;
            await Settle();
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
            var bounds = DockedBounds(reusable);
            var minimum = reusable.MinimumMapPaneSize();
            double width = mapDockScroll.ViewportWidth, height = mapDockScroll.ViewportHeight - 2;
            bool horizontal = scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible;
            bool vertical = scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible;
            bool fits = minimum.Width <= width && minimum.Height <= height;
            // The deliberately undersized pane can temporarily add a workspace
            // scrollbar. Its removal gives the viewport one scrollbar's height back.
            bool valid = ReferenceEquals(CommandMap, reusable) && SameBounds(pinnedBounds, DockedBounds(pinned)) &&
                (fits ? !horizontal && !vertical : (!horizontal || bounds.Width >= width - SystemParameters.VerticalScrollBarWidth - 2) &&
                    (!vertical || bounds.Height >= height - SystemParameters.HorizontalScrollBarHeight - 2));
            if (valid && (horizontal || vertical))
            {
                reusable.SelectCell(selected.Count - 1);
                await Settle();
                valid = reusable.SelectedIndices().SequenceEqual([selected.Count - 1]) &&
                    (!horizontal || scroll.HorizontalOffset > 0) && (!vertical || scroll.VerticalOffset > 0);
            }
            if (!valid)
                failures.Add($"The saved 300×260 preview did not fit {selected.Name} to the available workspace " +
                    $"({width:F0}×{height:F0}, minimum {minimum}, pane {bounds}, scroll {horizontal}/{vertical}).");
        }
        var namedMap = tables.FirstOrDefault(t => t.Name == "P0012 Intake Cam Timing Over-Retarded - Monitor Requests")
            ?? tables.Where(t => t is { Count: > 1, Bits.Length: > 0 }).MaxBy(t => t.BitNames.Max(label => label.Length));
        if (namedMap is not null) await ExerciseRowLabelResize(pinned, namedMap, folder);
        if (!bytes.AsSpan().SequenceEqual(document!.Snapshot())) throw new InvalidOperationException("Opening maps changed the ROM bytes.");
        return new { passed = failures.Count == 0, checkedMaps = results.Count, stableBrowserSelections, namedRowsChecked,
            rowLabelResize = namedMap is not null, complete, scrolling, failures, results };
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
            var size = new Size(slot.Frame.ActualWidth, slot.Frame.ActualHeight);
            var placement = PlanDockPlacement(point, slot, size);
            if (PlanDockPlacement(new(width / 2, height / 2), slot, size) is not null ||
                PlanDockPlacement(new(-70, height / 2), slot, size) is not null || placement?.Edge != side)
                throw new InvalidOperationException($"The dock target did not recognize the {side} edge.");
            if (side == MapResizeEdge.Left &&
                (PlanDockPlacement(new(12, 8), slot, size, side)?.Edge != side ||
                 PlanDockPlacement(new(20, 2), slot, size, side)?.Edge != MapResizeEdge.Top))
                throw new InvalidOperationException("The dock preview did not handle the corner consistently.");
            ShowDockTarget(placement);
            await Settle();
            var outline = dockTarget.TransformToVisual(mapDockScroll).TransformBounds(new Rect(dockTarget.RenderSize));
            if (!dockTarget.IsVisible || dockTarget.IsHitTestVisible ||
                Math.Abs(outline.Left - placement.Preview.Left) > 1 || Math.Abs(outline.Top - placement.Preview.Top) > 1 ||
                Math.Abs(outline.Width - placement.Preview.Width) > 1 || Math.Abs(outline.Height - placement.Preview.Height) > 1)
                throw new InvalidOperationException("The dock preview did not match the planned pane.");
            ShowDockTarget(null);
            DockDetachedMap(slot, placement);
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
                !SameBounds(placement.Bounds, bounds) ||
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
