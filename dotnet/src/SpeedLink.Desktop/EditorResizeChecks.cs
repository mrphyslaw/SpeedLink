using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private async Task CheckPreviewSplitter(string folder, Action<double, double> resize)
    {
        byte[] bytes = document!.Snapshot();
        if (!DataSplitter.IsVisible || DataSplitter.ResizeDirection != GridResizeDirection.Columns || Grid.GetColumn(DataSplitter) != 1)
            throw new InvalidOperationException("The table's right boundary is not a vertical resize divider.");
        async Task Drag(double amount)
        {
            double width = GraphColumn.ActualWidth;
            DataSplitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            DataSplitter.RaiseEvent(new DragDeltaEventArgs(amount, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            await Settle();
            if (Math.Abs(GraphColumn.ActualWidth - (width - amount)) > 2) throw new InvalidOperationException("The preview divider did not track the drag.");
            DataSplitter.RaiseEvent(new DragCompletedEventArgs(amount, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            await Settle();
            if (Math.Abs(GraphColumn.ActualWidth - (width - amount)) > 2 || resizingData)
                throw new InvalidOperationException("The preview width snapped back after dragging.");
        }
        await Drag(-120); await Drag(55);
        double requested = previewWidth!.Value;
        if (Math.Abs(EditorLayoutPreferences.Load(layoutPath!).PreviewWidth!.Value - requested) > 1) throw new InvalidOperationException("Preview width was not saved.");
        CaptureHost(Path.Combine(folder, "ResizablePreview.png"));
        PopGraph_Click(this, new()); await Settle(); DockGraph_Click(this, new()); await Settle();
        if (Math.Abs(GraphColumn.ActualWidth - requested) > 1) throw new InvalidOperationException("Docking lost the resized preview width.");
        resize(760, 700); await Settle();
        if (GraphColumn.ActualWidth < 159 || TableColumn.ActualWidth < 199) throw new InvalidOperationException("Resizing the window collapsed a pane.");
        resize(1280, 720); await Settle();
        if (Math.Abs(GraphColumn.ActualWidth - requested) > 1 || previewWidth != requested) throw new InvalidOperationException("Window resizing lost the chosen preview width.");
        FitMap_Click(this, new()); await Settle();
        if (previewWidth is not null || EditorLayoutPreferences.Load(layoutPath!).PreviewWidth is not null || GraphPanel.ActualHeight > 210)
            throw new InvalidOperationException("Fit map did not restore the automatic compact preview.");
        if (!bytes.AsSpan().SequenceEqual(document.Snapshot())) throw new InvalidOperationException("Resizing a pane changed ROM bytes.");
    }
}
