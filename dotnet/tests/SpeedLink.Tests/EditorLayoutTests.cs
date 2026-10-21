using System.Globalization;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class EditorLayoutTests
{
    [Fact]
    public void LayoutRoundTripsAndInvalidSettingsRecover()
    {
        using var folder = new TempFolder(); string path = Path.Combine(folder.Path, "layout.json");
        Assert.Equal(new EditorLayoutPreferences(), EditorLayoutPreferences.Load(path));
        var expected = new EditorLayoutPreferences { NavigationCollapsed = false, BrowserFloating = true, GraphFloating = true, BrowserHidden = true,
            BrowserWidth = 231, CurveWidth = 325, View = 1, TableFraction = .42, FitTable = true, GraphBounds = new(-800, 40, 900, 600) };
        expected.Save(path); Assert.Equal(expected, EditorLayoutPreferences.Load(path));
        (expected with { View = 2 }).Save(path); Assert.Equal(2, EditorLayoutPreferences.Load(path).View);
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.partial"));
        foreach (string corrupt in new[] { "{", "null", "{\"View\":\"bad\"}", new string(' ', 65537) })
        { File.WriteAllText(path, corrupt); Assert.Equal(new EditorLayoutPreferences(), EditorLayoutPreferences.Load(path)); }
        File.WriteAllText(path, "{\"BrowserBounds\":null,\"GraphBounds\":null,\"View\":99,\"BrowserWidth\":-100,\"CurveWidth\":900,\"TableFraction\":1}");
        var recovered = EditorLayoutPreferences.Load(path); Assert.Equal(3, recovered.View); Assert.Equal(160, recovered.BrowserWidth);
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
