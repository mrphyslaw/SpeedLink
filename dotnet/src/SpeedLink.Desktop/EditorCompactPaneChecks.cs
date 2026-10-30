using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    internal async Task<string[]> ExerciseCompactScalarPanes(string folder)
    {
        var cases = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            cases.Add(name);
            File.WriteAllText(Path.Combine(folder, "CompactPaneProgress.json"), System.Text.Json.JsonSerializer.Serialize(new { cases }));
        }
        async Task Idle() { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); }
        var doc = document!; byte[] before = doc.Snapshot();
        string previousStatus = EditorStatus.Text;
        EditorStatus.Text = "Synthetic layout example · five values and states";
        var host = Window.GetWindow(this); double width = host.Width, height = host.Height;
        bool hidden = FocusButton.IsChecked;
        var scalars = doc.Definition.Tables.Where(t => t.Count == 1 && !t.IsChoice && t.Storage == "float").Take(4)
            .Concat(doc.Definition.Tables.Where(t => t.Count == 1 && t.IsChoice).Take(1)).ToArray();
        Check(scalars.Length == 5, "Compact pane matrix includes four values and a named state");
        FocusButton.IsChecked = true;
        dockColumnFraction = .5; dockRowWeights = []; scalarColumnWidths = [];
        var editors = scalars.Select(OpenMap).ToArray();
        foreach (var size in new[] { new Size(1280, 850), new Size(1024, 768) })
        {
            string sizeKey = FormattableString.Invariant($"{size.Width:0}x{size.Height:0}");
            host.Width = size.Width; host.Height = size.Height; await Idle();
            CaptureHost(Path.Combine(folder, $"FiveScalarPanes-{size.Width}.png"));
            File.WriteAllText(Path.Combine(folder, $"ScalarPaneMetrics-{size.Width}.json"), System.Text.Json.JsonSerializer.Serialize(editors.Select(editor => new
            {
                map = editor.table!.Name, pane = editor.ScalarPanel.RenderSize.ToString(),
                host.ActualWidth, host.ActualHeight, workspace = mapDockScroll.RenderSize.ToString(), frame = DockedBounds(editor),
                editor.ScalarPanel.ViewportWidth, editor.ScalarPanel.ViewportHeight, editor.ScalarPanel.ScrollableWidth, editor.ScalarPanel.ScrollableHeight,
                controls = new FrameworkElement[] { editor.ScalarInput, editor.ScalarChoice, editor.ScalarUnits, editor.ScalarApply, editor.ScalarCancel, editor.ScalarReset, editor.ScalarReference, editor.ScalarDifference }
                    .Select(control => new { control.Name, control.IsVisible, bounds = control.TransformToAncestor(editor.ScalarPanel).TransformBounds(new Rect(control.RenderSize)).ToString() })
            })));
            Check(mapSlots.Count == 5 && mapDockScroll.ScrollableHeight <= 1, $"Five scalar panes fit together at {sizeKey}");
            Check(mapDock.RowDefinitions.Where((_, i) => i % 2 == 0).All(r => r.Height.IsAuto && r.ActualHeight <= 150), $"Scalar-only rows use compact content height at {sizeKey}");
            // Windows may cap a requested window to the attached display. The
            // labeled sidebar also uses more space than compact navigation.
            // Require three cards across only when their actual viewport fits it.
            int minimumColumns = mapDockScroll.ViewportWidth >= 920 ? 3 : 2;
            Check(mapDock.ColumnDefinitions.Where((_, i) => i % 2 == 0).Count() >= minimumColumns && mapSlots.All(s => s.Frame.ActualWidth <= 302), $"Value cards wrap at roughly half the old pane width at {sizeKey}");
            foreach (var editor in editors)
            {
                bool Inside(FrameworkElement control)
                {
                    var bounds = control.TransformToAncestor(editor.ScalarPanel).TransformBounds(new Rect(control.RenderSize));
                    return control.IsVisible && bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= editor.ScalarPanel.ViewportWidth + 1 && bounds.Bottom <= editor.ScalarPanel.ViewportHeight + 1;
                }
                Check(editor.ScalarPanel.ScrollableHeight <= 1 && editor.ScalarPanel.ScrollableWidth <= 1 &&
                    Inside(editor.ScalarReset) && Inside(editor.ScalarReference) && Inside(editor.ScalarDifference) &&
                    (editor.table!.IsChoice ? Inside(editor.ScalarChoice) : Inside(editor.ScalarInput) && Inside(editor.ScalarUnits)),
                    $"All value, state, baseline and edit controls fit in {editor.table!.Name} at {size}");
                Check(!editor.ScalarActions.IsVisible && editor.ScalarInput.FontSize == 13 && editor.ScalarInput.MaxWidth == 160, $"Normal text and no idle edit buttons in {editor.table.Name} at {sizeKey}");
                Check(editor.PaneCaption.Text == editor.table.Name && editor.ScalarInfoButton.IsVisible && !editor.MapInfoButton.IsVisible && !editor.ValueUnitsText.IsVisible && !editor.EditBar.IsVisible,
                    $"Scalar pane has one title and no duplicate toolbar in {editor.table.Name} at {size}");
            }
        }
        FocusButton.IsChecked = false; await Idle();
        Check(scalarDockColumns is >= 1 and <= 2 && mapSlots.Count == 5 && mapDockScroll.ScrollableHeight <= 1 &&
            mapSlots.All(s => s.Frame.ActualWidth <= 302) && editors.All(e => e.ScalarPanel.ScrollableHeight <= 1), "Value cards reflow to available space when the browser is shown");
        FocusButton.IsChecked = true; await Idle();
        double cardWidth = DockedBounds(editors[^1]).Width;
        var otherBounds = editors[..^1].ToDictionary(e => e, DockedBounds);
        DragPaneEdge(editors[^1], MapResizeEdge.Right, 25, 0); await Idle();
        Check(DockedBounds(editors[^1]).Width > cardWidth && otherBounds.All(p => SameBounds(p.Value, DockedBounds(p.Key))), "Compact cards resize without changing other cards");
        double cardHeight = DockedBounds(editors[^1]).Height;
        DragPaneEdge(editors[^1], MapResizeEdge.Bottom, 0, 30); await Idle();
        Check(DockedBounds(editors[^1]).Height > cardHeight && otherBounds.All(p => SameBounds(p.Value, DockedBounds(p.Key))), "Compact card height changes independently");
        ArrangeMaps(); await Idle();
        Check(DockedBounds(editors[^1]).Height > cardHeight && editors.All(e => e.ScalarInput.IsVisible || e.ScalarChoice.IsVisible), "Deliberate scalar sizes survive layout recalculation");
        var first = editors[0]; var scalar = scalars[0]; first.ActivateMap();
        first.ScalarInfoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(first.infoWindow is { IsVisible: true } && first.HelpText.Text == scalar.Description && first.HelpUnitsText.Text.Contains(scalar.Units, StringComparison.Ordinal), "Compact scalar retains full help and units through Map info");
        first.DetailsButton.IsChecked = false; await Idle();
        double original = doc.Read(scalar, 0);
        first.ScalarInput.Text = (original + 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        await Idle();
        Check(first.ScalarActions.IsVisible && !first.ScalarDifference.IsVisible && first.ScalarPanel.ScrollableHeight <= 1, "Apply and Cancel appear only while a value is being edited");
        first.ScalarApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(doc.Read(scalar, 0) == original + 1 && doc.IsDirty && !first.scalarDraft, "Compact Apply edits the shared ROM");
        first.ScalarReset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(before.AsSpan().SequenceEqual(doc.Snapshot()), "Compact Reset returns to baseline byte for byte");
        first.ScalarInput.Text = "NaN";
        first.ScalarApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(first.ScalarError.IsVisible && !FinishAllMapEdits() && before.AsSpan().SequenceEqual(doc.Snapshot()), "Compact invalid input still blocks export without changing bytes");
        first.ScalarCancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(!first.scalarDraft && !first.ScalarError.IsVisible && FinishAllMapEdits(), "Compact Cancel clears validation and restores export preflight");
        var stateEditor = editors[^1]; var state = scalars[^1]; double old = doc.Read(state, 0);
        stateEditor.ScalarChoice.SelectedItem = stateEditor.ScalarChoice.Items.Cast<TuningState>().First(s => s.Value != old);
        Check(doc.Read(state, 0) != old, "Compact state selector applies the chosen state");
        doc.Undo();
        Check(before.AsSpan().SequenceEqual(doc.Snapshot()), "Compact state undo restores exact original bytes");
        SaveMapWorkspace(); var savedBounds = mapSlots.ToDictionary(s => MapKey(s.Editor.table!), s => DockedBounds(s.Editor));
        OpenRom(doc.SourcePath); await Idle();
        // Opening a ROM intentionally shows the browser. Restore the same
        // available width before comparing the saved card dimensions.
        FocusButton.IsChecked = true; await Idle();
        Check(mapSlots.Count == 5 && mapSlots.All(s => SameBounds(savedBounds[MapKey(s.Editor.table!)], DockedBounds(s.Editor))), "Reopening the calibration restores compact card sizes and maps");
        ClearMapWindows(); MapTabs.Items.Clear();
        EditorStatus.Text = previousStatus;
        host.Width = width; host.Height = height; FocusButton.IsChecked = hidden; await Idle();
        return cases.ToArray();
    }
}
