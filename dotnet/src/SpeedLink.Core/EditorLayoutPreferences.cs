using System.Text.Json;

namespace SpeedLink.Core;

public sealed record PanelBounds(double? Left = null, double? Top = null, double Width = 340, double Height = 650);
public sealed record EditorLayoutPreferences
{
    public bool NavigationCollapsed { get; init; }
    public bool BrowserHidden { get; init; }
    public bool BrowserFloating { get; init; }
    public bool GraphFloating { get; init; }
    public double BrowserWidth { get; init; } = 200;
    public double CurveWidth { get; init; } = 300;
    public double? PreviewWidth { get; init; }
    public int View { get; init; } = 4;
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
            PreviewWidth = PreviewWidth is double preview && double.IsFinite(preview) ? Math.Clamp(preview, 160, 800) : null,
            View = Math.Clamp(View, 0, 4), TableFraction = double.IsFinite(TableFraction) ? Math.Clamp(TableFraction, .2, .8) : .6,
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

public readonly record struct TablePreviewSize(double Width, double Height)
{
    public static TablePreviewSize Calculate(double width, double height, int columns, double rowHeader = 64, double? requestedWidth = null, double minimumCellWidth = 44)
    {
        if (columns < 1 || !double.IsFinite(minimumCellWidth) || minimumCellWidth < 0) throw new ArgumentOutOfRangeException(nameof(columns));
        double automatic = Math.Clamp(width - (columns * minimumCellWidth + rowHeader + 4) - 7, 160, 224);
        double previewWidth = requestedWidth is double requested && double.IsFinite(requested)
            ? Math.Clamp(requested, 160, Math.Max(160, Math.Min(800, width - 207))) : automatic;
        return new(previewWidth, Math.Max(0, Math.Min(height, Math.Clamp(previewWidth * .8 + 28, 156, requestedWidth is null ? 208 : 480))));
    }
}

/// <summary>Keep small maps proportional to their data instead of filling an empty viewport.</summary>
public readonly record struct CompactMapSize(double Width, double Height)
{
    public static CompactMapSize? Calculate(int columns, int rows, double rowHeader, double axisWidth, double axisHeight, bool choice = false)
    {
        if (columns < 1 || rows < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        if (columns * (long)rows == 1 || columns > 8 || rows > 8) return null;
        double cell = columns <= 4 ? 112 : 88;
        return new(axisWidth + rowHeader + (choice ? Math.Max(200, columns * cell) : columns * cell) + 4,
            axisHeight + 26 + rows * 28 + 4);
    }
}
