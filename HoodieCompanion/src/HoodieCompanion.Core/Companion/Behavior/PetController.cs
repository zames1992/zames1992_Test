using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Interaction;
using HoodieCompanion.Companion.Physics;
using HoodieCompanion.Features.SystemMonitor;
using HoodieCompanion.Geometry;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;

namespace HoodieCompanion.Companion.Behavior;

/// <summary>Everything the platform layer measures each frame.</summary>
public struct PetInput
{
    public double Dt;
    public Vec2 Cursor;
    public bool LeftButtonDown;
    public double UserIdleSeconds;
    /// <summary>Monitor currently showing a fullscreen app/video/presentation, if any.</summary>
    public string? FullscreenMonitorId;
    /// <summary>Rule configured for the foreground process.</summary>
    public AppPresenceMode ForegroundRule;
    /// <summary>Monitor holding the foreground window.</summary>
    public string? ForegroundMonitorId;
}

/// <summary>What the renderer needs to draw one frame.</summary>
public readonly record struct RenderState(
    RigTransform Transform,
    Pose Pose,
    PoseEffect Effect,
    double EffectTime,
    bool Visible,
    BehaviorState State,
    AnimClip Clip,
    bool Calm,
    MonitorInfo Monitor,
    double MonitorScale,
    WorldProp? Prop = null);

public enum AlertKind
{
    Reminder,
    Timer,
}

public enum PetCommand
{
    ComeHere,
    StayHere,
    YoureFree,
    GoHome,
    BeQuiet,
    LetsPlay,
    LeaveMeAlone,
    ComeBack,
    ShowBackpack,
    Normal,
    Focus,
    Company,
}

/// <summary>
/// The companion's brain and body. Pure logic: no WPF, no Win32. The platform layer feeds
/// <see cref="PetInput"/> every frame and draws the returned <see cref="RenderState"/>.
///
/// Priority of intent (highest first):
/// 1. system safety / emergency hide (handled by the host, which stops updating and hides the window)
/// 2. user presence mode (Alone, fullscreen and app rules)
/// 3. NO_GO / spatial restrictions (TerritoryService)
/// 4. direct user physical interaction (grab, throw)
/// 5. active reminders / timers
/// 6. utility actions (backpack, notes)
/// 7. character internal drives
/// 8. autonomous idle
/// </summary>
public sealed partial class PetController
{
    public const double BaseHeightDip = 150;
    public const double WalkSpeedDip = 72;
    public const double RunSpeedDip = 175;
    public const double HardLandingDip = 1500;

    private readonly Random _rng;
    private double _time;
    private double _monitorScale = 1;

    // Render anchor
    private Vec2 _anchorLocal = BodyMetrics.RootLocal;
    private Vec2 _anchorWorld;
    private double _tilt;
    private double _tiltVel;

    public PetController(WorldGeometry world, TerritoryService territory, AppSettings settings, Random? rng = null)
    {
        _rng = rng ?? new Random();
        World = world;
        Territory = territory;
        Settings = settings;
        Animation = new AnimationController(_rng);
        Brain = new BehaviorController(_rng);
        Mode = settings.RestorePresenceOnStart ? settings.CurrentPresenceMode : settings.DefaultPresenceMode;
        _modeBeforeAlone = Mode == PresenceMode.Alone ? PresenceMode.Normal : Mode;
        var p = world.Primary.WorkArea;
        Feet = new Vec2(p.Right - p.Width * 0.18, p.Bottom);
        _monitorScale = world.Primary.Scale;
        _nextDecisionAt = 2;
    }

    public WorldGeometry World { get; private set; }
    public TerritoryService Territory { get; }
    public AppSettings Settings { get; }
    public AnimationController Animation { get; }
    public PetStateMachine Machine { get; } = new();
    public CharacterDrives Drives { get; } = new();
    public CursorInteractionService Cursor { get; } = new();
    public BehaviorController Brain { get; }
    public PetPhysics Physics { get; } = new();
    public GrabController Grab { get; } = new();
    public ThrowController Thrower { get; } = new();

    public Vec2 Feet { get; private set; }
    public int Facing { get; private set; } = -1;
    public PresenceMode Mode { get; private set; }
    public HiddenReason HiddenReasons { get; private set; }
    public double Time => _time;
    public BehaviorState State => Machine.State;
    public bool HasItems { get; set; }

    /// <summary>Mode after applying the foreground app rule (Quiet rules calm Hoodie down).</summary>
    public PresenceMode EffectiveMode { get; private set; }

    public BodyMetrics Metrics => BodyMetrics.For(BaseHeightDip * Settings.Scale, _monitorScale);

    public MonitorInfo CurrentMonitor => World.NearestMonitor(Machine.State is BehaviorState.Airborne ? Physics.Center : Feet);

    public RigTransform Transform => new(_anchorWorld, _anchorLocal, _tilt, Facing, Metrics.RefToPx);

    public event Action<PresenceMode>? ModeChanged;
    public event Action<string>? Log;

    /// <summary>Places Hoodie standing on the floor at the given x (used on start-up and respawn).</summary>
    public void Place(Vec2 feet, bool appear)
    {
        var m = World.NearestMonitor(feet);
        var x = Math.Clamp(feet.X, m.WorkArea.Left + 40, m.WorkArea.Right - 40);
        Feet = new Vec2(x, m.WorkArea.Bottom);
        _monitorScale = m.Scale;
        _anchorLocal = BodyMetrics.RootLocal;
        _anchorWorld = Feet;
        _tilt = 0;
        _walkTargetX = null;
        _travel = null;
        DropActivity();
        DropClimb();
        if (appear) StartAppear();
        else Go(BehaviorState.Idle, "placed", force: true);
    }

