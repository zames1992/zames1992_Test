using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Physics;

/// <summary>
/// Spring-follow of the grabbed point plus a damped pendulum for the body hanging from it.
/// The hood (grab point) responds first; the body swings behind it with lag.
/// Angle is the rig rotation around the grab point in degrees (clockwise positive).
/// </summary>
public sealed class GrabController
{
    public const double Stiffness = 700;      // 1/s^2
    public const double DampingRatio = 0.85;
    public const double PendulumDamping = 5.5; // 1/s
    public const double MaxAngle = 65;

    public Vec2 Pivot { get; private set; }
    public Vec2 PivotVelocity { get; private set; }
    public double Angle { get; private set; }
    public double AngularVelocity { get; private set; } // deg/s

    /// <summary>Grab point in rig reference pixels.</summary>
    public Vec2 GrabLocal { get; private set; }

    /// <summary>Offset from cursor to the pivot target (keeps the pick-up point under the finger).</summary>
    public Vec2 CursorOffset { get; private set; }

    public void Begin(Vec2 pivotWorld, Vec2 grabLocal, Vec2 cursorWorld, double initialAngle = 0)
    {
        Pivot = pivotWorld;
        PivotVelocity = Vec2.Zero;
        GrabLocal = grabLocal;
        CursorOffset = pivotWorld - cursorWorld;
        Angle = initialAngle;
        AngularVelocity = 0;
    }

    public void Update(double dt, Vec2 cursorWorld, BodyMetrics m, bool reducedMotion)
    {
        dt = Math.Clamp(dt, 0, 0.05);
        var steps = 4;
        var h = dt / steps;
        var target = cursorWorld + CursorOffset;
        var c = 2 * Math.Sqrt(Stiffness) * DampingRatio;
        var comDist = Math.Max(40, (BodyMetrics.CenterLocal - GrabLocal).Length * m.RefToPx);
        var g = m.Dip(PetPhysics.GravityDip);

        for (var i = 0; i < steps; i++)
        {
            var acc = (target - Pivot) * Stiffness - PivotVelocity * c;
            PivotVelocity += acc * h;
            Pivot += PivotVelocity * h;

            var th = Angle * Math.PI / 180;
            var w = AngularVelocity * Math.PI / 180;
            var gEff = Math.Max(g * 0.3, g - acc.Y);
            var alpha = -(gEff / comDist) * Math.Sin(th) + (acc.X / comDist) * Math.Cos(th) - PendulumDamping * w;
            if (reducedMotion) alpha -= 6 * w;
            w += alpha * h;
            th += w * h;
            var max = (reducedMotion ? 35 : MaxAngle) * Math.PI / 180;
            if (Math.Abs(th) > max)
            {
                th = Math.Sign(th) * max;
                w *= -0.3;
            }
            Angle = th * 180 / Math.PI;
            AngularVelocity = w * 180 / Math.PI;
        }
    }
}
