using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private void CheckNamedRowLabels(bool requireUnwrapped)
    {
        var grid = MapGrid;
        var headers = Descendants(grid).OfType<DataGridRowHeader>().Where(header => header.IsVisible)
            .SelectMany(Descendants).OfType<TextBlock>().Where(label => table!.BitNames.Contains(label.Text)).ToArray();
        if (headers.Length != table!.Rows || !RowLabelDivider.IsVisible)
            throw new InvalidOperationException($"Named rows or the divider are missing in {table.Name}.");

        foreach (var label in headers)
        {
            var text = FormatRowLabel(label.Text);
            if (label.ActualWidth <= 0 || label.ActualHeight + 1 < text.Height ||
                label.TextTrimming != TextTrimming.CharacterEllipsis || !Equals(label.ToolTip, label.Text) ||
                requireUnwrapped && label.ActualWidth + 1 < text.WidthIncludingTrailingWhitespace)
                throw new InvalidOperationException($"Row label is cut off in {table.Name}: {label.Text}. " +
                    $"Label {label.ActualWidth:F0}×{label.ActualHeight:F0}, text {text.WidthIncludingTrailingWhitespace:F0}×{text.Height:F0}.");
        }
    }

    private async Task ExerciseRowLabelResize(EditorWorkspace pinned, TuningTable selected, string folder)
    {
        BrowseMap(selected);
        await Settle();
        var editor = CommandMap;
        var initial = DockedBounds(editor);
        var fixedBounds = DockedBounds(pinned);
        double natural = editor.MapGrid.RowHeaderWidth;
        if (initial.Width < natural + 120)
        {
            DragPaneEdge(editor, MapResizeEdge.Right, natural + 120 - initial.Width, 0);
            await Settle();
            initial = DockedBounds(editor);
        }
        editor.CheckNamedRowLabels(requireUnwrapped: true);
        var grid = editor.MapGrid;
        double fontSize = grid.FontSize, rowHeight = grid.RowHeight, valueWidth = grid.Columns[0].ActualWidth;
        void CheckFixedCellSize()
        {
            var rows = Descendants(grid).OfType<DataGridRow>().Where(row => row.IsVisible).ToArray();
            if (grid.FontSize != fontSize || grid.RowHeight != rowHeight || Math.Abs(grid.Columns[0].ActualWidth - valueWidth) > 1 ||
                rows.Length != selected.Rows || rows.Any(row => Math.Abs(row.ActualHeight - rowHeight) > 1))
                throw new InvalidOperationException($"Resizing the label column changed text or cell size in {selected.Name}.");
        }
        var grip = editor.RowLabelDivider;
        var dividerPoint = grip.TranslatePoint(new(grip.ActualWidth / 2, 12), editor.TablePanel);
        var hit = editor.TablePanel.InputHitTest(dividerPoint) as Visual;
        if (hit is null || !grip.IsAncestorOf(hit) && !ReferenceEquals(hit, grip))
            throw new InvalidOperationException("The visible row label divider does not receive mouse input.");

        void DragLabel(double change, bool canceled = false)
        {
            grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            grip.RaiseEvent(new DragDeltaEventArgs(change, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            grip.RaiseEvent(new DragCompletedEventArgs(change, 0, canceled) { RoutedEvent = Thumb.DragCompletedEvent });
        }

        DragLabel(36);
        await Settle();
        double widened = editor.MapGrid.RowHeaderWidth;
        if (widened < natural + 12 || !SameBounds(initial, DockedBounds(editor)) || !SameBounds(fixedBounds, DockedBounds(pinned)))
            throw new InvalidOperationException("Dragging the divider did not widen the labels independently.");
        DragLabel(-20, canceled: true);
        await Settle();
        if (Math.Abs(editor.MapGrid.RowHeaderWidth - widened) > 1)
            throw new InvalidOperationException("Canceling a divider drag lost the previous label width.");

        DragLabel(MinimumRowLabelWidth - widened);
        await Settle();
        if (Math.Abs(grid.RowHeaderWidth - MinimumRowLabelWidth) > 1)
            throw new InvalidOperationException("The divider could not narrow the label column.");
        editor.CheckNamedRowLabels(requireUnwrapped: false);
        CheckFixedCellSize();
        if (selected.Name.StartsWith("P0032 ", StringComparison.Ordinal) && document!.Definition.Calibration == "L3M6EG000")
            CaptureHost(Path.Combine(folder, "ReportingNarrow.png"));
        DragLabel(widened - MinimumRowLabelWidth);
        await Settle();
        CheckFixedCellSize();

        double smaller = Math.Max(260, natural - 24);
        DragPaneEdge(editor, MapResizeEdge.Right, smaller - initial.Width, 0);
        await Settle();
        if (editor.MapGrid.RowHeaderWidth >= natural || editor.MapGrid.Columns[0].ActualWidth < 44)
            throw new InvalidOperationException("The narrow pane did not make room for both labels and values.");
        editor.CheckNamedRowLabels(requireUnwrapped: false);
        CheckFixedCellSize();
        DragPaneEdge(editor, MapResizeEdge.Right, initial.Width - smaller, 0);
        await Settle();
        if (Math.Abs(editor.MapGrid.RowHeaderWidth - widened) > 1)
            throw new InvalidOperationException("Widening the pane did not restore the requested label width.");
        editor.CheckNamedRowLabels(requireUnwrapped: true);
        CheckFixedCellSize();
        if (selected.Name.StartsWith("P0032 ", StringComparison.Ordinal) && document!.Definition.Calibration == "L3M6EG000")
            CaptureHost(Path.Combine(folder, "ReportingWide.png"));
        if (selected.Name.StartsWith("P0012 ", StringComparison.Ordinal) && document!.Definition.Calibration == "L3M6EG000")
            CaptureHost(Path.Combine(folder, "MonitorRequests.png"));

        string path = document!.SourcePath;
        SaveMapWorkspace();
        OpenRom(path);
        await Settle();
        var restored = mapSlots.Single(slot => ReferenceEquals(slot.Editor.table, selected)).Editor;
        if (Math.Abs(restored.MapGrid.RowHeaderWidth - widened) > 1 || Math.Abs((restored.rowLabelWidth ?? 0) - widened) > 1)
            throw new InvalidOperationException("Reopening the ROM did not restore the resized row labels.");
        restored.CheckNamedRowLabels(requireUnwrapped: true);
    }
}
