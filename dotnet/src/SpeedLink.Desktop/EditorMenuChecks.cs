using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    internal async Task<object> ExerciseMenusOffline(string folder)
    {
        Directory.CreateDirectory(folder);
        var app = Application.Current;
        // Deliberately conflict with the product palette, as a light Windows
        // theme does. Popup surfaces and text must retain their own contrast.
        app.Resources[SystemColors.MenuBrushKey] = Brushes.White;
        app.Resources[SystemColors.MenuTextBrushKey] = Brushes.Black;
        try
        {
            var menus = EditorMenu.Items.OfType<MenuItem>().ToArray();
            var results = new List<object>();
            foreach (var menu in menus)
            {
                menu.IsSubmenuOpen = true;
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var popup = menu.Template.FindName("PART_Popup", menu) as Popup;
                if (popup?.IsOpen != true || popup.Placement != PlacementMode.Bottom || popup.Child is not Border surface ||
                    surface.Background is not SolidColorBrush background || background.Color != UiTheme.Panel.Color)
                    throw new InvalidOperationException("Menu did not open with the product's dark surface: " + menu.Header);
                surface.UpdateLayout();
                foreach (var item in menu.Items.OfType<MenuItem>())
                {
                    item.ApplyTemplate();
                    if (item.Template.FindName("MenuHeader", item) is not ContentPresenter label || label.ActualWidth < 5 || label.ActualHeight < 5 ||
                        item.Foreground is not SolidColorBrush foreground || Contrast(foreground.Color, background.Color) < 4.5)
                        throw new InvalidOperationException("Menu label is blank or unreadable: " + item.Header);
                    if (item.InputGestureText.Length > 0 && (item.Template.FindName("MenuGesture", item) is not TextBlock gesture || gesture.Text != item.InputGestureText || gesture.ActualWidth < 10))
                        throw new InvalidOperationException("Menu shortcut is missing: " + item.Header);
                    if (!item.IsEnabled && item.Template.FindName("MenuContent", item) is Grid disabledContent)
                    {
                        double opacity = disabledContent.Opacity;
                        var effective = Color.FromRgb((byte)(foreground.Color.R * opacity + background.Color.R * (1 - opacity)),
                            (byte)(foreground.Color.G * opacity + background.Color.G * (1 - opacity)),
                            (byte)(foreground.Color.B * opacity + background.Color.B * (1 - opacity)));
                        if (Contrast(effective, background.Color) < 3) throw new InvalidOperationException("Disabled menu text is unreadable: " + item.Header);
                    }
                    if (item.IsChecked && (item.Template.FindName("MenuCheck", item) is not TextBlock check || !check.IsVisible))
                        throw new InvalidOperationException("Checked menu state is not visible: " + item.Header);
                }
                string name = menu.Header.ToString()!.Replace("_", "");
                SaveMenuImage(surface, Path.Combine(folder, name + ".png"));
                results.Add(new { menu = name, items = menu.Items.OfType<MenuItem>().Count(), surfaceDark = true, labelsReadable = true, shortcutsVisible = true });
                menu.IsSubmenuOpen = false;
            }
            var file = menus[0];
            if (((MenuItem)file.Items[1]).IsEnabled) throw new InvalidOperationException("Save is enabled without an open ROM.");
            var view = menus[2]; view.IsSubmenuOpen = true;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (GraphItem.IsEnabled || BesideItem.IsEnabled || BelowItem.IsEnabled || PreviewItem.IsEnabled)
                throw new InvalidOperationException("Graph commands are enabled without an active numeric map.");
            TableItem.Focus(); SendMenuKey(TableItem, Key.Enter);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (ViewMode != 2 || !TableItem.IsChecked || !TablePanel.IsVisible || GraphPanel.IsVisible)
                throw new InvalidOperationException("Keyboard menu activation did not run the selected action.");
            view.IsSubmenuOpen = true;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            TableItem.Focus(); SendMenuKey(TableItem, Key.Escape);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (view.IsSubmenuOpen) throw new InvalidOperationException("Escape did not close the menu.");
            SetViewMode(4);
            return new { passed = true, hardwareAccessed = false, menus = results, hostColorsIndependent = true, checkedStatesVisible = true, disabledSave = true, keyboardActivation = true, escapeCloses = true };
        }
        finally
        {
            foreach (var menu in EditorMenu.Items.OfType<MenuItem>()) menu.IsSubmenuOpen = false;
            app.Resources.Remove(SystemColors.MenuBrushKey); app.Resources.Remove(SystemColors.MenuTextBrushKey);
        }
    }
    private static void SendMenuKey(MenuItem target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target), Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
    private static double Contrast(Color text, Color background)
    {
        static double Light(Color c)
        {
            static double Channel(byte value) { double v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        }
        double a = Light(text), b = Light(background); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static void SaveMenuImage(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}

public partial class MainWindow
{
    internal async Task<object> ExerciseMenusOffline(string folder)
    {
        Show(); Activate(); Navigate(1); UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        return await EditorPane.ExerciseMenusOffline(folder);
    }
}
