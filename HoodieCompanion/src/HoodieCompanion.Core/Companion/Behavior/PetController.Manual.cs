using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Geometry;
using HoodieCompanion.Settings;

namespace HoodieCompanion.Companion.Behavior;

/// <summary>What the user is pressing while driving Hoodie by hand (see <see cref="PetController.SetManualControl"/>).</summary>
public struct ManualInput
{
    /// <summary>-1 left, 0 none, +1 right.</summary>
    public int MoveX;
    public bool Run;
    /// <summary>Pressed this frame (edge), not held.</summary>
    public bool Jump;
    /// <summary>Up: climb onto a window top / the screen side when there is one nearby.</summary>
    public bool Up;
    /// <summary>Down: hop down from a platform.</summary>
    public bool Down;
    /// <summary>Sit / stand up.</summary>
    public bool Sit;
}

public sealed partial class PetController
{
    private bool _manual;
    private bool _manualWalking;
    private double _manualIdleFor;

    /// <summary>True while the user drives Hoodie with the keyboard (autonomous decisions are paused).</summary>
    public bool ManualControl => _manual;

    /// <summary>Seconds since the last key the user pressed in manual mode (the host auto-exits after a while).</summary>
    public double ManualIdleSeconds => _manualIdleFor;

    public event Action<bool>? ManualControlChanged;

    /// <summary>
    /// Turns manual control on or off. The user's control always wins over autonomy: while it is on, Hoodie makes
    /// no decisions of its own, drops what it was doing, and only moves when asked. Territory still applies —
    /// those are the user's own rules too. Nothing about the keys is remembered.
    /// </summary>
    public void SetManualControl(bool on)
    {
        if (_manual == on) return;
        _manual = on;
        _manualIdleFor = 0;
        _manualWalking = false;
        if (on)
        {
            Mind.CurrentIntent = "Manual";
            Mind.CurrentReason = "you're driving";
            // Drop autonomous plans but keep physical states (a throw finishes, a climb ends).
            _sequence.Clear();
            if (Machine.State is BehaviorState.Walking or BehaviorState.Emote or BehaviorState.Activity or BehaviorState.ReceivingItem or BehaviorState.Turning)
            {
                if (_activity is not null) StopActivity();
                _walkTargetX = null;
                _travel = null;
                if (Machine.State is BehaviorState.Walking or BehaviorState.Emote or BehaviorState.ReceivingItem) Go(BehaviorState.Idle, "manual control", force: true);
            }
            if (Machine.State == BehaviorState.Sleeping) WakeUp(null);
        }
        else
        {
            if (Machine.State == BehaviorState.Walking && _manualWalking)
            {
                _walkTargetX = null;
                Go(BehaviorState.Idle, "manual control ended", force: true);
            }
            ScheduleDecision(3 + _rng.NextDouble() * 4);
        }
        Log?.Invoke(on ? "manual control on" : "manual control off");
        ManualControlChanged?.Invoke(on);
    }

    /// <summary>Applies this frame's keys. Called from Update while manual control is on.</summary>
    private void UpdateManual(in ManualInput k, double dt)
    {
        var any = k.MoveX != 0 || k.Jump || k.Up || k.Down || k.Sit;
        _manualIdleFor = any ? 0 : _manualIdleFor + dt;
        // Keep autonomy quiet: never let a decision fire while the user drives.
        _nextDecisionAt = _time + 60;

        // Only states the user can act from. Physical states (airborne, grabbed, climbing) finish by themselves.
        if (Machine.State is not (BehaviorState.Idle or BehaviorState.Walking or BehaviorState.Sitting or BehaviorState.Emote or BehaviorState.Turning)) return;
        if (Machine.State == BehaviorState.Turning) return;

        var m = Metrics;
        if (k.Sit)
        {
            if (Machine.State == BehaviorState.Sitting) StandUp(null);
            else if (Machine.State is BehaviorState.Idle or BehaviorState.Emote) { _walkTargetX = null; BeginSit(false); }
            return;
        }
        if (k.Jump && Machine.State is BehaviorState.Idle or BehaviorState.Walking && !Settings.ReducedMotion)
        {
            _manualWalking = false;
            _walkTargetX = null;
            var ahead = new Vec2(Feet.X + Facing * Dip(k.MoveX != 0 ? 150 : 40), Feet.Y);
            var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
            ahead = new Vec2(Math.Clamp(ahead.X, mon.WorkArea.Left + m.HalfWidthPx, mon.WorkArea.Right - m.HalfWidthPx), Feet.Y);
            if (!Territory.CanStop(ahead, m.HeightPx)) ahead = Feet;
            JumpTo(ahead, extraApexDip: 70);
            return;
        }
        if (k.Up && Machine.State is BehaviorState.Idle or BehaviorState.Walking)
        {
            _manualWalking = false;
            _walkTargetX = null;
            if (_onSurface is null && (VisitSurface() || ClimbWall())) return;
            if (Machine.State == BehaviorState.Walking) Go(BehaviorState.Idle, "nothing to climb", force: true);
            PlayEmoteAt(AnimClip.LookUp, null, ReactionPriority.Idle);
            return;
        }
        if (k.Down && _onSurface is not null && Machine.State is BehaviorState.Idle or BehaviorState.Walking)
        {
            _manualWalking = false;
            HopDown("you asked");
            return;
        }

        if (k.MoveX != 0)
        {
            if (Machine.State == BehaviorState.Sitting) { StandUp(null); return; }
            if (Machine.State == BehaviorState.Emote) Go(BehaviorState.Idle, "manual move", force: true);
            _run = k.Run && !Settings.ReducedMotion;
            _chase = false;
            _walkIgnoresTerritory = false;
            if (k.MoveX != Facing)
            {
                _manualWalking = true;
                _walkTargetX = Feet.X + k.MoveX * Dip(4000);
                TurnThen(() => { if (_manual && _manualWalking) Go(BehaviorState.Walking, "manual walk", force: true); else Go(BehaviorState.Idle, "turned", force: true); });
                return;
            }
            _manualWalking = true;
            // A far target in the pressed direction; UpdateWalking stops at monitor edges and territory as usual.
            _walkTargetX = Feet.X + k.MoveX * Dip(4000);
            if (Machine.State != BehaviorState.Walking) Go(BehaviorState.Walking, "manual walk", force: true);
        }
        else if (_manualWalking && Machine.State == BehaviorState.Walking)
        {
            _manualWalking = false;
            StopWalk("key released");
        }
    }
}
