using System.Globalization;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class EditorLayoutTests
{
    [Fact]
    public void NavigationLabelsAreTheDefaultAndExplicitChoicesSurvive()
    {
        using var folder = new TempFolder(); string path = Path.Combine(folder.Path, "layout.json");
        Assert.False(EditorLayoutPreferences.Load(path).NavigationCollapsed);
        File.WriteAllText(path, "{\"View\":4}");
        Assert.False(EditorLayoutPreferences.Load(path).NavigationCollapsed);
        foreach (bool collapsed in new[] { true, false })
        {
            new EditorLayoutPreferences { NavigationCollapsed = collapsed }.Save(path);
            Assert.Equal(collapsed, EditorLayoutPreferences.Load(path).NavigationCollapsed);
        }
    }
    [Fact]
    public void LayoutRoundTripsAndInvalidSettingsRecover()
    {
        using var folder = new TempFolder(); string path = Path.Combine(folder.Path, "layout.json");
        Assert.Equal(new EditorLayoutPreferences(), EditorLayoutPreferences.Load(path));
        var expected = new EditorLayoutPreferences { NavigationCollapsed = false, BrowserFloating = true, GraphFloating = true, BrowserHidden = true,
            BrowserWidth = 231, CurveWidth = 325, PreviewWidth = 390, View = 1, TableFraction = .42, FitTable = true, GraphBounds = new(-800, 40, 900, 600) };
        expected.Save(path); Assert.Equal(expected, EditorLayoutPreferences.Load(path));
        (expected with { View = 2 }).Save(path); Assert.Equal(2, EditorLayoutPreferences.Load(path).View);
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.partial"));
        foreach (string corrupt in new[] { "{", "null", "{\"View\":\"bad\"}", new string(' ', 65537) })
        { File.WriteAllText(path, corrupt); Assert.Equal(new EditorLayoutPreferences(), EditorLayoutPreferences.Load(path)); }
        File.WriteAllText(path, "{\"BrowserBounds\":null,\"GraphBounds\":null,\"View\":99,\"BrowserWidth\":-100,\"CurveWidth\":900,\"TableFraction\":1}");
        var recovered = EditorLayoutPreferences.Load(path); Assert.Equal(4, recovered.View); Assert.Equal(160, recovered.BrowserWidth);
        Assert.Equal(600, recovered.CurveWidth); Assert.Equal(.8, recovered.TableFraction); Assert.Equal(new PanelBounds(), recovered.BrowserBounds);
        var nonfinite = (expected with { BrowserWidth = double.NaN, CurveWidth = double.PositiveInfinity, TableFraction = double.NaN, GraphBounds = new(double.NaN, double.PositiveInfinity, double.NaN, double.NegativeInfinity) }).Validate();
        Assert.Equal(200, nonfinite.BrowserWidth); Assert.Equal(300, nonfinite.CurveWidth); Assert.Null(nonfinite.GraphBounds.Left); Assert.Null(nonfinite.GraphBounds.Top);
    }
    [Theory]
    [InlineData(940, 443)]
    [InlineData(1196, 463)]
    public void Full16SquareKeepsLegibleCells(double width, double height)
    {
        var fit = TableFit.Calculate(width, height, 16, 16);
        Assert.True(fit.Fits); Assert.InRange(fit.ColumnWidth, 44, 90); Assert.InRange(fit.RowHeight, 22, 28);
        Assert.True(fit.ColumnWidth * 16 + 68 <= width); Assert.True(fit.RowHeight * 16 + 30 <= height);
        Assert.False(TableFit.Calculate(600, 300, 16, 16).Fits);
    }
    [Theory]
    [InlineData(940, 443)]
    [InlineData(1196, 463)]
    public void PreviewLeavesRoomForAll16RowsAndColumns(double width, double height)
    {
        var preview = TablePreviewSize.Calculate(width, height, 16);
        var table = TableFit.Calculate(width - preview.Width - 5, height, 16, 16);
        Assert.True(table.Fits); Assert.InRange(preview.Width, 160, 224); Assert.InRange(preview.Height, 156, 208);
        Assert.True(preview.Height < height / 2);
        using var folder = new TempFolder(); string path = Path.Combine(folder.Path, "preview.json");
        new EditorLayoutPreferences { View = 4, FitTable = true }.Save(path);
        Assert.Equal(4, EditorLayoutPreferences.Load(path).View);
    }
    [Fact]
    public void PreviewWidthIsBoundedWithoutLosingTheSavedPreference()
    {
        var wide = TablePreviewSize.Calculate(1100, 700, 16, requestedWidth: 440);
        var narrow = TablePreviewSize.Calculate(550, 300, 16, requestedWidth: 440);
        Assert.Equal(440, wide.Width); Assert.Equal(343, narrow.Width);
        Assert.InRange(wide.Height, 156, 480); Assert.InRange(narrow.Height, 0, 300);
        Assert.Null((new EditorLayoutPreferences { PreviewWidth = double.NaN }).Validate().PreviewWidth);
        Assert.Equal(160, (new EditorLayoutPreferences { PreviewWidth = -20 }).Validate().PreviewWidth);
        Assert.Equal(800, (new EditorLayoutPreferences { PreviewWidth = 1200 }).Validate().PreviewWidth);
        using var folder = new TempFolder(); string path = Path.Combine(folder.Path, "layout.json");
        new EditorLayoutPreferences { PreviewWidth = 440 }.Save(path);
        Assert.Equal(440, EditorLayoutPreferences.Load(path).PreviewWidth);
        File.WriteAllText(path, "{\"View\":4}"); Assert.Null(EditorLayoutPreferences.Load(path).PreviewWidth);
    }
    [Theory]
    [InlineData(2, 1, 292, 79)]
    [InlineData(1, 2, 180, 107)]
    [InlineData(2, 2, 292, 107)]
    [InlineData(4, 4, 516, 163)]
    [InlineData(8, 1, 772, 79)]
    public void SmallMapsUseTheirNaturalSize(int columns, int rows, double width, double height)
    {
        var size = CompactMapSize.Calculate(columns, rows, 64, 0, 21)!.Value;
        Assert.Equal(width, size.Width); Assert.Equal(height, size.Height);
        Assert.Null(CompactMapSize.Calculate(1, 1, 64, 0, 0));
        Assert.Null(CompactMapSize.Calculate(16, 16, 64, 20, 21));
        Assert.Null(CompactMapSize.Calculate(129, 1, 36, 0, 0));
    }
    public static IEnumerable<object[]> Profiles => Fixture.Profiles.Select(p => new object[] { p.Calibration });
    [Theory, MemberData(nameof(Profiles))]
    public void LongCurvePastePreservesAxesAndTransactions(string calibration)
    {
        var p = Fixture.Profiles.Single(p => p.Calibration == calibration);
        var editor = new RomEditor(Fixture.Image(p), "original.bin", Fixture.Inspector(), Fixture.TuningGuard);
        var table = editor.Definition.Tables.First(t => t.Name.StartsWith("MAF Calibration", StringComparison.Ordinal) && t.Columns > 100);
        var axes = table.X.Values.ToArray(); var before = editor.Snapshot();
        string text = string.Join('\n', axes.Select((v, i) => v.ToString("R", CultureInfo.InvariantCulture) + "\t" + (i * 1.25).ToString("R", CultureInfo.InvariantCulture)));
        editor.PasteCurve(table, 0, text); Assert.Equal((table.Count - 1) * 1.25, editor.Read(table, table.Count - 1), 4);
        Assert.Equal(axes, table.X.Values); var pasted = editor.Snapshot();
        Assert.All(Enumerable.Range(0, before.Length).Where(i => before[i] != pasted[i]), i => Assert.InRange(i, table.Address, table.End - 1));
        editor.Undo(); Assert.Equal(before, editor.Snapshot()); editor.Redo(); Assert.Equal(pasted, editor.Snapshot());
        Assert.Throws<InvalidDataException>(() => editor.PasteCurve(table, 0, (axes[0] + .1).ToString("R", CultureInfo.InvariantCulture) + "\t12"));
        Assert.Equal(pasted, editor.Snapshot());
        Assert.Throws<ArgumentException>(() => editor.PasteCurve(table, table.Count - 1, "1\n2")); Assert.Equal(pasted, editor.Snapshot());
        Assert.Throws<InvalidDataException>(() => editor.PasteCurve(table, 0, "1\nNaN")); Assert.Equal(pasted, editor.Snapshot());
        editor.PasteCurve(table, table.Count - 2, "12.25\n13.5"); Assert.Equal(13.5, editor.Read(table, table.Count - 1), 5);
        editor.PasteCurve(table, 0, "1\t2\t3"); Assert.Equal(3, editor.Read(table, 2), 5);
        editor.Apply([new(table, 0, 19.091319625854492)], "Precise value"); var precise = editor.Snapshot();
        Assert.True(table.Compact(editor.Read(table, 0)).Length < editor.Read(table, 0).ToString("R", CultureInfo.InvariantCulture).Length);
        editor.Apply([new(table, 0, double.Parse(editor.Read(table, 0).ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture))], "No-op full precision edit");
        Assert.Equal(precise, editor.Snapshot());
        editor.Transform(table, [1, 2], TuningOperation.Multiply, 1.1); Assert.Equal(3.3, editor.Read(table, 2), 5);
        editor.Interpolate(table, 0, 0, 3, 0, "Horizontal");
        double x = (axes[1] - axes[0]) / (axes[3] - axes[0]);
        Assert.Equal(editor.Read(table, 0) * (1 - x) + editor.Read(table, 3) * x, editor.Read(table, 1), 5);
    }
}
