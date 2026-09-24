using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Animation;

/// <summary>
/// The v1.2 clip library: idle variants, emotions, traversal, grab-anywhere hangs, lying/sleeping,
/// diegetic system clips. Same rules as the core clips: pure functions of (clip, time, context),
/// only pivot rotations/offsets, so the character's silhouette never changes.
/// </summary>
public static partial class ProceduralAnimator
{
    // Arm angles used while hanging by a hand. HangAnchor() below must match them.
    public const double HangArmAngle = 168;
    private static readonly Vec2 ArmLPivot = new(168, 350), ArmRPivot = new(378, 350);
    private static readonly Vec2 HandL = new(140, 596), HandR = new(412, 610);

    /// <summary>
    /// Where the rig is held (reference pixels, facing left) for a grab clip: the posed hand for hand
    /// grabs, the shoe for a foot grab, the belly for a torso grab. The grab pivot sits exactly there.
    /// </summary>
    public static Vec2 HangAnchor(AnimClip clip) => clip switch
    {
        AnimClip.HangHandL => ArmLPivot + (HandL - ArmLPivot).Rotate(HangArmAngle),
        AnimClip.HangHandR => ArmRPivot + (HandR - ArmRPivot).Rotate(-HangArmAngle),
        AnimClip.HangFoot => new Vec2(186, 800),
        AnimClip.HangTorso => new Vec2(272, 500),
        _ => new Vec2(272, 150),
    };

    /// <summary>Lying on the floor on its side. k = 0 standing .. 1 lying. Rotation happens around the feet.</summary>
    public const double LieAngle = -84;

    private static void Lie(ref Pose p, double k, double time)
    {
        var br = Math.Sin(time * 2 * Math.PI / 4.2);
        p.RootRot = LieAngle * k;
        p.RootDx = 372 * k;
        p.RootDy = -126 * k;
        p.LegLRot = 12 * k;
        p.LegRRot = 24 * k;
        p.LegLDy = -8 * k;
        p.LegRDy = -12 * k;
        p.ArmLRot = 18 * k;
        p.ArmRRot = 40 * k;
        p.TorsoDy = 1.8 * br * k;
        p.HeadRot = -6 * k;
        p.ShadowScale = 1 + 0.55 * k;
        p.ShadowAlpha = 1;
    }

    /// <summary>Sitting on a ledge: the pelvis rests on the edge, the legs hang below it.</summary>
    private static void EdgeSit(ref Pose p, double k, double time, double swing)
    {
        Idle(ref p, time);
        p.RootDy = 206 * k;
        p.LegLRot = (18 + 16 * swing) * k;
        p.LegRRot = (8 - 16 * swing) * k;
        p.ArmLRot = p.ArmLRot * (1 - k) - 16 * k;
        p.ArmRRot = p.ArmRRot * (1 - k) + 16 * k;
        p.ArmLDy = 8 * k;
        p.ArmRDy = 8 * k;
        p.ShadowAlpha = 1 - k;
    }

    private static double Env(double u, double inT, double outT) =>
        MathUtil.SmoothStep(u / inT) * (1 - MathUtil.SmoothStep((u - (1 - outT)) / outT));

