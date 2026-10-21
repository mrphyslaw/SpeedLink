using System.Windows;
namespace SpeedLink.Desktop;

public sealed class LogViewerWindow : Window
{
    private readonly LogViewerWorkspace workspace;
    public LogViewerWindow()
    {
        UiTheme.Ensure(); SetResourceReference(StyleProperty, typeof(Window)); Title = "SpeedLink · Recorded log"; Width = 1180; Height = 850; MinWidth = 920; MinHeight = 650;
        workspace = new(); Content = workspace; Closed += (_, _) => workspace.CancelLoading();
    }
    internal async Task<Dictionary<string, object?>> ExerciseOffline(string path, string screenshot) { Show(); return await workspace.ExerciseOffline(path, screenshot); }
}
