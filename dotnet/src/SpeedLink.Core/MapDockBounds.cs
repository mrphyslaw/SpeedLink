namespace SpeedLink.Core;

[Flags]
public enum MapResizeEdge { Left = 1, Top = 2, Right = 4, Bottom = 8 }

/// <summary>Workspace coordinates only; independent of ROM contents and native window bounds.</summary>
public sealed record MapDockBounds(double Left, double Top, double Width, double Height)
{
    public MapDockBounds? Validate() => new[] { Left, Top, Width, Height }.All(double.IsFinite)
        ? new(Math.Clamp(Left, 0, 32_000), Math.Clamp(Top, 0, 32_000), Math.Clamp(Width, 220, 16_000), Math.Clamp(Height, 100, 16_000)) : null;

    public MapDockBounds Move(double dx, double dy) => double.IsFinite(dx) && double.IsFinite(dy)
        ? this with { Left = Math.Clamp(Left + dx, 0, 32_000), Top = Math.Clamp(Top + dy, 0, 32_000) } : this;

    public MapDockBounds Resize(MapResizeEdge edges, double dx, double dy, double minimumWidth, double minimumHeight)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return this;
        double left = Left, top = Top, right = Left + Width, bottom = Top + Height;
        if (edges.HasFlag(MapResizeEdge.Left))
        {
            left = Math.Clamp(Left + dx, 0, Math.Min(32_000, right - minimumWidth));
        }
        if (edges.HasFlag(MapResizeEdge.Right))
        {
            right = Math.Clamp(right + dx, left + minimumWidth, left + 16_000);
        }
        if (edges.HasFlag(MapResizeEdge.Top))
        {
            top = Math.Clamp(Top + dy, 0, Math.Min(32_000, bottom - minimumHeight));
        }
        if (edges.HasFlag(MapResizeEdge.Bottom))
        {
            bottom = Math.Clamp(bottom + dy, top + minimumHeight, top + 16_000);
        }
        return new(left, top, right - left, bottom - top);
    }
}
