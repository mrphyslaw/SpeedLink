using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class MapDockBoundsTests
{
    [Fact]
    public void A_lower_map_grows_across_empty_space_without_changing_upper_maps()
    {
        MapDockBounds[] upper = [new(0, 0, 300, 150), new(307, 0, 300, 150)];
        var bottom = new MapDockBounds(0, 157, 300, 260);
        Assert.Equal(new(0, 157, 607, 260), bottom.Resize(MapResizeEdge.Right, 307, 0, 260, 260));
        Assert.Equal(new(0, 0, 300, 150), upper[0]); Assert.Equal(new(307, 0, 300, 150), upper[1]);
    }
    [Theory]
    [InlineData(MapResizeEdge.Left, -100, 0, 0, 100, 400, 300)]
    [InlineData(MapResizeEdge.Top, 0, -100, 100, 0, 300, 400)]
    [InlineData(MapResizeEdge.Right, 100, 0, 100, 100, 400, 300)]
    [InlineData(MapResizeEdge.Bottom, 0, 100, 100, 100, 300, 400)]
    [InlineData(MapResizeEdge.Left | MapResizeEdge.Top, -100, -100, 0, 0, 400, 400)]
    [InlineData(MapResizeEdge.Right | MapResizeEdge.Top, 100, -100, 100, 0, 400, 400)]
    [InlineData(MapResizeEdge.Left | MapResizeEdge.Bottom, -100, 100, 0, 100, 400, 400)]
    [InlineData(MapResizeEdge.Right | MapResizeEdge.Bottom, 100, 100, 100, 100, 400, 400)]
    public void Each_edge_and_corner_changes_only_its_requested_dimensions(MapResizeEdge edges, double dx, double dy, double x, double y, double width, double height)
        => Assert.Equal(new(x, y, width, height), new MapDockBounds(100, 100, 300, 300).Resize(edges, dx, dy, 220, 100));

    [Fact]
    public void Manual_growth_allows_overlapping_windows_without_reallocating_neighbors()
    {
        var current = new MapDockBounds(300, 300, 300, 300);
        var neighbor = new MapDockBounds(650, 300, 250, 300);
        Assert.Equal(new(0, 300, 600, 300), current.Resize(MapResizeEdge.Left, -5000, 0, 220, 100));
        Assert.Equal(new(300, 300, 5300, 300), current.Resize(MapResizeEdge.Right, 5000, 0, 220, 100));
        Assert.Equal(new(300, 0, 300, 600), current.Resize(MapResizeEdge.Top, 0, -5000, 220, 100));
        Assert.Equal(new(300, 300, 300, 5300), current.Resize(MapResizeEdge.Bottom, 0, 5000, 220, 100));
        Assert.Equal(new(300, 300, 5300, 5300), current.Resize(MapResizeEdge.Right | MapResizeEdge.Bottom, 5000, 5000, 220, 100));
        Assert.Equal(new(650, 300, 250, 300), neighbor);
    }
    [Fact]
    public void Moving_a_pane_preserves_its_size_and_stays_in_reachable_workspace_coordinates()
    {
        var current = new MapDockBounds(400, 400, 300, 300);
        Assert.Equal(new(150, 200, 300, 300), current.Move(-250, -200));
        Assert.Equal(new(0, 32_000, 300, 300), current.Move(-100_000, 100_000));
        Assert.Equal(current, current.Move(double.NaN, 0));
    }
    [Fact]
    public void Resizing_preserves_readable_minimums_and_rejects_nonfinite_changes()
    {
        var current = new MapDockBounds(100, 100, 300, 300);
        Assert.Equal(new(100, 100, 260, 260), current.Resize(MapResizeEdge.Right | MapResizeEdge.Bottom, -500, -500, 260, 260));
        Assert.Equal(new(140, 140, 260, 260), current.Resize(MapResizeEdge.Left | MapResizeEdge.Top, 500, 500, 260, 260));
        Assert.Equal(current, current.Resize(MapResizeEdge.Right, double.NaN, 0, 220, 100));
        Assert.Equal(current, current.Resize(MapResizeEdge.Bottom, 0, double.PositiveInfinity, 220, 100));
        Assert.Null(new MapDockBounds(0, 0, double.NaN, 300).Validate());
        Assert.Equal(new(0, 32_000, 220, 16_000), new MapDockBounds(-20, 100_000, 1, 100_000).Validate());
    }
    [Fact]
    public void Saved_independent_geometry_migrates_without_affecting_other_calibrations()
    {
        using var temp = new TempFolder(); string path = Path.Combine(temp.Path, "maps.json");
        File.WriteAllText(path, "[{\"Calibration\":\"L38KEH000\",\"Maps\":[{\"Key\":\"old\"}]}]");
        Assert.Null(MapWorkspacePreferences.Load(path).Single().Maps.Single().DockBounds);
        var bounds = new MapDockBounds(307, 157, 600, 350);
        MapWorkspacePreferences.Save(path, [new() { Calibration = "L38KEH000", Maps = [new() { Key = "new", DockBounds = bounds, ZOrder = 2 }] }, new() { Calibration = "L3CGEE000", Maps = [new() { Key = "other" }] }]);
        var saved = MapWorkspacePreferences.Load(path);
        Assert.Equal(bounds, saved[0].Maps.Single().DockBounds); Assert.Null(saved[1].Maps.Single().DockBounds);
        Assert.Equal(2, saved[0].Maps.Single().ZOrder); Assert.Equal(23, new MapPanePreferences { ZOrder = int.MaxValue }.Validate().ZOrder);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.partial"));
        Assert.DoesNotContain("SourcePath", File.ReadAllText(path));
    }
}
