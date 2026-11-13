using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace SpeedLink.Desktop;

internal sealed partial class KeyboardShortcutsWindow : Window
{
    private readonly string settingsPath;
    internal ShortcutPreferences Draft { get; }
    private sealed record Row(ShortcutDefinition Definition, string Gesture)
    {
        public string Group => Definition.Group;
        public string Label => Definition.Label;
    }

    internal KeyboardShortcutsWindow(Window owner, string settingsPath, ShortcutPreferences preferences)
    {
        Owner = owner;
        this.settingsPath = settingsPath;
        Draft = preferences.Copy();
        InitializeComponent();
        RefreshRows();
        List.SelectedIndex = 0;
    }

    private void RefreshRows()
    {
        var action = (List.SelectedItem as Row)?.Definition.Action;
        List.ItemsSource = null;
        var view = new ListCollectionView(ShortcutPreferences.Definitions.Select(item => new Row(item, Display(item.Action))).ToArray());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShortcutDefinition.Group)));
        List.ItemsSource = view;
        if (action is { } selected) List.SelectedItem = view.Cast<Row>().First(item => item.Definition.Action == selected);
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Capture is null || ResetButton is null || Feedback is null) return;
        bool selected = List.SelectedItem is Row;
        Capture.IsEnabled = ClearButton.IsEnabled = ResetButton.IsEnabled = selected;
        Capture.Text = selected ? Display(((Row)List.SelectedItem).Definition.Action) : "";
        Feedback.Text = "";
    }

    private string Display(ShortcutAction action) => Draft[action]?.ToString() ?? "Unassigned";

    private void CaptureKey(object sender, KeyEventArgs e)
    {
        var key = ShortcutKey.From(e);
        if (key.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return;
        }
        if (key.Key == Key.Escape || key.Key == Key.Tab && key.Modifiers is ModifierKeys.None or ModifierKeys.Shift) return;
        e.Handled = true;
        if (List.SelectedItem is Row selected) Assign(selected.Definition, key);
    }

    private void Assign(ShortcutDefinition selected, ShortcutKey? key)
    {
        if (!Draft.TryAssign(selected.Action, key, out var error)) { Feedback.Text = error; return; }
        RefreshRows();
        Capture.Text = Display(selected.Action);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is Row selected) Assign(selected.Definition, null);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is Row selected) Assign(selected.Definition, selected.Definition.Default);
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        Draft.RestoreDefaults();
        RefreshRows();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { Draft.Save(settingsPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Feedback.Text = "Could not save keyboard shortcuts. " + error.Message;
            return;
        }
        DialogResult = true;
    }
}
