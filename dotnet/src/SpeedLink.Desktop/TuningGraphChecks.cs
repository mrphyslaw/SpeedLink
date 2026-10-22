using System.Windows;
using System.Windows.Media.Media3D;

namespace SpeedLink.Desktop;

public sealed partial class TuningGraph
{
    internal void CheckSurfaceDrag()
    {
        if (surface?.Geometry is not MeshGeometry3D mesh || viewport.Children.Count == 0)
            throw new InvalidOperationException("Surface drag check needs a rendered map.");
        ResetView();
        var bounds = mesh.Bounds;
        var center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        var towardViewer = -camera.LookDirection; towardViewer.Y = 0; towardViewer.Normalize();
        var front = center + towardViewer * .6;
        Point ProjectedOffset()
        {
            var projection = viewport.Children[0].TransformToAncestor(viewport);
            var a = projection.Transform(front); var b = projection.Transform(center);
            return new(a.X - b.X, a.Y - b.Y);
        }
        foreach (var movement in new[] { new Vector(-20, 0), new Vector(20, 0), new Vector(0, -20), new Vector(0, 20) })
        {
            ResetView(); Point before = ProjectedOffset(); RotateSurface(movement); Point after = ProjectedOffset();
            double delta = movement.X != 0 ? after.X - before.X : after.Y - before.Y;
            double expected = movement.X != 0 ? movement.X : movement.Y;
            if (!double.IsFinite(delta) || delta * expected <= 0)
                throw new InvalidOperationException($"Surface did not follow drag {movement}: projected movement {delta}.");
        }
        RotateSurface(new(0, 10000)); if (elevation != 80) throw new InvalidOperationException("Upper tilt limit failed.");
        RotateSurface(new(0, -10000)); if (elevation != 10) throw new InvalidOperationException("Lower tilt limit failed.");
        ResetView();
    }
    internal void CheckSurfaceAxes()
    {
        if (table is null || table.Rows <= 1 || !axes.IsVisible || viewport.ActualWidth <= 0)
            throw new InvalidOperationException("The surface axis guide is missing.");
        string labels = axes.Labels;
        if (!labels.Contains(table.X.Name, StringComparison.Ordinal) ||
            !labels.Contains(table.Y.Name, StringComparison.Ordinal) ||
            !labels.Contains(table.Units, StringComparison.Ordinal))
            throw new InvalidOperationException("Surface axis names do not match the table.");
        double savedYaw = yaw, savedElevation = elevation;
        try
        {
            foreach (double angle in new[] { 0d, 40, 90, 180, 270 })
            foreach (double tilt in new[] { 10d, 32, 80 })
            {
                yaw = angle; elevation = tilt; Camera();
                var projection = viewport.Children[0].TransformToAncestor(viewport);
                var origin = projection.Transform(new Point3D());
                foreach (var expected in new[] { ("X", new Vector3D(1, 0, 0)), ("Y", new Vector3D(0, 0, 1)), ("Z", new Vector3D(0, 1, 0)) })
                {
                    var tip = projection.Transform(new Point3D() + expected.Item2);
                    Vector projected = (tip - origin) * (camera.Width / viewport.ActualWidth);
                    Vector guide = (axes.Project(new Point3D() + expected.Item2) - axes.Project(new Point3D())) * (camera.Width / axes.ActualWidth);
                    if (!double.IsFinite(guide.X) || !double.IsFinite(guide.Y) || (guide - projected).Length > .000001)
                        throw new InvalidOperationException("Axis guide does not follow the surface camera: " + expected.Item1);
                }
            }
        }
        finally { yaw = savedYaw; elevation = savedElevation; Camera(); }
    }
}
