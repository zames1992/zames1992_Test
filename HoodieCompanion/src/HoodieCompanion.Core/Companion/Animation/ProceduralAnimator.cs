using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Animation;

/// <summary>Per-frame inputs that clips may read.</summary>
public struct AnimContext
{
    /// <summary>Global time in seconds (drives breathing and loops).</summary>
    public double Time;
    /// <summary>Walk phase in cycles. Advanced by distance travelled, so feet never slide.</summary>
    public double WalkPhase;
    /// <summary>Airborne vertical velocity normalised to roughly -1 (up) .. 1 (down).</summary>
    public double AirVy;
    /// <summary>Pendulum angle while held (degrees).</summary>
    public double SwingAngle;
    /// <summary>Pendulum angular speed while held (deg/s).</summary>
    public double SwingSpeed;
    public bool ReducedMotion;
}

/// <summary>
/// Pure functions that turn (clip, clip time, context) into a rig pose.
/// Everything here is deterministic and allocation free.
/// </summary>
public static partial class ProceduralAnimator
{
    /// <summary>Walk stride per cycle in reference pixels (two steps). Must match the leg swing amplitude.</summary>
    public const double LegLength = 200;
    public const double WalkAmplitude = 22;
    public const double RunAmplitude = 30;

    public static double StrideFor(double amplitudeDeg) => 4 * LegLength * Math.Sin(amplitudeDeg * Math.PI / 180);

    public static double WalkStride => StrideFor(WalkAmplitude);
    public static double RunStride => StrideFor(RunAmplitude);

