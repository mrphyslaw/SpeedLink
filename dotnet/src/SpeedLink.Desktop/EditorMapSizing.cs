using System.Windows;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private const double CurveGraphSpace = 207;
    private bool automaticCompactRows;

    private void FitInitialMapDensity()
    {
        if (automaticCompactRows)
        {
            automaticCompactRows = compactRows = false;
            ApplyGridDensity();
        }
        if (compactRows || !autoFitCells || table is not { Count: > 1, IsChoice: false }) return;
        var root = MapRoot;
        var normal = MinimumMapPaneSize();
        if (CurveList && normal.Height <= root.mapDockScroll.ViewportHeight - 2 && normal.Width + CurveGraphSpace <= root.mapDockScroll.ViewportWidth)
            return;
        if (!CurveList && normal.Width <= root.mapDockScroll.ViewportWidth && normal.Height <= root.mapDockScroll.ViewportHeight - 2)
            return;
        if (CurveList && table.Count * ReadableTableFit.MinimumRowHeight(Math.Max(11, 12 * cellZoom), cellZoom, true) >
            root.mapDockScroll.ViewportHeight)
            return;
        automaticCompactRows = compactRows = true;
        ApplyGridDensity();
    }

    private double MinimumMapGridWidth()
    {
        double minimum = MinimumCellWidth();
        if (CurveList)
            return MapGrid.RowHeaderWidth + 2 * (autoFitCells ? minimum : CurveColumnWidth) + 4;
        if (!autoFitCells)
            minimum = Math.Max(minimum, (compactRows ? 48 : 70) * cellZoom);
        return MapGrid.RowHeaderWidth + minimum * MapGrid.Columns.Count + 4;
    }

    private Size MinimumMapPaneSize()
    {
        if (ScalarMap) return new(220, 100);
        if (table is null) return new(260, 260);
        bool compactGrid = CompactMapSize.Calculate(table.Columns, table.Rows, 0, 0, 0) is not null;
        double row = CurveList ? CurveRowHeight : compactGrid ? CompactGridRowHeight : MinimumGridRowHeight;
        double height = MapGrid.ColumnHeaderHeight + (CurveList ? table.Count : table.Rows) * row + AxisTitleRow.Height.Value + 4;
        double width = MinimumMapGridWidth() + AxisLabelColumn.Width.Value;
        if (table.IsChoice)
            width = Math.Max(width, MapGrid.RowHeaderWidth + 200 + 4);
        // Leave enough space for the title, footer, pane frame and the inner
        // margins of secondary workspaces. Include the optional controls row.
        double controls = table.IsChoice || showMapEditTools ? Math.Max(54, EditBar.ActualHeight) : 0;
        return new(Math.Max(260, width + 18 + SystemParameters.VerticalScrollBarWidth),
            Math.Max(118, height + 68 + SystemParameters.HorizontalScrollBarHeight + controls));
    }

    private void FitBrowsedMap(MapSlot slot, bool keepPosition)
    {
        var editor = slot.Editor;
        if (editor.ScalarMap)
        {
            if (keepPosition && !slot.PreserveBoundsOnOpen && slot.DockBounds is { } previousBounds)
                slot.DockBounds = previousBounds with { Width = 300, Height = 120 };
            return;
        }
        double availableWidth = Math.Max(260, mapDockScroll.ViewportWidth);
        double availableHeight = Math.Max(118, mapDockScroll.ViewportHeight - 2);
        var minimum = editor.MinimumMapPaneSize();
        var occupied = mapSlots.Where(s => !ReferenceEquals(s, slot) && s.Window is null && s.Editor.table is not null)
            .Select(s => s.DockBounds).OfType<MapDockBounds>().ToArray();
        var previous = keepPosition ? slot.DockBounds : null;

        MapDockBounds? Place(double width, double height)
        {
            if (width > availableWidth || height > availableHeight) return null;
            bool Fits(double x, double y) => x >= 0 && y >= 0 && x + width <= availableWidth + .5 && y + height <= availableHeight + .5 &&
                !occupied.Any(b => x < b.Left + b.Width && x + width > b.Left && y < b.Top + b.Height && y + height > b.Top);
            if (previous is { } bounds && Fits(bounds.Left, bounds.Top)) return new(bounds.Left, bounds.Top, width, height);
            foreach (double y in occupied.Select(b => b.Top + b.Height + 7).Prepend(0).Order())
                foreach (double x in occupied.Select(b => b.Left + b.Width + 7).Prepend(0).Order())
                    if (Fits(x, y)) return new(x, y, width, height);
            return null;
        }

        if (previous is { } custom && slot.PreserveBoundsOnOpen && custom.Width >= minimum.Width && custom.Height >= minimum.Height)
            return;

        bool shortCurve = editor.table is { Rows: 1, Columns: > 1 and <= 16, IsChoice: false };
        MapDockBounds? fitted = null;
        if (shortCurve)
            fitted = Place(Math.Max(400, minimum.Width), minimum.Height) ?? Place(minimum.Width, minimum.Height);
        else if (editor.CurveList)
        {
            double width = Math.Max(Math.Max(600, minimum.Width + CurveGraphSpace), previous?.Width ?? 0);
            fitted = Place(Math.Min(width, availableWidth), Math.Min(availableHeight, minimum.Height));
        }
        else
        {
            if (previous is { } old && old.Width >= minimum.Width && old.Height >= minimum.Height)
                fitted = Place(old.Width, old.Height);
            fitted ??= Place(minimum.Width, minimum.Height);
            if (fitted is null && !editor.compactRows && editor.autoFitCells && editor.table is { IsChoice: false })
            {
                editor.automaticCompactRows = editor.compactRows = true;
                editor.ApplyGridDensity();
                var compact = editor.MinimumMapPaneSize();
                fitted = Place(compact.Width, compact.Height);
                // Keep compact rows if they expose more of a table that is
                // larger than this workspace even at the readable minimum.
                if (fitted is null && minimum.Width <= availableWidth && minimum.Height <= availableHeight)
                {
                    editor.automaticCompactRows = editor.compactRows = false;
                    editor.ApplyGridDensity();
                }
                minimum = editor.MinimumMapPaneSize();
            }
        }

        // Open in front if other maps occupy the only space that can show the
        // data at a readable size. Their positions and dimensions stay intact.
        fitted ??= new(0, 0, shortCurve ? Math.Min(availableWidth, Math.Max(400, minimum.Width)) : availableWidth,
            Math.Min(availableHeight, minimum.Height));
        slot.DockBounds = fitted;
        slot.PreserveBoundsOnOpen = false;
        ArrangeMaps();
    }
}
