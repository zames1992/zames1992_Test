using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Animation;

/// <summary>
/// Plays one primary clip at a time with priority/interruptibility rules, cross-fades between clips
/// and layers breathing, blinking and cursor-look on top.
/// Behavior state (what Hoodie is doing) lives in the state machine; this only decides how it looks.
/// </summary>
public sealed class AnimationController
{
    private readonly Random _rng;
    private Pose _from = Pose.Neutral;
    private Pose _last = Pose.Neutral;
    private double _blend = 1;
    private double _blendDuration = 0.2;
    private double _nextBlink;
    private double _blinkT = -1;
    private readonly Dictionary<AnimClip, double> _lastPlayed = new();
    private double _lookX, _lookY, _lookWeight;

    public AnimationController(Random? rng = null)
    {
        _rng = rng ?? new Random();
        _nextBlink = 2 + _rng.NextDouble() * 3;
    }

    public AnimClip Current { get; private set; } = AnimClip.IdleBreathing;
    public ClipInfo CurrentInfo => AnimationCatalog.Get(Current);
    public double ClipTime { get; private set; }
    public double Now { get; private set; }

    /// <summary>True when a one-shot clip has reached its end (loops are never finished).</summary>
    public bool IsFinished => !CurrentInfo.Loop && ClipTime >= CurrentInfo.Duration;

    public PoseEffect Effect { get; private set; }
    public double EffectTime { get; private set; }

    public bool ReducedMotion { get; set; }

    /// <summary>Desired look direction in the rig's local frame (-1..1). Weight 0 disables the overlay.</summary>
    public void SetLook(double x, double y, double weight)
    {
        _lookX = Math.Clamp(x, -1, 1);
        _lookY = Math.Clamp(y, -1, 1);
        _lookWeight = MathUtil.Clamp01(weight);
    }

    // Secondary motion springs (degrees): driven by body acceleration, they make the hood, strings and
    // sleeves lag and overshoot, which gives the cut-out rig its "plastic" follow-through.
    private double _accel;
    private bool _walking;
    private double _secHead, _secHeadV, _secArms, _secArmsV, _secStrings, _secStringsV;

    /// <summary>Body acceleration along the facing axis (DIP/s², rig frame) for follow-through.</summary>
    public void SetBodyMotion(double accelDip, bool walking)
    {
        _accel = Math.Clamp(accelDip, -6000, 6000);
        _walking = walking;
    }

    private static void Spring(ref double x, ref double v, double target, double stiffness, double damping, double dt)
    {
        var a = (target - x) * stiffness - v * damping;
        v += a * dt;
        x += v * dt;
    }

    public bool IsOnCooldown(AnimClip clip)
    {
        var info = AnimationCatalog.Get(clip);
        return info.Cooldown > 0 && _lastPlayed.TryGetValue(clip, out var at) && Now - at < info.Cooldown;
    }

    /// <summary>
    /// Requests a clip. Returns false if the current clip outranks it and may not be interrupted.
    /// <paramref name="force"/> bypasses priority (physics and user manipulation always win).
    /// </summary>
    public bool Play(AnimClip clip, bool force = false, bool restart = false)
    {
        if (clip == Current && !restart) return true;
        var cur = CurrentInfo;
        var next = AnimationCatalog.Get(clip);
        if (!force)
        {
            var currentActive = cur.Loop || ClipTime < cur.Duration;
            if (currentActive && !cur.Interruptible && cur.Priority > next.Priority) return false;
        }

        _from = _last;
        _blend = 0;
        _blendDuration = ReducedMotion ? Math.Max(0.25, next.BlendIn) : next.BlendIn;
        Current = clip;
        ClipTime = 0;
        _lastPlayed[clip] = Now;
        return true;
    }

    public Pose Update(double dt, in AnimContext context)
    {
        Now += dt;
        ClipTime += dt;
        var ctx = context;
        ctx.ReducedMotion = ReducedMotion;

        var frame = ProceduralAnimator.Evaluate(Current, ClipTime, ctx);
        var pose = frame.Pose;
        Effect = frame.Effect;
        EffectTime = frame.EffectTime;
        var info = CurrentInfo;

        // Blink overlay.
        if (info.AllowBlink)
        {
            _nextBlink -= dt;
            if (_nextBlink <= 0 && _blinkT < 0)
            {
                _blinkT = 0;
                _nextBlink = 2.5 + _rng.NextDouble() * 3.5;
                if (_rng.NextDouble() < 0.15) _nextBlink = 0.3; // occasional double blink
            }
            if (_blinkT >= 0)
            {
                _blinkT += dt;
                var d = AnimationCatalog.Get(AnimClip.Blink).Duration;
                var closed = MathUtil.Bump(_blinkT / d);
                pose.EyeOpen *= 1 - 0.95 * closed;
                if (_blinkT >= d) _blinkT = -1;
            }
        }

        // Look overlay (cursor / points of interest). Clip-authored look is kept, overlay adds on top.
        if (info.AllowLook && _lookWeight > 0)
        {
            var w = _lookWeight;
            pose.LookX = Math.Clamp(pose.LookX + _lookX * w, -1, 1);
            pose.LookY = Math.Clamp(pose.LookY + _lookY * w, -1, 1);
            pose.HeadRot += -_lookX * 2.5 * w + _lookY * 1.5 * w;
            pose.HeadDx += _lookX * 3 * w;
        }

        // Cross-fade from the previous pose.
        if (_blend < 1)
        {
            _blend = _blendDuration <= 0 ? 1 : Math.Min(1, _blend + dt / _blendDuration);
            pose = Pose.Lerp(_from, pose, MathUtil.SmoothStep(_blend));
        }

        // Follow-through: when the body accelerates forward the hood tips back and the sleeves trail,
        // when it brakes they swing forward and settle with a small overshoot.
        if (dt > 0)
        {
            var k = ReducedMotion ? 0.35 : 1.0;
            var push = _accel / 1000.0 * k;
            var sdt = Math.Min(dt, 1 / 30.0);
            Spring(ref _secHead, ref _secHeadV, Math.Clamp(-push * 2.2, -6, 6), 90, 9, sdt);
            Spring(ref _secArms, ref _secArmsV, Math.Clamp(push * 5, -14, 14), 70, 7, sdt);
            Spring(ref _secStrings, ref _secStringsV, Math.Clamp(push * 7, -16, 16), 55, 5, sdt);
            var w = info.AllowBreath || _walking ? 1.0 : 0.5;
            pose.HeadRot = Math.Clamp(pose.HeadRot + _secHead * w, -20, 20);
            pose.ArmLRot += _secArms * w;
            pose.ArmRRot += _secArms * w;
            pose.StringsRot += _secStrings * w;
        }

        _last = pose;
        return pose;
    }

    public Pose LastPose => _last;
}
