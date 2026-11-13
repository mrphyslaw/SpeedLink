using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private const double DockSnapDistance = 24;
    private sealed record DockPlacement(MapResizeEdge Edge, Rect Preview, MapDockBounds Bounds);
    private readonly Border dockTarget = new()
    {
        IsHitTestVisible = false, Visibility = Visibility.Collapsed, BorderThickness = new(2),
        CornerRadius = new(3), Opacity = .55, HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top
    };

    private void EnableEdgeDocking(MapSlot slot, Window window)
    {
        const int moving = 0x0216, exitSizeMove = 0x0232, cancelMode = 0x001F, escape = 0x1B;
        DockPlacement? placement = null;
        nint TrackMove(nint handle, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == moving)
            {
                placement = GetCursorPos(out var cursor) && mapDockScroll.IsVisible
                    ? PlanDockPlacement(mapDockScroll.PointFromScreen(new(cursor.X, cursor.Y)), slot,
                        new(slot.Frame.ActualWidth, slot.Frame.ActualHeight), placement?.Edge)
                    : null;
                ShowDockTarget(placement);
            }
            else if (message == cancelMode)
            {
                placement = null;
                ShowDockTarget(null);
            }
            else if (message == exitSizeMove)
            {
                var drop = placement;
                placement = null;
                ShowDockTarget(null);
                if (drop is not null && GetAsyncKeyState(escape) >= 0)
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        if (slot.Window == window && mapDockScroll.IsVisible) DockDetachedMap(slot, drop);
                    }));
            }
            return 0;
        }

        window.SourceInitialized += (_, _) => ((HwndSource)PresentationSource.FromVisual(window)!).AddHook(TrackMove);
        window.Closed += (_, _) => ShowDockTarget(null);
    }

    private DockPlacement? PlanDockPlacement(Point point, MapSlot slot, Size pane, MapResizeEdge? previous = null)
    {
        const double target = DockSnapDistance;
        const double cornerHysteresis = 8;
        double width = mapDockScroll.ViewportWidth, height = mapDockScroll.ViewportHeight;
        if (width <= 0 || height <= 0 || point.X < -target || point.X > width + target ||
            point.Y < -target || point.Y > height + target) return null;

        var edges = new (MapResizeEdge Edge, double Distance)[]
        {
            (MapResizeEdge.Left, Math.Abs(point.X)), (MapResizeEdge.Top, Math.Abs(point.Y)),
            (MapResizeEdge.Right, Math.Abs(width - point.X)), (MapResizeEdge.Bottom, Math.Abs(height - point.Y))
        };
        var nearest = edges.MinBy(candidate => candidate.Distance);
        if (nearest.Distance > target) return null;
        var side = nearest.Edge;
        if (previous is { } prior)
        {
            double distance = edges.Single(candidate => candidate.Edge == prior).Distance;
            if (distance <= target && distance <= nearest.Distance + cornerHysteresis) side = prior;
        }

        var minimum = slot.Editor.MinimumMapPaneSize();
        double paneWidth = Math.Min(width, Math.Max(minimum.Width, pane.Width));
        double paneHeight = Math.Min(height, Math.Max(minimum.Height, pane.Height));
        double left = side switch
        {
            MapResizeEdge.Left => 0,
            MapResizeEdge.Right => width - paneWidth,
            _ => Math.Clamp(point.X - paneWidth / 2, 0, width - paneWidth)
        };
        double top = side switch
        {
            MapResizeEdge.Top => 0,
            MapResizeEdge.Bottom => height - paneHeight,
            _ => Math.Clamp(point.Y - paneHeight / 2, 0, height - paneHeight)
        };
        return new(side, new(left, top, paneWidth, paneHeight),
            new(left + mapDockScroll.HorizontalOffset, top + mapDockScroll.VerticalOffset, paneWidth, paneHeight));
    }

    private void ShowDockTarget(DockPlacement? placement)
    {
        if (placement is null)
        {
            dockTarget.Visibility = Visibility.Collapsed;
            return;
        }
        var area = placement.Preview;
        dockTarget.Width = area.Width;
        dockTarget.Height = area.Height;
        dockTarget.Margin = new(area.Left, area.Top, 0, 0);
        dockTarget.Visibility = Visibility.Visible;
    }

    private void DockDetachedMap(MapSlot slot, DockPlacement? placement = null)
    {
        if (slot.Window is not Window window || !slot.Editor.FinishEdit()) return;
        if (placement is not null)
        {
            if (expandedMap is not null) { expandedMap = null; ArrangeMaps(); }
            FreezeMapPositions();
            slot.DockBounds = placement.Bounds;
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
