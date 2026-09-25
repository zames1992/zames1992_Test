using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Physics;

public readonly record struct LandingInfo(double ImpactDipPerSec, double HorizontalDipPerSec);

/// <summary>
/// Lightweight deterministic 2D physics for the airborne body (thrown, falling, jumping).
/// State is the visual centre of mass in virtual-desktop pixels, so a throw continues seamlessly
/// across monitor edges: there is no per-monitor coordinate reset anywhere.
/// </summary>
public sealed class PetPhysics
{
    public const double GravityDip = 2600;      // DIP / s^2
    public const double AirDrag = 0.35;         // 1/s
    public const double MaxSpeedDip = 5200;     // DIP / s
    public const double WallRestitution = 0.42;
    public const double CeilingRestitution = 0.3;

    public Vec2 Center { get; set; }
    public Vec2 Velocity { get; set; }

    /// <summary>Floors above this Y are ignored (used to drop through to a monitor below).</summary>
    public double? IgnoreFloorsAbove { get; set; }

    public int WallHits { get; private set; }

    public void Launch(Vec2 center, Vec2 velocity, double? ignoreFloorsAbove = null)
    {
        Center = center;
        Velocity = velocity;
        IgnoreFloorsAbove = ignoreFloorsAbove;
        WallHits = 0;
    }

    /// <summary>Advances the body. Returns landing info when it touches a floor this step.</summary>
    public LandingInfo? Step(double dt, WorldGeometry world, BodyMetrics m)
    {
        dt = Math.Clamp(dt, 0, 0.05);
        var maxSpeed = m.Dip(MaxSpeedDip);
        if (Velocity.Length > maxSpeed) Velocity = Velocity.Normalized() * maxSpeed;

        var travel = Velocity.Length * dt;
        var steps = Math.Clamp((int)Math.Ceiling(travel / Math.Max(4, m.Dip(14))), 1, 16);
        var h = dt / steps;
        var g = m.Dip(GravityDip);

        for (var i = 0; i < steps; i++)
        {
            var c = Center;
            var v = Velocity;
            v = new Vec2(v.X, v.Y + g * h);
            // Air drag only slows horizontal flight so ballistic jumps reach their computed apex.
            v = new Vec2(v.X * Math.Max(0, 1 - AirDrag * h), v.Y);
            var next = c + v * h;

            // Walls: the leading side of the body must stay inside some monitor.
            if (Math.Abs(v.X) > 1e-6)
            {
                var side = next.X + Math.Sign(v.X) * m.HalfWidthPx * 0.6;
                var open = world.ColumnOpen(side, next.Y) || world.ColumnOpen(side, next.Y + m.FeetOffsetPx - 2);
                if (!open)
                {
                    next = new Vec2(c.X, next.Y);
                    v = new Vec2(-v.X * WallRestitution, v.Y);
                    WallHits++;
                }
            }

            // Ceiling of the column.
            var ceil = world.CeilingAt(next.X);
            if (v.Y < 0 && next.Y - m.HeadOffsetPx < ceil)
            {
                next = new Vec2(next.X, ceil + m.HeadOffsetPx);
                v = new Vec2(v.X, -v.Y * CeilingRestitution);
            }

            // Floor: first floor at or below the previous feet position.
            var prevFeet = c.Y + m.FeetOffsetPx;
            var floor = world.FloorBelow(next.X, prevFeet - 1);
            if (floor is double f && IgnoreFloorsAbove is double ign && f <= ign + WorldGeometry.Epsilon)
            {
                floor = world.FloorBelow(next.X, ign + 2);
            }
            if (floor is double fl && v.Y >= 0 && next.Y + m.FeetOffsetPx >= fl)
            {
                Center = new Vec2(next.X, fl - m.FeetOffsetPx);
                Velocity = new Vec2(v.X, 0);
                IgnoreFloorsAbove = null;
                return new LandingInfo(v.Y / m.MonitorScale, v.X / m.MonitorScale);
            }

            Center = next;
            Velocity = v;
        }

        return null;
    }

    /// <summary>Initial velocity for a ballistic hop from <paramref name="from"/> landing near <paramref name="to"/>.</summary>
    public static Vec2 JumpVelocity(Vec2 from, Vec2 to, BodyMetrics m, double extraApexDip = 60)
    {
        var g = m.Dip(GravityDip);
        var rise = Math.Max(0, from.Y - to.Y) + m.Dip(extraApexDip);
        var vy = -Math.Sqrt(2 * g * rise);
        // time to apex + time to fall to target height
        var tUp = -vy / g;
        var fall = rise - Math.Max(0, from.Y - to.Y) + Math.Max(0, to.Y - from.Y);
        var tDown = Math.Sqrt(2 * Math.Max(1, fall) / g);
        var vx = (to.X - from.X) / (tUp + tDown);
        return new Vec2(vx, vy);
    }
}
