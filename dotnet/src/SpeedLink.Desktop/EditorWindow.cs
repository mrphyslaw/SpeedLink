using System.Windows;
using SpeedLink.Core;
namespace SpeedLink.Desktop;

// Standalone developer/command-line host. Normal navigation uses the same workspace in MainWindow.
public sealed class EditorWindow : Window
{
    private readonly EditorWorkspace workspace;
    public EditorWindow() : this(new(), new()) { }
    internal EditorWindow(RomInspector inspector, TuningCodeGuard guard)
    {
        UiTheme.Ensure(); SetResourceReference(StyleProperty, typeof(Window)); Title = "SpeedLink · ROM editor"; Width = 1480; Height = 940; MinWidth = 1080; MinHeight = 720;
        workspace = new(inspector, guard); Content = workspace;
        Closing += (_, e) => e.Cancel = !workspace.MayClose();
    }
    public void OpenRom(string path) => workspace.OpenRom(path);
    internal void CloseOfflineTest() { workspace.CloseOfflineTest(); Close(); }
    internal async Task<object> ExerciseOffline(string path, string folder) { Show(); return await workspace.ExerciseOffline(path, folder); }
}
