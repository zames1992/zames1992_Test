using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Physics;
using HoodieCompanion.Geometry;
using HoodieCompanion.Presence;

namespace HoodieCompanion.Companion.Behavior;

public sealed partial class PetController
{
    private sealed class TravelPlan
    {
        public required string MonitorId;
        public required double TargetX;
        public bool Run;
        public Action? OnArrive;
        public int Hops;
        public bool IgnoreTerritory;
    }

    private double? _walkTargetX;
    private bool _run;
    private bool _chase;
    private Action? _onArrive;
    private double _walkPhase;
    private bool _walkIgnoresTerritory;
    private TravelPlan? _travel;
    private Action? _afterTurn;
    private bool _turnFlipped;
    private bool _thrownByUser;
    private bool _jumpToMonitor;
    private double _landingImpact;
    private double _slideVelocity;
    private double _outOfWorld;
    private Vec2 _jumpTarget;
    private double _jumpExtraApex;

    // ------------------------------------------------------------------ walking

    /// <summary>Walk (or run) to a floor X on the current monitor.</summary>
    private void StartWalk(double targetX, bool run, Action? onArrive, bool ignoreTerritory = false, bool chase = false)
    {
        if (Machine.State is BehaviorState.Sitting or BehaviorState.Sleeping)
        {
            EnsureStanding(() => StartWalk(targetX, run, onArrive, ignoreTerritory, chase));
            return;
        }
        _walkTargetX = targetX;
        _run = run && !Settings.ReducedMotion;
        _chase = chase;
        _onArrive = onArrive;
        _walkIgnoresTerritory = ignoreTerritory;
        var dx = targetX - Feet.X;
        if (Math.Abs(dx) < 1.5)
        {
            ArriveWalk();
            return;
        }
        var dir = Math.Sign(dx);
        if (dir != Facing) TurnThen(() => Go(BehaviorState.Walking, "walk after turn", force: true));
        else Go(BehaviorState.Walking, "walk", force: true);
    }

    private void TurnThen(Action then)
    {
        _afterTurn = then;
        _turnFlipped = false;
        Go(BehaviorState.Turning, "turn", force: true);
        Animation.Play(AnimClip.Turn, force: true, restart: true);
    }

    private void UpdateTurning()
    {
        var d = AnimationCatalog.Get(AnimClip.Turn).Duration;
        if (!_turnFlipped && Animation.ClipTime >= d * 0.5)
        {
            Facing = -Facing;
            _turnFlipped = true;
        }
        if (Animation.Current != AnimClip.Turn || Animation.IsFinished)
        {
            if (!_turnFlipped) Facing = -Facing;
            var next = _afterTurn;
            _afterTurn = null;
            if (next is not null) next();
            else Go(BehaviorState.Idle, "turned", force: true);
        }
    }

