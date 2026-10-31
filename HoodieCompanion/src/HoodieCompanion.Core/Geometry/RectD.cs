namespace HoodieCompanion.Geometry;

/// <summary>Axis-aligned rectangle in virtual-desktop pixels.</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public Vec2 Center => new(X + Width / 2, Y + Height / 2);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static RectD FromEdges(double left, double top, double right, double bottom) =>
        new(Math.Min(left, right), Math.Min(top, bottom), Math.Abs(right - left), Math.Abs(bottom - top));

    public bool Contains(Vec2 p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    public bool ContainsX(double x) => x >= Left && x < Right;

    public bool Intersects(RectD o) => o.Left < Right && o.Right > Left && o.Top < Bottom && o.Bottom > Top;

    public RectD Inflate(double dx, double dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    public Vec2 Clamp(Vec2 p) => new(Math.Clamp(p.X, Left, Math.Max(Left, Right - 1)), Math.Clamp(p.Y, Top, Math.Max(Top, Bottom - 1)));

    public double DistanceTo(Vec2 p)
    {
        var dx = Math.Max(Math.Max(Left - p.X, 0), p.X - Right);
        var dy = Math.Max(Math.Max(Top - p.Y, 0), p.Y - Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() => $"[{X:0},{Y:0} {Width:0}x{Height:0}]";
}
