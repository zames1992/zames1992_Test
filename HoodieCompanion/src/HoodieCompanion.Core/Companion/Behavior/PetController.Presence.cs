using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Geometry;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;

namespace HoodieCompanion.Companion.Behavior;

public sealed partial class PetController
{
    public const double PoofDuration = 0.4;

    private void UpdatePresence(in PetInput input)
    {
        EffectiveMode = input.ForegroundRule == AppPresenceMode.Quiet && Mode is PresenceMode.Normal or PresenceMode.Play or PresenceMode.Company
            ? PresenceMode.Quiet
            : Mode;

        var physical = Machine.IsPhysical || Machine.State == BehaviorState.Grabbed;
        if (physical) return;

        // 2. Presence mode: Alone means gone.
        if (Mode == PresenceMode.Alone)
        {
            if (Machine.State is not (BehaviorState.Leaving or BehaviorState.Hidden or BehaviorState.Vanishing)) StartLeaving();
            return;
        }

        var cur = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);

        // Fullscreen games / video / presentations: step out of the way.
        var fullscreenHere = Settings.HideOnFullscreen && input.FullscreenMonitorId is not null && input.FullscreenMonitorId == cur.Id;
        var appHide = input.ForegroundRule == AppPresenceMode.Hide;
        var appAvoid = input.ForegroundRule == AppPresenceMode.Avoid && input.ForegroundMonitorId == cur.Id;

        if (Machine.State == BehaviorState.Hidden)
        {
            // Come back once the reason is gone.
            var reasons = HiddenReasons;
            if (reasons.HasFlag(HiddenReason.Fullscreen) && !(Settings.HideOnFullscreen && input.FullscreenMonitorId == cur.Id)) reasons &= ~HiddenReason.Fullscreen;
            if (reasons.HasFlag(HiddenReason.AppRule) && !appHide) reasons &= ~HiddenReason.AppRule;
            if (reasons != HiddenReasons)
            {
                HiddenReasons = reasons;
                if (reasons == HiddenReason.None) StartAppear();
            }
            return;
        }

        if (Machine.State is BehaviorState.Vanishing or BehaviorState.Appearing or BehaviorState.Leaving or BehaviorState.Returning) return;

