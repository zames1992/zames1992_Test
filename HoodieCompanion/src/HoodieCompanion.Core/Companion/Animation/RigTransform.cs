using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Animation;

/// <summary>
/// Placement of the rig in the world:
/// world = AnchorWorld + Rotate(Tilt) * Scale(RefToPx) * (Mirror(local) - Mirror(AnchorLocal)).
/// Mirror flips around the rig's vertical axis (x = 272) when Hoodie faces right.
/// The WPF renderer builds the identical matrix.
/// </summary>
public readonly record struct RigTransform(Vec2 AnchorWorld, Vec2 AnchorLocal, double Tilt, int Facing, double RefToPx)
{
    public const double AxisX = 272;

    public static Vec2 Mirror(Vec2 local, int facing) => facing > 0 ? new Vec2(2 * AxisX - local.X, local.Y) : local;

    public Vec2 LocalToWorld(Vec2 local)
    {
        var v = (Mirror(local, Facing) - Mirror(AnchorLocal, Facing)) * RefToPx;
        return AnchorWorld + v.Rotate(Tilt);
    }

    public Vec2 WorldToLocal(Vec2 world)
    {
        var v = (world - AnchorWorld).Rotate(-Tilt) / RefToPx;
        return Mirror(v + Mirror(AnchorLocal, Facing), Facing);
    }

    /// <summary>Axis-aligned world bounds of a local rectangle.</summary>
    public RectD Bounds(RectD local)
    {
        var a = LocalToWorld(new Vec2(local.Left, local.Top));
        var b = LocalToWorld(new Vec2(local.Right, local.Top));
        var c = LocalToWorld(new Vec2(local.Left, local.Bottom));
        var d = LocalToWorld(new Vec2(local.Right, local.Bottom));
        var l = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        var t = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        var r = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        var btm = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        return RectD.FromEdges(l, t, r, btm);
    }

    /// <summary>The character's body in reference pixels (without the ground shadow).</summary>
    public static readonly RectD BodyLocal = RectD.FromEdges(104, 70, 454, 836);
}
