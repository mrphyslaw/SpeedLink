using System.IO;
using System.Windows;
using System.Windows.Controls;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private async Task CheckMapInformation(string folder)
    {
        var snapshot = document!.Snapshot();
        var previous = table;
        var sample = document.Definition.Tables.Single(t => t.Name == "Knock Retard Additive");
        OpenTable(sample); DetailsButton.IsChecked = true; UpdateLayout(); infoWindow!.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (HelpUnitsText.Text != "Values: " + sample.Units || !HelpUnitsText.IsVisible ||
            !HelpAxesText.Text.Contains(sample.X.Name, StringComparison.Ordinal) ||
            !HelpAxesText.Text.Contains(sample.Y.Name, StringComparison.Ordinal) || HelpText.Text != sample.Description ||
            Descendants(DetailsPanel).OfType<Expander>().Single().IsExpanded)
            throw new InvalidOperationException("Map information hides units or does not match the selected map.");
        // Changing maps while the window is open must replace both help and units.
        var injector = document.Definition.Tables.First(t => t.Name.StartsWith("Injector Base Pulsewidth - ", StringComparison.Ordinal));
        OpenTable(injector); UpdateLayout();
        if (HelpUnitsText.Text != "Values: " + injector.Units || HelpText.Text != injector.Description)
            throw new InvalidOperationException("Map information retained the previous map's units or description.");
        OpenTable(sample); UpdateLayout(); infoWindow.UpdateLayout();
        CaptureWindow(infoWindow, Path.Combine(folder, "MapInformation.png"));
        DetailsButton.IsChecked = false;
        if (previous is not null) OpenTable(previous);
        UpdateLayout();
        if (!snapshot.AsSpan().SequenceEqual(document.Snapshot()))
            throw new InvalidOperationException("Viewing map information changed ROM bytes.");
    }
}