    private static PoseEffect EvaluateLibrary(AnimClip clip, double t, double u, in AnimContext ctx, ref Pose p)
    {
        var fx = PoseEffect.None;
        var tau = 2 * Math.PI;
        switch (clip)
        {
            // ---------------- IDLE BASIC ----------------
            case AnimClip.IdleBreathing2:
            {
                // Deeper, slower breathing with the weight on the other leg.
                var br = Math.Sin(ctx.Time * tau / 4.6);
                Idle(ref p, ctx.Time);
                p.TorsoDy = 2.6 * br;
                p.BodySy = 1 + 0.012 * br;
                p.RootRot = 1.5;
                p.LegLRot = -2;
                p.LegRRot = 3;
                p.HeadRot = -2 + 1.5 * Math.Sin(ctx.Time * tau / 8.1);
                p.ArmLRot = 3 + 1.5 * br;
                p.ArmRRot = -2 - 1.5 * br;
                break;
            }
            case AnimClip.LookLeft:
            case AnimClip.LookRight:
            case AnimClip.LookUp:
            case AnimClip.LookDown:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.2, 0.25);
                var (lx, ly, hr) = clip switch
                {
                    AnimClip.LookLeft => (-1.0, 0.0, -6.0),
                    AnimClip.LookRight => (1.0, 0.0, 6.0),
                    AnimClip.LookUp => (0.1, -1.0, 3.0),
                    _ => (0.0, 1.0, -4.0),
                };
                p.LookX = lx * k;
                p.LookY = ly * k;
                p.HeadRot += hr * k;
                p.HeadDy += (clip == AnimClip.LookDown ? 4 : clip == AnimClip.LookUp ? -3 : 0) * k;
                break;
            }
            case AnimClip.HeadTilt:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.25, 0.3);
                p.HeadRot = 15 * k;
                p.LookX = 0.3 * k;
                p.EyeScale = 1 + 0.08 * k;
                break;
            }
            case AnimClip.WeightShift:
            {
                Idle(ref p, ctx.Time);
                var s = Math.Sin(Math.PI * MathUtil.SmoothStep(u));
                p.RootRot = 3 * s;
                p.RootDx = -6 * s;
                p.LegLDy = -5 * MathUtil.Bump(u * 2);
                p.LegRDy = -5 * MathUtil.Bump(u * 2 - 1);
                p.HeadRot += -2 * s;
                break;
            }
            case AnimClip.StretchBody:
            {
                // Side stretch: arms up, lean to one side, then the other.
                Idle(ref p, ctx.Time);
                var up = Env(u, 0.2, 0.2);
                var side = Math.Sin(u * tau);
                p.ArmLRot = 160 * up;
                p.ArmRRot = -160 * up;
                p.ArmLDy = -12 * up;
                p.ArmRDy = -12 * up;
                p.TorsoRot = 8 * side * up;
                p.RootRot = 3 * side * up;
                p.BodySy = 1 + 0.05 * up;
                p.EyeOpen = 1 - 0.85 * up;
                break;
            }
            case AnimClip.Scratch:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.2, 0.2);
                p.ArmRRot = -150 * k + 8 * Math.Sin(t * 30) * k;
                p.ArmRDy = -20 * k;
                p.HeadRot = 8 * k;
                p.EyeOpen = 1 - 0.5 * k;
                break;
            }
            case AnimClip.InspectSelf:
            {
                // Looks down at its hoodie, turns a sleeve, brushes off a speck.
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.15, 0.2);
                p.LookY = 0.9 * k;
                p.HeadRot = -8 * k;
                p.HeadDy = 5 * k;
                p.ArmLRot = -35 * k + 6 * Math.Sin(u * tau * 2) * k;
                p.ArmLDy = -20 * k;
                var brush = MathUtil.Bump((u - 0.55) / 0.35);
                p.ArmRRot = 30 * brush + 12 * Math.Sin(t * 22) * brush;
                p.ArmRDy = -18 * brush;
                if (brush > 0.3) fx = PoseEffect.Dust;
                break;
            }

            // ---------------- IDLE CURIOUS ----------------
            case AnimClip.Listen:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.2, 0.2);
                p.HeadRot = 12 * k;
                p.LookX = 0.8 * k;
                p.LookY = -0.2 * k;
                p.EyeScale = 1 + 0.1 * k;
                p.ArmRRot = -20 * k;
                fx = u > 0.3 && u < 0.8 ? PoseEffect.Dots : PoseEffect.None;
                break;
            }
            case AnimClip.NoticeMovement:
            {
                Idle(ref p, ctx.Time);
                var snap = MathUtil.SmoothStep(u / 0.12);
                var k = snap * (1 - MathUtil.SmoothStep((u - 0.75) / 0.25));
                p.EyeScale = 1 + 0.25 * k;
                p.HeadRot = 6 * k;
                p.RootDy = -6 * MathUtil.Bump(u / 0.2);
                p.LookX = 0.9 * k;
                p.ArmLRot = 10 * k;
                p.ArmRRot = -10 * k;
                fx = u < 0.5 ? PoseEffect.Exclaim : PoseEffect.None;
                break;
            }
            case AnimClip.WatchWindow:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.15, 0.15);
                p.LookX = (0.6 + 0.35 * Math.Sin(t * 0.9)) * k;
                p.LookY = -0.4 * k;
                p.HeadRot = 5 * k;
                p.ArmLRot = -20 * k;
                p.ArmRRot = 20 * k;
                p.ArmLDy = -10 * k;
                p.ArmRDy = -10 * k;
                break;
            }
            case AnimClip.PeekIn:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.25, 0.25);
                p.RootRot = 12 * k;
                p.RootDx = -10 * k;
                p.HeadRot = 10 * k;
                p.LookX = 0.9 * k;
                p.ArmRRot = -30 * k;
                p.LegLRot = -12 * k;
                p.LegLDy = -8 * k;
                break;
            }
            case AnimClip.ReachCursor:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.3, 0.25);
                var tip = MathUtil.Bump((u - 0.3) / 0.5);
                p.ArmRRot = -145 * k - 10 * tip;
                p.ArmRDy = -24 * k;
                p.LookY = -0.9 * k;
                p.LookX = 0.4 * k;
                p.BodySy = 1 + 0.06 * tip;
                p.LegLDy = -12 * tip;
                p.LegRDy = -12 * tip;
                p.RootDy = -10 * tip;
                break;
            }
            case AnimClip.SearchCursor:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.1, 0.1);
                p.LookX = Math.Sin(u * tau * 1.5) * k;
                p.LookY = -0.3 * k;
                p.HeadRot = 8 * Math.Sin(u * tau * 1.5) * k;
                p.ArmRRot = -40 * k;
                p.ArmRDy = -30 * k;
                fx = u > 0.6 ? PoseEffect.Question : PoseEffect.None;
                break;
            }

            // ---------------- IDLE BORED ----------------
            case AnimClip.SitEdge:
            {
                var k = MathUtil.SmoothStep(t / 0.35);
                EdgeSit(ref p, k, ctx.Time, Math.Sin(ctx.Time * 3.1));
                p.LegLRot += 5 * Math.Sin(ctx.Time * 3.1 + 1.1) * k;
                p.HeadRot += 3 * Math.Sin(ctx.Time * 0.6);
                p.LookY = 0.35;
                break;
            }
            case AnimClip.ChinRest:
            {
                Sit(ref p, 1, ctx.Time);
                var k = MathUtil.SmoothStep(t / 0.5);
                p.ArmRRot = 62 * k;
                p.ArmRDy = -32 * k;
                p.TorsoRot = -4 * k;
                p.HeadRot = 9 * k + 2 * Math.Sin(ctx.Time * 0.5);
                p.HeadDy = 6 * k;
                p.EyeOpen = 1 - 0.35 * k;
                p.LookX = 0.5 * Math.Sin(ctx.Time * 0.4) * k;
                break;
            }
            case AnimClip.PickSurface:
            {
                Sit(ref p, 1, ctx.Time);
                var k = Env(u, 0.2, 0.2);
                p.ArmLRot = 30 * k + 6 * Math.Sin(t * 18) * k;
                p.ArmLDy = 12 * k;
                p.HeadRot = -8 * k;
                p.LookY = 0.9 * k;
                p.LookX = -0.3 * k;
                break;
            }
            case AnimClip.Sigh:
            {
                Idle(ref p, ctx.Time);
                var inhale = MathUtil.Bump(u / 0.45);
                var slump = MathUtil.Bump((u - 0.35) / 0.65);
                p.BodySy = 1 + 0.04 * inhale - 0.04 * slump;
                p.TorsoDy = -3 * inhale + 5 * slump;
                p.HeadDy = 6 * slump;
                p.HeadRot = -5 * slump;
                p.ArmLRot = 5 * inhale - 4 * slump;
                p.ArmRRot = -5 * inhale + 4 * slump;
                p.EyeOpen = 1 - 0.5 * slump;
                fx = slump > 0.4 ? PoseEffect.Dots : PoseEffect.None;
                break;
            }
            case AnimClip.CountFingers:
            {
                Sit(ref p, 1, ctx.Time);
                var k = Env(u, 0.15, 0.15);
                var tap = Math.Abs(Math.Sin(u * tau * 2.5));
                p.ArmLRot = -30 * k;
                p.ArmLDy = -26 * k;
                p.ArmRRot = (32 + 8 * tap) * k;
                p.ArmRDy = -28 * k;
                p.HeadRot = 5 * k + 2 * tap * k;
                p.LookY = 0.7 * k;
                break;
            }
            case AnimClip.StareVoid:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.3, 0.2);
                p.EyeScale = 1 - 0.1 * k;
                p.EyeOpen = 1 - 0.2 * k;
                p.LookX = 0;
                p.LookY = 0;
                p.ArmLRot = 3 * k;
                p.ArmRRot = -3 * k;
                p.HeadRot = -2 * k;
                p.TorsoDy += 2 * k;
                break;
            }
            case AnimClip.LieDown:
            {
                // Kneel first (squash), then roll onto its side.
                var kneel = MathUtil.Bump(u / 0.55);
                var lie = MathUtil.SmoothStep((u - 0.25) / 0.75);
                Idle(ref p, ctx.Time);
                p.BodySy = 1 - 0.08 * kneel;
                p.ArmLRot = 30 * kneel;
                p.ArmRRot = -20 * kneel;
                var q = p;
                Lie(ref q, lie, ctx.Time);
                q.BodySy = p.BodySy;
                p = q;
                p.EyeOpen = 1 - 0.3 * lie;
                break;
            }
            case AnimClip.LieIdle:
            {
                Lie(ref p, 1, ctx.Time);
                var kick = Math.Max(0, Math.Sin(ctx.Time * 1.7));
                p.LegRRot += 14 * kick;
                p.LegRDy -= 4 * kick;
                p.LookY = -0.6;
                p.LookX = 0.3 * Math.Sin(ctx.Time * 0.3);
                p.ArmRRot += 10 * Math.Sin(ctx.Time * 0.8);
                break;
            }

            // ---------------- IDLE PLAYFUL ----------------
            case AnimClip.Spin:
            {
                var hop = ctx.ReducedMotion ? 0 : MathUtil.Bump(u);
                p.RootDy = -26 * hop;
                // A full turn drawn as two squash-through-the-middle flips.
                p.BodySx = 0.4 + 0.6 * Math.Abs(Math.Cos(tau * u));
                p.ArmLRot = 70 * MathUtil.Bump(u);
                p.ArmRRot = -70 * MathUtil.Bump(u);
                p.LegLRot = 10 * hop;
                p.LegRRot = -10 * hop;
                p.StringsRot = 12 * Math.Sin(u * tau);
                fx = u > 0.6 ? PoseEffect.Sparkle : PoseEffect.None;
                break;
            }
            case AnimClip.CatchCursor:
            {
                var crouch = MathUtil.Bump(u / 0.3);
                var air = MathUtil.Bump((u - 0.2) / 0.6);
                p.BodySy = 1 - 0.1 * crouch + 0.06 * air;
                p.RootDy = -70 * air;
                p.ArmRRot = -20 * crouch - 160 * air;
                p.ArmRDy = -20 * air;
                p.ArmLRot = 30 * air;
                p.LegLRot = 16 * air;
                p.LegRRot = -8 * air;
                p.LegLDy = -14 * air;
                p.LookY = -1;
                p.EyeScale = 1 + 0.15 * air;
                p.ShadowScale = 1 - 0.25 * air;
                break;
            }

            // ---------------- MOVEMENT ----------------
            case AnimClip.Stop:
            {
                // Skid: lean back, front foot planted, then settle forward with an overshoot.
                var skid = MathUtil.Bump(u / 0.6);
                var settle = Math.Sin(Math.Clamp((u - 0.5) / 0.5, 0, 1) * Math.PI * 1.5) * (1 - u);
                p.RootRot = 7 * skid - 4 * settle;
                p.LegLRot = 16 * skid;
                p.LegRRot = -8 * skid;
                p.ArmLRot = 28 * skid;
                p.ArmRRot = -28 * skid;
                p.HeadRot = -5 * skid + 3 * settle;
                p.StringsRot = -8 * skid;
                p.BodySy = 1 - 0.04 * skid;
                if (skid > 0.4) fx = PoseEffect.Dust;
                break;
            }
            case AnimClip.Slip:
            {
                var slip = MathUtil.Bump(u / 0.5);
                var flail = Math.Sin(t * 26) * Math.Max(0, 1 - u * 1.3);
                p.RootRot = 16 * slip;
                p.RootDy = -18 * slip;
                p.LegLRot = 40 * slip;
                p.LegRRot = -10 * slip;
                p.LegLDy = -14 * slip;
                p.ArmLRot = 80 * slip + 20 * flail;
                p.ArmRRot = -80 * slip - 20 * flail;
                p.EyeScale = 1 + 0.3 * slip;
                p.HeadRot = -8 * slip;
                fx = u < 0.6 ? PoseEffect.Exclaim : PoseEffect.Sweat;
                break;
            }
            case AnimClip.Balance:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.15, 0.2);
                var w = Math.Sin(t * 5.5);
                p.RootRot = 7 * w * k;
                p.ArmLRot = (90 + 18 * w) * k;
                p.ArmRRot = (-90 + 18 * w) * k;
                p.LegRRot = -18 * k;
                p.LegRDy = -12 * k;
                p.HeadRot = -5 * w * k;
                p.EyeScale = 1 + 0.12 * k;
                break;
            }

            // ---------------- SCREEN TRAVERSAL ----------------
            case AnimClip.HangEdge:
            {
                // Hanging from a ledge by both hands (the controller places the hands on the edge).
                var kick = Math.Sin(ctx.Time * 7);
                p.ArmLRot = 170 + 3 * kick;
                p.ArmRRot = -170 + 3 * kick;
                p.ArmLDy = -18;
                p.ArmRDy = -18;
                p.LegLRot = 18 * kick;
                p.LegRRot = -18 * kick;
                p.LegLDy = -8 * Math.Max(0, kick);
                p.LegRDy = -8 * Math.Max(0, -kick);
                p.BodySy = 1.03;
                p.LookY = -0.9;
                p.EyeScale = 1.12;
                p.ShadowAlpha = 0;
                fx = PoseEffect.Sweat;
                break;
            }
            case AnimClip.ClimbEdge:
            {
                // Pull up (0-0.55), swing a leg over (0.4-0.8), stand (0.8-1). The controller raises the feet
                // along ClimbEdgeRise(u) so the hands stay on the edge.
                var pull = MathUtil.SmoothStep(u / 0.6);
                var leg = MathUtil.Bump((u - 0.4) / 0.45);
                var stand = MathUtil.SmoothStep((u - 0.75) / 0.25);
                p.ArmLRot = 170 * (1 - pull) + 40 * pull * (1 - stand);
                p.ArmRRot = -170 * (1 - pull) - 40 * pull * (1 - stand);
                p.ArmLDy = -18 * (1 - pull);
                p.ArmRDy = -18 * (1 - pull);
                p.LegLRot = 55 * leg;
                p.LegLDy = -18 * leg;
                p.LegRRot = -6 * leg;
                p.RootRot = -8 * leg;
                p.BodySy = 1 - 0.06 * leg;
                p.LookY = -0.8 * (1 - stand);
                p.ShadowAlpha = stand;
                break;
            }

            // ---------------- CURSOR INTERACTION ----------------
            case AnimClip.Dodge:
            {
                var k = MathUtil.Bump(u);
                p.RootRot = -14 * k;
                p.RootDx = 24 * k;
                p.BodySy = 1 - 0.08 * k;
                p.ArmLRot = 50 * k;
                p.ArmRRot = -20 * k;
                p.HeadRot = -10 * k;
                p.EyeScale = 1 + 0.25 * k;
                p.LegLRot = -12 * k;
                fx = PoseEffect.Exclaim;
                break;
            }
            case AnimClip.Annoyed:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.15, 0.2);
                p.ArmLRot = -42 * k;
                p.ArmRRot = 42 * k;
                p.ArmLDy = -24 * k;
                p.ArmRDy = -24 * k;
                p.EyeOpen = 1 - 0.5 * k;
                p.HeadRot = -6 * k;
                p.LegLRot = 4 * Math.Max(0, Math.Sin(t * 12)) * k;
                p.LegLDy = -5 * Math.Max(0, Math.Sin(t * 12)) * k;
                fx = PoseEffect.Anger;
                break;
            }
            case AnimClip.Happy:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.15, 0.2);
                var bob = Math.Abs(Math.Sin(t * 7));
                p.RootDy = -10 * bob * k;
                p.ArmLRot = 45 * k;
                p.ArmRRot = -45 * k;
                p.HeadRot = 7 * Math.Sin(t * 7) * k;
                p.EyeOpen = 1 - 0.55 * k;
                fx = PoseEffect.Heart;
                break;
            }

            // ---------------- DRAG INTERACTION ----------------
            case AnimClip.HangHandL:
            case AnimClip.HangHandR:
            {
                var left = clip == AnimClip.HangHandL;
                var sw = Math.Clamp(ctx.SwingSpeed / 300.0, -1, 1);
                var dangle = Math.Sin(ctx.Time * 4.3);
                if (left)
                {
                    p.ArmLRot = HangArmAngle;
                    p.ArmRRot = -20 - 10 * dangle - 25 * sw;
                }
                else
                {
                    p.ArmRRot = -HangArmAngle;
                    p.ArmLRot = 20 + 10 * dangle - 25 * sw;
                }
                p.LegLRot = 6 * dangle - 14 * sw;
                p.LegRRot = -4 * dangle - 14 * sw;
                p.HeadRot = left ? 8 : -8;
                p.LookY = -0.7;
                p.LookX = left ? -0.3 : 0.3;
                p.StringsRot = -ctx.SwingAngle * 0.3;
                p.ShadowAlpha = 0;
                break;
            }
            case AnimClip.HangFoot:
            {
                // Upside down: the held leg stays straight, arms and the other leg dangle "up" (towards the floor).
                var sw = Math.Clamp(ctx.SwingSpeed / 300.0, -1, 1);
                var dangle = Math.Sin(ctx.Time * 3.7);
                p.ArmLRot = 150 + 8 * dangle + 12 * sw;
                p.ArmRRot = -150 + 8 * dangle + 12 * sw;
                p.ArmLDy = -10;
                p.ArmRDy = -10;
                p.LegRRot = -24 + 8 * dangle;
                p.HeadRot = 6 * dangle;
                p.HeadDy = -6;
                p.StringsRot = 170;
                p.EyeScale = 1.15;
                p.LookY = 0.9;
                p.ShadowAlpha = 0;
                fx = PoseEffect.Sweat;
                break;
            }
            case AnimClip.HangTorso:
            {
                // Scruffed round the middle: arms and legs hang forward, a little curled.
                var sw = Math.Clamp(ctx.SwingSpeed / 300.0, -1, 1);
                var d = Math.Sin(ctx.Time * 4);
                p.ArmLRot = -12 + 6 * d - 20 * sw;
                p.ArmRRot = 12 + 6 * d - 20 * sw;
                p.LegLRot = 14 + 6 * d - 16 * sw;
                p.LegRRot = 10 - 6 * d - 16 * sw;
                p.LegLDy = -6;
                p.HeadRot = -6;
                p.HeadDy = 5;
                p.LookY = 0.5;
                p.ShadowAlpha = 0;
                break;
            }
            case AnimClip.Struggle:
            {
                var a = Math.Sin(ctx.Time * 19);
                var b = Math.Sin(ctx.Time * 15 + 1);
                p.ArmLRot = 60 + 45 * a;
                p.ArmRRot = -60 + 45 * b;
                p.LegLRot = 25 * b;
                p.LegRRot = -25 * a;
                p.LegLDy = -10 * Math.Max(0, a);
                p.LegRDy = -10 * Math.Max(0, b);
                p.HeadRot = 10 * a;
                p.BodySy = 1.02 + 0.03 * b;
                p.EyeOpen = 0.55;
                p.ShadowAlpha = 0;
                fx = PoseEffect.Anger;
                break;
            }
            case AnimClip.RelaxedCarry:
            {
                var d = Math.Sin(ctx.Time * 1.6);
                p.ArmLRot = 6 + 3 * d;
                p.ArmRRot = -6 + 3 * d;
                p.LegLRot = 4 * d - ctx.SwingAngle * 0.25;
                p.LegRRot = -3 * d - ctx.SwingAngle * 0.25;
                p.HeadRot = 5 + 2 * d;
                p.EyeOpen = 0.35;
                p.TorsoDy = 2 * d;
                p.ShadowAlpha = 0;
                fx = PoseEffect.Music;
                break;
            }
            case AnimClip.Dizzy:
            {
                Idle(ref p, ctx.Time);
                var fade = 1 - MathUtil.SmoothStep((u - 0.8) / 0.2);
                var w = Math.Sin(t * 5);
                p.RootRot = 7 * w * fade;
                p.HeadRot = 12 * Math.Sin(t * 5 + 0.8) * fade;
                p.ArmLRot = (25 + 10 * w) * fade;
                p.ArmRRot = (-25 + 10 * w) * fade;
                p.LegLRot = 6 * w * fade;
                p.EyeOpen = 1 - 0.4 * fade;
                p.LookX = 0.7 * Math.Sin(t * 9) * fade;
                p.LookY = 0.7 * Math.Cos(t * 9) * fade;
                fx = PoseEffect.Stars;
                break;
            }

            // ---------------- WORK / PC LOAD / DOWNLOADS ----------------
            case AnimClip.CheckResult:
            {
                Idle(ref p, ctx.Time);
                var nod = MathUtil.Bump((u - 0.3) / 0.25) + MathUtil.Bump((u - 0.55) / 0.25);
                p.HeadRot = -9 * nod;
                p.HeadDy = 4 * nod;
                p.LookY = 0.3;
                p.ArmRRot = -35 * MathUtil.Bump(u);
                fx = u > 0.6 ? PoseEffect.Sparkle : PoseEffect.None;
                break;
            }
            case AnimClip.CarryLoad:
            {
                // A crate on its head: knees bent, small wobbly steps in place.
                var k = MathUtil.SmoothStep(t / 0.5);
                var step = Math.Sin(ctx.Time * 5);
                var br = Math.Sin(ctx.Time * 2.5);
                p.PropCrate = k;
                p.ArmLRot = 160 * k;
                p.ArmRRot = -160 * k;
                p.ArmLDy = -16 * k;
                p.ArmRDy = -16 * k;
                p.BodySy = 1 - 0.07 * k + 0.01 * br;
                p.BodySx = 1 + 0.03 * k;
                p.HeadDy = 6 * k;
                p.HeadRot = 2 * step * k;
                p.LegLDy = -6 * Math.Max(0, step) * k;
                p.LegRDy = -6 * Math.Max(0, -step) * k;
                p.RootRot = 2 * step * k;
                p.EyeOpen = 1 - 0.4 * k;
                fx = PoseEffect.Sweat;
                break;
            }
            case AnimClip.CatchPackage:
            {
                var k = MathUtil.SmoothStep(u / 0.6);
                var catchBump = MathUtil.Bump((u - 0.55) / 0.3);
                p.ArmLRot = -38 * k - 10 + 60 * (1 - k);
                p.ArmRRot = 38 * k + 10 - 60 * (1 - k);
                p.ArmLDy = -30 * k;
                p.ArmRDy = -30 * k;
                p.ItemAlpha = 1;
                p.ItemDy = -520 + 470 * k;
                p.ItemRot = 40 * (1 - k);
                p.ItemScale = 1.1;
                p.BodySy = 1 - 0.07 * catchBump;
                p.LookY = -0.9 + 1.3 * k;
                fx = u > 0.7 ? PoseEffect.Sparkle : PoseEffect.Arrow;
                break;
            }

            // ---------------- NOTIFICATION ----------------
            case AnimClip.Knock:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.15, 0.15);
                var knock = Math.Max(0, Math.Sin(u * tau * 3));
                p.ArmRRot = (-95 - 25 * knock) * k;
                p.ArmRDy = -20 * k;
                p.RootRot = -3 * k;
                p.HeadRot = 4 * k;
                p.LookX = 0.2;
                fx = knock > 0.8 ? PoseEffect.Knock : PoseEffect.None;
                break;
            }
            case AnimClip.Point:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.2, 0.2);
                p.ArmLRot = 95 * k + 4 * Math.Sin(t * 9) * k;
                p.ArmLDy = -18 * k;
                p.LookX = -0.8 * k;
                p.HeadRot = -4 * k;
                p.RootRot = -3 * k;
                break;
            }

            // ---------------- SUCCESS / FAILURE / EMOTIONS ----------------
            case AnimClip.ThumbsUp:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.2, 0.2);
                p.ArmRRot = -70 * k;
                p.ArmRDy = -34 * k;
                p.HeadRot = 6 * k;
                p.EyeOpen = 1 - 0.45 * k;
                fx = u > 0.3 ? PoseEffect.Sparkle : PoseEffect.None;
                break;
            }
            case AnimClip.Proud:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.2, 0.2);
                p.BodySy = 1 + 0.05 * k;
                p.TorsoDy = -4 * k;
                p.ArmLRot = -45 * k;
                p.ArmRRot = 45 * k;
                p.ArmLDy = -20 * k;
                p.ArmRDy = -20 * k;
                p.HeadRot = 9 * k;
                p.HeadDy = -4 * k;
                p.EyeOpen = 1 - 0.6 * k;
                p.LookY = -0.4 * k;
                fx = PoseEffect.Sparkle;
                break;
            }
            case AnimClip.Confused:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.2, 0.2);
                var s = Math.Sign(Math.Sin(u * tau * 1.2));
                p.HeadRot = 14 * s * k;
                p.ArmLRot = 40 * k;
                p.ArmRRot = -40 * k;
                p.ArmLDy = -10 * k;
                p.ArmRDy = -10 * k;
                p.LookX = 0.4 * s * k;
                fx = PoseEffect.Question;
                break;
            }
            case AnimClip.Frustrated:
            {
                var stomp = Math.Max(0, Math.Sin(u * tau * 3));
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.1, 0.2);
                p.ArmLRot = (20 + 20 * stomp) * k;
                p.ArmRRot = (-20 - 20 * stomp) * k;
                p.LegLRot = 10 * stomp * k;
                p.LegLDy = -14 * stomp * k;
                p.BodySy = 1 - 0.05 * stomp * k;
                p.HeadRot = -8 * k;
                p.EyeOpen = 1 - 0.5 * k;
                fx = PoseEffect.Anger;
                break;
            }
            case AnimClip.Facepalm:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.25, 0.2);
                p.ArmRRot = 150 * k;
                p.ArmRDy = -34 * k;
                p.HeadRot = -12 * k;
                p.HeadDy = 6 * k;
                p.EyeOpen = 1 - 0.9 * k;
                p.TorsoDy = 3 * k;
                break;
            }
            case AnimClip.Excited:
            {
                var hop = ctx.ReducedMotion ? 0 : Math.Abs(Math.Sin(t * 9));
                var k = Env(u, 0.08, 0.15);
                p.RootDy = -18 * hop * k;
                p.ArmLRot = (80 + 30 * Math.Sin(t * 18)) * k;
                p.ArmRRot = (-80 - 30 * Math.Sin(t * 18 + 1)) * k;
                p.LegLDy = -8 * hop * k;
                p.LegRDy = -8 * hop * k;
                p.EyeScale = 1 + 0.2 * k;
                p.BodySy = 1 + 0.04 * hop * k;
                fx = PoseEffect.Sparkle;
                break;
            }
            case AnimClip.Suspicious:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.25, 0.2);
                p.EyeOpen = 1 - 0.6 * k;
                p.EyeScale = 1 - 0.05 * k;
                p.LookX = (0.8 * Math.Sign(Math.Sin(u * tau)) ) * k;
                p.RootRot = -5 * k;
                p.HeadRot = -8 * k;
                p.ArmLRot = -20 * k;
                p.ArmRRot = 20 * k;
                p.ArmLDy = -10 * k;
                break;
            }
            case AnimClip.Scared:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.08, 0.3);
                var shake = Math.Sin(t * 40) * k;
                p.BodySy = 1 - 0.1 * k;
                p.BodySx = 1 + 0.03 * k;
                p.RootDx = 2 * shake;
                p.ArmLRot = -40 * k;
                p.ArmRRot = 40 * k;
                p.ArmLDy = -32 * k;
                p.ArmRDy = -32 * k;
                p.HeadDy = 8 * k;
                p.EyeScale = 1 + 0.3 * k;
                fx = PoseEffect.Sweat;
                break;
            }
            case AnimClip.Embarrassed:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.2, 0.2);
                p.HeadRot = -12 * k;
                p.HeadDy = 5 * k;
                p.LookY = 0.8 * k;
                p.LookX = -0.5 * k;
                p.ArmLRot = -30 * k;
                p.ArmRRot = 30 * k;
                p.ArmLDy = -14 * k + 3 * Math.Sin(t * 6) * k;
                p.ArmRDy = -14 * k - 3 * Math.Sin(t * 6) * k;
                p.LegLRot = -6 * k;
                p.LegRRot = 6 * k;
                fx = PoseEffect.Sweat;
                break;
            }
            case AnimClip.Sad:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.3, 0.2);
                p.HeadRot = -10 * k;
                p.HeadDy = 8 * k;
                p.TorsoDy += 4 * k;
                p.BodySy = 1 - 0.04 * k;
                p.LookY = 0.9 * k;
                p.EyeOpen = 1 - 0.3 * k;
                p.ArmLRot = -4 * k;
                p.ArmRRot = 4 * k;
                p.StringsRot = 0;
                break;
            }
            case AnimClip.Angry:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.1, 0.2);
                var shake = Math.Sin(t * 30) * k;
                p.ArmLRot = (30 + 6 * shake) * k;
                p.ArmRRot = (-30 - 6 * shake) * k;
                p.ArmLDy = -14 * k;
                p.ArmRDy = -14 * k;
                p.BodySy = 1 - 0.05 * k;
                p.BodySx = 1 + 0.03 * k;
                p.EyeOpen = 1 - 0.45 * k;
                p.HeadRot = -5 * k + 2 * shake;
                p.RootDy = -4 * Math.Abs(shake);
                fx = PoseEffect.Anger;
                break;
            }

            // ---------------- SLEEP / AFK ----------------
            case AnimClip.SleepLying:
            {
                Lie(ref p, 1, ctx.Time);
                var br = Math.Sin(ctx.Time * tau / 4.2);
                p.EyeOpen = 0;
                p.TorsoDy = 3 * br;
                p.BodySy = 1 + 0.015 * br;
                p.HeadRot = -6 + 1.5 * br;
                // Curled up a little: feet together, arms tucked.
                p.LegLRot = 16;
                p.LegRRot = 26;
                p.ArmLRot = -20;
                p.ArmRRot = 55;
                fx = PoseEffect.Zzz;
                break;
            }
            case AnimClip.DreamTwitch:
            {
                Lie(ref p, 1, ctx.Time);
                var tw = MathUtil.Bump(u / 0.3) + 0.6 * MathUtil.Bump((u - 0.45) / 0.25);
                p.EyeOpen = 0;
                p.LegLRot = 16 - 14 * tw;
                p.LegRRot = 26 - 10 * tw;
                p.ArmLRot = -20 + 18 * tw;
                p.ArmRRot = 55;
                p.HeadRot = -6 + 4 * tw;
                fx = PoseEffect.Zzz;
                break;
            }
            case AnimClip.WakeFromLying:
            {
                // Open eyes, sit up (lie -> sit), then a big stretch.
                var sitUp = MathUtil.SmoothStep((u - 0.15) / 0.5);
                var q = Pose.Neutral;
                Lie(ref q, 1, ctx.Time);
                q.LegLRot = 16;
                q.LegRRot = 26;
                var s = Pose.Neutral;
                Sit(ref s, 1, ctx.Time);
                p = Pose.Lerp(q, s, sitUp);
                p.EyeOpen = MathUtil.SmoothStep(u / 0.2);
                var stretch = MathUtil.Bump((u - 0.6) / 0.4);
                p.ArmLRot += 140 * stretch;
                p.ArmRRot -= 140 * stretch;
                p.ArmLDy = -10 * stretch;
                p.ArmRDy = -10 * stretch;
                break;
            }
            case AnimClip.WakeStartled:
            {
                var q = Pose.Neutral;
                Lie(ref q, 1, ctx.Time);
                var up = MathUtil.SmoothStep(u / 0.45);
                p = Pose.Lerp(q, Pose.Neutral, up);
                var hop = MathUtil.Bump((u - 0.3) / 0.5);
                p.RootDy -= 36 * hop;
                p.EyeScale = 1 + 0.35 * MathUtil.Bump(u);
                p.ArmLRot += 55 * hop;
                p.ArmRRot -= 55 * hop;
                fx = PoseEffect.Exclaim;
                break;
            }

            // ---------------- INVENTORY ----------------
            case AnimClip.BackpackHeavy:
            {
                var lift = MathUtil.Bump(u);
                Hold(ref p, 1, ctx.Time);
                p.PropBackpack = Env(u, 0.15, 0.15);
                p.BackpackLid = 0;
                p.BodySy = 1 - 0.09 * lift;
                p.BodySx = 1 + 0.04 * lift;
                p.RootRot = 5 * Math.Sin(u * tau);
                p.HeadDy = 6 * lift;
                p.EyeOpen = 1 - 0.5 * lift;
                p.LegLDy = -6 * Math.Max(0, Math.Sin(u * tau * 2));
                p.LegRDy = -6 * Math.Max(0, -Math.Sin(u * tau * 2));
                fx = PoseEffect.Sweat;
                break;
            }
            case AnimClip.TearPage:
            {
                // 0-0.3 hold the notebook and rip, 0.3-0.65 crumple the page, 0.65-1 toss it over the shoulder.
                var hold = 1 - MathUtil.SmoothStep((u - 0.3) / 0.15);
                var rip = MathUtil.Bump(u / 0.3);
                var crumple = MathUtil.Bump((u - 0.3) / 0.35);
                var toss = MathUtil.SmoothStep((u - 0.65) / 0.2);
                var back = MathUtil.SmoothStep((u - 0.85) / 0.15);
                Idle(ref p, ctx.Time);
                p.PropNotebook = hold;
                p.ArmLRot = -34 * hold - 30 * crumple * (1 - hold);
                p.ArmLDy = -24 * hold - 20 * crumple;
                p.ArmRRot = (38 - 60 * rip) * hold + (30 * crumple) * (1 - hold) - 150 * toss * (1 - back);
                p.ArmRDy = -28 * hold - 20 * crumple - 12 * toss * (1 - back);
                // The torn page shows briefly, then becomes the paper ball effect (EffectLayer) that gets tossed.
                p.ItemAlpha = u < 0.22 ? 0 : 1 - MathUtil.SmoothStep((u - 0.36) / 0.08);
                p.ItemScale = 1 - 0.5 * MathUtil.SmoothStep((u - 0.25) / 0.15);
                p.ItemDy = -60;
                p.ItemRot = 20 * Math.Sin(t * 30) * crumple;
                p.HeadRot = 6 * crumple - 5 * toss;
                p.LookY = 0.6 * (1 - toss);
                fx = u > 0.3 && u < 0.97 ? PoseEffect.PaperBall : PoseEffect.None;
                break;
            }

            // ---------------- ENVIRONMENT / BOUNDARIES ----------------
            case AnimClip.CheckTime:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.2, 0.2);
                p.ArmLRot = -55 * k;
                p.ArmLDy = -30 * k;
                p.LookY = 0.7 * k;
                p.LookX = -0.3 * k;
                p.HeadRot = -6 * k;
                fx = u > 0.4 && u < 0.8 ? PoseEffect.Dots : PoseEffect.None;
                break;
            }
            case AnimClip.Shiver:
            {
                Idle(ref p, ctx.Time);
                var k = Env(u, 0.1, 0.2);
                var s = Math.Sin(t * 45) * k;
                p.ArmLRot = -40 * k;
                p.ArmRRot = 40 * k;
                p.ArmLDy = -26 * k;
                p.ArmRDy = -26 * k;
                p.RootDx = 2.5 * s;
                p.BodySy = 1 - 0.05 * k;
                p.HeadDy = 6 * k;
                p.EyeOpen = 1 - 0.3 * k;
                break;
            }
            case AnimClip.BoundaryBump:
            {
                var hit = MathUtil.Bump(u / 0.25);
                var think = MathUtil.Bump((u - 0.25) / 0.5);
                Idle(ref p, ctx.Time);
                p.RootRot = 8 * hit;
                p.BodySx = 1 - 0.06 * hit;
                p.ArmLRot = 60 * hit;
                p.ArmRRot = -20 * hit;
                p.HeadRot = 10 * hit - 6 * think;
                p.ArmRRot += 60 * think;
                p.ArmRDy = -24 * think;
                fx = u < 0.3 ? PoseEffect.Exclaim : u < 0.75 ? PoseEffect.Question : PoseEffect.None;
                break;
            }
        }
        return fx;
    }

    /// <summary>
    /// Fraction of the hang depth still below the edge during ClimbEdge (1 = hanging .. 0 = standing on it).
    /// The controller moves the feet along it so the hands stay on the edge while the pose pulls up.
    /// </summary>
    public static double ClimbEdgeDepth(double u) => 1 - MathUtil.SmoothStep(MathUtil.Clamp01(u / 0.7));

    /// <summary>How far below the edge the feet hang during HangEdge, in reference pixels.</summary>
    public const double HangEdgeDepth = 680;
}
