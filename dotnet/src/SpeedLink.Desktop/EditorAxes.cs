using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    public static readonly DependencyProperty IsAxisSelectedProperty = DependencyProperty.RegisterAttached(
        "IsAxisSelected", typeof(bool), typeof(EditorWorkspace), new PropertyMetadata(false));
    public static bool GetIsAxisSelected(DependencyObject target) => (bool)target.GetValue(IsAxisSelectedProperty);
    public static void SetIsAxisSelected(DependencyObject target, bool value) => target.SetValue(IsAxisSelectedProperty, value);
    private HashSet<int> selectedAxisRows = [];
    private ScrollContentPresenter? axisRowsViewport;
    private DataGridRowsPresenter? axisRowsPanel;
    private DataGridCellsPanel? axisHeadersPanel;
    private bool axisGeometryQueued;

    private void RefreshAxes()
    {
        bool numeric = table is { IsChoice: false };
        bool x = numeric && !CurveList && table!.Columns > 1;
        bool y = numeric && !CurveList && table!.Rows > 1;
        XAxisText.Text = x ? table!.X.Name : "";
        YAxisText.Text = y ? table!.Y.Name : "";
        XAxisText.ToolTip = XAxisText.Text; YAxisText.ToolTip = YAxisText.Text;
        XAxisBand.Visibility = x ? Visibility.Visible : Visibility.Collapsed;
        YAxisBand.Visibility = y ? Visibility.Visible : Visibility.Collapsed;
        AxisTitleRow.Height = new(x ? 21 : 0); AxisLabelColumn.Width = new(y ? 20 : 0);
        AxisFrame.Visibility = x || y ? Visibility.Visible : Visibility.Collapsed;
        ValueUnitsText.Text = table is null ? "" : "Values: " + table.Units;
        ValueUnitsText.ToolTip = ValueUnitsText.Text;
        AutomationProperties.SetName(MapGrid, table is null ? "ROM map" :
            table.Name + (x ? "; columns: " + table.X.Name : "") + (y ? "; rows: " + table.Y.Name : "") + "; values: " + table.Units);
        if (y)
        {
            // The label uses the former row-header padding. Size breakpoints from
            // their formatted text so a long value remains readable at any DPI.
            var typeface = new Typeface(MapGrid.FontFamily, MapGrid.FontStyle, MapGrid.FontWeight, MapGrid.FontStretch);
            double width = table!.Y.Values.Max(value => new FormattedText(value.ToString("G5", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, MapGrid.FontSize, UiTheme.Text,
                VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace);
            MapGrid.RowHeaderWidth = Math.Max(44, Math.Ceiling(width) + 6);
        }
        UpdateSelectedAxes(); UpdateAxisGeometry(); QueueAxisGeometry();
    }

    private void UpdateSelectedAxes()
    {
        int[] indices = SelectedIndices();
        selectedAxisRows = indices.Select(i => CurveList ? i : i / table!.Columns).ToHashSet();
        var columns = indices.Select(i => CurveList ? 1 : i % table!.Columns).ToHashSet();
        foreach (var column in MapGrid.Columns) SetIsAxisSelected(column, columns.Contains(column.DisplayIndex));
        foreach (var row in Descendants(MapGrid).OfType<DataGridRow>())
            SetIsAxisSelected(row, row.Item is EditorRow item && selectedAxisRows.Contains(item.Index));
    }

    private void Grid_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateAxisGeometry();
    private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (resizingData) UpdateTableSizing();
        UpdateAxisGeometry();
    }
    private void QueueAxisGeometry()
    {
        if (axisGeometryQueued) return;
        axisGeometryQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            axisGeometryQueued = false;
            UpdateAxisGeometry();
        }));
    }
    private void Grid_LayoutUpdated(object? sender, EventArgs e) => UpdateAxisTitleGeometry();
    private void UpdateAxisGeometry()
    {
        if (XAxisBand is null || MapGrid is null) return;
        var scroll = Descendants(MapGrid).OfType<ScrollViewer>().FirstOrDefault();
        var bars = scroll is null ? [] : Descendants(scroll).OfType<ScrollBar>().ToArray();
        double verticalBar = bars.FirstOrDefault(b => b.Orientation == Orientation.Vertical && b.IsVisible)?.ActualWidth ?? 0;
        double horizontalBar = bars.FirstOrDefault(b => b.Orientation == Orientation.Horizontal && b.IsVisible)?.ActualHeight ?? 0;
        AxisHeaderSpacer.Width = new(MapGrid.RowHeaderWidth);
        AxisScrollbarSpacer.Width = new(verticalBar);
        YAxisBand.Margin = new(0, 0, 0, horizontalBar);
        AxisFrame.Margin = new(0, 0, verticalBar, horizontalBar);
        axisRowsViewport = scroll?.Template.FindName("PART_ScrollContentPresenter", scroll) as ScrollContentPresenter;
        axisRowsPanel = axisRowsViewport is null ? null : Descendants(axisRowsViewport).OfType<DataGridRowsPresenter>().FirstOrDefault();
        var headers = scroll is null ? null : Descendants(scroll).OfType<DataGridColumnHeadersPresenter>().FirstOrDefault();
        axisHeadersPanel = headers is null ? null : Descendants(headers).OfType<DataGridCellsPanel>().FirstOrDefault();
        UpdateAxisTitleGeometry();
    }
    private void UpdateAxisTitleGeometry()
    {
        if (MapGrid is null || !MapGrid.IsVisible || !MapGrid.IsArrangeValid) return;
        // Measure the arranged headers/rows, including pixel rounding and
        // virtualization. Layout can change without changing the scroll extent.
        // Only visit the cached panels' direct children, never all grid cells.
        if (XAxisBand.Visibility == Visibility.Visible && axisHeadersPanel is { IsArrangeValid: true })
        {
            var columns = VisibleAxisSpan(axisHeadersPanel, true, MapGrid.RowHeaderWidth,
                MapGrid.ActualWidth - AxisScrollbarSpacer.Width.Value);
            if (columns is { } span)
            {
                XAxisTitleArea.Margin = new(Math.Max(0, span.Start - MapGrid.RowHeaderWidth), 0, 0, 0);
                XAxisTitleArea.Width = span.End - span.Start;
            }
        }
        if (axisRowsViewport is not { IsArrangeValid: true } viewport || YAxisBand.Visibility != Visibility.Visible) return;

        // The scroll content presenter excludes headers and scrollbars. Its
        // height is in pixels even when the DataGrid scrolls in logical rows.
        // Keep the full outer band/frame joined to the grid, but exclude empty
        // space below short tables and partially visible final rows from titles.
        Rect rows = viewport.TransformToAncestor(MapGrid).TransformBounds(new Rect(viewport.RenderSize));
        double top = Math.Max(0, rows.Top);
        double bottom = Math.Min(rows.Bottom, MapGrid.ActualHeight - YAxisBand.Margin.Bottom);
        if (axisRowsPanel is { IsArrangeValid: true } && VisibleAxisSpan(axisRowsPanel, false, top, bottom) is { } visible)
        {
            YAxisTitleArea.Margin = new(0, visible.Start, 0, 0);
            YAxisTitleArea.Height = visible.End - visible.Start;
        }
    }
    private (double Start, double End)? VisibleAxisSpan(Panel panel, bool horizontal, double minimum, double maximum)
    {
        double first = maximum, last = minimum;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(panel); i++)
        {
            if (VisualTreeHelper.GetChild(panel, i) is not FrameworkElement { IsVisible: true } child ||
                horizontal && child is not DataGridColumnHeader { Column: not null } ||
                !horizontal && child is not DataGridRow) continue;
            Rect bounds = child.TransformToAncestor(MapGrid).TransformBounds(new Rect(child.RenderSize));
            double start = Math.Max(minimum, horizontal ? bounds.Left : bounds.Top);
            double end = Math.Min(maximum, horizontal ? bounds.Right : bounds.Bottom);
            if (end <= start) continue;
            first = Math.Min(first, start); last = Math.Max(last, end);
        }
        return last > first ? (first, last) : null;
    }
}