    private void UpdateWalking(double dt)
    {
        if (_walkTargetX is not double target)
        {
            Go(BehaviorState.Idle, "no target", force: true);
            return;
        }
        var m = Metrics;
        var dx = target - Feet.X;
        var dir = Math.Sign(dx);
        if (dir == 0 || Math.Abs(dx) < 1.0)
        {
            ArriveWalk();
            return;
        }
        if (dir != Facing)
        {
            TurnThen(() => Go(BehaviorState.Walking, "resume walk", force: true));
            return;
        }

        var quiet = EffectiveMode is HoodieCompanion.Settings.PresenceMode.Quiet or HoodieCompanion.Settings.PresenceMode.Focus || Territory.IsQuietAt(Feet, m.HeightPx);
        var run = _run && !quiet;
        var speedDip = (run ? RunSpeedDip : WalkSpeedDip) * Settings.WalkSpeed * (quiet ? 0.8 : 1);
        // Ease in at the start and ease out when arriving.
        var ease = Math.Min(1, 0.35 + Machine.TimeInState * 2.5) * Math.Min(1, 0.3 + Math.Abs(dx) / Dip(40));
        var step = Math.Min(Math.Abs(dx), Dip(speedDip) * ease * dt);
        var nextX = Feet.X + dir * step;
        Animation.Play(run ? (_chase ? AnimClip.FollowCursor : AnimClip.Run) : AnimClip.Walk);

        // Spatial restrictions always win over wandering.
        if (!_walkIgnoresTerritory)
        {
            if (!Territory.CanTraverse(new Vec2(nextX + dir * m.HalfWidthPx * 0.3, Feet.Y), m.HeightPx))
            {
                StopWalk("territory");
                return;
            }
            if (Territory.Anchor is { } a && Math.Abs(nextX - a.Center.X) > a.RadiusPx && Math.Abs(nextX - a.Center.X) > Math.Abs(Feet.X - a.Center.X))
            {
                StopWalk("anchor");
                return;
            }
        }

        var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        var lead = nextX + dir * m.HalfWidthPx * 0.25;
        var floorY = Feet.Y;
        if (!mon.WorkArea.ContainsX(lead))
        {
            var candidates = World.Monitors.Where(o => o != mon && o.WorkArea.ContainsX(lead)).ToList();
            var tol = Dip(16);
            var same = candidates.FirstOrDefault(o => Math.Abs(o.WorkArea.Bottom - Feet.Y) <= tol);
            var lower = candidates.Where(o => o.WorkArea.Bottom > Feet.Y + tol && o.WorkArea.Top <= Feet.Y).OrderBy(o => o.WorkArea.Bottom).FirstOrDefault();
            var higher = candidates.Where(o => o.WorkArea.Bottom < Feet.Y - tol).OrderByDescending(o => o.WorkArea.Bottom).FirstOrDefault();
            if (same is not null)
            {
                floorY = same.WorkArea.Bottom;
            }
            else if (lower is not null)
            {
                // Step off the edge and drop down onto the lower monitor.
                Physics.Launch(Feet - new Vec2(0, m.FeetOffsetPx), new Vec2(dir * Dip(speedDip) * 1.2, -Dip(140)));
                _thrownByUser = false;
                _jumpToMonitor = true;
                EnterAirborne("walked off edge");
                return;
            }
            else if (higher is not null)
            {
                var landX = lead + dir * m.HalfWidthPx;
                JumpTo(new Vec2(landX, higher.WorkArea.Bottom), extraApexDip: 50);
                return;
            }
            else if (!_leavingThroughEdge)
            {
                // Edge of the world.
                StopWalk("world edge");
                return;
            }
        }

        Feet = new Vec2(nextX, floorY);
        var stride = (run ? ProceduralAnimator.RunStride : ProceduralAnimator.WalkStride) * m.RefToPx;
        _walkPhase += step / Math.Max(1e-3, stride);

        var nowMon = World.MonitorAt(Feet);
        if (_travel is not null && nowMon is not null && nowMon != mon && nowMon.Id == _travel.MonitorId)
        {
            ContinueTravel();
            return;
        }
        if (Math.Abs(target - Feet.X) < 1.0) ArriveWalk();
    }

    private void StopWalk(string reason)
    {
        _walkTargetX = null;
        _onArrive = null;
        _travel = null;
        Log?.Invoke("walk stopped: " + reason);
        Go(BehaviorState.Idle, reason, force: true);
        Animation.Play(AnimClip.IdleBreathing);
        ScheduleDecision(1.5);
    }

    private void ArriveWalk()
    {
        _walkTargetX = null;
        var cb = _onArrive;
        _onArrive = null;
        _chase = false;
        Go(BehaviorState.Idle, "arrived", force: true);
        Animation.Play(AnimClip.IdleBreathing);
        cb?.Invoke();
    }

    // ------------------------------------------------------------------ travel across monitors

