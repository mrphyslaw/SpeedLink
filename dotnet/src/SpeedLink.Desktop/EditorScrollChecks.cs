using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private async Task CheckScrollingFrame(string folder)
    {
        Directory.CreateDirectory(folder);
        var bytes = document!.Snapshot();
        var scroll = Descendants(MapGrid).OfType<ScrollViewer>().First();
        await Settle(); UpdateAxisGeometry(); UpdateLayout();
        var bars = Descendants(scroll).OfType<ScrollBar>().Where(b => b.IsVisible).ToArray();
        var horizontal = bars.Single(b => b.Orientation == Orientation.Horizontal);
        var vertical = bars.Single(b => b.Orientation == Orientation.Vertical);
        Rect Bounds(FrameworkElement e) => e.TransformToAncestor(TablePanel).TransformBounds(new Rect(e.RenderSize));
        if (Math.Abs(Bounds(XAxisChrome).Right - Bounds(vertical).Left) > 1 ||
            Math.Abs(Bounds(YAxisBand).Bottom - Bounds(horizontal).Top) > 1)
            throw new InvalidOperationException("Axis bands extend into the scrollbar corners.");
        Track Part(ScrollBar bar)
        {
            var part = bar.Template.FindName("PART_Track", bar) as Track ?? throw new InvalidOperationException("Scrollbar track missing.");
            if (bar.Background is not SolidColorBrush background || part.Thumb.Background is not SolidColorBrush thumb ||
                Brightness(background.Color) > .4 || Brightness(thumb.Color) - Brightness(background.Color) < .2)
                throw new InvalidOperationException("Scrollbar track/thumb contrast does not match the dark interface.");
            return part;
        }
        var h = Part(horizontal); var v = Part(vertical);
        void Invoke(RepeatButton button, ScrollBar bar)
        {
            if (button.Command is not RoutedCommand command || !command.CanExecute(button.CommandParameter, bar))
                throw new InvalidOperationException("Scrollbar command is not available.");
            command.Execute(button.CommandParameter, bar);
        }
        async Task Origin() { scroll.ScrollToLeftEnd(); scroll.ScrollToTop(); await Settle(); }
        await Origin(); Capture(Path.Combine(folder, "TableScrollbars.png"));
        Invoke((RepeatButton)horizontal.Template.FindName("LineIncrease", horizontal), horizontal);
        Invoke((RepeatButton)vertical.Template.FindName("LineIncrease", vertical), vertical);
        await Settle();
        if (scroll.HorizontalOffset <= 0 || scroll.VerticalOffset <= 0) throw new InvalidOperationException("Scrollbar arrows do not move the viewport.");
        await Origin(); Invoke(h.IncreaseRepeatButton, horizontal); Invoke(v.IncreaseRepeatButton, vertical); await Settle();
        if (scroll.HorizontalOffset <= 0 || scroll.VerticalOffset <= 0) throw new InvalidOperationException("Scrollbar page commands do not move the viewport.");
        await Origin();
        void Drag(Thumb thumb, double x, double y)
        {
            thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            thumb.RaiseEvent(new DragDeltaEventArgs(x, y) { RoutedEvent = Thumb.DragDeltaEvent });
            thumb.RaiseEvent(new DragCompletedEventArgs(x, y, false) { RoutedEvent = Thumb.DragCompletedEvent });
        }
        Drag(h.Thumb, 35, 0); Drag(v.Thumb, 0, 35); await Settle();
        double right = scroll.HorizontalOffset, down = scroll.VerticalOffset;
        if (right <= 0 || down <= 0) throw new InvalidOperationException("Dragging a scrollbar thumb does not follow the mouse direction.");
        Drag(h.Thumb, -35, 0); Drag(v.Thumb, 0, -35); await Settle();
        if (scroll.HorizontalOffset >= right || scroll.VerticalOffset >= down) throw new InvalidOperationException("Reverse thumb dragging does not move back toward the origin.");
        SelectCell(table!.Count - 1); scroll.ScrollToRightEnd(); scroll.ScrollToBottom(); await Settle();
        var last = MapGrid.Columns[^1].GetCellContent(MapGrid.Items[^1]) ?? throw new InvalidOperationException("Last map cell is not visible after scrolling to the edge.");
        if (Bounds(last).Right > Bounds(vertical).Left + 1 || Bounds(last).Bottom > Bounds(horizontal).Top + 1)
            throw new InvalidOperationException("The last map value is clipped by a scrollbar at the end of its range.");
        Capture(Path.Combine(folder, "TableScrollbarsEnd.png"));
        if (!bytes.AsSpan().SequenceEqual(document.Snapshot())) throw new InvalidOperationException("Scrolling changed ROM bytes.");
    }
    private async Task Settle()
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
    }
    private static double Brightness(Color c) => (.2126 * c.R + .7152 * c.G + .0722 * c.B) / 255;

    private async Task CheckStockScrolling(string folder)
    {
        var previous = table; bool fitted = fitTable; int mode = ViewMode;
        var stock = document!.Definition.Tables.Single(t => t.Name == "Closed Loop Max Ignition Timing");
        OpenTable(stock); fitTable = false; SetViewMode(4); UpdateLayout();
        var transform = TablePanel.LayoutTransform;
        try
        {
            foreach (double scale in new[] { 1d, 1.25 })
            {
                TablePanel.LayoutTransform = new ScaleTransform(scale, scale); await Settle();
                foreach (var column in MapGrid.Columns) column.Width = new(MapGrid.ActualWidth / stock.Columns + 40);
                MapGrid.RowHeight = Math.Max(34, MapGrid.ActualHeight / stock.Rows + 5); await Settle();
                await CheckScrollingFrame(scale == 1 ? folder : Path.Combine(folder, "ScaledScrollbars"));
            }
        }
        finally { TablePanel.LayoutTransform = transform; }
        if (previous is not null) OpenTable(previous);
        fitTable = fitted; SetViewMode(mode); UpdateLayout();
    }
}
