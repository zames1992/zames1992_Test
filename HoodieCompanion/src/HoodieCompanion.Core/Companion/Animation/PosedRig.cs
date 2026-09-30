using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Animation;

/// <summary>
/// Where rig parts are in the *current* pose (reference pixels, facing left), mirroring the transform chain
/// CharacterRig builds from rig.json (body → torso → arms/head, body → legs). Used to find what the user
/// grabbed, whatever Hoodie is doing (sitting, lying, walking).
/// </summary>
public static class PosedRig
{
    private static readonly Vec2 BodyPivot = new(272, 830), TorsoPivot = new(272, 612), HeadPivot = new(262, 350);
    private static readonly Vec2 ArmLPivot = new(168, 350), ArmRPivot = new(378, 350), LegLPivot = new(214, 612), LegRPivot = new(340, 612);

    public static readonly Vec2 HandL = new(140, 596), HandR = new(412, 610), FootL = new(186, 800), FootR = new(336, 806);
    public static readonly Vec2 Belly = new(272, 480), HeadCenter = new(262, 230);

    private static Vec2 Local(Vec2 p, Vec2 pivot, Vec2 offset, double rot, double sx = 1, double sy = 1)
    {
        var d = p - pivot;
        d = new Vec2(d.X * sx, d.Y * sy).Rotate(rot);
        return pivot + offset + d;
    }

    private static Vec2 Body(in Pose q, Vec2 p) => Local(p, BodyPivot, new Vec2(q.RootDx, q.RootDy), q.RootRot, q.BodySx * q.Scale, q.BodySy * q.Scale);

    private static Vec2 Torso(in Pose q, Vec2 p) => Body(q, Local(p, TorsoPivot, new Vec2(0, q.TorsoDy), q.TorsoRot));

    public static Vec2 HandLeft(in Pose q) => Torso(q, Local(HandL, ArmLPivot, new Vec2(0, q.ArmLDy), q.ArmLRot));

    public static Vec2 HandRight(in Pose q) => Torso(q, Local(HandR, ArmRPivot, new Vec2(0, q.ArmRDy), q.ArmRRot));

    public static Vec2 FootLeft(in Pose q) => Body(q, Local(FootL, LegLPivot, new Vec2(0, q.LegLDy), q.LegLRot, 1, q.LegLSy));

    public static Vec2 FootRight(in Pose q) => Body(q, Local(FootR, LegRPivot, new Vec2(0, q.LegRDy), q.LegRRot, 1, q.LegRSy));

    public static Vec2 BellyPoint(in Pose q) => Torso(q, Belly);

    public static Vec2 Head(in Pose q) => Torso(q, Local(HeadCenter, HeadPivot, new Vec2(q.HeadDx, q.HeadDy), q.HeadRot));
}
