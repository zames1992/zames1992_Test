using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Physics;
using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Behavior;

public enum WorldPropKind
{
    Ladder,
    Rope,
}

/// <summary>
/// A prop that lives in the world rather than in Hoodie's hands (drawn by the host in its own
/// click-through window). Coordinates in virtual-desktop pixels.
/// </summary>
public readonly record struct WorldProp(WorldPropKind Kind, Vec2 Top, Vec2 Bottom, double Reveal, double Alpha, double Scale);

public sealed partial class PetController
{
    public const double ClimbSpeedDip = 190;
    public const double RopeSpeedDip = 240;
    private const double PropFadeSeconds = 0.45;

    private sealed class ClimbPlan
    {
        public required WorldPropKind Kind;
        public required double X;          // column Hoodie climbs in
        public required double StartY;
        public required double EndY;
        public required double PropX;      // where the ladder / rope hangs
        public required double PropTopY;
        public required double PropBottomY;
        public double StepOffX;             // after the ladder: hop sideways onto a neighbour floor
        public int Phase;                   // 0 place, 1 climb, 2 finish
    }

    private ClimbPlan? _climb;
    private WorldProp? _fadingProp;
    private double _fadeStart;

    /// <summary>The ladder / rope currently in the world, if any.</summary>
    public WorldProp? CurrentProp
    {
        get
        {
            var scale = _monitorScale;
            if (_climb is { } c)
            {
                double reveal = 1;
                if (c.Phase == 0)
                {
                    var d = AnimationCatalog.Get(c.Kind == WorldPropKind.Ladder ? AnimClip.PlaceLadder : AnimClip.TieRope).Duration;
                    reveal = MathUtil.SmoothStep(Machine.TimeInState / d);
                }
                return new WorldProp(c.Kind, new Vec2(c.PropX, c.PropTopY), new Vec2(c.PropX, c.PropBottomY), reveal, 1, scale);
            }
            if (_fadingProp is { } f)
            {
                var a = 1 - (_time - _fadeStart) / PropFadeSeconds;
                if (a <= 0)
                {
                    _fadingProp = null;
                    return null;
                }
                return f with { Alpha = a };
            }
            return null;
        }
    }

    /// <summary>Climb a ladder from the current floor up to <paramref name="topY"/> (a higher floor).</summary>
    private void ClimbUp(double topY, double stepOffX)
    {
        if (Machine.State is BehaviorState.Sitting or BehaviorState.Sleeping)
        {
            EnsureStanding(() => ClimbUp(topY, stepOffX));
            return;
        }
        var m = Metrics;
        _climb = new ClimbPlan
        {
            Kind = WorldPropKind.Ladder,
            X = Feet.X,
            StartY = Feet.Y,
            EndY = topY,
            PropX = Feet.X - Facing * m.HalfWidthPx * 0.15,
            PropTopY = topY - Dip(14),
            PropBottomY = Feet.Y,
            StepOffX = stepOffX,
        };
        Go(BehaviorState.Climbing, "place ladder", force: true);
        Animation.Play(AnimClip.PlaceLadder, force: true, restart: true);
    }

    /// <summary>Tie a rope at the edge and slide down to <paramref name="bottomY"/> in column <paramref name="x"/>.</summary>
    private void RopeDown(double x, double bottomY)
    {
        if (Machine.State is BehaviorState.Sitting or BehaviorState.Sleeping)
        {
            EnsureStanding(() => RopeDown(x, bottomY));
            return;
        }
        _climb = new ClimbPlan
        {
            Kind = WorldPropKind.Rope,
            X = x,
            StartY = Feet.Y,
            EndY = bottomY,
            PropX = x,
            PropTopY = Feet.Y - Dip(6),
            PropBottomY = bottomY - Dip(4),
        };
        Go(BehaviorState.Climbing, "tie rope", force: true);
        Animation.Play(AnimClip.TieRope, force: true, restart: true);
    }

    private void UpdateClimbing(double dt)
    {
        var c = _climb;
        if (c is null)
        {
            Go(BehaviorState.Idle, "no climb", force: true);
            return;
        }
        var m = Metrics;
        switch (c.Phase)
        {
            case 0:
                if (!Animation.IsFinished) return;
                c.Phase = 1;
                if (c.Kind == WorldPropKind.Rope)
                {
                    // Swing over the edge onto the rope.
                    Feet = new Vec2(c.X, Feet.Y);
                }
                Animation.Play(c.Kind == WorldPropKind.Ladder ? AnimClip.ClimbLadder : AnimClip.ClimbRope, force: true);
                return;
            case 1:
            {
                var speed = Dip(c.Kind == WorldPropKind.Ladder ? ClimbSpeedDip : RopeSpeedDip) * Math.Max(0.6, Settings.WalkSpeed);
                var dir = Math.Sign(c.EndY - Feet.Y);
                var step = Math.Min(Math.Abs(c.EndY - Feet.Y), speed * dt);
                Feet = new Vec2(Feet.X, Feet.Y + dir * step);
                // One climbing cycle per ~2 rungs.
                _walkPhase += step / Dip(64);
                if (Math.Abs(c.EndY - Feet.Y) > 0.5) return;
                Feet = new Vec2(Feet.X, c.EndY);
                c.Phase = 2;
                _fadingProp = new WorldProp(c.Kind, new Vec2(c.PropX, c.PropTopY), new Vec2(c.PropX, c.PropBottomY), 1, 1, _monitorScale);
                _fadeStart = _time;
                _climb = null;
                if (c.Kind == WorldPropKind.Ladder && Math.Abs(c.StepOffX - Feet.X) > 1)
                {
                    // Hop from the top of the ladder onto the neighbouring, higher floor.
                    JumpTo(new Vec2(c.StepOffX, c.EndY), extraApexDip: 30);
                    return;
                }
                Go(BehaviorState.Landing, "reached floor", force: true);
                _landingImpact = 0;
                _slideVelocity = 0;
                Animation.Play(AnimClip.LandSoft, force: true, restart: true);
                return;
            }
        }
    }

    private void DropClimb()
    {
        if (_climb is { } c)
        {
            _fadingProp = new WorldProp(c.Kind, new Vec2(c.PropX, c.PropTopY), new Vec2(c.PropX, c.PropBottomY), 1, 1, _monitorScale);
            _fadeStart = _time;
        }
        _climb = null;
    }
}
