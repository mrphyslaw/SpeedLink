using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    internal bool AllowsKeyboardShortcuts => !closing && flashWorkspace?.HardwareInUse != true;
    private string ShortcutPath => System.IO.Path.Combine(vault, "keyboard-shortcuts.json");

    private void KeyboardShortcuts_Click(object sender, RoutedEventArgs e) => ShowKeyboardShortcuts();

    private void ShowKeyboardShortcuts()
    {
        var dialog = new KeyboardShortcutsWindow(this, ShortcutPath, EditorPane.Shortcuts);
        if (dialog.ShowDialog() == true) EditorPane.SetShortcuts(dialog.Draft);
    }

    internal bool HandleApplicationShortcut(ShortcutAction action, RoutedEventArgs e)
    {
        if (!AllowsKeyboardShortcuts) return false;
        switch (action)
        {
            case ShortcutAction.OpenRom: ApplicationOpenRom_Click(this, e); break;
            case ShortcutAction.SaveRom when ApplicationSaveMenu.IsEnabled: ApplicationSave_Click(this, e); break;
            case ShortcutAction.OpenRecording: ApplicationOpenRecording_Click(this, e); break;
            case ShortcutAction.KeyboardShortcuts: ShowKeyboardShortcuts(); break;
            case >= ShortcutAction.Connection and <= ShortcutAction.FlashWorkspace:
                Navigate(action - ShortcutAction.Connection);
                break;
            default: return false;
        }
        return true;
    }
}

public partial class EditorWorkspace
{
    private ShortcutPreferences shortcuts = new();
    private readonly List<(MenuItem Menu, ShortcutAction Action)> shortcutMenuItems = [];
    internal ShortcutPreferences Shortcuts => MapRoot.shortcuts;

    internal void SetShortcuts(ShortcutPreferences preferences)
    {
        shortcuts = preferences;
        foreach (var (menu, action) in shortcutMenuItems) menu.InputGestureText = preferences.Label(action);
        UndoButton.ToolTip = ShortcutTooltip("Undo", ShortcutAction.Undo);
        RedoButton.ToolTip = ShortcutTooltip("Redo", ShortcutAction.Redo);
        MapsButton.ToolTip = ShortcutTooltip("Toggle the map browser", ShortcutAction.ToggleBrowser);
        FitMapButton.ToolTip = ShortcutTooltip("Show the complete table", ShortcutAction.FitMap);
        NewWorkspaceButton.ToolTip = ShortcutTooltip("Create an empty workspace tab", ShortcutAction.NewTab);
    }

    internal void BindShortcutHint(MenuItem menu, ShortcutAction action)
    {
        shortcutMenuItems.Add((menu, action));
        menu.InputGestureText = Shortcuts.Label(action);
    }

    private string ShortcutTooltip(string text, ShortcutAction action) =>
        Shortcuts[action] is { } key ? $"{text} ({key})" : text;

    internal static bool NativeKeyboardInput(object source)
    {
        for (var element = source as DependencyObject; element is not null; element = System.Windows.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is TextBoxBase or PasswordBox or ComboBox or MenuItem) return true;
            if (element is not System.Windows.Media.Visual && element is not System.Windows.Media.Media3D.Visual3D) break;
        }
        return false;
    }

    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (Shortcuts.Find(ShortcutKey.From(e)) is not { } action || NativeKeyboardInput(e.OriginalSource)) return;
        if (Window.GetWindow(MapRoot) is MainWindow host)
        {
            if (!host.AllowsKeyboardShortcuts) return;
            if (action <= ShortcutAction.FlashWorkspace)
            {
                e.Handled = e.IsRepeat || host.HandleApplicationShortcut(action, e);
                return;
            }
        }
        else if (action is ShortcutAction.OpenRom or ShortcutAction.SaveRom)
        {
            if (!e.IsRepeat)
            {
                if (action == ShortcutAction.OpenRom) MapRoot.RequestOpen();
                else if (MapRoot.SaveButton.IsEnabled) MapRoot.RequestSave();
            }
            e.Handled = true;
            return;
        }
        e.Handled = RunEditorShortcut(action, e.IsRepeat);
    }

    private bool RunEditorShortcut(ShortcutAction action, bool repeat)
    {
        var root = MapRoot;
        var map = CommandMap;
        if (root.document is null) return false;

        switch (action)
        {
            case ShortcutAction.FindMap:
                if (!repeat) { root.ShowMapBrowser(); root.SearchBox.SelectAll(); }
                return true;
            case ShortcutAction.ToggleBrowser:
                if (!repeat) root.FocusButton.IsChecked = !root.FocusButton.IsChecked;
                return true;
            case ShortcutAction.NewTab:
                if (!repeat) root.NewWorkspace_Click(root, new());
                return true;
            case ShortcutAction.NextTab or ShortcutAction.PreviousTab:
                if (!repeat && root.workspacePages.Count > 1)
                {
                    int step = action == ShortcutAction.NextTab ? 1 : -1;
                    root.WorkspaceTabs.SelectedIndex = (root.currentPage + step + root.workspacePages.Count) % root.workspacePages.Count;
                }
                return true;
        }

        if (action is ShortcutAction.Undo or ShortcutAction.Redo)
        {
            if (!repeat && root.FinishAllMapEdits())
            {
                if (action == ShortcutAction.Undo) root.document.Undo();
                else root.document.Redo();
            }
            return true;
        }
        if (map.table is null) return false;
        bool numeric = map.table is { Count: > 1, IsChoice: false };
        Action? command = action switch
        {
            ShortcutAction.MapInformation => map.ShowMapInformation,
            ShortcutAction.ToggleGraph when numeric => map.ToggleGraphView,
            ShortcutAction.FitMap => () => map.FitMap_Click(map, new()),
            ShortcutAction.ExpandMap => () => map.ExpandMap_Click(map, new()),
            ShortcutAction.DockMap => () => map.UndockMap_Click(map, new()),
            ShortcutAction.PinMap when map.PinMapButton.IsEnabled => () => map.PinMap_Click(map, new()),
            ShortcutAction.CloseMap => () => root.CloseMap(map),
            ShortcutAction.EditTools when numeric => map.ToggleMapEditTools,
            _ => null
        };
        if (command is not null)
        {
            if (!repeat) command();
            return true;
        }
        if (!map.MapGrid.IsKeyboardFocusWithin) return false;
        if (action == ShortcutAction.Copy && map.MapGrid.SelectedCells.Count > 0)
        {
            if (!repeat) map.Copy_Click(map, new());
            return true;
        }
        if (!map.HasEditableCells) return false;
        switch (action)
        {
            case ShortcutAction.Paste: if (!repeat) map.Paste_Click(map, new()); break;
            case ShortcutAction.ResetCells: if (!repeat) map.Reset_Click(map, new()); break;
            case ShortcutAction.Increase: map.Nudge(1); break;
            case ShortcutAction.Decrease: map.Nudge(-1); break;
            case ShortcutAction.IncreaseLarge: map.Nudge(10); break;
            case ShortcutAction.DecreaseLarge: map.Nudge(-10); break;
            case ShortcutAction.InterpolateAcross when map.table.Columns > 1: if (!repeat) map.Interpolate("Horizontal"); break;
            case ShortcutAction.InterpolateDown when map.table.Rows > 1: if (!repeat) map.Interpolate("Vertical"); break;
            default: return false;
        }
        return true;
    }
}
