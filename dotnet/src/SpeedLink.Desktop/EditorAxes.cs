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
        UpdateSelectedAxes(); UpdateAxisGeometry();
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
        YAxisText.Margin = new(0, MapGrid.ColumnHeaderHeight, 0, 0);
    }
}
