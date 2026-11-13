using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private readonly Border dockTarget = new()
    {
        IsHitTestVisible = false, Visibility = Visibility.Collapsed, BorderThickness = new(2),
        CornerRadius = new(3), Opacity = .75
    };

    private void EnableEdgeDocking(MapSlot slot, Window window)
    {
        const int moving = 0x0216, exitSizeMove = 0x0232, cancelMode = 0x001F, escape = 0x1B;
        MapResizeEdge? CursorEdge() => GetCursorPos(out var cursor) && mapDockScroll.IsVisible
            ? DockEdgeAt(mapDockScroll.PointFromScreen(new(cursor.X, cursor.Y)))
            : null;
        MapResizeEdge? edge = null;
        nint TrackMove(nint handle, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == moving)
            {
                edge = CursorEdge();
                ShowDockTarget(edge);
            }
            else if (message == cancelMode)
            {
                edge = null;
                ShowDockTarget(null);
            }
            else if (message == exitSizeMove)
            {
                var drop = edge is not null ? CursorEdge() : null;
                edge = null;
                ShowDockTarget(null);
                if (drop is { } side && GetAsyncKeyState(escape) >= 0)
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        if (slot.Window == window && mapDockScroll.IsVisible) DockDetachedMap(slot, side);
                    }));
            }
            return 0;
        }

        window.SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(window)!;
            source.AddHook(TrackMove);
        };
        window.Closed += (_, _) => ShowDockTarget(null);
    }

    private MapResizeEdge? DockEdgeAt(Point point)
    {
        const double targetWidth = 32;
        double width = mapDockScroll.ViewportWidth, height = mapDockScroll.ViewportHeight;
        if (width <= 0 || height <= 0 || point.X < -targetWidth || point.X > width + targetWidth ||
            point.Y < -targetWidth || point.Y > height + targetWidth) return null;

        double left = Math.Abs(point.X), right = Math.Abs(width - point.X);
        double top = Math.Abs(point.Y), bottom = Math.Abs(height - point.Y);
        double closest = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (closest > targetWidth) return null;
        if (closest == left) return MapResizeEdge.Left;
        if (closest == top) return MapResizeEdge.Top;
        return closest == right ? MapResizeEdge.Right : MapResizeEdge.Bottom;
    }

    private void ShowDockTarget(MapResizeEdge? edge)
    {
        dockTarget.Visibility = edge is null ? Visibility.Collapsed : Visibility.Visible;
        if (edge is null) return;
        bool horizontal = edge is MapResizeEdge.Top or MapResizeEdge.Bottom;
        dockTarget.Width = horizontal ? double.NaN : 28;
        dockTarget.Height = horizontal ? 28 : double.NaN;
        dockTarget.HorizontalAlignment = horizontal ? HorizontalAlignment.Stretch : edge == MapResizeEdge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        dockTarget.VerticalAlignment = horizontal ? edge == MapResizeEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom : VerticalAlignment.Stretch;
    }

    private void DockDetachedMap(MapSlot slot, MapResizeEdge? edge = null)
    {
        if (slot.Window is not Window window || !slot.Editor.FinishEdit()) return;
        if (edge is { } side)
        {
            if (expandedMap is not null) { expandedMap = null; ArrangeMaps(); }
            FreezeMapPositions();
            double width = Math.Max(260, mapDockScroll.ViewportWidth);
            double height = Math.Max(118, mapDockScroll.ViewportHeight - 2);
            var minimum = slot.Editor.MinimumMapPaneSize();
            double paneWidth = Math.Min(width, Math.Max(minimum.Width, slot.Editor.ScalarMap ? 300 : Math.Min(window.ActualWidth, width)));
            double paneHeight = Math.Min(height, Math.Max(minimum.Height, slot.Editor.ScalarMap ? 120 : Math.Min(window.ActualHeight, height)));
            var position = mapDockScroll.PointFromScreen(window.PointToScreen(new()));
            double left = mapDockScroll.HorizontalOffset + (side switch
            {
                MapResizeEdge.Left => 0,
                MapResizeEdge.Right => width - paneWidth,
                _ => Math.Clamp(position.X, 0, width - paneWidth)
            });
            double top = mapDockScroll.VerticalOffset + (side switch
            {
                MapResizeEdge.Top => 0,
                MapResizeEdge.Bottom => height - paneHeight,
                _ => Math.Clamp(position.Y, 0, height - paneHeight)
            });
            slot.DockBounds = new(left, top, paneWidth, paneHeight);
            slot.PreserveBoundsOnOpen = true;
        }
        slot.DockOnClose = true;
        window.Close();
        if (slot.Window == window) slot.DockOnClose = false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPosition { internal int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPosition position);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
