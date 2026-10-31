using System;
using System.Windows;
using HoodieCompanion.Geometry;

namespace HoodieCompanion.Platform;

/// <summary>
/// Explicit physical-pixel / DIP conversion. The world model is in physical pixels; WPF content is in DIPs
/// at the DPI of the window's current monitor. Keeping the conversion in one place avoids DPI corruption
/// when Hoodie crosses monitors with different scaling.
/// </summary>
public static class DpiService
{
    public static void EnablePerMonitorV2()
    {
        try
        {
            NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch (Exception ex)
        {
            // Older Windows or already set by the manifest.
            Log.Info("SetProcessDpiAwarenessContext not applied: " + ex.Message);
        }
    }

    public static double PxToDip(double px, double scale) => px / (scale <= 0 ? 1 : scale);
    public static double DipToPx(double dip, double scale) => dip * (scale <= 0 ? 1 : scale);

    public static Point PxToDip(Vec2 px, double scale) => new(px.X / scale, px.Y / scale);
}
