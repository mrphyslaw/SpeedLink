using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

internal sealed class DefinitionManagerWindow : Window
{
    private readonly DefinitionLibrary library;
    private readonly DataGrid list = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, Margin = new(0, 14, 0, 10) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
    private readonly Button revert = new() { Content = "Use bundled copy", IsEnabled = false };
    internal DefinitionManagerWindow(DefinitionLibrary library)
    {
        this.library = library;
        UiTheme.Ensure(); SetResourceReference(StyleProperty, typeof(Window));
        Title = "SpeedLink · Definitions"; Width = 850; Height = 650; MinWidth = 700; MinHeight = 530;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new(20) }; Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "ROM definitions", FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Import an editor XML or logger JSON update. Imported copies take priority over bundled definitions.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(details);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var import = new Button { Content = "Import…" }; import.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Import a ROM definition", Filter = "ROM definitions|*.xml;*.json|Editor XML|*.xml|Logger JSON|*.json", CheckFileExists = true };
            if (picker.ShowDialog(this) == true) Attempt(() => ImportPath(picker.FileName));
        };
        buttons.Children.Add(import); buttons.Children.Add(revert);
        revert.Click += (_, _) => Attempt(() =>
        {
            if (list.SelectedItem is not DefinitionEntry entry) return;
            library.UseBundled(entry); Refresh(entry.Kind, entry.Calibration);
            status.Text = "Bundled copy restored. Reopen the ROM or reload the logger ROM to use it. Existing edits are unchanged.";
        });
        var folder = new Button { Content = "Open folder" }; folder.Click += (_, _) => Attempt(() =>
        {
            Directory.CreateDirectory(library.Folder); Process.Start(new ProcessStartInfo { FileName = library.Folder, UseShellExecute = true });
        }); buttons.Children.Add(folder);
        var close = new Button { Content = "Close", IsCancel = true }; close.Click += (_, _) => Close(); buttons.Children.Add(close);
        footer.Children.Add(new TextBlock { Text = "Updates apply when a ROM is next opened or loaded for logging. The bundled definitions stay available.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) });
        footer.Children.Add(status);
        foreach (var (title, property, width) in new[] { ("Type", "Kind", 70d), ("ROM", "Calibration", 115d), ("Version", "Version", 210d), ("Controls", "Controls", 75d), ("Source", "Source", 100d) })
            list.Columns.Add(new DataGridTextColumn { Header = title, Binding = new System.Windows.Data.Binding(property), Width = new DataGridLength(width) });
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not DefinitionEntry entry) return;
            revert.IsEnabled = entry.InstalledSha256 is not null;
            details.Text = $"{entry.Calibration} · {entry.Kind} · {(entry.Imported ? "Imported copy" : "Bundled copy")}\n" + (entry.Problem ?? $"SHA-256: {entry.Sha256}");
        };
        root.Children.Add(list); Refresh();
    }
    private void Attempt(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { status.Text = "Definition was not changed. " + error.Message; }
    }
    private void Refresh(DefinitionKind? kind = null, string? calibration = null)
    {
        var snapshot = library.Load(); list.ItemsSource = snapshot.Entries;
        list.SelectedItem = snapshot.Entries.FirstOrDefault(e => e.Kind == kind && e.Calibration == calibration) ?? snapshot.Entries.First();
        if (snapshot.Entries.Any(e => e.Problem is not null)) status.Text = "Some imported definitions were rejected. Select their rows for details; bundled copies remain active.";
    }
    internal bool ImportPath(string path, Func<DefinitionCandidate, DefinitionEntry, bool>? confirm = null)
    {
        var candidate = library.Preview(path); var existing = library.Load().Entries.Single(e => e.Kind == candidate.Entry.Kind && e.Calibration == candidate.Entry.Calibration);
        bool accepted = confirm?.Invoke(candidate, existing) ?? MessageBox.Show(this,
            $"{candidate.Entry.Kind} definition for {candidate.Entry.Calibration}\nNew version: {candidate.Entry.Version}\n{candidate.Entry.Controls:N0} controls\n\nCurrent version: {existing.Version} ({(existing.Imported ? "imported" : "bundled")})\n\nInstall this definition? Only import files from a source you trust.",
            "Import definition", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!accepted) return false;
        library.Install(candidate, existing.InstalledSha256); Refresh(candidate.Entry.Kind, candidate.Entry.Calibration);
        status.Text = "Definition installed. Reopen the ROM or reload the logger ROM to use it. Existing edits are unchanged."; return true;
    }
    internal void SelectAndUseBundled(DefinitionKind kind, string calibration)
    {
        list.SelectedItem = list.Items.Cast<DefinitionEntry>().Single(e => e.Kind == kind && e.Calibration == calibration);
        revert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}

public partial class MainWindow
{
    private readonly DefinitionLibrary definitionLibrary;
    private DefinitionSnapshot definitionSnapshot;
    private void Definitions_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closing || transport is not null) return;
        new DefinitionManagerWindow(definitionLibrary) { Owner = this }.ShowDialog();
        RefreshDefinitions();
    }
    private void RefreshDefinitions()
    {
        var old = definitionSnapshot; definitionSnapshot = definitionLibrary.Load(); EditorPane.Definitions = definitionSnapshot.Editors;
        var previous = old.Entries.Single(e => e.Kind == DefinitionKind.Logger && e.Calibration == loggerDefinition.SoftwareIds.Single());
        var current = definitionSnapshot.Entries.Single(e => e.Kind == previous.Kind && e.Calibration == previous.Calibration);
        if (previous.Sha256 != current.Sha256) ConfigureLoggerProfile(definitionSnapshot.LoggerFor(current.Calibration)!);
        StatusText.Text = definitionSnapshot.Entries.Any(e => e.Problem is not null) ? "Some imported definitions were rejected · open Definitions for details" : "Definitions refreshed · reopen the ROM to use editor updates";
        Controls();
    }
}
