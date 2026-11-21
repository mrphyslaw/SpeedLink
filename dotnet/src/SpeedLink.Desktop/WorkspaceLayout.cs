using System.IO;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private void ConfigureWorkspaceLayout()
    {
        // Keep the existing map layout file and its panes; the obsolete sidebar
        // collapse preference no longer controls application navigation.
        EditorPane.ConfigureLayout(Path.Combine(vault, "editor-layout.json"), () => false);
    }
}
