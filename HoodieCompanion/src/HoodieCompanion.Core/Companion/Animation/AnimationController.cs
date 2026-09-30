using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Animation;

/// <summary>
/// Short additive moves layered on top of whatever clip is playing, so a click is answered in every state
/// (seated, busy with a laptop, on a ladder...) without breaking the state's own animation.
/// </summary>
public enum Gesture
{
    None,
    /// <summary>Little nod and happy squint: "boop".</summary>
    Boop,
    /// <summary>Looks up and waves without getting up.</summary>
    SeatedWave,
    /// <summary>Looks up from what it is doing.</summary>
    Glance,
    /// <summary>Holds on with one hand and waves with the other (ladder, rope, screen edge).</summary>
    HoldWave,
    /// <summary>"I'm okay": two nods and a quick dust-off.</summary>
    OkayNod,
    /// <summary>Looks back over its shoulder and waves while leaving.</summary>
    LookBackWave,
    /// <summary>Gives its ball a big kick.</summary>
    KickBall,
    /// <summary>Uses the thing in its hands (sips from the mug, lifts the book to show it).</summary>
    PropUse,
}

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
    private Gesture _gesture;
    private double _gestureT = -1;
    private double _pressT = -1;

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

    /// <summary>The gesture being layered on top of the clip right now (None when idle).</summary>
    public Gesture ActiveGesture => _gestureT >= 0 ? _gesture : Gesture.None;

    /// <summary>True while the press "squash" is still visible.</summary>
    public bool PressActive => _pressT >= 0;

    public static double GestureDuration(Gesture g) => g switch
    {
        Gesture.Boop => 0.55,
        Gesture.SeatedWave => 1.3,
        Gesture.Glance => 1.4,
        Gesture.HoldWave => 1.3,
        Gesture.OkayNod => 0.9,
        Gesture.LookBackWave => 1.2,
        Gesture.KickBall => 0.8,
        Gesture.PropUse => 1.1,
        _ => 0,
    };

    /// <summary>Starts (or restarts) a gesture on top of the current clip.</summary>
    public void PlayGesture(Gesture g)
    {
        _gesture = g;
        _gestureT = g == Gesture.None ? -1 : 0;
    }

    /// <summary>The mouse button went down on Hoodie: a small squash shows the touch was felt.</summary>
    public void Press() => _pressT = 0;

    /// <summary>Layers a gesture onto a pose. <paramref name="t"/> is seconds since the gesture started.</summary>
    public static void ApplyGesture(ref Pose p, Gesture g, double t, bool reducedMotion)
    {
        var d = GestureDuration(g);
        if (d <= 0 || t < 0 || t > d) return;
        var u = t / d;
        var w = MathUtil.SmoothStep(u / 0.18) * (1 - MathUtil.SmoothStep((u - 0.78) / 0.22));
        switch (g)
        {
            case Gesture.Boop:
            {
                var nod = MathUtil.Bump(u / 0.55);
                p.HeadDy += 9 * nod;
                p.HeadRot += 5 * nod;
                p.BodySy *= 1 - 0.03 * nod;
                p.EyeOpen *= 1 - 0.8 * MathUtil.Bump((u - 0.1) / 0.45);
                break;
            }
            case Gesture.Glance:
                p.HeadDy -= 6 * w;
                p.HeadRot += 4 * w;
                p.LookY = MathUtil.Lerp(p.LookY, -0.35, w);
                p.EyeScale *= 1 + 0.1 * w;
                break;
            case Gesture.SeatedWave:
            case Gesture.HoldWave:
            case Gesture.LookBackWave:
            {
                var raise = MathUtil.SmoothStep(u / 0.2) * (1 - MathUtil.SmoothStep((u - 0.8) / 0.2));
                var swing = reducedMotion ? 8 : 22;
                p.ArmRRot = MathUtil.Lerp(p.ArmRRot, -(125 + swing * Math.Sin(t * 14)), raise);
                p.ArmRDy = MathUtil.Lerp(p.ArmRDy, -8, raise);
                p.HeadRot += 4 * raise;
                if (g == Gesture.SeatedWave) p.LookY = MathUtil.Lerp(p.LookY, -0.25, raise);
                break;
            }
            case Gesture.OkayNod:
            {
                var nods = MathUtil.Bump(u / 0.35) + MathUtil.Bump((u - 0.4) / 0.35);
                p.HeadDy += 7 * nods;
                p.ArmLRot += 20 * MathUtil.Bump((u - 0.1) / 0.4);
                p.ArmRRot -= 20 * MathUtil.Bump((u - 0.35) / 0.4);
                break;
            }
            case Gesture.KickBall:
            {
                var kick = MathUtil.Bump(u / 0.4);
                p.LegLRot += 40 * kick;
                p.LegLDy -= 12 * kick;
                p.ArmLRot += 20 * kick;
                p.ArmRRot -= 20 * kick;
                var fly = MathUtil.SmoothStep((u - 0.15) / 0.35) * (1 - MathUtil.SmoothStep((u - 0.6) / 0.4));
                p.BallDx -= 260 * fly;
                p.BallDy -= (reducedMotion ? 40 : 130) * MathUtil.Bump((u - 0.15) / 0.7);
                p.BallRot -= 540 * fly;
                p.LookX = MathUtil.Lerp(p.LookX, -0.8, fly);
                break;
            }
            case Gesture.PropUse:
                p.ArmLDy -= 14 * w;
                p.ArmRDy -= 14 * w;
                p.ArmLRot -= 10 * w;
                p.ArmRRot += 10 * w;
                p.MugTilt += 28 * w;
                p.HeadDy -= 4 * w;
                p.LookY = MathUtil.Lerp(p.LookY, 0.2, w);
                break;
        }
    }

    /// <summary>Press squash envelope: full on the first frame, gone after ~0.22 s.</summary>
    public static double PressEnvelope(double t) => t < 0 ? 0 : t < 0.05 ? 1 : 1 - MathUtil.SmoothStep((t - 0.05) / 0.17);

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

        // Gesture and press overlays come last: they answer the user right now, whatever is playing.
        if (_gestureT >= 0)
        {
            ApplyGesture(ref pose, _gesture, _gestureT, ReducedMotion);
            _gestureT += dt;
            if (_gestureT > GestureDuration(_gesture)) _gestureT = -1;
        }
        if (_pressT >= 0)
        {
            var k = PressEnvelope(_pressT) * (ReducedMotion ? 0.5 : 1);
            pose.BodySy *= 1 - 0.045 * k;
            pose.BodySx *= 1 + 0.035 * k;
            pose.EyeScale *= 1 + 0.18 * k;
            pose.EyeOpen = Math.Max(pose.EyeOpen, k);
            _pressT += dt;
            if (_pressT > 0.25) _pressT = -1;
        }

        _last = pose;
        return pose;
    }

    public Pose LastPose => _last;
}
