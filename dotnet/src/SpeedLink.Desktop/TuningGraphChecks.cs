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
}
