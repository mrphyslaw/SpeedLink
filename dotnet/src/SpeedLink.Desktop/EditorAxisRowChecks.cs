using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private void CheckAxisRowAlignment()
    {
        if (table is null) throw new InvalidOperationException("No table selected for axis check.");
        bool x = !table.IsChoice && !CurveList && table.Columns > 1;
        bool y = !table.IsChoice && !CurveList && table.Rows > 1;
        if (XAxisBand.IsVisible != x || YAxisBand.IsVisible != y ||
            ValueUnitsText.Text != "Values: " + table.Units ||
            x && (XAxisText.Text != table.X.Name || XAxisText.ToolTip?.ToString() != table.X.Name) ||
            y && (YAxisText.Text != table.Y.Name || YAxisText.ToolTip?.ToString() != table.Y.Name))
            throw new InvalidOperationException($"Incorrect axis visibility/metadata: {table.Name}.");
        Rect Bounds(FrameworkElement element) => element.TransformToVisual(MapGrid).TransformBounds(new Rect(element.RenderSize));
        if (x)
        {
            double left = MapGrid.RowHeaderWidth;
            double right = Descendants(MapGrid).OfType<ScrollBar>().Where(b => b.IsVisible && b.Orientation == Orientation.Vertical)
                .Select(b => Bounds(b).Left).DefaultIfEmpty(MapGrid.ActualWidth).Min();
            var columns = Descendants(MapGrid).OfType<DataGridColumnHeader>().Where(h => h.IsVisible && h.Column is not null)
                .Select(Bounds).Where(r => r.Right > left && r.Left < right).ToArray();
            if (columns.Length == 0) throw new InvalidOperationException($"No visible column headers: {table.Name}.");
            double firstColumn = Math.Max(left, columns.Min(r => r.Left)), lastColumn = Math.Min(right, columns.Max(r => r.Right));
            Rect xTitle = Bounds(XAxisText), xArea = Bounds(XAxisTitleArea);
            if (Math.Abs((xTitle.Left + xTitle.Right - firstColumn - lastColumn) / 2) > 1 ||
                Math.Abs(xArea.Left - firstColumn) > 1 || Math.Abs(xArea.Right - lastColumn) > 1 ||
                xTitle.Left < firstColumn - 1 || xTitle.Right > lastColumn + 1 || xTitle.Width < 1)
                throw new InvalidOperationException($"Axis title is not centered on visible columns: {table.Name}; columns {firstColumn:F2}..{lastColumn:F2}, title {xTitle}, area {xArea}; widths {string.Join(',', MapGrid.Columns.Select(c => c.ActualWidth))}; headers {string.Join(';', Descendants(MapGrid).OfType<DataGridColumnHeader>().Where(h => h.Column is not null).Select(h => $"{h.Column.DisplayIndex}: {Bounds(h)}, visible={h.IsVisible}"))}.");
        }
        if (x || y) CheckAxisFrame();
        if (!y) return;
        double top = Descendants(MapGrid).OfType<DataGridColumnHeader>().Where(h => h.IsVisible)
            .Select(h => Bounds(h).Bottom).DefaultIfEmpty(MapGrid.ColumnHeaderHeight).Max();
        double bottom = Descendants(MapGrid).OfType<ScrollBar>().Where(b => b.IsVisible && b.Orientation == Orientation.Horizontal)
            .Select(b => Bounds(b).Top).DefaultIfEmpty(MapGrid.ActualHeight).Min();
        var rows = Descendants(MapGrid).OfType<DataGridRow>().Where(r => r.IsVisible).Select(Bounds)
            .Where(r => r.Bottom > top && r.Top < bottom).ToArray();
        if (rows.Length == 0) throw new InvalidOperationException($"No visible table rows: {table.Name}.");
        double first = Math.Max(top, rows.Min(r => r.Top)), last = Math.Min(bottom, rows.Max(r => r.Bottom));
        Rect title = Bounds(YAxisText), area = Bounds(YAxisTitleArea);
        if (Math.Abs((title.Top + title.Bottom - first - last) / 2) > 1 ||
            Math.Abs(area.Top - first) > 1 || Math.Abs(area.Bottom - last) > 1 ||
            title.Top < first - 1 || title.Bottom > last + 1 || title.Height < 1)
            throw new InvalidOperationException($"Axis title is not centered on visible data rows: {table.Name}; rows {first:F2}..{last:F2}, title {title}, area {area}.");
    }

    internal async Task<object> ExerciseEveryTableAxes(string folder, Action<double, double> resize, string? tableName = null, bool panesOnly = false, Action<string>? capture = null)
    {
        byte[] original = document!.Snapshot();
        var checkedTables = new List<object>();
        var shapes = document.Definition.Tables.GroupBy(t => (t.Columns, t.Rows, t.IsChoice)).Select(g => g.First()).ToArray();
        foreach (var size in new[] { (1280d, 900d), (1024d, 720d) }.Where(_ => !panesOnly))
        {
            resize(size.Item1, size.Item2);
            foreach (var selected in document.Definition.Tables.Where(t => tableName is null || t.Name == tableName))
            {
                OpenTable(selected);
                foreach (var old in MapTabs.Items.Cast<TabItem>().Where(t => !ReferenceEquals(t, MapTabs.SelectedItem)).ToArray()) MapTabs.Items.Remove(old);
                SetViewMode(4); await Settle(); await Settle();
                try { CheckAxisRowAlignment(); }
                catch { CaptureHost(Path.Combine(folder, "AxisFailure.png")); throw; }
                if (CurveList && (MapGrid.Columns.Count != 2 || MapGrid.Columns[0].Header?.ToString() != selected.X.Name))
                    throw new InvalidOperationException($"Long-curve breakpoint label is missing: {selected.Name}.");
                checkedTables.Add(new { table = selected.Name, selected.Columns, selected.Rows, selected.Kind, requestedWidth = size.Item1,
                    actualWidth = Window.GetWindow(this).ActualWidth, gridHeight = MapGrid.ActualHeight, verticalAxis = YAxisBand.IsVisible });
                if (checkedTables.Count % 50 == 0)
                    File.WriteAllText(Path.Combine(folder, "AxisProgress.json"), JsonSerializer.Serialize(new { checkedViews = checkedTables.Count, selected.Name }));
            }
        }
        var variations = new List<string>();
        foreach (var selected in shapes.Where(t => !panesOnly && !t.IsChoice && t.Rows > 1))
        {
            OpenTable(selected);
            foreach (int mode in new[] { 0, 1, 2, 4 })
            {
                SetViewMode(mode);
                foreach (double zoom in new[] { .85, 1.5 })
                {
                    cellZoom = zoom; measuredCellWidth = double.NaN; UpdateTableSizing(); await Settle(); await Settle();
                    var scroll = Descendants(MapGrid).OfType<ScrollViewer>().First();
                    scroll.ScrollToTop(); scroll.ScrollToLeftEnd(); await Settle(); CheckAxisRowAlignment();
                    scroll.ScrollToBottom(); scroll.ScrollToRightEnd(); await Settle(); CheckAxisRowAlignment();
                    variations.Add($"{selected.Columns}x{selected.Rows}: view {mode}, zoom {zoom}, both scroll ends");
                    File.WriteAllText(Path.Combine(folder, "AxisProgress.json"), JsonSerializer.Serialize(new { checkedViews = checkedTables.Count, checkedVariations = variations.Count, selected.Name }));
                }
            }
            cellZoom = 1; measuredCellWidth = double.NaN;
            SetViewMode(3); await Settle();
            if (TablePanel.IsVisible || XAxisText.IsVisible || YAxisText.IsVisible)
                throw new InvalidOperationException("Table axes remain visible in graph-only mode.");
            SetViewMode(4); await Settle(); await Settle(); CheckAxisRowAlignment();
        }
        var small = document.Definition.Tables.Where(t => !t.IsChoice && t.Columns is > 1 and <= 8).OrderBy(t => t.Columns).First();
        OpenTable(small); SetViewMode(2); await Settle(); await Settle();
        foreach (var column in MapGrid.Columns) column.Width = new(column.MinWidth);
        await Settle(); await Settle(); CheckAxisRowAlignment();
        double oldTitleWidth = XAxisTitleArea.ActualWidth;
        MapGrid.Columns[0].Width = new(MapGrid.Columns[0].ActualWidth + 12);
        await Settle(); await Settle(); CheckAxisRowAlignment();
        if (Math.Abs(XAxisTitleArea.ActualWidth - oldTitleWidth - 12) > 1)
            throw new InvalidOperationException("Horizontal title did not follow a fitting column resize.");
        if (!original.AsSpan().SequenceEqual(document.Snapshot())) throw new InvalidOperationException("Axis layout changed ROM bytes.");

        // Reproduce the reported short-row tables in two tall, independent panes.
        resize(1440, 900); FocusButton.IsChecked = true;
        OpenTable(document.Definition.Tables.First(t => t.Name == "Overboost Protection Fuel Cut"));
        SetViewMode(4);
        var peer = OpenMap(document.Definition.Tables.First(t => t.Name == "Overload Protection Fuel Cut"));
        await Settle(); await Settle();
        foreach (var editor in new[] { this, peer })
        {
            var scroll = Descendants(editor.MapGrid).OfType<ScrollViewer>().First();
            scroll.ScrollToLeftEnd(); scroll.ScrollToTop();
        }
        await Settle(); await Settle(); CheckAxisRowAlignment(); peer.CheckAxisRowAlignment();
        string preview = Path.Combine(folder, "ShortTableAxisLabels.png");
        if (capture is null) CaptureHost(preview); else capture(preview);
        foreach (double scale in new[] { 1d, 1.25, 1.5 })
        {
            TablePanel.LayoutTransform = new ScaleTransform(scale, scale); await Settle(); await Settle();
            CheckAxisRowAlignment();
            var otherBounds = DockedBounds(peer);
            DragPaneEdge(this, MapResizeEdge.Right, -60, 0); await Settle(); await Settle();
            CheckAxisRowAlignment(); peer.CheckAxisRowAlignment();
            if (!SameBounds(otherBounds, DockedBounds(peer))) throw new InvalidOperationException("Axis resize moved the other map.");
            DragPaneEdge(this, MapResizeEdge.Right, 60, 0); await Settle();
        }
        TablePanel.LayoutTransform = System.Windows.Media.Transform.Identity;
        peer.UndockMap_Click(peer, new()); await Settle(); await Settle(); peer.CheckAxisRowAlignment();
        var floating = mapSlots.Single(s => ReferenceEquals(s.Editor, peer)).Window!;
        floating.Height = 420; floating.Width = 600; floating.UpdateLayout(); await Settle(); await Settle(); peer.CheckAxisRowAlignment();
        floating.Height = 850; floating.UpdateLayout(); await Settle(); await Settle(); peer.CheckAxisRowAlignment();
        if (!original.AsSpan().SequenceEqual(document.Snapshot())) throw new InvalidOperationException("Moving axes changed ROM bytes.");
        return new { passed = true, tableCount = document.Definition.Tables.Count, checkedViews = checkedTables.Count, checkedTables, variations, fittingColumnResize = true,
            shortTablesCentered = true, longTablesCentered = true, frozenLabels = true, resizedAndDetached = true, scaledLayout = true, romBytesUnchanged = true };
    }
}

public partial class MainWindow
{
    internal async Task<object> ExerciseEveryTableAxesOffline(string path, string folder, string? tableName = null, bool panesOnly = false)
    {
        Show(); Navigate(1); EditorPane.OpenRom(path); UpdateLayout();
        try { return await EditorPane.ExerciseEveryTableAxes(folder, (width, height) => { Width = width; Height = height; UpdateLayout(); }, tableName, panesOnly, image => CaptureApplicationPreview(image)); }
        catch { CaptureApplicationPreview(Path.Combine(folder, "AxisFailure.png")); throw; }
    }
}
