using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private const double MinimumRowLabelWidth = 64;
    private const double RowLabelPadding = 10;
    private double naturalRowLabelWidth;
    private double? rowLabelWidth;
    private double? previousRowLabelWidth;

    private bool HasNamedRows => table is { Count: > 1, Bits.Length: > 0 };
    private double DesiredRowHeaderWidth => HasNamedRows ? rowLabelWidth ?? naturalRowLabelWidth : MapGrid.RowHeaderWidth;
    private double NamedRowValueWidth => Math.Max(MinimumCellWidth(), 112 * cellZoom);

    private FormattedText FormatRowLabel(string label) => new(label, CultureInfo.InvariantCulture, MapGrid.FlowDirection,
        new(MapGrid.FontFamily, MapGrid.FontStyle, MapGrid.FontWeight, MapGrid.FontStretch),
        MapGrid.FontSize, UiTheme.Text, VisualTreeHelper.GetDpi(MapGrid).PixelsPerDip);

    private void RefreshRowLabels()
    {
        RowLabelDivider.Visibility = HasNamedRows ? Visibility.Visible : Visibility.Collapsed;
        MapGrid.RowHeaderTemplate = HasNamedRows ? (DataTemplate)FindResource("NamedMapRowHeader") : null;
        if (HasNamedRows)
            naturalRowLabelWidth = Math.Max(MinimumRowLabelWidth, Math.Ceiling(table!.BitNames
                .Max(label => FormatRowLabel(label).WidthIncludingTrailingWhitespace)) + RowLabelPadding);
    }

    private double MaximumRowLabelWidth()
    {
        // Reserve the full Value column so dragging the divider changes only
        // the width of the labels.
        double reserved = NamedRowValueWidth + SystemParameters.VerticalScrollBarWidth + 4;
        return Math.Max(MinimumRowLabelWidth, TableViewport.ActualWidth - reserved);
    }

    private void SizeRowLabels()
    {
        if (!HasNamedRows) return;
        MapGrid.RowHeaderWidth = TableViewport.ActualWidth > 0
            ? Math.Min(DesiredRowHeaderWidth, MaximumRowLabelWidth()) : DesiredRowHeaderWidth;
    }

    private void RowLabelDivider_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        rowLabelWidth = null;
        UpdateCompactMap();
        UpdateTableSizing();
        QueueMapSave();
        e.Handled = true;
    }

    private void RowLabelDivider_Started(object sender, DragStartedEventArgs e) => previousRowLabelWidth = rowLabelWidth;

    private void RowLabelDivider_Delta(object sender, DragDeltaEventArgs e)
    {
        double renderedWidth = RowLabelDivider.TranslatePoint(new(RowLabelDivider.ActualWidth / 2, 0), MapGrid).X;
        rowLabelWidth = Math.Clamp(renderedWidth + e.HorizontalChange, MinimumRowLabelWidth, MaximumRowLabelWidth());
        UpdateCompactMap();
        UpdateTableSizing();
    }

    private void RowLabelDivider_Completed(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled)
        {
            rowLabelWidth = previousRowLabelWidth;
            UpdateCompactMap();
            UpdateTableSizing();
        }
        else QueueMapSave();
    }
}
