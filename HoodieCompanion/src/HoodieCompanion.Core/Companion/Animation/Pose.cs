namespace HoodieCompanion.Companion.Animation;

/// <summary>
/// A full rig pose. Units are reference-image pixels and degrees (clockwise positive, y down).
/// The rig's native orientation faces screen-left; "forward" therefore means negative X / positive
/// limb rotation (a positive leg or arm rotation swings the foot/hand forward).
/// </summary>
public struct Pose
{
    public double RootDx, RootDy, RootRot, BodySx, BodySy;
    public double TorsoRot, TorsoDy;
    public double HeadRot, HeadDx, HeadDy;
    public double LookX, LookY, EyeOpen, EyeScale;
    public double ArmLRot, ArmRRot, ArmLDy, ArmRDy;
    public double LegLRot, LegRRot, LegLDy, LegRDy, LegLSy, LegRSy;
    public double StringsRot;
    public double ItemAlpha, ItemDx, ItemDy, ItemRot, ItemScale;
    public double ShadowScale, ShadowAlpha;
    public double Opacity, Scale;

    public static Pose Neutral => new()
    {
        BodySx = 1, BodySy = 1, EyeOpen = 1, EyeScale = 1, LegLSy = 1, LegRSy = 1,
        ItemScale = 1, ShadowScale = 1, ShadowAlpha = 1, Opacity = 1, Scale = 1,
    };

    public static Pose Lerp(in Pose a, in Pose b, double t)
    {
        static double L(double x, double y, double k) => x + (y - x) * k;
        return new Pose
        {
            RootDx = L(a.RootDx, b.RootDx, t), RootDy = L(a.RootDy, b.RootDy, t), RootRot = L(a.RootRot, b.RootRot, t),
            BodySx = L(a.BodySx, b.BodySx, t), BodySy = L(a.BodySy, b.BodySy, t),
            TorsoRot = L(a.TorsoRot, b.TorsoRot, t), TorsoDy = L(a.TorsoDy, b.TorsoDy, t),
            HeadRot = L(a.HeadRot, b.HeadRot, t), HeadDx = L(a.HeadDx, b.HeadDx, t), HeadDy = L(a.HeadDy, b.HeadDy, t),
            LookX = L(a.LookX, b.LookX, t), LookY = L(a.LookY, b.LookY, t),
            EyeOpen = L(a.EyeOpen, b.EyeOpen, t), EyeScale = L(a.EyeScale, b.EyeScale, t),
            ArmLRot = L(a.ArmLRot, b.ArmLRot, t), ArmRRot = L(a.ArmRRot, b.ArmRRot, t),
            ArmLDy = L(a.ArmLDy, b.ArmLDy, t), ArmRDy = L(a.ArmRDy, b.ArmRDy, t),
            LegLRot = L(a.LegLRot, b.LegLRot, t), LegRRot = L(a.LegRRot, b.LegRRot, t),
            LegLDy = L(a.LegLDy, b.LegLDy, t), LegRDy = L(a.LegRDy, b.LegRDy, t),
            LegLSy = L(a.LegLSy, b.LegLSy, t), LegRSy = L(a.LegRSy, b.LegRSy, t),
            StringsRot = L(a.StringsRot, b.StringsRot, t),
            ItemAlpha = L(a.ItemAlpha, b.ItemAlpha, t), ItemDx = L(a.ItemDx, b.ItemDx, t), ItemDy = L(a.ItemDy, b.ItemDy, t),
            ItemRot = L(a.ItemRot, b.ItemRot, t), ItemScale = L(a.ItemScale, b.ItemScale, t),
            ShadowScale = L(a.ShadowScale, b.ShadowScale, t), ShadowAlpha = L(a.ShadowAlpha, b.ShadowAlpha, t),
            Opacity = L(a.Opacity, b.Opacity, t), Scale = L(a.Scale, b.Scale, t),
        };
    }

    /// <summary>Returns every value as a flat array (used by validation tests).</summary>
    public readonly double[] ToArray() => new[]
    {
        RootDx, RootDy, RootRot, BodySx, BodySy, TorsoRot, TorsoDy, HeadRot, HeadDx, HeadDy, LookX, LookY, EyeOpen, EyeScale,
        ArmLRot, ArmRRot, ArmLDy, ArmRDy, LegLRot, LegRRot, LegLDy, LegRDy, LegLSy, LegRSy, StringsRot,
        ItemAlpha, ItemDx, ItemDy, ItemRot, ItemScale, ShadowScale, ShadowAlpha, Opacity, Scale,
    };
}

/// <summary>Small diegetic overlays drawn next to Hoodie (never UI chrome).</summary>
public enum PoseEffect
{
    None,
    Zzz,
    Exclaim,
    Question,
    Dust,
    Sparkle,
    Heat,
    Dots,
    Arrow,
}

public readonly record struct AnimFrame(Pose Pose, PoseEffect Effect, double EffectTime);
