using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    internal async Task CheckRemappedEditorShortcuts(TuningTable firstMap)
    {
        var doc = document!;
        var other = doc.Definition.Tables.First(t => !ReferenceEquals(t, firstMap) && t is { Rows: > 1, Columns: > 1, IsChoice: false });
        var first = OpenMap(firstMap);
        var second = OpenMap(other);
        byte[] bytes = doc.Snapshot();
        int firstView = first.ViewMode;
        int secondView = second.ViewMode;
        var original = Shortcuts;
        var custom = original.Copy();
        if (!custom.TryAssign(ShortcutAction.ToggleGraph, new(Key.F9), out _) ||
            !custom.TryAssign(ShortcutAction.Increase, new(Key.U), out _) ||
            !custom.TryAssign(ShortcutAction.MapInformation, null, out _))
            throw new InvalidOperationException("Could not assign the shortcut test values.");
        try
        {
            SetShortcuts(custom);
            await Settle();
            second.MapGrid.Focus();
            second.SelectCell(0);
            double value = doc.Read(other, 0);
            if (RaiseShortcut(second.MapGrid, Key.F4) || RaiseShortcut(second.MapGrid, Key.Q) ||
                RaiseShortcut(second.MapGrid, Key.F1) || second.ViewMode != secondView || doc.Read(other, 0) != value)
                throw new InvalidOperationException("Reassigned and cleared shortcuts still ran their old actions.");
            if (!RaiseShortcut(second.MapGrid, Key.U) || doc.Read(other, 0) <= value)
                throw new InvalidOperationException("The reassigned shortcut did not edit the active map.");
            doc.Undo();
            if (!bytes.AsSpan().SequenceEqual(doc.Snapshot()))
                throw new InvalidOperationException("Undo did not restore the shortcut edit.");
            if (!RaiseShortcut(second.MapGrid, Key.F9) || second.ViewMode != 3 || first.ViewMode != firstView)
                throw new InvalidOperationException("A remapped command did not stay scoped to the focused map.");
            if (!RaiseShortcut(second.GraphPanel, Key.F9) || second.ViewMode != secondView)
                throw new InvalidOperationException("The reassigned graph shortcut did not return to the table.");

            SearchBox.Focus();
            if (RaiseShortcut(SearchBox, Key.F9) || RaiseShortcut(SearchBox, Key.U))
                throw new InvalidOperationException("A map shortcut took input from the search field.");

            if (!RaiseShortcut(second.MapGrid, Key.F7))
                throw new InvalidOperationException("The default dock shortcut did not detach the active map.");
            await Settle();
            var window = mapSlots.Single(slot => ReferenceEquals(slot.Editor, second)).Window!;
            window.Activate();
            second.MapGrid.Focus();
            await Settle();
            bool handled = RaiseShortcut(second.MapGrid, Key.F9);
            if (!handled || second.ViewMode != 3 || first.ViewMode != firstView)
                throw new InvalidOperationException($"Detached shortcut failed (handled {handled}, map {second.ViewMode}, other map {first.ViewMode}, visible {window.IsVisible}).");
            if (!RaiseShortcut(second.GraphPanel, Key.F7))
                throw new InvalidOperationException("The default dock shortcut did not return the detached map.");
            await Settle();
        }
        finally
        {
            SetShortcuts(original);
            CloseMap(second);
            first.ActivateMap();
        }
    }

    internal static bool RaiseShortcut(UIElement element, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element)!, 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        element.RaiseEvent(args);
        return args.Handled;
    }
}

public partial class MainWindow
{
    private async Task CheckShortcutSettings(string folder)
    {
        var original = EditorPane.Shortcuts;
        var dialog = new KeyboardShortcutsWindow(this, ShortcutPath, original);
        bool saved = false;
        dialog.Loaded += (_, _) =>
        {
            try
            {
                dialog.CheckSettingsControls(folder);
                saved = true;
            }
            finally { if (!saved) dialog.Close(); }
        };
        if (dialog.ShowDialog() != true || !saved)
            throw new InvalidOperationException("The keyboard shortcut settings did not save.");
        var loaded = ShortcutPreferences.Load(ShortcutPath);
        if (loaded[ShortcutAction.OpenRom] is not null ||
            loaded[ShortcutAction.ToggleGraph] != new ShortcutKey(Key.F9))
            throw new InvalidOperationException("Clearing or remapping a shortcut did not survive reload.");
        try
        {
            EditorPane.SetShortcuts(loaded);
            if (ApplicationOpenRomMenu.InputGestureText.Length != 0 ||
                EditorPane.ToggleGraphItem.InputGestureText != "F9")
                throw new InvalidOperationException("Menus did not show the remapped or cleared shortcut.");
            var restarted = new MainWindow(vault);
            try
            {
                restarted.Show();
                await MenuIdle();
                if (restarted.EditorPane.Shortcuts[ShortcutAction.ToggleGraph] != new ShortcutKey(Key.F9) ||
                    restarted.ApplicationOpenRomMenu.InputGestureText.Length != 0)
                    throw new InvalidOperationException("The new main window did not load the shortcut preferences.");
            }
            finally { restarted.CloseWorkbenchTest(); }
        }
        finally
        {
            original.Save(ShortcutPath);
            EditorPane.SetShortcuts(original);
        }
    }
}

internal sealed partial class KeyboardShortcutsWindow
{
    internal void CheckSettingsControls(string folder)
    {
        List.SelectedItem = List.Items.Cast<Row>().First(item => item.Definition.Action == ShortcutAction.ToggleGraph);
        Capture.Focus();
        if (!EditorWorkspace.RaiseShortcut(Capture, Key.F1) || Draft[ShortcutAction.ToggleGraph] != new ShortcutKey(Key.F4) ||
            !Feedback.Text.Contains("Already assigned", StringComparison.Ordinal))
            throw new InvalidOperationException("The settings dialog allowed a duplicate shortcut.");
        if (!EditorWorkspace.RaiseShortcut(Capture, Key.F9) || Capture.Text != "F9")
            throw new InvalidOperationException("The capture field did not record the replacement shortcut.");
        Reset_Click(this, new());
        if (Capture.Text != "F4") throw new InvalidOperationException("Reset did not restore the action default.");
        ResetAll_Click(this, new());
        if (Draft[ShortcutAction.OpenRom] != new ShortcutKey(Key.O, ModifierKeys.Control))
            throw new InvalidOperationException("Reset All did not restore the application defaults.");
        EditorWorkspace.RaiseShortcut(Capture, Key.F9);
        List.SelectedItem = List.Items.Cast<Row>().First(item => item.Definition.Action == ShortcutAction.OpenRom);
        Clear_Click(this, new());
        if (Capture.Text != "Unassigned") throw new InvalidOperationException("Clear did not unassign the shortcut.");
        UpdateLayout();
        EditorWorkspace.CaptureWindow(this, Path.Combine(folder, "KeyboardShortcuts.png"));
        Save_Click(this, new());
    }
}
