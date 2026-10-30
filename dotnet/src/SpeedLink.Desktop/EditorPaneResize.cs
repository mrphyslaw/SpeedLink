using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private void AddPaneResizeGrips(Border frame, Grid surface)
    {
        foreach (var edge in new[] { MapResizeEdge.Left, MapResizeEdge.Top, MapResizeEdge.Right, MapResizeEdge.Bottom,
            MapResizeEdge.Left | MapResizeEdge.Top, MapResizeEdge.Right | MapResizeEdge.Top,
            MapResizeEdge.Left | MapResizeEdge.Bottom, MapResizeEdge.Right | MapResizeEdge.Bottom })
        {
            bool horizontal = edge.HasFlag(MapResizeEdge.Left) || edge.HasFlag(MapResizeEdge.Right);
            bool vertical = edge.HasFlag(MapResizeEdge.Top) || edge.HasFlag(MapResizeEdge.Bottom);
            bool corner = horizontal && vertical;
            var visual = new FrameworkElementFactory(typeof(Border)); visual.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            if (edge == (MapResizeEdge.Right | MapResizeEdge.Bottom))
            {
                var mark = new FrameworkElementFactory(typeof(TextBlock)); mark.SetValue(TextBlock.TextProperty, "◢");
                mark.SetValue(TextBlock.FontSizeProperty, 9d); mark.SetValue(TextBlock.ForegroundProperty, UiTheme.Muted); visual.AppendChild(mark);
            }
            var grip = new Thumb { Tag = edge, Focusable = false, Margin = new(-3), Template = new(typeof(Thumb)) { VisualTree = visual },
                Width = horizontal ? corner ? 11 : 6 : double.NaN, Height = vertical ? corner ? 11 : 6 : double.NaN,
                HorizontalAlignment = edge.HasFlag(MapResizeEdge.Left) ? HorizontalAlignment.Left : edge.HasFlag(MapResizeEdge.Right) ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment = edge.HasFlag(MapResizeEdge.Top) ? VerticalAlignment.Top : edge.HasFlag(MapResizeEdge.Bottom) ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                Cursor = corner ? edge is (MapResizeEdge.Left | MapResizeEdge.Top) or (MapResizeEdge.Right | MapResizeEdge.Bottom) ? Cursors.SizeNWSE : Cursors.SizeNESW : horizontal ? Cursors.SizeWE : Cursors.SizeNS,
                ToolTip = "Resize this map only; other maps stay in place" };
            System.Windows.Automation.AutomationProperties.SetName(grip, "Resize map " + edge);
            MapDockBounds? original = null; double dx = 0, dy = 0;
            void FinishResize(bool canceled)
            {
                if (original is null) return;
                var root = MapRoot; var slot = root.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Frame, frame));
                if (canceled && slot is not null) { slot.DockBounds = original; PlaceMapFrame(slot, original); }
                original = null; root.cancelPaneGesture = null; root.resizingDock = false; root.UpdateIndependentExtent(); root.QueueMapSave();
            }
            grip.DragStarted += (_, _) =>
            {
                var root = MapRoot; var slot = root.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Frame, frame));
                if (slot is null || slot.Window is not null || slot.Editor.table is null || root.expandedMap is not null) return;
                root.FreezeMapPositions(); original = slot.DockBounds; dx = dy = 0; root.resizingDock = true; slot.Editor.ActivateMap();
                root.cancelPaneGesture = () => { FinishResize(true); grip.CancelDrag(); };
            };
            grip.DragDelta += (_, e) =>
            {
                if (original is null) return;
                var root = MapRoot; var slot = root.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Frame, frame));
                if (slot is null) { original = null; root.cancelPaneGesture = null; root.resizingDock = false; return; }
                dx += e.HorizontalChange; dy += e.VerticalChange;
                slot.DockBounds = original.Resize(edge, dx, dy, slot.Editor.ScalarMap ? 220 : 260, slot.Editor.ScalarMap ? 100 : 260);
                PlaceMapFrame(slot, slot.DockBounds);
            };
            grip.DragCompleted += (_, e) => FinishResize(e.Canceled);
            surface.Children.Add(grip);
        }
    }
    private static IEnumerable<Thumb> PaneGrips(MapSlot slot) => ((Grid)slot.Frame.Child).Children.OfType<Thumb>();
    private void RefreshPaneResizeGrips(MapSlot slot)
    {
        foreach (var grip in PaneGrips(slot)) grip.Visibility = slot.Window is null && slot.Editor.table is not null && expandedMap is null ? Visibility.Visible : Visibility.Collapsed;
    }
    private void FreezeMapPositions()
    {
        if (independentMapLayout) return;
        UpdateLayout();
        foreach (var slot in mapSlots.Where(s => s.Window is null && s.Editor.table is not null))
        {
            var point = slot.Frame.TranslatePoint(new(), mapDock);
            slot.DockBounds = new(point.X, point.Y, slot.Frame.ActualWidth, slot.Frame.ActualHeight);
        }
        independentMapLayout = true; ArrangeMaps(); UpdateLayout();
    }
    private void MoveMapPane(MapSlot slot, MapDockBounds original, double dx, double dy)
    {
        slot.DockBounds = original.Move(dx, dy); PlaceMapFrame(slot, slot.DockBounds); slot.Editor.ActivateMap();
    }
    private static void PlaceMapFrame(MapSlot slot, MapDockBounds bounds)
    {
        Grid.SetRow(slot.Frame, 0); Grid.SetColumn(slot.Frame, 0);
        slot.Frame.HorizontalAlignment = HorizontalAlignment.Left; slot.Frame.VerticalAlignment = VerticalAlignment.Top;
        slot.Frame.Width = bounds.Width; slot.Frame.Height = bounds.Height; slot.Frame.Margin = new(bounds.Left, bounds.Top, 0, 0);
    }
    private void ArrangeIndependentMaps(MapSlot[] visible, bool empty)
    {
        mapDockScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        foreach (var slot in visible)
        {
            if (expandedMap is not null || empty)
                PlaceMapFrame(slot, new(0, 0, Math.Max(260, mapDockScroll.ViewportWidth), Math.Max(260, mapDockScroll.ViewportHeight)));
            else
            {
                slot.DockBounds ??= FindMapSpace(slot);
                slot.DockBounds = slot.DockBounds with { Width = Math.Max(slot.Editor.ScalarMap ? 220 : 260, slot.DockBounds.Width), Height = Math.Max(slot.Editor.ScalarMap ? 100 : 260, slot.DockBounds.Height) };
                PlaceMapFrame(slot, slot.DockBounds);
            }
            if (!mapDock.Children.Contains(slot.Frame)) mapDock.Children.Add(slot.Frame);
            bool expanded = ReferenceEquals(slot, expandedMap);
            slot.Editor.ExpandMapButton.Content = slot.Editor.ScalarMap ? expanded ? "⤡" : "⤢" : expanded ? "Restore" : "Expand";
            System.Windows.Automation.AutomationProperties.SetName(slot.Editor.ExpandMapButton, expanded ? "Restore map layout" : "Expand map");
        }
        UpdateIndependentExtent();
    }
    private MapDockBounds FindMapSpace(MapSlot slot)
    {
        var occupied = mapSlots.Where(s => !ReferenceEquals(s, slot) && s.Window is null && s.Editor.table is not null && s.DockBounds is not null).Select(s => s.DockBounds!).ToArray();
        double available = Math.Max(260, mapDockScroll.ViewportWidth);
        double width = slot.Editor.ScalarMap ? Math.Min(300, available) : Math.Max(260, (available - 7) / Math.Max(1, dockColumns));
        double height = slot.Editor.ScalarMap ? 120 : Math.Max(260, Math.Min(500, mapDockScroll.ViewportHeight));
        foreach (double y in occupied.Select(b => b.Top + b.Height + 7).Prepend(0).Order())
            foreach (double x in occupied.Select(b => b.Left + b.Width + 7).Prepend(0).Order())
                if (x <= 32_000 && y <= 32_000 && x + width <= available + 1 && !occupied.Any(b => x < b.Left + b.Width && x + width > b.Left && y < b.Top + b.Height && y + height > b.Top))
                    return new(x, y, width, height);
        return new(0, Math.Min(32_000, occupied.Select(b => b.Top + b.Height + 7).DefaultIfEmpty(0).Max()), width, height);
    }
    private void UpdateIndependentExtent()
    {
        var visible = mapSlots.Where(s => s.Window is null && s.Frame.Visibility == Visibility.Visible).ToArray();
        double right = visible.Select(s => s.Frame.Margin.Left + s.Frame.Width).DefaultIfEmpty(0).Max();
        mapDock.Width = right <= mapDockScroll.ViewportWidth + 1 ? mapDockScroll.ViewportWidth : right;
        mapDock.Height = Math.Max(Math.Max(0, mapDockScroll.ViewportHeight - 2), visible.Select(s => s.Frame.Margin.Top + s.Frame.Height).DefaultIfEmpty(0).Max());
    }
}
