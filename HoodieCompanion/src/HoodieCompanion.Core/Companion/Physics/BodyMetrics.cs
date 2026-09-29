using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Physics;

/// <summary>
/// Maps rig reference pixels (docs/reference.png) to device-independent and physical pixels.
/// </summary>
public readonly record struct BodyMetrics(double RefToDip, double MonitorScale)
{
    /// <summary>Feet/root point in reference pixels.</summary>
    public static readonly Vec2 RootLocal = new(272, 830);
    /// <summary>Visual centre of mass in reference pixels (upper belly).</summary>
    public static readonly Vec2 CenterLocal = new(272, 450);
    /// <summary>Top of the hood in reference pixels.</summary>
    public static readonly Vec2 HoodTopLocal = new(272, 110);
    public const double RefHeight = 765; // hood top (70) .. sole (835)
    public const double RefHalfWidth = 150;

    public double RefToPx => RefToDip * MonitorScale;

    public double FeetOffsetPx => (RootLocal.Y - CenterLocal.Y) * RefToPx;
    public double HeadOffsetPx => (CenterLocal.Y - 70) * RefToPx;
    public double HalfWidthPx => RefHalfWidth * RefToPx;
    public double HeightPx => RefHeight * RefToPx;

    /// <summary>Converts DIP-based speeds/accelerations into physical pixels for the current monitor.</summary>
    public double Dip(double dip) => dip * MonitorScale;

    public static BodyMetrics For(double characterHeightDip, double monitorScale) =>
        new(characterHeightDip / RefHeight, monitorScale);
}