    /// <summary>Travels to a floor point anywhere in the world: walk, hop up, drop down, or poof as a last resort.</summary>
    public bool TravelTo(Vec2 targetFeet, bool run, Action? onArrive, bool ignoreTerritory = false)
    {
        var m = Metrics;
        var tm = World.NearestMonitor(targetFeet);
        double x;
        if (ignoreTerritory)
        {
            x = Math.Clamp(targetFeet.X, tm.WorkArea.Left + m.HalfWidthPx, tm.WorkArea.Right - m.HalfWidthPx);
        }
        else
        {
            var sx = Territory.NearestStandableX(tm, targetFeet.X, m.HeightPx, m.HalfWidthPx);
            if (sx is null) return false;
            x = sx.Value;
        }
        _travel = new TravelPlan { MonitorId = tm.Id, TargetX = x, Run = run, OnArrive = onArrive, IgnoreTerritory = ignoreTerritory };
        ContinueTravel();
        return true;
    }

    private void ContinueTravel()
    {
        var plan = _travel;
        if (plan is null) return;
        var tm = World.FindById(plan.MonitorId);
        if (tm is null)
        {
            _travel = null;
            return;
        }
        var cur = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        var m = Metrics;
        if (cur == tm)
        {
            _travel = null;
            StartWalk(plan.TargetX, plan.Run, plan.OnArrive, plan.IgnoreTerritory);
            return;
        }
        if (++plan.Hops > 6)
        {
            _travel = null;
            PoofTo(new Vec2(plan.TargetX, tm.WorkArea.Bottom), plan.OnArrive);
            return;
        }

        var cw = cur.WorkArea;
        var tw = tm.WorkArea;
        var xOverlap = WorldGeometry.Overlap(cw.Left, cw.Right, tw.Left, tw.Right);

        if (xOverlap < m.HalfWidthPx * 2)
        {
            // Target is to the side: walk across the shared edge if a neighbour exists in that direction.
            var dir = Math.Sign(tw.Center.X - cw.Center.X);
            var n = World.MonitorBeside(cur, dir);
            var passable = n is not null && (n == tm || Territory.MonitorRule(n.Id) != RegionType.NoGo);
            if (passable)
            {
                var edge = dir > 0 ? cw.Right : cw.Left;
                StartWalk(edge + dir * m.HalfWidthPx * 1.2, plan.Run, ContinueTravel, ignoreTerritory: true);
                return;
            }
        }
        else
        {
            var lo = Math.Max(cw.Left, tw.Left) + m.HalfWidthPx;
            var hi = Math.Min(cw.Right, tw.Right) - m.HalfWidthPx;
            var x = Math.Clamp(Feet.X, lo, hi);
            if (tw.Bottom <= cw.Top + Dip(8))
            {
                // Monitor above: walk under it, then leap up.
                var landX = Territory.NearestStandableX(tm, x, m.HeightPx, m.HalfWidthPx) ?? x;
                landX = Math.Clamp(landX, lo, hi);
                StartWalk(landX, plan.Run, () => JumpTo(new Vec2(landX, tw.Bottom), extraApexDip: 80), ignoreTerritory: plan.IgnoreTerritory);
                return;
            }
            if (tw.Top >= cw.Bottom - Dip(8))
            {
                // Monitor below: walk over it, peek, drop through the floor.
                StartWalk(x, plan.Run, DropThrough, ignoreTerritory: plan.IgnoreTerritory);
                return;
            }
        }

        _travel = null;
        PoofTo(new Vec2(plan.TargetX, tw.Bottom), plan.OnArrive);
    }

    private void DropThrough()
    {
        var m = Metrics;
        var cur = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        Physics.Launch(Feet - new Vec2(0, m.FeetOffsetPx), new Vec2(-Facing * Dip(-40), -Dip(420)), ignoreFloorsAbove: cur.WorkArea.Bottom);
        _thrownByUser = false;
        _jumpToMonitor = true;
        EnterAirborne("drop to monitor below");
    }

    private void JumpTo(Vec2 targetFeet, double extraApexDip)
    {
        if (Machine.State is BehaviorState.Sitting or BehaviorState.Sleeping)
        {
            EnsureStanding(() => JumpTo(targetFeet, extraApexDip));
            return;
        }
        _jumpTarget = targetFeet;
        _jumpExtraApex = extraApexDip;
        var dir = Math.Sign(targetFeet.X - Feet.X);
        if (dir != 0 && dir != Facing)
        {
            TurnThen(() => JumpTo(targetFeet, extraApexDip));
            return;
        }
        Go(BehaviorState.Jumping, "jump", force: true);
        Animation.Play(AnimClip.JumpMonitor, force: true, restart: true);
    }