    public static AnimFrame Evaluate(AnimClip clip, double t, in AnimContext ctx)
    {
        var p = Pose.Neutral;
        var fx = PoseEffect.None;
        var info = AnimationCatalog.Get(clip);
        var u = info.Duration > 0 ? MathUtil.Clamp01(t / info.Duration) : 0;
        var tau = 2 * Math.PI;

        switch (clip)
        {
            case AnimClip.IdleBreathing:
            case AnimClip.LookCursor:
            case AnimClip.Blink:
                Idle(ref p, ctx.Time);
                break;

            case AnimClip.Walk:
            case AnimClip.Run:
            case AnimClip.FollowCursor:
            case AnimClip.LeaveScreen when t > 0.9:
            {
                var run = clip is AnimClip.Run or AnimClip.FollowCursor;
                var a = run ? RunAmplitude : WalkAmplitude;
                var s = Math.Sin(tau * ctx.WalkPhase);
                var c = Math.Cos(tau * ctx.WalkPhase);
                p.LegLRot = a * s;
                p.LegRRot = -a * s;
                p.LegLDy = -(run ? 14 : 9) * Math.Max(0, c);
                p.LegRDy = -(run ? 14 : 9) * Math.Max(0, -c);
                p.RootDy = -(run ? 10 : 6) * Math.Abs(c);
                p.RootRot = run ? -6 : -2.5;
                p.ArmLRot = -a * 0.55 * s;
                p.ArmRRot = a * 0.55 * s;
                p.StringsRot = (run ? 6 : 3) * Math.Sin(tau * ctx.WalkPhase * 2);
                // Hips carry the step: the pelvis sways and the torso counter-rotates, the head stays calm.
                p.TorsoRot = -(run ? 2.5 : 1.8) * s;
                p.RootDx = (run ? 3 : 2) * s;
                p.HeadRot = 1.5 * Math.Sin(tau * ctx.WalkPhase * 2) + (run ? 1.5 : 1) * s;
                p.TorsoDy = 2 * Math.Abs(s);
                p.ShadowScale = 1 - 0.04 * Math.Abs(c);
                break;
            }

            case AnimClip.Turn:
                Idle(ref p, ctx.Time);
                p.BodySx = 0.4 + 0.6 * Math.Abs(Math.Cos(Math.PI * u));
                p.RootDy = -6 * MathUtil.Bump(u);
                p.ArmLRot = 6 * MathUtil.Bump(u);
                p.ArmRRot = -6 * MathUtil.Bump(u);
                break;

            case AnimClip.Jump:
            case AnimClip.JumpMonitor:
            {
                var k = MathUtil.SmoothStep(u);
                p.BodySy = 1 - 0.14 * k;
                p.BodySx = 1 + 0.07 * k;
                p.ArmLRot = -18 * k;
                p.ArmRRot = 18 * k;
                p.HeadDy = 6 * k;
                p.LookY = -0.6 * k;
                p.ShadowScale = 1 + 0.05 * k;
                break;
            }

            case AnimClip.Airborne:
            case AnimClip.Fall:
            case AnimClip.Thrown:
            {
                var falling = MathUtil.Clamp01((ctx.AirVy + 0.2) / 1.2);
                p.ArmLRot = 35 + 55 * falling + 6 * Math.Sin(ctx.Time * 18);
                p.ArmRRot = -35 - 55 * falling - 6 * Math.Sin(ctx.Time * 18 + 1);
                p.LegLRot = 12 - 18 * falling + 5 * Math.Sin(ctx.Time * 14);
                p.LegRRot = -10 + 14 * falling - 5 * Math.Sin(ctx.Time * 14 + 0.7);
                p.LegLDy = -10 * (1 - falling);
                p.LegRDy = -6 * (1 - falling);
                p.BodySy = 1 + 0.05 * Math.Clamp(Math.Abs(ctx.AirVy), 0, 1);
                p.BodySx = 1 - 0.03 * Math.Clamp(Math.Abs(ctx.AirVy), 0, 1);
                p.EyeScale = 1 + 0.2 * falling;
                p.LookY = -0.4 + 0.9 * falling;
                p.StringsRot = -14 * ctx.AirVy;
                p.ShadowAlpha = 0;
                if (clip == AnimClip.Thrown) fx = PoseEffect.None;
                break;
            }

            case AnimClip.LandSoft:
            case AnimClip.LandMonitor:
            {
                var b = MathUtil.Bump(u);
                p.BodySy = 1 - 0.12 * b;
                p.BodySx = 1 + 0.06 * b;
                p.ArmLRot = 14 * b;
                p.ArmRRot = -14 * b;
                p.HeadDy = 5 * b;
                break;
            }

            case AnimClip.LandHard:
            {
                var squash = u < 0.35 ? MathUtil.Bump(u / 0.7) : Math.Max(0, 1 - (u - 0.35) / 0.65) * 0.9;
                var wobble = Math.Sin(t * 22) * Math.Max(0, 1 - u) * 0.05;
                p.BodySy = 1 - 0.24 * squash + wobble;
                p.BodySx = 1 + 0.12 * squash - wobble;
                p.ArmLRot = 60 * squash + 10 * Math.Sin(t * 16);
                p.ArmRRot = -60 * squash - 10 * Math.Sin(t * 16 + 1);
                p.LegLRot = 16 * squash;
                p.LegRRot = -14 * squash;
                p.EyeOpen = u < 0.5 ? 0.25 : 1;
                p.HeadRot = 6 * Math.Sin(t * 12) * (1 - u);
                p.ShadowScale = 1 + 0.15 * squash;
                fx = PoseEffect.Dust;
                break;
            }

            case AnimClip.Recover:
            {
                var e = MathUtil.SmoothStep(u);
                p.BodySy = 0.94 + 0.06 * e;
                p.HeadRot = -8 * (1 - e) + 3 * Math.Sin(t * 9) * (1 - e);
                p.ArmLRot = 20 * (1 - e);
                p.ArmRRot = -20 * (1 - e);
                p.EyeOpen = 0.6 + 0.4 * e;
                break;
            }

            case AnimClip.RecoverFromThrow:
            {
                Idle(ref p, ctx.Time);
                // 0-0.55 dust off (patting the hoodie), 0.55-0.8 head shake, 0.8-1 look at the user.
                if (u < 0.55)
                {
                    var k = u / 0.55;
                    p.ArmLRot = -28 + 14 * Math.Sin(t * 20);
                    p.ArmRRot = 28 - 14 * Math.Sin(t * 20 + 1.2);
                    p.LookY = 0.8 * MathUtil.Bump(k);
                    p.HeadRot = -4;
                    fx = PoseEffect.Dust;
                }
                else if (u < 0.8)
                {
                    p.HeadRot = 9 * Math.Sin((u - 0.55) * 60);
                }
                else
                {
                    p.LookY = -0.2;
                    p.HeadRot = 4 * MathUtil.Bump((u - 0.8) / 0.2);
                }
                break;
            }

            // ---------------- REST ----------------
            case AnimClip.SitDown:
                Sit(ref p, MathUtil.SmoothStep(u), ctx.Time);
                break;
            case AnimClip.SitIdle:
                Sit(ref p, 1, ctx.Time);
                p.HeadRot += 3 * Math.Sin(ctx.Time * 0.35);
                break;
            case AnimClip.StandUp:
                Sit(ref p, 1 - MathUtil.SmoothStep(u), ctx.Time);
                break;
            case AnimClip.SleepStart:
                Sit(ref p, 1, ctx.Time);
                p.EyeOpen = 1 - MathUtil.SmoothStep(u);
                p.HeadRot = -9 * MathUtil.SmoothStep(u);
                p.HeadDy = 6 * MathUtil.SmoothStep(u);
                break;
            case AnimClip.SleepLoop:
            {
                Sit(ref p, 1, ctx.Time);
                var br = Math.Sin(ctx.Time * tau / 4.2);
                p.EyeOpen = 0;
                p.HeadRot = -9 + 1.5 * br;
                p.HeadDy = 6 + 2 * br;
                p.TorsoDy = 2.5 * br;
                p.BodySy = 1 + 0.012 * br;
                fx = PoseEffect.Zzz;
                break;
            }
            case AnimClip.WakeUp:
            {
                Sit(ref p, 1, ctx.Time);
                var open = MathUtil.SmoothStep(u / 0.3);
                p.EyeOpen = open;
                var stretch = MathUtil.Bump((u - 0.25) / 0.6);
                p.ArmLRot = 150 * stretch;
                p.ArmRRot = -150 * stretch;
                p.ArmLDy = -10 * stretch;
                p.ArmRDy = -10 * stretch;
                p.HeadRot = -9 * (1 - open) + 4 * stretch;
                p.BodySy = 1 + 0.04 * stretch;
                break;
            }
            case AnimClip.Stretch:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.Bump(u);
                p.ArmLRot = 155 * k;
                p.ArmRRot = -155 * k;
                p.ArmLDy = -12 * k;
                p.ArmRDy = -12 * k;
                p.BodySy = 1 + 0.06 * k;
                p.BodySx = 1 - 0.03 * k;
                p.EyeOpen = 1 - 0.8 * k;
                p.HeadRot = 3 * k;
                p.LegLDy = -4 * k;
                p.LegRDy = -4 * k;
                break;
            }
            case AnimClip.Yawn:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.Bump(u);
                p.HeadRot = 7 * k;
                p.HeadDy = -4 * k;
                p.EyeOpen = 1 - 0.9 * k;
                p.ArmRRot = 55 * k;
                p.ArmRDy = -8 * k;
                p.BodySy = 1 + 0.03 * k;
                break;
            }

