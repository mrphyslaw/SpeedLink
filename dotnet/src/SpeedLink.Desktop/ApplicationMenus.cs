using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private readonly string appearancePath;
    private Appearance appearance = Appearance.Dark;
    private bool greenForHighValues;
    private string MapColorDirectionPath => Path.Combine(vault, "map-cell-colors.txt");

    private void ConfigureApplicationMenus()
    {
        EditorPane.SetShortcuts(ShortcutPreferences.Load(ShortcutPath));
        EditorPane.BindShortcutHint(ApplicationOpenRomMenu, ShortcutAction.OpenRom);
        EditorPane.BindShortcutHint(ApplicationSaveMenu, ShortcutAction.SaveRom);
        EditorPane.BindShortcutHint(ApplicationOpenRecordingMenu, ShortcutAction.OpenRecording);
        EditorPane.BindShortcutHint(KeyboardShortcutsMenu, ShortcutAction.KeyboardShortcuts);
        foreach (var item in WorkspaceMenu.Items.OfType<MenuItem>())
            EditorPane.BindShortcutHint(item, ShortcutAction.Connection + int.Parse((string)item.Tag, CultureInfo.InvariantCulture));
        EditorPane.AttachApplicationMenus(ApplicationEditMenu, ApplicationMapViewMenu);
        ApplicationSaveMenu.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = EditorPane.SaveButton });
        RefreshApplicationMenus();
        RefreshAppearanceMenu();
        RefreshMapColorMenu();
    }

    private void RefreshAppearanceMenu()
    {
        foreach (var item in AppearanceMenu.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag as string == appearance.ToString();
    }

    private void Appearance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string name } || !Enum.TryParse(name, out Appearance choice)) return;
        appearance = choice;
        UiTheme.Apply(appearance, greenForHighValues);
        try
        {
            UiTheme.Save(appearancePath, choice);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Appearance could not be saved. " + error.Message;
        }
        RefreshAppearanceMenu();
    }

    private void RefreshMapColorMenu()
    {
        RedHighMenu.IsChecked = !greenForHighValues;
        GreenHighMenu.IsChecked = greenForHighValues;
    }

    private void MapColors_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string direction }) return;
        greenForHighValues = direction == "GreenHigh";
        UiTheme.Apply(appearance, greenForHighValues);
        RefreshMapColorMenu();
        try
        {
            UiTheme.SaveMapColorDirection(MapColorDirectionPath, greenForHighValues);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Map cell colors could not be saved. " + error.Message;
        }
    }

    private void WindowsAppearanceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (appearance == Appearance.System) UiTheme.Apply(appearance, greenForHighValues);
        }));
    }

    private void RefreshApplicationMenus()
    {
        if (ApplicationMenu is null || EditorPane is null) return;
        ApplicationEditMenu.IsEnabled = Pages.SelectedIndex == 1;
        ApplicationMapViewMenu.IsEnabled = Pages.SelectedIndex == 1;
        foreach (var item in WorkspaceMenu.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag as string == Pages.SelectedIndex.ToString(CultureInfo.InvariantCulture);
    }

    private void ApplicationOpenRom_Click(object sender, RoutedEventArgs e) { Navigate(1); EditorPane.RequestOpen(); }
    private void ApplicationSave_Click(object sender, RoutedEventArgs e) => EditorPane.RequestSave();
    private void ApplicationOpenRecording_Click(object sender, RoutedEventArgs e) { Navigate(3); RecordingsPane.RequestOpen(); }
    private void ApplicationExit_Click(object sender, RoutedEventArgs e) => Close();
    private void ApplicationGuide_Click(object sender, RoutedEventArgs e) => HelpWindow((sender as MenuItem)?.Tag as string ?? "Getting started").Show();
    private void NavigationLabels_Click(object sender, RoutedEventArgs e)
    {
        navigationIsCollapsed = !NavigationLabelsMenu.IsChecked;
        ApplyNavigation(); EditorPane.SaveNavigationLayout();
    }
    private void WorkspaceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out int page) && page >= 0 && page < Pages.Items.Count) Navigate(page);
    }
    private void Application_KeyDown(object sender, KeyEventArgs e)
    {
        if (!AllowsKeyboardShortcuts || EditorWorkspace.NativeKeyboardInput(e.OriginalSource) ||
            EditorPane.Shortcuts.Find(ShortcutKey.From(e)) is not { } action) return;
        if (e.IsRepeat && action <= ShortcutAction.FlashWorkspace) { e.Handled = true; return; }
        e.Handled = HandleApplicationShortcut(action, e);
    }
}

public partial class EditorWorkspace
{
    internal void RequestSave() => Save_Click(this, new());

    internal void AttachApplicationMenus(MenuItem edit, MenuItem view)
    {
        // Move the existing commands so the main window and each map continue to
        // use the same editing, pending-input and mandatory-save checks.
        var menus = EditorMenu.Items.OfType<MenuItem>().ToArray();
        MenuItem UndoRedo(string header, ShortcutAction shortcut, Button button, RoutedEventHandler action)
        {
            var item = new MenuItem { Header = header };
            BindShortcutHint(item, shortcut);
            item.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = button });
            item.Click += action; return item;
        }
        edit.Items.Add(UndoRedo("_Undo", ShortcutAction.Undo, UndoButton, Undo_Click));
        edit.Items.Add(UndoRedo("_Redo", ShortcutAction.Redo, RedoButton, Redo_Click));
        edit.Items.Add(new Separator());
        foreach (var item in menus[1].Items.OfType<MenuItem>().Take(3))
            item.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = FitMapButton });
        MoveItems(menus[1], edit); MoveItems(menus[2], view);
        view.SubmenuOpened += (_, e) => { if (ReferenceEquals(e.Source, view)) RefreshMapCommandState(); };
        EditorMenu.Visibility = Visibility.Collapsed;
        FitMapButton.Visibility = Visibility.Collapsed;
        MapsButton.Margin = new(0);
        static void MoveItems(MenuItem source, MenuItem target)
        {
            while (source.Items.Count > 0) { var item = source.Items[0]; source.Items.RemoveAt(0); target.Items.Add(item); }
        }
    }
}