    private void UpdateJumping()
    {
        if (!Animation.IsFinished && Animation.Current == AnimClip.JumpMonitor) return;
        var m = Metrics;
        var from = Feet - new Vec2(0, m.FeetOffsetPx);
        var to = _jumpTarget - new Vec2(0, m.FeetOffsetPx);
        var v = PetPhysics.JumpVelocity(from, to, m, _jumpExtraApex);
        Physics.Launch(from, v);
        _thrownByUser = false;
        _jumpToMonitor = true;
        EnterAirborne("takeoff");
    }

    // ------------------------------------------------------------------ physical interaction

    /// <summary>User started dragging Hoodie. <paramref name="cursor"/> is in world pixels.</summary>
    public bool BeginGrab(Vec2 cursor)
    {
        if (!Settings.GrabThrow) return false;
        if (Machine.State is BehaviorState.Hidden or BehaviorState.Leaving or BehaviorState.Vanishing or BehaviorState.Appearing) return false;
        var t = Transform;
        var local = t.WorldToLocal(cursor);
        // The hood is the natural handle. Grabbing lower keeps the finger offset but hangs from the chest.
        var grabLocal = new Vec2(Math.Clamp(local.X, 150, 400), Math.Clamp(local.Y, 90, 400));
        var pivot = t.LocalToWorld(grabLocal);
        Grab.Begin(pivot, grabLocal, cursor, _tilt);
        Thrower.Reset();
        Thrower.AddSample(_time, cursor);
        _anchorLocal = grabLocal;
        _anchorWorld = pivot;
        _walkTargetX = null;
        _travel = null;
        _sequence.Clear();
        Go(BehaviorState.Grabbed, "grabbed", force: true);
        Animation.Play(AnimClip.GrabReaction, force: true, restart: true);
        Drives.OnUserAttention();
        return true;
    }

    public void EndGrab(Vec2 cursor)
    {
        if (Machine.State != BehaviorState.Grabbed) return;
        Thrower.AddSample(_time, cursor);
        var v = Thrower.Estimate(_time);
        var m = Metrics;
        var t = Transform;
        var center = t.LocalToWorld(BodyMetrics.CenterLocal);
        _anchorLocal = BodyMetrics.CenterLocal;
        _anchorWorld = center;
        Physics.Launch(center, v);
        _thrownByUser = true;
        _jumpToMonitor = false;
        _tiltVel = Grab.AngularVelocity * 0.5;
        EnterAirborne(v.Length > m.Dip(300) ? "thrown" : "released");
        Animation.Play(v.Length > m.Dip(300) ? AnimClip.Thrown : AnimClip.Fall, force: true);
    }

    private void UpdateGrabbed(double dt, Vec2 cursor)
    {
        var m = Metrics;
        Thrower.AddSample(_time, cursor);
        Grab.Update(dt, cursor, m, Settings.ReducedMotion);
        _anchorLocal = Grab.GrabLocal;
        _anchorWorld = Grab.Pivot;
        _tilt = Grab.Angle;
        _tiltVel = 0;
        if (Animation.Current == AnimClip.GrabReaction && !Animation.IsFinished) return;
        Animation.Play(Math.Abs(Grab.AngularVelocity) > 140 ? AnimClip.Swinging : AnimClip.Grabbed, force: true);
    }

    private void EnterAirborne(string reason)
    {
        _anchorLocal = BodyMetrics.CenterLocal;
        _anchorWorld = Physics.Center;
        _outOfWorld = 0;
        Go(BehaviorState.Airborne, reason, force: true);
        if (!_thrownByUser) Animation.Play(Physics.Velocity.Y < 0 ? AnimClip.Airborne : AnimClip.Fall, force: true);
    }

