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
    private bool _dizzyOnLanding;
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
                // Bumps into the invisible wall, understands, turns back.
                React(PetEvent.BoundaryHit);
                return;
            }
            if (Territory.Anchor is { } a && Math.Abs(nextX - a.Center.X) > a.RadiusPx && Math.Abs(nextX - a.Center.X) > Math.Abs(Feet.X - a.Center.X))
            {
                StopWalk("anchor");
                return;
            }
        }

        if (_onSurface is { } sid)
        {
            // On a window top / icon: the platform ends before the monitor does.
            if (FindSurface(sid) is { } surf && !surf.Contains(nextX, m.HalfWidthPx * 0.1))
            {
                HopDown("walked to the end of the platform");
                return;
            }
            Feet = new Vec2(nextX, Feet.Y);
            _walkPhase += step / Math.Max(1e-3, (run ? ProceduralAnimator.RunStride : ProceduralAnimator.WalkStride) * m.RefToPx);
            if (Math.Abs(target - Feet.X) < 1.0) ArriveWalk();
            return;
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
                var drop = lower.WorkArea.Bottom - Feet.Y;
                if (drop > Dip(170) && !Settings.ReducedMotion || drop > Dip(170) && Settings.ReducedMotion)
                {
                    // A long way down: tie a rope at the edge and climb down it.
                    var ropeX = (dir > 0 ? mon.WorkArea.Right : mon.WorkArea.Left) + dir * m.HalfWidthPx * 0.9;
                    RopeDown(ropeX, lower.WorkArea.Bottom);
                    return;
                }
                // Small step down: just hop off the edge.
                Physics.Launch(Feet - new Vec2(0, m.FeetOffsetPx), new Vec2(dir * Dip(speedDip) * 1.2, -Dip(140)));
                _thrownByUser = false;
                _jumpToMonitor = true;
                EnterAirborne("walked off edge");
                return;
            }
            else if (higher is not null)
            {
                var landX = (dir > 0 ? higher.WorkArea.Left : higher.WorkArea.Right) + dir * m.HalfWidthPx * 1.4;
                var rise = Feet.Y - higher.WorkArea.Bottom;
                if (rise > Dip(60))
                {
                    // Too high to hop: prop a ladder against the higher monitor and climb.
                    ClimbUp(higher.WorkArea.Bottom, landX);
                    return;
                }
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
        var wasRunning = Machine.State == BehaviorState.Walking && Animation.Current is AnimClip.Run or AnimClip.FollowCursor;
        Go(BehaviorState.Idle, "arrived", force: true);
        Animation.Play(IdleDirector.BaseLoop);
        // A run ends with a little skid and follow-through instead of a dead stop.
        if (wasRunning && !Settings.ReducedMotion && PlayEmoteAt(AnimClip.Stop, cb, ReactionPriority.Idle)) return;
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
                StartWalk(landX, plan.Run, () => ClimbUp(tw.Bottom, landX), ignoreTerritory: plan.IgnoreTerritory);
                return;
            }
            if (tw.Top >= cw.Bottom - Dip(8))
            {
                // Monitor below: walk over it, peek, drop through the floor.
                StartWalk(x, plan.Run, () => RopeDown(Feet.X, tw.Bottom), ignoreTerritory: plan.IgnoreTerritory);
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
    /// <summary>Which part of Hoodie the user is holding.</summary>
    public enum GrabRegion
    {
        Hood,
        HandLeft,
        HandRight,
        Foot,
        Torso,
    }

    private GrabRegion _grabRegion;
    private double _grabStartedAt;

    public GrabRegion CurrentGrabRegion => _grabRegion;

    /// <summary>Classifies a point in rest-pose reference pixels (facing left) into a grab region.</summary>
    public static GrabRegion RegionAt(Vec2 local)
    {
        if (local.Y >= 650) return GrabRegion.Foot;
        if (local.Y >= 420 && local.X < 178) return GrabRegion.HandLeft;
        if (local.Y >= 420 && local.X > 372) return GrabRegion.HandRight;
        if (local.Y >= 400) return GrabRegion.Torso;
        return GrabRegion.Hood;
    }

    /// <summary>
    /// Nearest body part to the grab point in the current pose: a hand or a foot when the point is close to
    /// it, otherwise the body (below the chest) or the hood.
    /// </summary>
    public static GrabRegion RegionInPose(Vec2 local, in Pose pose)
    {
        var parts = new (GrabRegion Region, Vec2 At, double Radius)[]
        {
            (GrabRegion.HandLeft, PosedRig.HandLeft(pose), 62),
            (GrabRegion.HandRight, PosedRig.HandRight(pose), 62),
            (GrabRegion.Foot, PosedRig.FootLeft(pose), 70),
            (GrabRegion.Foot, PosedRig.FootRight(pose), 70),
        };
        var best = GrabRegion.Hood;
        var bestD = double.MaxValue;
        foreach (var (r, at, radius) in parts)
        {
            var d = Vec2.Distance(local, at);
            if (d < radius && d < bestD) { best = r; bestD = d; }
        }
        if (bestD < double.MaxValue) return best;
        // Body vs hood: closer to the belly than to the head means the body.
        return Vec2.Distance(local, PosedRig.BellyPoint(pose)) < Vec2.Distance(local, PosedRig.Head(pose)) ? GrabRegion.Torso : GrabRegion.Hood;
    }

    public bool BeginGrab(Vec2 cursor)
    {
        if (!Settings.GrabThrow) return false;
        if (Machine.State is BehaviorState.Hidden or BehaviorState.Leaving or BehaviorState.Vanishing or BehaviorState.Appearing) return false;
        var t = Transform;
        var local = t.WorldToLocal(cursor);
        var wasAsleep = Machine.State == BehaviorState.Sleeping;
        // What was grabbed is judged on the pose as it is drawn now (sitting, lying, mid-step...).
        var region = RegionInPose(local, Animation.LastPose);
        Vec2 grabLocal;
        var keepOffset = true;
        switch (region)
        {
            case GrabRegion.HandLeft:
                grabLocal = ProceduralAnimator.HangAnchor(AnimClip.HangHandL);
                keepOffset = false;
                break;
            case GrabRegion.HandRight:
                grabLocal = ProceduralAnimator.HangAnchor(AnimClip.HangHandR);
                keepOffset = false;
                break;
            case GrabRegion.Foot:
                var pose = Animation.LastPose;
                grabLocal = Vec2.Distance(local, PosedRig.FootLeft(pose)) <= Vec2.Distance(local, PosedRig.FootRight(pose)) ? new Vec2(186, 800) : new Vec2(336, 806);
                keepOffset = false;
                break;
            case GrabRegion.Torso:
                grabLocal = new Vec2(Math.Clamp(local.X, 200, 344), 430);
                break;
            default:
                // The hood is the natural handle. Grabbing lower keeps the finger offset.
                grabLocal = new Vec2(Math.Clamp(local.X, 150, 400), Math.Clamp(local.Y, 90, 400));
                break;
        }
        // The pivot starts where that point is drawn now, so nothing jumps; it then eases to the finger.
        var posed = Animation.LastPose;
        var drawnAt = region switch
        {
            GrabRegion.HandLeft => PosedRig.HandLeft(posed),
            GrabRegion.HandRight => PosedRig.HandRight(posed),
            GrabRegion.Foot => grabLocal.X < RigTransform.AxisX ? PosedRig.FootLeft(posed) : PosedRig.FootRight(posed),
            _ => local,
        };
        var pivot = t.LocalToWorld(drawnAt);
        var rest = region == GrabRegion.Hood ? 0 : GrabController.RestAngleFor(grabLocal, Facing);
        Grab.Begin(pivot, grabLocal, cursor, _tilt, rest, limitSwing: region is GrabRegion.Hood or GrabRegion.Torso, keepCursorOffset: keepOffset);
        _grabRegion = region;
        _grabStartedAt = _time;
        Thrower.Reset();
        Thrower.AddSample(_time, cursor);
        _anchorLocal = grabLocal;
        _anchorWorld = pivot;
        _walkTargetX = null;
        _travel = null;
        _sequence.Clear();
        DropActivity();
        DropClimb();
        Go(BehaviorState.Grabbed, $"grabbed ({region})", force: true);
        Animation.Play(wasAsleep ? AnimClip.WakeStartled : region == GrabRegion.Hood ? AnimClip.GrabReaction : HangClip(region), force: true, restart: true);
        Drives.OnUserAttention();
        Mind.OnGrabbed(wasAsleep);
        return true;
    }

    private static AnimClip HangClip(GrabRegion r) => r switch
    {
        GrabRegion.HandLeft => AnimClip.HangHandL,
        GrabRegion.HandRight => AnimClip.HangHandR,
        GrabRegion.Foot => AnimClip.HangFoot,
        GrabRegion.Torso => AnimClip.HangTorso,
        _ => AnimClip.Grabbed,
    };

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
        _tilt = GrabController.NormalizeDeg(_tilt);
        _dizzyOnLanding = Grab.SwingEnergy > 1.2;
        Mind.OnReleased(v.Length / m.MonitorScale, _time - _grabStartedAt);
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
        if (Animation.Current is AnimClip.GrabReaction or AnimClip.WakeStartled && !Animation.IsFinished) return;
        var held = _time - _grabStartedAt;
        var fast = Math.Abs(Grab.AngularVelocity) > 140;
        AnimClip clip;
        if (Mind.Stress > 0.75 && held > 1.5 && !fast) clip = AnimClip.Struggle;
        else if (_grabRegion == GrabRegion.Hood) clip = fast ? AnimClip.Swinging : held > 5 && Mind.Affection > 0.45 && Grab.SwingEnergy < 0.2 ? AnimClip.RelaxedCarry : AnimClip.Grabbed;
        else if (_grabRegion == GrabRegion.Torso && held > 5 && Grab.SwingEnergy < 0.2 && Mind.Affection > 0.45) clip = AnimClip.RelaxedCarry;
        else clip = HangClip(_grabRegion);
        Animation.Play(clip, force: true);
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
        if (TryCatchLedge(m)) return;
        var prevCenter = Physics.Center;
        var velocityBefore = Physics.Velocity;
        var landing = Physics.Step(dt, World, m);
        if (TryLandOnSurface(prevCenter, velocityBefore, m)) return;
        _anchorLocal = BodyMetrics.CenterLocal;
        _anchorWorld = Physics.Center;

        // The body rights itself while flying (hood up), with a little spin from the throw, and gets its
        // feet under itself just before touching down (so the landing needs no big correction).
        var floor = World.FloorBelow(Physics.Center.X, Physics.Center.Y + m.FeetOffsetPx - 1);
        var toFloor = floor is double fy && Physics.Velocity.Y > 1 ? (fy - Physics.Center.Y - m.FeetOffsetPx) / Physics.Velocity.Y : double.MaxValue;
        var brace = MathUtil.Clamp01(1 - toFloor / 0.45);
        _tilt = GrabController.NormalizeDeg(_tilt);
        var acc = -(40 + 110 * brace) * _tilt - (5 + 14 * brace) * _tiltVel;
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
        // Continuity: the body's centre stays exactly where it was drawn at touchdown. Any leftover tilt is
        // settled by rotating around the centre (not around the feet), so nothing jumps sideways, and the
        // tilt is never clamped (a clamp was a visible snap).
        var floorY = Physics.Center.Y + m.FeetOffsetPx;
        var mon = World.MonitorAt(new Vec2(Physics.Center.X, floorY)) ?? World.NearestMonitor(new Vec2(Physics.Center.X, floorY));
        var half = m.HalfWidthPx * 0.5;
        var fx = Math.Clamp(Physics.Center.X, mon.WorkArea.Left + half, mon.WorkArea.Right - half);
        Feet = new Vec2(fx, floorY);
        _tilt = GrabController.NormalizeDeg(_tilt);
        _settleOnCenter = Math.Abs(_tilt) > 0.5;
        _anchorLocal = _settleOnCenter ? BodyMetrics.CenterLocal : BodyMetrics.RootLocal;
        _anchorWorld = _settleOnCenter ? new Vec2(Feet.X, Feet.Y - m.FeetOffsetPx) : Feet;
        _landingImpact = l.ImpactDipPerSec;
        // A short skid in the direction of travel (it used to slide much further, which read as a jump).
        _slideVelocity = l.HorizontalDipPerSec * 0.22 * _monitorScale;
        Go(BehaviorState.Landing, $"landed {l.ImpactDipPerSec:0} dip/s", force: true);
        var hard = _thrownByUser && l.ImpactDipPerSec >= HardLandingDip;
        Mind.OnLanded(hard);
        if (hard)
        {
            Drives.OnHardLanding();
            Animation.Play(AnimClip.LandHard, force: true, restart: true);
        }
        else if (Math.Abs(_slideVelocity) > Dip(110) && Math.Sign(_slideVelocity) == Facing && !Settings.ReducedMotion)
        {
            // Visibly skids to a stop (leaning back, dust) instead of gliding in a landing pose.
            Animation.Play(AnimClip.Stop, force: true, restart: true);
        }
        else
        {
            Animation.Play(_jumpToMonitor ? AnimClip.LandMonitor : AnimClip.LandSoft, force: true, restart: true);
        }
    }

    private bool _settleOnCenter;

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
        if (Animation.Current == AnimClip.LandHard || _dizzyOnLanding)
        {
            var steps = new List<(AnimClip, double?)>();
            if (Animation.Current == AnimClip.LandHard) steps.Add((AnimClip.Recover, null));
            if (_dizzyOnLanding) steps.Add((AnimClip.Dizzy, null));
            steps.Add((AnimClip.RecoverFromThrow, null));
            _dizzyOnLanding = false;
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