        if (fullscreenHere || appHide || appAvoid)
        {
            var m = Metrics;
            var fsId = input.FullscreenMonitorId;
            var fgId = input.ForegroundMonitorId;
            // Prefer another monitor where nothing fullscreen is happening.
            var alt = Territory.StandableMonitors(m.HeightPx, m.HalfWidthPx)
                .FirstOrDefault(o => o != cur && o.Id != fsId && (!appAvoid || o.Id != fgId));
            if (alt is not null && !appHide)
            {
                var x = Territory.NearestStandableX(alt, alt.WorkArea.Center.X, m.HeightPx, m.HalfWidthPx) ?? alt.WorkArea.Center.X;
                PoofTo(new Vec2(x, alt.WorkArea.Bottom), null);
                return;
            }
            if (fullscreenHere || appHide)
            {
                HideFor(fullscreenHere ? HiddenReason.Fullscreen : HiddenReason.AppRule);
            }
        }
    }

    private void HideFor(HiddenReason reason)
    {
        HiddenReasons |= reason;
        _walkTargetX = null;
        _travel = null;
        _poofTarget = null;
        _afterPoof = () => Go(BehaviorState.Hidden, reason.ToString(), force: true);
        Go(BehaviorState.Vanishing, "hide: " + reason, force: true);
        Animation.Play(AnimClip.IdleBreathing, force: true);
    }

    // ------------------------------------------------------------------ Alone: leave the screen politely

    private void StartLeaving()
    {
        if (Machine.State is BehaviorState.Sitting or BehaviorState.Sleeping)
        {
            // Stand up first; the next frame continues leaving.
            if (Animation.Current != AnimClip.StandUp && Animation.Current != AnimClip.WakeUp) EnsureStanding(() => { });
            return;
        }
        var m = Metrics;
        var cur = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        var leftOuter = World.MonitorBeside(cur, -1) is null;
        var rightOuter = World.MonitorBeside(cur, 1) is null;
        var dl = Feet.X - cur.WorkArea.Left;
        var dr = cur.WorkArea.Right - Feet.X;
        int dir;
        if (leftOuter && rightOuter) dir = dl < dr ? -1 : 1;
        else if (leftOuter) dir = -1;
        else if (rightOuter) dir = 1;
        else dir = 0;

        _walkTargetX = null;
        _travel = null;
        _sequence.Clear();

        if (dir == 0)
        {
            // No outer edge on this monitor: wave and disappear in place.
            _exitWasEdge = false;
            _exitFeet = Feet;
            HiddenReasons |= HiddenReason.Alone;
            _afterPoof = () => Go(BehaviorState.Hidden, "alone", force: true);
            Go(BehaviorState.Vanishing, "leave (no edge)", force: true);
            return;
        }

        var exitX = dir < 0 ? cur.WorkArea.Left - m.HalfWidthPx * 1.6 : cur.WorkArea.Right + m.HalfWidthPx * 1.6;
        // Must not intentionally walk through a NO_GO area on the way out.
        var blocked = false;
        for (var x = Feet.X; dir < 0 ? x > cur.WorkArea.Left : x < cur.WorkArea.Right; x += dir * Dip(24))
        {
            if (!Territory.CanTraverse(new Vec2(x, Feet.Y), m.HeightPx)) { blocked = true; break; }
        }
        _exitWasEdge = !blocked;
        _exitFeet = new Vec2(exitX, Feet.Y);
        Go(BehaviorState.Leaving, "leave me alone", force: true);
        Machine.Phase = 0;
        _leaveSpeedDip = 0;
        Animation.Play(AnimClip.LeaveScreen, force: true, restart: true);
        _leaveDir = dir;
    }

    private int _leaveDir;
    private double _leaveSpeedDip;

    private void UpdateLeaving(double dt)
    {
        if (Mode != PresenceMode.Alone)
        {
            // Changed their mind mid-exit.
            _leavingThroughEdge = false;
            Go(BehaviorState.Idle, "stay after all", force: true);
            Animation.Play(AnimClip.IdleBreathing);
            return;
        }
        if (Machine.Phase == 0)
        {
            if (Animation.ClipTime < AnimationCatalog.Get(AnimClip.LeaveScreen).Duration) return;
            if (!_exitWasEdge)
            {
                HiddenReasons |= HiddenReason.Alone;
                _afterPoof = () => Go(BehaviorState.Hidden, "alone", force: true);
                Go(BehaviorState.Vanishing, "leave (blocked path)", force: true);
                return;
            }
            Machine.Phase = 1;
            if (_leaveDir != Facing)
            {
                Facing = _leaveDir;
            }
        }

        // Walk out through the edge of the world.
        var m = Metrics;
        _leavingThroughEdge = true;
        // Leave promptly: walk when close, hurry (up to a run) when the edge is far, ~5 s at most.
        if (_leaveSpeedDip <= 0)
        {
            var distanceDip = Math.Abs(_exitFeet.X - Feet.X) / _monitorScale;
            _leaveSpeedDip = Math.Clamp(distanceDip / 4.5, WalkSpeedDip * 1.25 * Settings.WalkSpeed, RunSpeedDip * 1.7);
        }
        var speedDip = _leaveSpeedDip;
        var hurry = speedDip > WalkSpeedDip * 1.8 && !Settings.ReducedMotion;
        var step = Dip(speedDip) * dt;
        Feet = new Vec2(Feet.X + _leaveDir * step, Feet.Y);
        _walkPhase += step / ((hurry ? ProceduralAnimator.RunStride : ProceduralAnimator.WalkStride) * m.RefToPx);
        Animation.Play(hurry ? AnimClip.Run : AnimClip.Walk);
        var gone = _leaveDir < 0 ? Feet.X <= _exitFeet.X : Feet.X >= _exitFeet.X;
        if (gone)
        {
            _leavingThroughEdge = false;
            HiddenReasons |= HiddenReason.Alone;
            Go(BehaviorState.Hidden, "left the screen", force: true);
        }
    }

    private void UpdateHidden()
    {
        Animation.Play(AnimClip.IdleBreathing);
    }

    private void ComeBack()
    {
        if (!HiddenReasons.HasFlag(HiddenReason.Alone) && Machine.State is not (BehaviorState.Leaving or BehaviorState.Vanishing))
            return;
        HiddenReasons &= ~HiddenReason.Alone;
        if (HiddenReasons != HiddenReason.None) return; // still hidden for another reason (fullscreen...)
        _leavingThroughEdge = false;

        if (Machine.State == BehaviorState.Leaving)
        {
            Go(BehaviorState.Idle, "came back", force: true);
            Animation.Play(AnimClip.IdleBreathing);
            return;
        }

        if (_exitWasEdge)
        {
            // Walk back in through the same edge.
            Feet = _exitFeet;
            Facing = -_leaveDir;
            var m = Metrics;
            _returnTargetX = _exitFeet.X - _leaveDir * (m.HalfWidthPx * 1.6 + Dip(120));
            Go(BehaviorState.Returning, "come back", force: true);
            Machine.Phase = 0;
            return;
        }
        var target = Territory.HomeFeet() ?? _exitFeet;
        Place(target, appear: true);
    }

    private double _returnTargetX;

    private void UpdateReturning(double dt)
    {
        var m = Metrics;
        if (Machine.Phase == 0)
        {
            _leavingThroughEdge = true;
            var dir = Math.Sign(_returnTargetX - Feet.X);
            var step = Math.Min(Math.Abs(_returnTargetX - Feet.X), Dip(WalkSpeedDip * 1.2 * Settings.WalkSpeed) * dt);
            Feet = new Vec2(Feet.X + dir * step, Feet.Y);
            _walkPhase += step / (ProceduralAnimator.WalkStride * m.RefToPx);
            Animation.Play(AnimClip.Walk);
            if (Math.Abs(_returnTargetX - Feet.X) < 1)
            {
                _leavingThroughEdge = false;
                Machine.Phase = 1;
                Animation.Play(AnimClip.ReturnToScreen, force: true, restart: true);
            }
            return;
        }
        if (Animation.IsFinished || Animation.Current != AnimClip.ReturnToScreen)
        {
            Go(BehaviorState.Idle, "returned", force: true);
            Animation.Play(AnimClip.IdleBreathing);
            ScheduleDecision(3);
        }
    }

    // ------------------------------------------------------------------ poof (intentional exit/entry transition)

    /// <summary>Last-resort teleport with a clear exit/entry transition (never a silent jump).</summary>
    private void PoofTo(Vec2 targetFeet, Action? then)
    {
        _poofTarget = targetFeet;
        _afterPoof = then;
        _walkTargetX = null;
        Go(BehaviorState.Vanishing, "poof", force: true);
        Animation.Play(AnimClip.IdleBreathing, force: true);
    }

    private void UpdateVanishing()
    {
        if (Machine.TimeInState < PoofDuration) return;
        if (_poofTarget is Vec2 t)
        {
            _poofTarget = null;
            var cb = _afterPoof;
            _afterPoof = null;
            var m = World.NearestMonitor(t);
            Feet = new Vec2(Math.Clamp(t.X, m.WorkArea.Left + 20, m.WorkArea.Right - 20), m.WorkArea.Bottom);
            _monitorScale = m.Scale;
            StartAppear(cb);
            return;
        }
        var after = _afterPoof;
        _afterPoof = null;
        if (after is not null) after();
        else Go(BehaviorState.Hidden, "vanished", force: true);
    }

    private Action? _afterAppear;

    private void StartAppear(Action? then = null)
    {
        _afterAppear = then;
        Go(BehaviorState.Appearing, "appear", force: true);
        Animation.Play(AnimClip.IdleBreathing, force: true);
    }

    private void UpdateAppearing()
    {
        if (Machine.TimeInState < PoofDuration) return;
        var cb = _afterAppear;
        _afterAppear = null;
        Go(BehaviorState.Idle, "appeared", force: true);
        ScheduleDecision(2);
        cb?.Invoke();
    }

    private void ApplyPoof(ref Pose pose)
    {
        double k;
        if (Machine.State == BehaviorState.Vanishing) k = 1 - MathUtil.Clamp01(Machine.TimeInState / PoofDuration);
        else if (Machine.State == BehaviorState.Appearing) k = MathUtil.Clamp01(Machine.TimeInState / PoofDuration);
        else return;
        pose.Opacity *= MathUtil.SmoothStep(k);
        if (!Settings.ReducedMotion)
        {
            // Shrink into / grow out of the ground, like pulling the hood down.
            pose.Scale *= 0.25 + 0.75 * MathUtil.EaseOutBack(k);
        }
    }
}
