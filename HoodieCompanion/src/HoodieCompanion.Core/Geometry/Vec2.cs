namespace HoodieCompanion.Geometry;

/// <summary>2D vector used for world-space (virtual desktop pixel) math.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        var len = Length;
        return len < 1e-9 ? Zero : new Vec2(X / len, Y / len);
    }

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

    /// <summary>Rotates the vector by <paramref name="degrees"/> (clockwise on screen, y down).</summary>
    public Vec2 Rotate(double degrees)
    {
        var r = degrees * Math.PI / 180.0;
        var c = Math.Cos(r);
        var s = Math.Sin(r);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public override string ToString() => $"({X:0.#}, {Y:0.#})";
}
