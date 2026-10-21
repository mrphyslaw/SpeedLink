using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private EditorLayoutPreferences preferences = new();
    private string? layoutPath;
    private Func<bool>? navigationCollapsed;
    private Window? browserWindow, graphWindow, infoWindow;
    private double browserWidth = 200, curveWidth = 300, tableFraction = .6;
    private bool fitTable, restoringLayout, layoutClosing;
    internal int ViewMode { get; private set; }
    internal bool CurveList => table is { Rows: 1, Columns: > 16, IsChoice: false };
    internal bool BrowserFloating => browserWindow is not null;
    internal bool GraphFloating => graphWindow is not null;
    internal void ConfigureLayout(string path, Func<bool> getNavigationCollapsed)
    {
        layoutPath = path; navigationCollapsed = getNavigationCollapsed; preferences = EditorLayoutPreferences.Load(path);
        restoringLayout = true; browserWidth = preferences.BrowserWidth; curveWidth = preferences.CurveWidth; tableFraction = preferences.TableFraction; fitTable = preferences.FitTable;
        FocusButton.IsChecked = preferences.BrowserHidden; SetViewMode(preferences.View);
        Loaded += RestoreFloating;
    }
    private void RestoreFloating(object sender, RoutedEventArgs e)
    {
        Loaded -= RestoreFloating;
        if (preferences.BrowserFloating) PopBrowser_Click(this, new());
        if (preferences.GraphFloating) PopGraph_Click(this, new());
        restoringLayout = false; ApplyLayout();
    }
    internal void SaveLayout()
    {
        if (layoutPath is null || restoringLayout || layoutClosing) return;
        preferences = preferences with { BrowserHidden = FocusButton.IsChecked, BrowserFloating = browserWindow is not null,
            GraphFloating = graphWindow is not null, BrowserWidth = browserWidth, CurveWidth = curveWidth, TableFraction = tableFraction, FitTable = fitTable,
            NavigationCollapsed = navigationCollapsed?.Invoke() ?? true, View = ViewMode,
            BrowserBounds = Bounds(browserWindow, preferences.BrowserBounds), GraphBounds = Bounds(graphWindow, preferences.GraphBounds) };
        try { preferences.Save(layoutPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { EditorStatus.ToolTip = "Layout settings could not be saved: " + error.Message; }
    }
    internal void SaveNavigationLayout()
    {
        if (!restoringLayout) { SaveLayout(); return; }
        if (layoutPath is null || layoutClosing) return;
        preferences = preferences with { NavigationCollapsed = navigationCollapsed?.Invoke() ?? true };
        try { preferences.Save(layoutPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { EditorStatus.ToolTip = "Layout settings could not be saved: " + error.Message; }
    }
    private static PanelBounds Bounds(Window? window, PanelBounds fallback) => window is null ? fallback : new(window.Left, window.Top, window.Width, window.Height);
    internal void PrepareHostClose()
    {
        SaveLayout(); layoutClosing = true;
        browserWindow?.Close(); graphWindow?.Close(); infoWindow?.Close();
    }
    private Window Floating(string title, double width, double height, PanelBounds? bounds = null)
    {
        var owner = Window.GetWindow(this);
        var result = new Window { Owner = owner, Title = title, Width = width, Height = height, MinWidth = 260, MinHeight = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        result.SetResourceReference(StyleProperty, typeof(Window));
        if (bounds?.Left is double left && bounds.Top is double top)
        {
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (screen.IntersectsWith(new Rect(left, top, Math.Min(100, bounds.Width), 40)))
            { result.WindowStartupLocation = WindowStartupLocation.Manual; result.Left = left; result.Top = top; result.Width = bounds.Width; result.Height = bounds.Height; }
        }
        return result;
    }
    private void Layout_Changed(object sender, RoutedEventArgs e)
    {
        if (BrowserColumn is null || DetailsPanel is null || FocusButton is null || ChangesButton is null) return;
        ApplyLayout(); SaveLayout();
    }
    private void ApplyLayout()
    {
        bool hidden = FocusButton.IsChecked;
        BrowserColumn.Width = new(hidden || browserWindow is not null ? 0 : browserWidth);
        BrowserDivider.Width = new(hidden || browserWindow is not null ? 0 : 5);
        BrowserPanel.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        BrowserSplitter.Visibility = hidden || browserWindow is not null ? Visibility.Collapsed : Visibility.Visible;
        if (browserWindow is not null) { if (hidden) browserWindow.Hide(); else if (!browserWindow.IsVisible) browserWindow.Show(); }
        bool changes = ChangesButton.IsChecked; ChangesColumn.Width = new(changes ? 220 : 0); ChangesDivider.Width = new(changes ? 5 : 0);
        ChangesPanel.Visibility = ChangesSplitter.Visibility = changes ? Visibility.Visible : Visibility.Collapsed;
        if (DetailsButton.IsChecked && infoWindow is null)
        {
            var window = Floating("SpeedLink · Map information", 470, 270); infoWindow = window;
            EditorLayout.Children.Remove(DetailsPanel); DetailsPanel.Visibility = Visibility.Visible; window.Content = DetailsPanel;
            window.Closed += (_, _) => { window.Content = null; EditorLayout.Children.Add(DetailsPanel); DetailsPanel.Visibility = Visibility.Collapsed; infoWindow = null; DetailsButton.IsChecked = false; };
            window.Show();
        }
        else if (!DetailsButton.IsChecked && infoWindow is not null) infoWindow.Close();
        ApplyView();
    }
    internal void SetViewMode(int mode)
    {
        ViewMode = Math.Clamp(mode, 0, 3);
        if (DataLayout is null) return;
        BelowItem.IsChecked = ViewMode == 0; BesideItem.IsChecked = ViewMode == 1; TableItem.IsChecked = ViewMode == 2; GraphItem.IsChecked = ViewMode == 3;
        ApplyView(); SaveLayout();
    }
    private void ApplyView()
    {
        bool floating = graphWindow is not null, beside = ViewMode == 1 && !floating, graphOnly = ViewMode == 3 && !floating, below = ViewMode == 0 && !floating;
        Grid.SetRow(GraphPanel, beside || graphOnly ? 0 : 2); Grid.SetColumn(GraphPanel, beside ? 2 : 0);
        TableRow.Height = new(below ? tableFraction : 1, GridUnitType.Star); GraphRow.Height = below ? new(1 - tableFraction, GridUnitType.Star) : new(0);
        DataDividerRow.Height = new(below ? 5 : 0); DataDividerColumn.Width = new(beside ? 5 : 0);
        TableColumn.Width = beside && CurveList ? new(Math.Min(curveWidth, Math.Max(180, DataLayout.ActualWidth - 200))) : new(beside ? tableFraction : 1, GridUnitType.Star);
        GraphColumn.Width = beside ? new(1 - tableFraction, GridUnitType.Star) : new(0);
        MapGrid.Visibility = graphOnly ? Visibility.Collapsed : Visibility.Visible; GraphPanel.Visibility = floating || ViewMode != 2 ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(DataSplitter, beside ? 0 : 1); Grid.SetColumn(DataSplitter, beside ? 1 : 0);
        DataSplitter.Width = beside ? 5 : double.NaN; DataSplitter.Height = beside ? double.NaN : 5;
        DataSplitter.HorizontalAlignment = beside ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        DataSplitter.VerticalAlignment = beside ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        DataSplitter.ResizeDirection = beside ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        DataSplitter.Visibility = below || beside ? Visibility.Visible : Visibility.Collapsed;
        UpdateTableSizing();
    }
    private void ViewMenu_Click(object sender, RoutedEventArgs e) { if (sender is MenuItem { Tag: string mode }) { fitTable = false; SetViewMode(int.Parse(mode, System.Globalization.CultureInfo.InvariantCulture)); } }
    private void FitMap_Click(object sender, RoutedEventArgs e)
    {
        FocusButton.IsChecked = true; ChangesButton.IsChecked = false; DetailsButton.IsChecked = false; fitTable = true;
        SetViewMode(CurveList ? 1 : 2); Graph.ResetView();
        Dispatcher.BeginInvoke(new Action(() => { UpdateLayout(); UpdateTableSizing(); SaveLayout(); }));
    }
    private void HideBrowser_Click(object sender, RoutedEventArgs e) => FocusButton.IsChecked = true;
    private void PopBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (browserWindow is not null) { FocusButton.IsChecked = false; browserWindow.Activate(); return; }
        var window = Floating("SpeedLink · Maps", 340, 650, preferences.BrowserBounds); browserWindow = window;
        EditorLayout.Children.Remove(BrowserPanel); window.Content = BrowserPanel;
        window.Closed += (_, _) =>
        {
            preferences = preferences with { BrowserBounds = Bounds(window, preferences.BrowserBounds) };
            window.Content = null; EditorLayout.Children.Add(BrowserPanel); browserWindow = null; FocusButton.IsChecked = false; ApplyLayout(); SaveLayout();
        };
        FocusButton.IsChecked = false; ApplyLayout(); window.Show(); SaveLayout();
    }
    private void DockBrowser_Click(object sender, RoutedEventArgs e) { browserWindow?.Close(); FocusButton.IsChecked = false; }
    private void PopGraph_Click(object sender, RoutedEventArgs e)
    {
        if (graphWindow is not null) { graphWindow.Activate(); return; }
        var window = Floating("SpeedLink · Graph", 700, 520, preferences.GraphBounds); graphWindow = window;
        DataLayout.Children.Remove(GraphPanel); window.Content = GraphPanel;
        window.Closed += (_, _) =>
        {
            preferences = preferences with { GraphBounds = Bounds(window, preferences.GraphBounds) };
            window.Content = null; DataLayout.Children.Add(GraphPanel); graphWindow = null; SetViewMode(0); ApplyLayout(); SaveLayout();
        };
        ApplyView(); window.Show(); SaveLayout();
    }
    private void DockGraph_Click(object sender, RoutedEventArgs e) => graphWindow?.Close();
    private void LayoutSplitter_Completed(object sender, DragCompletedEventArgs e)
    {
        if (ReferenceEquals(sender, BrowserSplitter)) browserWidth = Math.Clamp(BrowserColumn.ActualWidth, 160, 480);
        else if (ViewMode == 0) tableFraction = Math.Clamp(TableRow.ActualHeight / Math.Max(1, TableRow.ActualHeight + GraphRow.ActualHeight), .2, .8);
        else if (ViewMode == 1 && CurveList) curveWidth = Math.Clamp(TableColumn.ActualWidth, 180, 600);
        else if (ViewMode == 1) tableFraction = Math.Clamp(TableColumn.ActualWidth / Math.Max(1, TableColumn.ActualWidth + GraphColumn.ActualWidth), .2, .8);
        ApplyLayout(); SaveLayout();
    }
    private void DataLayout_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTableSizing();
    private void UpdateTableSizing()
    {
        if (table is null || MapGrid.ActualWidth < 10 || MapGrid.Columns.Count == 0) return;
        if (CurveList)
        { MapGrid.Columns[0].Width = new(1, DataGridLengthUnitType.Star); MapGrid.Columns[1].Width = new(1, DataGridLengthUnitType.Star); MapGrid.RowHeight = 25; return; }
        var size = TableFit.Calculate(MapGrid.ActualWidth, MapGrid.ActualHeight, table.Columns, table.Rows, MapGrid.RowHeaderWidth);
        foreach (var column in MapGrid.Columns) column.Width = table.Columns == 1 ? new(1, DataGridLengthUnitType.Star) : new(size.ColumnWidth);
        MapGrid.RowHeight = fitTable ? size.RowHeight : 25;
    }
}
