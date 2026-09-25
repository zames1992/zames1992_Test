using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Interaction;
using HoodieCompanion.Companion.Memory;
using HoodieCompanion.Companion.Perception;
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
    /// <summary>Local hour of day (0-23) for time-of-day moods; null = unknown.</summary>
    public int? LocalHour;
    /// <summary>What the platform perceives about the PC (foreground app, load, typing...).</summary>
    public EnvironmentSample Env;
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
        Mind = new Mind(Drives);
        Intents = new IntentSystem(Brain, _rng);
        // Until the host attaches the persistent memory: an in-memory one whose personality follows the rng seed.
        var memory = new CompanionMemory(null);
        memory.Doc.PersonalitySeed = _rng.Next();
        Memory = memory;
        Perception.IsNewApp = p => Memory.SeeApp(p, AppCategories.Of(p, false));
        Reactions = new ReactionSystem(_rng);
        IdleDirector = new IdleDirector(_rng);
        IdleDirector.Allowed = c => c switch
        {
            AnimClip.Spin => Memory.IsUnlocked("spin"),
            AnimClip.Dance => Memory.IsUnlocked("dance"),
            _ => true,
        };
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
    /// <summary>Hidden inner parameters (energy, mood, curiosity, boredom, sleepiness, stress, affection...).</summary>
    public Mind Mind { get; }
    public IntentSystem Intents { get; }
    /// <summary>What Hoodie notices about the PC.</summary>
    public PerceptionSystem Perception { get; } = new();

    private CompanionMemory _memory = null!;

    /// <summary>What Hoodie remembers (local). Setting it also derives the stable personality from it.</summary>
    public CompanionMemory Memory
    {
        get => _memory;
        set
        {
            _memory = value;
            Mind.Traits = ShapePersonality(value);
        }
    }
    /// <summary>Event → reaction table with priorities and cooldowns.</summary>
    public ReactionSystem Reactions { get; }
    public IdleDirector IdleDirector { get; }
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
        _tiltVel = 0;
        _settleOnCenter = false;
        _walkTargetX = null;
        _travel = null;
        _onSurface = null;
        DropActivity();
        DropClimb();
        if (appear) StartAppear();
        else Go(BehaviorState.Idle, "placed", force: true);
    }

    public void UpdateWorld(WorldGeometry world)
    {
        World = world;
        Territory.UpdateWorld(world);
        if (Machine.IsPhysical || _onSurface is not null) return;
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
        if (input.LocalHour is int hour) UpdateClock(hour);
        // Perception and memory run on wall time (capped): a slow or paused frame loop must not lose time together.
        UpdatePerception(input, Math.Clamp(input.Dt, 0, 5));
        var afkChange = Mind.Update(dt, Machine.State, input.UserIdleSeconds, Machine.State == BehaviorState.Walking);
        if (afkChange is AfkPhase oldPhase) OnAfkPhaseChanged(oldPhase, Mind.Afk);
        if (Machine.State == BehaviorState.Grabbed) Mind.OnHeldTick(dt, Grab.AngularVelocity);
        UpdateScale(dt);

        UpdatePresence(input);
        UpdateCursor(input, dt);
        UpdateSurface();

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
            case BehaviorState.Alert: UpdateAlert(); break;
            case BehaviorState.Leaving: UpdateLeaving(dt); break;
            case BehaviorState.Hidden: UpdateHidden(); break;
            case BehaviorState.Returning: UpdateReturning(dt); break;
            case BehaviorState.Vanishing: UpdateVanishing(); break;
            case BehaviorState.Appearing: UpdateAppearing(); break;
        }

        // Standing on something (including while landing / recovering: the skid after a landing must be
        // drawn as it happens, not applied all at once afterwards).
        if ((!Machine.IsPhysical || Machine.State is BehaviorState.Climbing or BehaviorState.Landing or BehaviorState.Recovering)
            && Machine.State != BehaviorState.Grabbed)
        {
            // Grounded: the rig stands on its feet; any leftover tilt settles quickly. Right after a landing
            // it settles around the body's centre (see Land) so the body never jumps sideways.
            var acc = -140 * _tilt - 20 * _tiltVel;
            _tiltVel += acc * dt;
            _tilt += _tiltVel * dt;
            if (Math.Abs(_tilt) < 0.05 && Math.Abs(_tiltVel) < 0.5) { _tilt = 0; _tiltVel = 0; _settleOnCenter = false; }
            if (_settleOnCenter)
            {
                _anchorLocal = BodyMetrics.CenterLocal;
                _anchorWorld = new Vec2(Feet.X, Feet.Y - Metrics.FeetOffsetPx);
            }
            else
            {
                _settleOnCenter = false;
                _anchorLocal = BodyMetrics.RootLocal;
                _anchorWorld = Feet;
            }
        }

        var m = Metrics;
        UpdateBodyMotion(dt);
        var ctx = new AnimContext
        {
            Time = _time,
            WalkPhase = _walkPhase,
            AirVy = Math.Clamp(Physics.Velocity.Y / m.Dip(1400), -1.5, 1.5),
            SwingAngle = Grab.Angle,
            SwingSpeed = Grab.AngularVelocity,
            HeldItem = HeldItem,
        };
        Animation.ReducedMotion = Settings.ReducedMotion;
        var pose = Animation.Update(dt, ctx);
        ApplyPoof(ref pose);

        // Calm frames (slow breathing only) can be drawn at a lower rate to keep CPU use tiny.
        var still = _lookWeight < 0.05 && Math.Abs(_tilt) < 0.01 && Machine.TimeInState > 0.4;
        var calm = Machine.State is BehaviorState.Sleeping or BehaviorState.Hidden
                   || (Machine.State is BehaviorState.Sitting && Animation.Current == AnimClip.SitIdle && still)
                   || (Machine.State is BehaviorState.Idle && Animation.Current is AnimClip.IdleBreathing or AnimClip.IdleBreathing2 && still);
        return new RenderState(Transform, pose, Animation.Effect, Animation.EffectTime, Machine.State != BehaviorState.Hidden,
            Machine.State, Animation.Current, calm, CurrentMonitor, _monitorScale, CurrentProp);
    }

    // Secondary motion: the head, strings and sleeves lag behind the body when it speeds up or stops.
    private Vec2 _lastBodyPos;
    private double _bodyVelX;
    private bool _hasBodyPos;

    private void UpdateBodyMotion(double dt)
    {
        var pos = Machine.State is BehaviorState.Airborne ? Physics.Center : Machine.State is BehaviorState.Grabbed ? Grab.Pivot : Feet;
        if (!_hasBodyPos || dt <= 0)
        {
            _lastBodyPos = pos;
            _hasBodyPos = true;
            return;
        }
        var v = (pos.X - _lastBodyPos.X) / dt / Math.Max(0.1, _monitorScale);
        _lastBodyPos = pos;
        if (Math.Abs(v) > 6000) v = 0; // teleports (respawn, placement) are not motion
        var acc = (v - _bodyVelX) / dt;
        _bodyVelX = v;
        // In the rig's own frame (facing left = forward is -X).
        Animation.SetBodyMotion(-Facing * acc, Machine.State is BehaviorState.Walking);
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
