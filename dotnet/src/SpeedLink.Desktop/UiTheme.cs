using System.Windows;
using System.Windows.Media;

namespace SpeedLink.Desktop;

internal static class UiTheme
{
    internal static readonly SolidColorBrush Text = new(Color.FromRgb(237, 240, 242));
    internal static readonly SolidColorBrush Muted = new(Color.FromRgb(178, 187, 194));
    internal static readonly SolidColorBrush Panel = new(Color.FromRgb(35, 39, 43));
    internal static readonly SolidColorBrush Line = new(Color.FromRgb(67, 74, 80));
    internal static readonly SolidColorBrush Amber = new(Color.FromRgb(242, 182, 93));
    internal static void Ensure()
    {
        if (Application.Current is { } app && !app.Resources.Contains("ShellBrush"))
            app.Resources.MergedDictionaries.Add(new() { Source = new Uri("/SpeedLink;component/Theme.xaml", UriKind.Relative) });
    }
}
