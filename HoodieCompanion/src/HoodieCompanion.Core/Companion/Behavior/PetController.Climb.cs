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
public readonly record struct WorldProp(WorldPropKind Kind, Vec2 Top, Vec2 Bottom, double Reveal, double Alpha, double Scale, double Sway = 0);

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

    /// <summary>Hanging from a ledge (the top of the taskbar) and pulling itself up.</summary>
    private sealed class LedgePlan
    {
        public required double X;
        public required double EdgeY;
        public double HangFor;
        public int Phase;      // 0 hang, 1 climb up
    }

    private LedgePlan? _ledge;

    public bool IsHangingOnLedge => _ledge is not null;

    private double LedgeDepthPx => ProceduralAnimator.HangEdgeDepth * Metrics.RefToPx;

    /// <summary>
    /// Released / falling below a floor inside a monitor (over the taskbar) with nothing below: instead of
    /// dropping out of the world, Hoodie grabs the edge of the floor above and climbs up.
    /// </summary>
    private bool TryCatchLedge(BodyMetrics m)
    {
        var c = Physics.Center;
        if (Physics.Velocity.Y < -m.Dip(50)) return false;
        var feetY = c.Y + m.FeetOffsetPx;
        MonitorInfo? mon = null;
        foreach (var mm in World.Monitors)
        {
            if (!mm.Bounds.Inflate(0, m.HeightPx).Contains(c) || !mm.WorkArea.ContainsX(c.X)) continue;
            mon = mm;
            break;
        }
        if (mon is null) return false;
        var edge = mon.WorkArea.Bottom;
        // Only when the body is already (mostly) below that floor and there is no other floor to fall onto.
        if (c.Y < edge - m.Dip(4)) return false;
        if (World.FloorBelow(c.X, feetY - 1) is not null) return false;
        StartLedgeHang(c.X, edge, fromAir: true);
        return true;
    }

    private void StartLedgeHang(double x, double edgeY, bool fromAir)
    {
        var mon = World.NearestMonitor(new Vec2(x, edgeY - 2));
        var half = Metrics.HalfWidthPx * 0.5;
        x = Math.Clamp(x, mon.WorkArea.Left + half, mon.WorkArea.Right - half);
        _ledge = new LedgePlan { X = x, EdgeY = edgeY, HangFor = 0.9 + _rng.NextDouble() * 0.8 };
        _climb = null;
        Feet = new Vec2(x, edgeY + LedgeDepthPx);
        _anchorLocal = BodyMetrics.RootLocal;
        _anchorWorld = Feet;
        _thrownByUser = false;
        _dizzyOnLanding = false;
        Go(BehaviorState.Climbing, fromAir ? "caught the ledge" : "hanging on the ledge", force: true);
        Animation.Play(AnimClip.HangEdge, force: true, restart: true);
    }

    private void UpdateLedge()
    {
        var l = _ledge!;
        if (l.Phase == 0)
        {
            Feet = new Vec2(l.X, l.EdgeY + LedgeDepthPx);
            if (Machine.TimeInState < l.HangFor) return;
            l.Phase = 1;
            Animation.Play(AnimClip.ClimbEdge, force: true, restart: true);
            return;
        }
        var u = Math.Clamp(Animation.ClipTime / AnimationCatalog.Get(AnimClip.ClimbEdge).Duration, 0, 1);
        Feet = new Vec2(l.X, l.EdgeY + LedgeDepthPx * ProceduralAnimator.ClimbEdgeDepth(u));
        if (!Animation.IsFinished) return;
        Feet = new Vec2(l.X, l.EdgeY);
        _ledge = null;
        Go(BehaviorState.Landing, "climbed up", force: true);
        _landingImpact = 0;
        _slideVelocity = 0;
        Mind.OnSmallWin();
        Animation.Play(AnimClip.RecoverFromThrow, force: true, restart: true);
    }
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
                double reveal = 1, sway;
                if (c.Phase == 0)
                {
                    // Pushed up / thrown down with a little overshoot, then it settles with a wobble.
                    var d = AnimationCatalog.Get(c.Kind == WorldPropKind.Ladder ? AnimClip.PlaceLadder : AnimClip.TieRope).Duration;
                    var k = MathUtil.Clamp01(Machine.TimeInState / d);
                    reveal = Math.Min(1.04, MathUtil.EaseOutBack(k));
                    sway = (c.Kind == WorldPropKind.Ladder ? 4 : 7) * Math.Sin(Machine.TimeInState * 13) * (1 - k);
                }
                else
                {
                    // Every step shakes the ladder a little; the rope swings under Hoodie's weight.
                    sway = c.Kind == WorldPropKind.Ladder
                        ? 1.6 * Math.Sin(2 * Math.PI * _walkPhase) + 0.6 * Math.Sin(_time * 3.1)
                        : 3.5 * Math.Sin(_time * 2.2) + 1.2 * Math.Sin(2 * Math.PI * _walkPhase);
                }
                if (Settings.ReducedMotion) sway *= 0.3;
                return new WorldProp(c.Kind, new Vec2(c.PropX, c.PropTopY), new Vec2(c.PropX, c.PropBottomY), reveal, 1, scale, sway);
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
        if (_ledge is not null)
        {
            UpdateLedge();
            return;
        }
        if (_wall is not null)
        {
            UpdateWall(dt);
            return;
        }
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
                if (_climbOntoSurface is not null)
                {
                    ArrivedAtLadderTop();
                    if (_onSurface is null) return;
                    Go(BehaviorState.Landing, "on the platform", force: true);
                    _landingImpact = 0;
                    _slideVelocity = 0;
                    Animation.Play(AnimClip.LandSoft, force: true, restart: true);
                    return;
                }
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
        _ledge = null;
        _wall = null;
        _climbOntoSurface = null;
    }
}