    public void UpdateWorld(WorldGeometry world)
    {
        World = world;
        Territory.UpdateWorld(world);
        if (Machine.IsPhysical) return;
        var m = World.MonitorAt(Feet);
        if (m is null || Math.Abs(m.WorkArea.Bottom - Feet.Y) > 2)
        {
            var near = World.NearestMonitor(Feet);
            var inside = near.WorkArea.Inflate(40, 40).Contains(Feet);
            Place(new Vec2(Feet.X, near.WorkArea.Bottom), appear: !inside);
        }
    }

    public RenderState Update(in PetInput input)
    {
        var dt = Math.Clamp(input.Dt, 0, 0.1);
        _time += dt;
        _userIdle = input.UserIdleSeconds;
        Machine.Tick(dt);
        Drives.Update(dt, Machine.State, _run && Machine.State == BehaviorState.Walking);
        UpdateScale(dt);

        UpdatePresence(input);
        UpdateCursor(input, dt);

        PumpPendingActivity();
        switch (Machine.State)
        {
            case BehaviorState.Idle: UpdateIdle(); break;
            case BehaviorState.Walking: UpdateWalking(dt); break;
            case BehaviorState.Turning: UpdateTurning(); break;
            case BehaviorState.Sitting: UpdateSitting(); break;
            case BehaviorState.Sleeping: UpdateSleeping(); break;
            case BehaviorState.Emote: UpdateEmote(); break;
            case BehaviorState.Grabbed: UpdateGrabbed(dt, input.Cursor); break;
            case BehaviorState.Airborne: UpdateAirborne(dt); break;
            case BehaviorState.Jumping: UpdateJumping(); break;
            case BehaviorState.Landing: UpdateLanding(dt); break;
            case BehaviorState.Recovering: UpdateSequence(); break;
            case BehaviorState.ReceivingItem: UpdateSequence(); break;
            case BehaviorState.Activity: UpdateActivity(); break;
            case BehaviorState.Climbing: UpdateClimbing(dt); break;
            case BehaviorState.Alert: break;
            case BehaviorState.Leaving: UpdateLeaving(dt); break;
            case BehaviorState.Hidden: UpdateHidden(); break;
            case BehaviorState.Returning: UpdateReturning(dt); break;
            case BehaviorState.Vanishing: UpdateVanishing(); break;
            case BehaviorState.Appearing: UpdateAppearing(); break;
        }

        if ((!Machine.IsPhysical || Machine.State == BehaviorState.Climbing) && Machine.State != BehaviorState.Grabbed)
        {
            // Grounded: the rig stands on its feet; any leftover tilt settles quickly.
            _anchorLocal = BodyMetrics.RootLocal;
            _anchorWorld = Feet;
            var acc = -160 * _tilt - 22 * _tiltVel;
            _tiltVel += acc * dt;
            _tilt += _tiltVel * dt;
            if (Math.Abs(_tilt) < 0.05 && Math.Abs(_tiltVel) < 0.5) { _tilt = 0; _tiltVel = 0; }
        }

        var m = Metrics;
        var ctx = new AnimContext
        {
            Time = _time,
            WalkPhase = _walkPhase,
            AirVy = Math.Clamp(Physics.Velocity.Y / m.Dip(1400), -1.5, 1.5),
            SwingAngle = Grab.Angle,
            SwingSpeed = Grab.AngularVelocity,
        };
        Animation.ReducedMotion = Settings.ReducedMotion;
        var pose = Animation.Update(dt, ctx);
        ApplyPoof(ref pose);

        // Calm frames (slow breathing only) can be drawn at a lower rate to keep CPU use tiny.
        var still = _lookWeight < 0.05 && Math.Abs(_tilt) < 0.01 && Machine.TimeInState > 0.4;
        var calm = Machine.State is BehaviorState.Sleeping or BehaviorState.Hidden
                   || (Machine.State is BehaviorState.Sitting && Animation.Current == AnimClip.SitIdle && still)
                   || (Machine.State is BehaviorState.Idle && Animation.Current == AnimClip.IdleBreathing && still);
        return new RenderState(Transform, pose, Animation.Effect, Animation.EffectTime, Machine.State != BehaviorState.Hidden,
            Machine.State, Animation.Current, calm, CurrentMonitor, _monitorScale, CurrentProp);
    }

    private void UpdateScale(double dt)
    {
        var probe = Machine.State is BehaviorState.Airborne ? Physics.Center : Machine.State is BehaviorState.Grabbed ? Grab.Pivot : Feet;
        var target = World.NearestMonitor(probe).Scale;
        // Ease between monitor scales so the character keeps its perceived size without popping.
        _monitorScale = Math.Abs(_monitorScale - target) < 0.002 ? target : MathUtil.Approach(_monitorScale, target, 10, dt);
    }

    private bool Go(BehaviorState next, string reason, bool force = false)
    {
        var ok = Machine.TransitionTo(next, reason, force);
        if (ok) Log?.Invoke($"{Machine.Previous} -> {next}: {reason}");
        return ok;
    }

    private double Dip(double v) => v * _monitorScale;

    private double DipScale => _monitorScale;

    public bool IsOverPet(Vec2 world) => Transform.Bounds(RigTransform.BodyLocal).Contains(world);

    public Vec2 HeadWorld => Transform.LocalToWorld(new Vec2(243, 250));
}
