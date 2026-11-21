using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SpeedLink.J2534;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private bool synchronizingSections;
    private string? renderedSection;
    private readonly Dictionary<string, int> sectionPages = new() { ["ecu"] = ObdPage, ["editor"] = 1, ["logging"] = 2, ["flash"] = FlashPage };
    private static string SectionFor(int page) => page switch { 1 or 4 => "editor", 2 or 3 => "logging", 6 => "flash", _ => "ecu" };
    private void Section_Checked(object sender, RoutedEventArgs e)
    {
        if (!synchronizingSections && Pages is not null && sender is RadioButton { Tag: string section })
            Navigate(sectionPages[section]);
    }
    private void RefreshSectionNavigation(int page)
    {
        string section = SectionFor(page);
        sectionPages[section] = page;
        synchronizingSections = true;
        try
        {
            foreach (var button in Navigation.Children.OfType<RadioButton>()) button.IsChecked = Equals(button.Tag, section);
            if (renderedSection != section)
            {
                SectionTabs.Children.Clear(); renderedSection = section;
                (int Page, string Label)[] tabs = section switch
                {
                    "ecu" => [(ObdPage, "Diagnostics"), (0, "Read backup"), (5, "Saved backups")],
                    "editor" => [(1, "Maps"), (4, "ROM information")],
                    "logging" => [(2, "Live"), (3, "Recordings")], _ => []
                };
                foreach (var tab in tabs)
                {
                    var button = new RadioButton { Content = tab.Label, Tag = tab.Page, GroupName = "SectionView", Padding = new(10, 3, 10, 3),
                        Style = (Style)FindResource("SectionTabNav"), ToolTip = tab.Label };
                    AutomationProperties.SetName(button, tab.Label);
                    button.Checked += (_, _) => { if (!synchronizingSections) Navigate((int)button.Tag); };
                    SectionTabs.Children.Add(button);
                }
            }
            foreach (var button in SectionTabs.Children.OfType<RadioButton>()) button.IsChecked = Equals(button.Tag, page);
        }
        finally { synchronizingSections = false; }
        RefreshSharedConnection();
    }
    private void ConfigureSharedConnection()
    {
        var info = new Button { Content = "ROM information…", Padding = new(8, 3, 8, 3), MinHeight = 26, Margin = new(0, 0, 6, 0) };
        info.Click += (_, _) => OpenEditorRomInformation();
        ((Panel)EditorPane.SaveButton.Parent).Children.Insert(0, info);
        PortBox.AddHandler(TextBoxBase.TextChangedEvent, new RoutedEventHandler((_, _) => SharedAdapterChanged()));
        DriverBox.SelectionChanged += (_, _) => SharedAdapterChanged();
        AdapterBox.SelectionChanged += (_, _) => SharedAdapterChanged();
    }
    private void SharedAdapterChanged()
    {
        if (flashWorkspace is not null && !flashWorkspace.IsWorking)
            flashWorkspace.UseSharedAdapter(AdapterBox.SelectedIndex == 0 ? PortBox.Text.Trim() : null,
                AdapterBox.SelectedIndex > 0 ? DriverBox.SelectedItem as J2534Driver : null);
        RefreshSharedConnection();
    }
    private void RefreshSharedConnection()
    {
        if (ConnectionSettingsButton is null) return;
        bool programming = flashWorkspace?.HardwareInUse == true;
        bool preparing = flashWorkspace?.IsWorking == true;
        string adapter = AdapterBox.SelectedIndex switch { 0 => "vLinker FS", 1 => "Tactrix", 2 => "Mongoose · experimental", _ => "Adapter" };
        ConnectionSettingsButton.Content = $"{adapter} · {(programming ? "Programming" : transport is null ? "Disconnected" : "Connected")} ▾";
        ConnectionSettingsButton.ToolTip = identity is null ? "Shared adapter, COM port, baud and J2534 driver settings." : $"{identity.Adapter}\n{identity.Calibration}\nShared by ECU, Editor, Logging and Flash & Recovery.";
        ConnectionSettingsButton.IsEnabled = !busy && !closing && !exportingReport && !preparing;
        ConnectButton.Visibility = transport is null ? Visibility.Visible : Visibility.Collapsed;
        DisconnectButton.Visibility = transport is null ? Visibility.Collapsed : Visibility.Visible;
        ActivityPanel.Visibility = busy || preparing ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = preparing && !busy ? Visibility.Collapsed : Visibility.Visible;
        SectionTabs.Visibility = Pages.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
        SectionBar.Visibility = (SectionFor(Pages.SelectedIndex) == "flash" || Pages.SelectedIndex == 1) && !busy && !preparing ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OpenEditorRomInformation()
    {
        if (EditorPane.Document is { } document)
        {
            string path = document.LastSavePath ?? document.SourcePath;
            bool loaded = false;
            TryFile(() => { LoadImage(path); loaded = true; });
            if (!loaded) return;
            RomText.Text = "Saved-file inspection. Unsaved map edits remain in Maps and are not included here.\n\n" + RomText.Text;
        }
        Navigate(4);
    }
    private void OpenSharedConnection() { SharedConnectionPopup.IsOpen = true; ConnectionSettingsButton.Focus(); }
    private async Task PrepareSharedProgramming()
    {
        if (busy || closing || exportingReport) throw new InvalidOperationException("Finish the current adapter operation first.");
        // Programming uses a fresh, exclusive transport. Release the read/log/OBD
        // session before the programming factory opens the same physical adapter.
        if (transport is not null) await Disconnect();
        Controls();
    }
}
