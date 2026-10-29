using System.Text.Json;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class MapWorkspaceTests
{
    [Theory]
    [InlineData(.85)]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void Auto_fit_never_crosses_measured_readability_bounds(double zoom)
    {
        double font = 13 * zoom, text = 67 * zoom;
        foreach (int columns in new[] { 1, 4, 16, 80 }) foreach (int rows in new[] { 1, 8, 16, 40 })
            foreach (double width in new[] { 0, 100, 380, 950, 2400 }) foreach (double height in new[] { 0, 90, 430, 900 })
            {
                var fit = ReadableTableFit.Calculate(width, height, columns, rows, 64, 26, text, font, zoom, true);
                Assert.True(fit.ColumnWidth >= text + 14 * zoom); Assert.True(fit.RowHeight >= font + 10);
                if (fit.Fits) { Assert.True(fit.ColumnWidth * columns + 68 <= width); Assert.True(fit.RowHeight * rows + 30 <= height); }
            }
    }
    [Fact]
    public void Fixed_spacing_ignores_window_size_but_honors_long_values_and_zoom()
    {
        var narrow = ReadableTableFit.Calculate(120, 90, 16, 16, 64, 26, 80, 13, 1, false);
        var wide = ReadableTableFit.Calculate(2400, 1800, 16, 16, 64, 26, 80, 13, 1, false);
        Assert.Equal(narrow.ColumnWidth, wide.ColumnWidth); Assert.Equal(narrow.RowHeight, wide.RowHeight);
        Assert.True(narrow.ColumnWidth >= 94); Assert.False(narrow.Fits); Assert.True(wide.Fits);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadableTableFit.Calculate(double.NaN, 300, 16, 16, 64, 26, 30, 13, 1, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadableTableFit.Calculate(500, 300, 16, 16, 64, 26, 30, 9, 1, true));
    }
    [Fact]
    public void Workspace_roundtrip_keeps_profiles_separate_without_rom_data()
    {
        using var temp = new TempFolder(); string path = Path.Combine(temp.Path, "maps.json");
        var maps = new[] { new MapPanePreferences { Key = "20000:Timing", Zoom = 1.25, Selection = [1, 18], Floating = true, Bounds = new(-800, 20, 900, 700), CurveStart = .2, CurveEnd = .6 } };
        var profiles = new[] { new MapWorkspacePreferences { Calibration = "L38KEH000", Maps = maps, Active = maps[0].Key, ColumnFraction = .65 }, new MapWorkspacePreferences { Calibration = "L3CGEE000", Maps = [maps[0] with { Zoom = .9 }] } };
        MapWorkspacePreferences.Save(path, profiles);
        var saved = MapWorkspacePreferences.Load(path);
        Assert.Equal(2, saved.Length); Assert.Equal(1.25, saved[0].Maps[0].Zoom); Assert.Equal(.9, saved[1].Maps[0].Zoom);
        Assert.Equal(new[] { 1, 18 }, saved[0].Maps[0].Selection); Assert.Equal(-800, saved[0].Maps[0].Bounds.Left);
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.partial"));
        var json = File.ReadAllText(path); Assert.DoesNotContain("SourcePath", json); Assert.DoesNotContain("ROM bytes", json);
    }
    [Fact]
    public void Invalid_and_oversized_workspace_settings_recover_and_are_bounded()
    {
        using var temp = new TempFolder(); string path = Path.Combine(temp.Path, "maps.json");
        foreach (string invalid in new[] { "null", "{", "[null]", new string(' ', 262145), "[{\"Calibration\":\"../../wrong\"}]" })
        { File.WriteAllText(path, invalid); Assert.Empty(MapWorkspacePreferences.Load(path)); }
        var pane = new MapPanePreferences { Key = "map", Zoom = double.NaN, View = 99, Baseline = 2, Selection = [-1, 3, 3], HorizontalOffset = double.PositiveInfinity, Bounds = null! }.Validate();
        Assert.Equal(1, pane.Zoom); Assert.Equal(4, pane.View); Assert.Equal(1, pane.Baseline); Assert.Equal(new[] { 3 }, pane.Selection); Assert.Equal(0, pane.HorizontalOffset); Assert.NotNull(pane.Bounds);
        var work = new MapWorkspacePreferences { Calibration = "L3CGEE000", ColumnFraction = double.NaN, RowFraction = 9, Maps = Enumerable.Range(0, 60).Select(i => pane with { Key = i.ToString() }).ToArray() }.Validate();
        Assert.Equal(.5, work.ColumnFraction); Assert.Equal(.8, work.RowFraction); Assert.Equal(24, work.Maps.Length);
        File.WriteAllText(path, JsonSerializer.Serialize(new[] { work })); Assert.Equal(24, MapWorkspacePreferences.Load(path)[0].Maps.Length);
    }
}
