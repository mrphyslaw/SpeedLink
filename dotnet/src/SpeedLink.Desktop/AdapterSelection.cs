using System.IO;
using System.Text.Json;
using System.Windows;
using SpeedLink.J2534;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private sealed record AdapterSelection(string Adapter, string? Port, int Baud, string? Driver);

    private AdapterSelection? rememberedAdapter;
    private string AdapterSettingsPath => Path.Combine(vault, "adapter-settings.json");

    private void RefreshPortChoices(string[] found)
    {
        string selected = PortBox.Text.Trim();
        if (selected.Length == 0) selected = rememberedAdapter?.Port ?? "";
        PortBox.ItemsSource = found;
        PortBox.Text = found.FirstOrDefault(port => port.Equals(selected, StringComparison.OrdinalIgnoreCase))
            ?? (selected.Length == 0 && found.Length == 1 ? found[0] : "");
        SharedAdapterChanged();
    }

    private void LoadAdapterSelection()
    {
        try
        {
            if (File.Exists(AdapterSettingsPath) && new FileInfo(AdapterSettingsPath).Length <= 16384)
                rememberedAdapter = JsonSerializer.Deserialize<AdapterSelection>(File.ReadAllText(AdapterSettingsPath));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Log("Adapter settings could not be read: " + error.Message);
        }

        int index = rememberedAdapter?.Adapter switch { "Tactrix" => 1, "Mongoose" => 2, _ => 0 };
        if (rememberedAdapter?.Baud is int baud && BaudBox.Items.Contains(baud))
            BaudBox.SelectedItem = baud;
        if (index > 0 && rememberedAdapter?.Driver is { Length: > 0 } driver)
            selectedDrivers[index == 1 ? J2534AdapterKind.Tactrix : J2534AdapterKind.MongoosePlusIsoCan3] = driver;
        AdapterBox.SelectedIndex = index;
    }

    private void SaveAdapterSelection()
    {
        if (BaudBox.SelectedItem is not int baud) return;
        string port = PortBox.Text.Trim();
        AdapterSelection? selection = AdapterBox.SelectedIndex switch
        {
            0 when PortBox.Items.Cast<string>().Contains(port, StringComparer.OrdinalIgnoreCase) || transport is not null =>
                new("vLinker", port, baud, null),
            1 or 2 when DriverBox.SelectedItem is J2534Driver driver =>
                new(driver.Experimental ? "Mongoose" : "Tactrix", null, baud, driver.LibraryPath),
            _ => null
        };
        if (selection is null || selection == rememberedAdapter) return;
        string temporary = AdapterSettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(vault);
            File.WriteAllText(temporary, JsonSerializer.Serialize(selection));
            File.Move(temporary, AdapterSettingsPath, overwrite: true);
            rememberedAdapter = selection;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log("Adapter settings could not be saved: " + error.Message);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private string? MissingAdapterSetup()
    {
        if (AdapterBox.SelectedIndex == 0)
            return string.IsNullOrWhiteSpace(PortBox.Text) ? "Select the vLinker COM port. Plug in the adapter and refresh devices if it is missing." : null;
        if (DriverBox.SelectedItem is J2534Driver driver && driver.Kind == SelectedDriverKind)
            return null;
        string adapter = SelectedDriverKind == J2534AdapterKind.Tactrix ? "Tactrix Openport" : "Mongoose-Plus ISO/CAN3";
        return $"Select the {adapter} driver. Install the manufacturer’s driver and refresh devices if it is missing.";
    }

    private void RefreshReadConnection(bool idle)
    {
        bool reconnect = transport is not null && (transport.ReadAttempted || loggerReadReconnectRequired);
        ReadConnectionPanel.Visibility = reconnect ? Visibility.Visible : Visibility.Collapsed;
        ReadReconnectButton.IsEnabled = idle && reconnect;
        ReadConnectionHint.Text = loggerReadReconnectRequired
            ? "Reconnect before reading a backup after logging."
            : "Reconnect before reading another backup.";
    }

    private async void ReadReconnect_Click(object sender, RoutedEventArgs e)
    {
        if (ReadReconnectButton.IsEnabled) await ConnectAdapter(reconnect: true);
    }
}
