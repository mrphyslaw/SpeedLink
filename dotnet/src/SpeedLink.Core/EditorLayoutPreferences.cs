using System.Text.Json;

namespace SpeedLink.Core;

public sealed record PanelBounds(double? Left = null, double? Top = null, double Width = 340, double Height = 650);
public sealed record EditorLayoutPreferences
{
    public bool NavigationCollapsed { get; init; } = true;
    public bool BrowserHidden { get; init; }
    public bool BrowserFloating { get; init; }
    public bool GraphFloating { get; init; }
    public double BrowserWidth { get; init; } = 200;
    public double CurveWidth { get; init; } = 300;
    public int View { get; init; }
    public double TableFraction { get; init; } = .6;
    public bool FitTable { get; init; }
    public PanelBounds BrowserBounds { get; init; } = new();
    public PanelBounds GraphBounds { get; init; } = new(Width: 700, Height: 520);
    public EditorLayoutPreferences Validate()
    {
        static PanelBounds Bounds(PanelBounds? p, PanelBounds fallback) => p is null ? fallback : new(
            p.Left is double x && double.IsFinite(x) ? x : null, p.Top is double y && double.IsFinite(y) ? y : null,
            double.IsFinite(p.Width) ? Math.Clamp(p.Width, 260, 1800) : fallback.Width,
            double.IsFinite(p.Height) ? Math.Clamp(p.Height, 200, 1400) : fallback.Height);
        return this with { BrowserWidth = double.IsFinite(BrowserWidth) ? Math.Clamp(BrowserWidth, 160, 480) : 200,
            CurveWidth = double.IsFinite(CurveWidth) ? Math.Clamp(CurveWidth, 180, 600) : 300,
            View = Math.Clamp(View, 0, 3), TableFraction = double.IsFinite(TableFraction) ? Math.Clamp(TableFraction, .2, .8) : .6,
            BrowserBounds = Bounds(BrowserBounds, new()), GraphBounds = Bounds(GraphBounds, new(Width: 700, Height: 520)) };
    }
    public static EditorLayoutPreferences Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > 65536) return new();
            return (JsonSerializer.Deserialize<EditorLayoutPreferences>(stream, new JsonSerializerOptions { MaxDepth = 8 }) ?? new()).Validate();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string path)
    {
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, Validate()); stream.Flush(true); }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public readonly record struct TableFit(double ColumnWidth, double RowHeight, bool Fits)
{
    public static TableFit Calculate(double width, double height, int columns, int rows, double rowHeader = 64)
    {
        if (columns < 1 || rows < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        double cell = Math.Max(44, Math.Min(90, Math.Floor((width - rowHeader - 4) / columns)));
        double row = Math.Max(22, Math.Min(28, Math.Floor((height - 30) / rows)));
        return new(cell, row, columns * cell + rowHeader + 4 <= width && rows * row + 30 <= height);
    }
}