            // ---------------- CURSOR ----------------
            case AnimClip.Curious:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.SmoothStep(u / 0.25) * (1 - MathUtil.SmoothStep((u - 0.8) / 0.2));
                p.HeadRot = 13 * k;
                p.RootRot = -3 * k;
                p.EyeScale = 1 + 0.12 * k;
                fx = u < 0.85 ? PoseEffect.Question : PoseEffect.None;
                break;
            }
            case AnimClip.Wave:
            {
                Idle(ref p, ctx.Time);
                var raise = MathUtil.SmoothStep(u / 0.2) * (1 - MathUtil.SmoothStep((u - 0.8) / 0.2));
                p.ArmRRot = -(125 + 22 * Math.Sin(t * 14)) * raise;
                p.ArmRDy = -8 * raise;
                p.HeadRot = 5 * raise;
                p.RootRot = 2 * raise;
                break;
            }
            case AnimClip.Surprised:
            {
                var hop = MathUtil.Bump(u / 0.6);
                p.RootDy = -34 * hop;
                p.EyeScale = 1 + 0.35 * MathUtil.Bump(u);
                p.ArmLRot = 45 * MathUtil.Bump(u);
                p.ArmRRot = -45 * MathUtil.Bump(u);
                p.LegLRot = 8 * hop;
                p.LegRRot = -8 * hop;
                p.BodySy = 1 + 0.06 * hop;
                fx = PoseEffect.Exclaim;
                break;
            }

            // ---------------- PHYSICAL ----------------
            case AnimClip.Grabbed:
            case AnimClip.Swinging:
            case AnimClip.GrabReaction:
            {
                var sw = Math.Clamp(ctx.SwingSpeed / 300.0, -1, 1);
                var flail = clip == AnimClip.Swinging ? 1.0 : 0.35;
                p.ArmLRot = 28 + 10 * Math.Sin(ctx.Time * 7) * flail - 20 * sw;
                p.ArmRRot = -28 - 10 * Math.Sin(ctx.Time * 7 + 1.3) * flail - 20 * sw;
                p.LegLRot = 6 * Math.Sin(ctx.Time * 5) * flail - ctx.SwingAngle * 0.35 - 14 * sw;
                p.LegRRot = -6 * Math.Sin(ctx.Time * 5 + 0.8) * flail - ctx.SwingAngle * 0.35 - 14 * sw;
                p.LegLSy = 1.03;
                p.LegRSy = 1.03;
                p.BodySy = 1.03;
                p.BodySx = 0.98;
                p.HeadDy = -4;
                p.StringsRot = -ctx.SwingAngle * 0.6;
                p.EyeScale = clip == AnimClip.GrabReaction ? 1 + 0.3 * MathUtil.Bump(u) : 1.08;
                p.LookY = 0.3;
                p.ShadowAlpha = 0;
                if (clip == AnimClip.GrabReaction) fx = PoseEffect.Exclaim;
                break;
            }

            // ---------------- INVENTORY ----------------
            case AnimClip.NoticeItem:
                Idle(ref p, ctx.Time);
                p.LookY = -0.9 * MathUtil.SmoothStep(u);
                p.EyeScale = 1 + 0.2 * MathUtil.SmoothStep(u);
                p.HeadRot = 4 * MathUtil.SmoothStep(u);
                p.ArmLRot = -12 * u;
                p.ArmRRot = 12 * u;
                fx = PoseEffect.Exclaim;
                break;
            case AnimClip.CatchItem:
            {
                var k = MathUtil.SmoothStep(u);
                p.ArmLRot = -38 * k - 12;
                p.ArmRRot = 38 * k + 12;
                p.ArmLDy = -30 * k;
                p.ArmRDy = -30 * k;
                p.ItemAlpha = 1;
                p.ItemDy = -460 + 410 * k;
                p.ItemRot = 25 * (1 - k);
                p.LookY = -0.9 + 1.2 * k;
                p.BodySy = 1 - 0.05 * MathUtil.Bump(u);
                break;
            }
            case AnimClip.InspectItem:
                Idle(ref p, ctx.Time);
                p.ArmLRot = -50;
                p.ArmRRot = 50;
                p.ArmLDy = -30;
                p.ArmRDy = -30;
                p.ItemAlpha = 1;
                p.ItemDy = -50;
                p.ItemRot = 8 * Math.Sin(t * 10);
                p.HeadRot = 8;
                p.LookY = 0.5;
                p.LookX = 0.15;
                break;
            case AnimClip.PutInBackpack:
            {
                // Backpack swings round and opens, the object goes in, the lid closes.
                var appear = MathUtil.SmoothStep(u / 0.25);
                var down = MathUtil.SmoothStep((u - 0.2) / 0.5);
                var close = MathUtil.SmoothStep((u - 0.75) / 0.25);
                Hold(ref p, 1, ctx.Time);
                p.PropBackpack = appear;
                p.BackpackLid = appear * (1 - close);
                p.ItemAlpha = 1 - MathUtil.SmoothStep((down - 0.7) / 0.3);
                p.ItemDy = -50 + 150 * down;
                p.ItemScale = 1 - 0.35 * down;
                p.LookY = 0.7;
                fx = u > 0.75 ? PoseEffect.Sparkle : PoseEffect.None;
                break;
            }
            case AnimClip.OpenBackpack:
            case AnimClip.SearchBackpack:
            {
                Idle(ref p, ctx.Time);
                var k = clip == AnimClip.OpenBackpack ? MathUtil.SmoothStep(u) : 1;
                var r = clip == AnimClip.SearchBackpack ? Math.Sin(ctx.Time * 7) : 0;
                Hold(ref p, k, ctx.Time);
                p.PropBackpack = MathUtil.SmoothStep(k * 1.6);
                p.BackpackLid = MathUtil.SmoothStep((k - 0.4) / 0.6);
                if (clip == AnimClip.SearchBackpack)
                {
                    // One hand dives into the backpack and rummages.
                    p.ArmRRot = 34 + 10 * r;
                    p.ArmRDy = 10 + 8 * Math.Max(0, r);
                    p.HeadRot = 6 + 2 * r;
                    p.TorsoRot = 1.5 * r;
                }
                p.LookY = 0.8 * k;
                break;
            }
            case AnimClip.CloseBackpack:
            {
                Idle(ref p, ctx.Time);
                var lid = 1 - MathUtil.SmoothStep(u / 0.4);
                var fade = 1 - MathUtil.SmoothStep((u - 0.45) / 0.55);
                Hold(ref p, fade, ctx.Time);
                p.PropBackpack = fade;
                p.BackpackLid = lid;
                p.LookY = 0.5 * fade;
                break;
            }
            case AnimClip.PresentItem:
            {
                var up = MathUtil.SmoothStep((u - 0.2) / 0.4);
                var hold = u > 0.6 ? 1 - MathUtil.SmoothStep((u - 0.85) / 0.15) : 1;
                Hold(ref p, hold, ctx.Time);
                p.PropBackpack = 1 - MathUtil.SmoothStep((u - 0.55) / 0.3);
                p.BackpackLid = p.PropBackpack;
                p.ItemAlpha = hold * MathUtil.SmoothStep(u / 0.25);
                p.ItemDy = 60 - 190 * up;
                p.ItemScale = 0.6 + 0.4 * up;
                p.LookY = -0.2;
                fx = u > 0.5 ? PoseEffect.Sparkle : PoseEffect.None;
                break;
            }
            case AnimClip.MissingItem:
            {
                Idle(ref p, ctx.Time);
                if (u < 0.55)
                {
                    p.LookX = Math.Sin(u / 0.55 * tau * 1.5);
                    p.LookY = 0.5;
                    p.ArmLRot = -20;
                    p.ArmRRot = 20;
                }
                else
                {
                    var k = MathUtil.Bump((u - 0.55) / 0.45);
                    p.ArmLRot = 55 * k;
                    p.ArmRRot = -55 * k;
                    p.ArmLDy = -14 * k;
                    p.ArmRDy = -14 * k;
                    p.HeadRot = 10 * k;
                    p.TorsoDy = -4 * k;
                    fx = PoseEffect.Question;
                }
                break;
            }

            // ---------------- UTILITY ----------------
            case AnimClip.ReminderAlert:
            {
                Idle(ref p, ctx.Time);
                var raise = MathUtil.SmoothStep(t / 0.4);
                p.ArmLRot = 150 * raise;
                p.ArmRRot = -150 * raise;
                p.ArmLDy = -14 * raise;
                p.ArmRDy = -14 * raise;
                p.ItemAlpha = raise;
                p.ItemDy = -500 * raise + 6 * Math.Sin(t * 5);
                p.ItemScale = 1.15;
                p.ItemRot = 5 * Math.Sin(t * 2.5);
                p.RootDy = -4 * Math.Abs(Math.Sin(t * 2.5));
                p.LookY = -0.5;
                fx = t < 1.2 ? PoseEffect.Exclaim : PoseEffect.None;
                break;
            }
            case AnimClip.TimerAlert:
            {
                var cycle = (t % 0.9) / 0.9;
                var hop = ctx.ReducedMotion ? 0 : MathUtil.Bump(cycle / 0.55);
                p.RootDy = -30 * hop;
                p.BodySy = cycle > 0.55 ? 1 - 0.07 * MathUtil.Bump((cycle - 0.55) / 0.45) : 1 + 0.04 * hop;
                p.ArmLRot = 60 + 50 * hop;
                p.ArmRRot = -60 - 50 * hop;
                p.LegLRot = 6 * hop;
                p.LegRRot = -6 * hop;
                p.EyeScale = 1.12;
                fx = PoseEffect.Exclaim;
                break;
            }
            case AnimClip.Thinking:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.SmoothStep(u / 0.25) * (1 - MathUtil.SmoothStep((u - 0.85) / 0.15));
                p.ArmRRot = 65 * k;
                p.ArmRDy = -24 * k;
                p.HeadRot = 7 * k;
                p.LookY = -0.7 * k;
                p.LookX = 0.4 * k;
                fx = PoseEffect.Dots;
                break;
            }
            case AnimClip.Success:
            {
                var hop = ctx.ReducedMotion ? 0 : MathUtil.Bump(u);
                p.RootDy = -24 * hop;
                p.ArmLRot = 70 * MathUtil.Bump(u);
                p.ArmRRot = -70 * MathUtil.Bump(u);
                p.BodySy = 1 + 0.05 * hop;
                fx = PoseEffect.Sparkle;
                break;
            }
            case AnimClip.Error:
                Idle(ref p, ctx.Time);
                p.HeadRot = 10 * Math.Sin(t * 26) * (1 - u);
                p.EyeOpen = 0.7;
                p.ArmLRot = -10;
                p.ArmRRot = 10;
                break;

            // ---------------- SYSTEM ----------------
            case AnimClip.PCBusy:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.SmoothStep(u / 0.15) * (1 - MathUtil.SmoothStep((u - 0.85) / 0.15));
                p.ArmRRot = (95 + 25 * Math.Sin(t * 28)) * k;
                p.ArmRDy = -30 * k;
                p.EyeOpen = 1 - 0.45 * k;
                p.HeadRot = -5 * k;
                p.BodySy = 1 - 0.02 * k;
                fx = PoseEffect.Heat;
                break;
            }
            case AnimClip.PCIdle:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.Bump(u);
                p.BodySy = 1 - 0.03 * k;
                p.TorsoDy = 4 * k;
                p.EyeOpen = 1 - 0.7 * k;
                p.ArmLRot = 6 * k;
                p.ArmRRot = -6 * k;
                break;
            }
            case AnimClip.DownloadWatching:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.SmoothStep(u / 0.15) * (1 - MathUtil.SmoothStep((u - 0.85) / 0.15));
                p.LookY = -0.8 * k;
                p.LookX = 0.5 * Math.Sin(t * 1.4) * k;
                p.HeadRot = 5 * k;
                fx = PoseEffect.Arrow;
                break;
            }

            // ---------------- WORLD ----------------
            case AnimClip.PeekEdge:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.SmoothStep(u / 0.3) * (1 - MathUtil.SmoothStep((u - 0.8) / 0.2));
                p.RootRot = -9 * k;
                p.LookX = -0.9 * k;
                p.LookY = 0.8 * k;
                p.ArmLRot = -25 * k;
                p.ArmRRot = 25 * k;
                p.LegRDy = -10 * k;
                p.LegRRot = -14 * k;
                break;
            }
            case AnimClip.LeaveScreen:
            case AnimClip.ReturnToScreen:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.Bump(u);
                p.ArmRRot = -(110 + 18 * Math.Sin(t * 14)) * k;
                p.HeadRot = 4 * k;
                break;
            }
            // ---------------- CLIMBING ----------------
            case AnimClip.PlaceLadder:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.SmoothStep(u / 0.6);
                p.ArmLRot = 150 * k;
                p.ArmRRot = -150 * k;
                p.ArmLDy = -14 * k;
                p.ArmRDy = -14 * k;
                p.LookY = -0.9 * k;
                p.BodySy = 1 + 0.04 * k;
                p.LegLDy = -6 * MathUtil.Bump(u);
                p.LegRDy = -6 * MathUtil.Bump(u);
                break;
            }
            case AnimClip.ClimbLadder:
            {
                var s1 = Math.Sin(tau * ctx.WalkPhase);
                p.ArmLRot = 150 + 22 * s1;
                p.ArmRRot = -150 + 22 * s1;
                p.ArmLDy = -14 - 10 * Math.Max(0, s1);
                p.ArmRDy = -14 - 10 * Math.Max(0, -s1);
                p.LegLDy = -18 * Math.Max(0, s1);
                p.LegRDy = -18 * Math.Max(0, -s1);
                p.LegLRot = 8 * s1;
                p.LegRRot = -8 * s1;
                p.RootRot = 2 * s1;
                p.LookY = -0.7;
                p.ShadowAlpha = 0;
                break;
            }
            case AnimClip.TieRope:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.Bump(u);
                p.BodySy = 1 - 0.1 * k;
                p.HeadDy = 8 * k;
                p.ArmLRot = -10 + 30 * k;
                p.ArmRRot = 10 - 60 * k + 12 * Math.Sin(t * 20) * k;
                p.ArmLDy = -10 * k;
                p.ArmRDy = -10 * k;
                p.LookY = 0.9;
                p.LookX = -0.5;
                break;
            }
            case AnimClip.ClimbRope:
            {
                var s1 = Math.Sin(tau * ctx.WalkPhase);
                p.ArmLRot = 165 + 8 * s1;
                p.ArmRRot = -165 + 8 * s1;
                p.ArmLDy = -14 - 8 * Math.Max(0, -s1);
                p.ArmRDy = -14 - 8 * Math.Max(0, s1);
                p.LegLRot = 10;
                p.LegRRot = -4;
                p.LegLDy = -8;
                p.RootRot = 3 * Math.Sin(ctx.Time * 2.2);
                p.LookY = 0.8;
                p.ShadowAlpha = 0;
                break;
            }

            // ---------------- ACCESSORIES ----------------
            case AnimClip.LaptopOpen:
            case AnimClip.LaptopType:
            case AnimClip.LaptopClose:
            {
                Sit(ref p, 1, ctx.Time);
                double show, lid;
                if (clip == AnimClip.LaptopOpen) { show = MathUtil.SmoothStep(u / 0.45); lid = MathUtil.SmoothStep((u - 0.4) / 0.6); }
                else if (clip == AnimClip.LaptopClose) { lid = 1 - MathUtil.SmoothStep(u / 0.5); show = 1 - MathUtil.SmoothStep((u - 0.45) / 0.55); }
                else { show = 1; lid = 1; }
                p.PropLaptop = show;
                p.LaptopLid = lid;
                var typing = clip == AnimClip.LaptopType ? 1.0 : lid * 0.3;
                var a1 = Math.Sin(ctx.Time * 17) * typing;
                var a2 = Math.Sin(ctx.Time * 13 + 1.3) * typing;
                p.ArmLRot = MathUtil.Lerp(p.ArmLRot, 6 + 3 * a1, show);
                p.ArmRRot = MathUtil.Lerp(p.ArmRRot, 26 + 3 * a2, show);
                p.ArmLDy = 4 * Math.Max(0, a1) * show;
                p.ArmRDy = 4 * Math.Max(0, a2) * show;
                p.HeadRot = -4 * show + 1.5 * Math.Sin(ctx.Time * 0.9) * typing;
                p.HeadDy = 4 * show;
                p.LookY = 0.75 * show;
                p.LookX = 0.35 * show;
                break;
            }
            case AnimClip.ReadBook:
            {
                Sit(ref p, 1, ctx.Time);
                var turn = MathUtil.Bump(((ctx.Time % 7.0) - 6.2) / 0.8);
                p.PropBook = MathUtil.SmoothStep(t / 0.4);
                p.ArmLRot = -24;
                p.ArmRRot = 24 + 30 * turn;
                p.ArmLDy = -26;
                p.ArmRDy = -26 - 8 * turn;
                p.HeadRot = -3 + 2 * Math.Sin(ctx.Time * 0.7);
                p.HeadDy = 5;
                p.LookY = 0.8;
                p.LookX = 0.15 * Math.Sin(ctx.Time * 1.3);
                break;
            }
            case AnimClip.WriteNotes:
            {
                Idle(ref p, ctx.Time);
                var k = MathUtil.SmoothStep(t / 0.35);
                var scribble = Math.Sin(ctx.Time * 16) * k;
                p.PropNotebook = k;
                p.PropPencil = k;
                p.ArmLRot = -34 * k;
                p.ArmLDy = -24 * k;
                p.ArmRRot = (38 + 4 * scribble + 3 * Math.Sin(ctx.Time * 2.3)) * k;
                p.ArmRDy = (-28 + 3 * Math.Abs(scribble)) * k;
                p.HeadRot = 5 * k;
                p.LookY = 0.75 * k;
                p.LookX = 0.2 * k;
                break;
            }
            case AnimClip.Dance:
            {
                var beat = t * 2.6;
                var b = Math.Abs(Math.Sin(Math.PI * beat));
                var side = Math.Sin(Math.PI * beat);
                p.RootDy = ctx.ReducedMotion ? 0 : -18 * b;
                p.RootRot = 5 * side;
                p.ArmLRot = 60 + 50 * Math.Max(0, side);
                p.ArmRRot = -60 - 50 * Math.Max(0, -side);
                p.LegLRot = 10 * side;
                p.LegRRot = 10 * side;
                p.LegLDy = -10 * Math.Max(0, side);
                p.LegRDy = -10 * Math.Max(0, -side);
                p.HeadRot = -6 * side;
                p.StringsRot = 8 * side;
                p.BodySy = 1 + 0.03 * b;
                break;
            }
            case AnimClip.JumpForJoy:
            {
                var crouch = MathUtil.Bump(u / 0.3);
                var air = MathUtil.Bump((u - 0.25) / 0.6);
                p.BodySy = 1 - 0.1 * crouch + 0.05 * air;
                p.RootDy = ctx.ReducedMotion ? 0 : -60 * air;
                p.ArmLRot = 20 * crouch + 140 * air;
                p.ArmRRot = -20 * crouch - 140 * air;
                p.LegLRot = 12 * air;
                p.LegRRot = -12 * air;
                p.LegLDy = -12 * air;
                p.LegRDy = -12 * air;
                fx = air > 0.3 ? PoseEffect.Sparkle : PoseEffect.None;
                break;
            }

            default:
                fx = EvaluateLibrary(clip, t, u, ctx, ref p);
                break;
        }

        if (ctx.ReducedMotion)
        {
            // Reduced motion: tame squash/stretch and large hops.
            p.BodySx = 1 + (p.BodySx - 1) * 0.35;
            p.BodySy = 1 + (p.BodySy - 1) * 0.35;
            p.RootDy *= 0.4;
        }

        return new AnimFrame(p, fx, t);
    }

    /// <summary>Both hands forward, holding something at the belly (backpack, parcel). k = 0..1.</summary>
    private static void Hold(ref Pose p, double k, double time)
    {
        p.ArmLRot = MathUtil.Lerp(p.ArmLRot, -30, k);
        p.ArmRRot = MathUtil.Lerp(p.ArmRRot, 30, k);
        p.ArmLDy = MathUtil.Lerp(p.ArmLDy, -12, k);
        p.ArmRDy = MathUtil.Lerp(p.ArmRDy, -12, k);
        p.TorsoDy += 1.5 * Math.Sin(time * 2) * k;
    }

    private static void Idle(ref Pose p, double time)
    {
        var br = Math.Sin(time * 2 * Math.PI / 3.4);
        p.TorsoDy = 1.6 * br;
        p.BodySy = 1 + 0.007 * br;
        p.ArmLRot = 1.2 * br;
        p.ArmRRot = -1.2 * br;
        p.HeadRot = 1.4 * Math.Sin(time * 2 * Math.PI / 7.3);
        p.HeadDy = 1.2 * br;
        p.StringsRot = 1.2 * Math.Sin(time * 1.3);
    }

    /// <summary>Sitting on the ground with legs stretched forward. k = 0 standing .. 1 seated.</summary>
    private static void Sit(ref Pose p, double k, double time)
    {
        Idle(ref p, time);
        p.RootDy = 168 * k;
        p.LegLRot = 80 * k;
        p.LegRRot = 70 * k;
        p.LegLDy = -6 * k;
        p.LegRDy = -2 * k;
        p.LegLSy = 1 - 0.08 * k;
        p.LegRSy = 1 - 0.08 * k;
        p.ArmLRot = p.ArmLRot * (1 - k) + 14 * k;
        p.ArmRRot = p.ArmRRot * (1 - k) - 6 * k;
        p.ShadowScale = 1 + 0.12 * k;
    }
}