    private void UpdateAirborne(double dt)
    {
        var m = Metrics;
        var landing = Physics.Step(dt, World, m);
        _anchorLocal = BodyMetrics.CenterLocal;
        _anchorWorld = Physics.Center;

        // The body rights itself while flying (hood up), with a little spin from the throw.
        var acc = -40 * _tilt - 5 * _tiltVel;
        _tiltVel += acc * dt;
        _tilt += _tiltVel * dt;

        if (landing is { } l)
        {
            Land(l, m);
            return;
        }

        if (Animation.Current != AnimClip.Thrown || Physics.Velocity.Y > Dip(250))
        {
            Animation.Play(Physics.Velocity.Y < 0 ? AnimClip.Airborne : AnimClip.Fall, force: true);
        }

        // Safety: never get lost outside the world.
        if (!World.Extent.Inflate(Dip(200), Dip(200)).Contains(Physics.Center) && World.MonitorAt(Physics.Center) is null)
        {
            _outOfWorld += dt;
            if (_outOfWorld > 1.2) Respawn("lost outside the world");
        }
        else
        {
            _outOfWorld = 0;
        }
    }

    private void Land(LandingInfo l, BodyMetrics m)
    {
        Feet = new Vec2(Physics.Center.X, Physics.Center.Y + m.FeetOffsetPx);
        _anchorLocal = BodyMetrics.RootLocal;
        _anchorWorld = Feet;
        // Keep the visual tilt but let it settle around the feet.
        _tilt = Math.Clamp(_tilt, -25, 25);
        _landingImpact = l.ImpactDipPerSec;
        _slideVelocity = l.HorizontalDipPerSec * 0.45 * _monitorScale;
        Go(BehaviorState.Landing, $"landed {l.ImpactDipPerSec:0} dip/s", force: true);
        var hard = _thrownByUser && l.ImpactDipPerSec >= HardLandingDip;
        if (hard)
        {
            Drives.OnHardLanding();
            Animation.Play(AnimClip.LandHard, force: true, restart: true);
        }
        else
        {
            Animation.Play(_jumpToMonitor ? AnimClip.LandMonitor : AnimClip.LandSoft, force: true, restart: true);
        }
    }

    private void UpdateLanding(double dt)
    {
        // Slide a little after touching down, with friction; walls still stop the slide.
        if (Math.Abs(_slideVelocity) > 1)
        {
            var nx = Feet.X + _slideVelocity * dt;
            var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
            var half = Metrics.HalfWidthPx * 0.5;
            if (nx - half < mon.WorkArea.Left || nx + half > mon.WorkArea.Right)
            {
                nx = Math.Clamp(nx, mon.WorkArea.Left + half, mon.WorkArea.Right - half);
                _slideVelocity = 0;
            }
            Feet = new Vec2(nx, Feet.Y);
            _slideVelocity = MathUtil.MoveTowards(_slideVelocity, 0, Dip(2600) * dt);
        }

        if (!Animation.IsFinished) return;
        if (Animation.Current == AnimClip.LandHard)
        {
            var steps = new List<(AnimClip, double?)> { (AnimClip.Recover, null), (AnimClip.RecoverFromThrow, null) };
            RunSequence(BehaviorState.Recovering, steps, AfterLanding);
            return;
        }
        AfterLanding();
    }

    private void AfterLanding()
    {
        _thrownByUser = false;
        _jumpToMonitor = false;
        Go(BehaviorState.Idle, "recovered", force: true);
        Animation.Play(AnimClip.IdleBreathing);
        if (_travel is not null)
        {
            ContinueTravel();
            return;
        }
        ScheduleDecision(1.0 + _rng.NextDouble() * 2);
    }

    private void Respawn(string reason)
    {
        Log?.Invoke("respawn: " + reason);
        var home = Territory.HomeFeet() ?? DefaultHome();
        Place(home, appear: true);
    }

    public Vec2 DefaultHome()
    {
        var p = World.Primary.WorkArea;
        return new Vec2(p.Right - Dip(140), p.Bottom);
    }
}
