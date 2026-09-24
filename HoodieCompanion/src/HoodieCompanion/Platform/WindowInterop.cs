using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HoodieCompanion.Platform;

/// <summary>Low-level window placement in physical pixels (avoids WPF Left/Top DPI rounding across monitors).</summary>
public static class WindowInterop
{
    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).Handle;

    /// <summary>Tool window that never steals focus and stays out of Alt+Tab.</summary>
    public static void MakeToolWindow(Window w, bool noActivate)
    {
        var h = Handle(w);
        if (h == IntPtr.Zero) return;
        var ex = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        if (noActivate) ex |= NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>Mouse clicks pass straight through (for decorative world props).</summary>
    public static void MakeClickThrough(Window w)
    {
        var h = Handle(w);
        if (h == IntPtr.Zero) return;
        var ex = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
        NativeMethods.SetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
    }

    public static void SetBounds(IntPtr h, int x, int y, int width, int height, bool topmost)
    {
        if (h == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(h, topmost ? NativeMethods.HWND_TOPMOST : IntPtr.Zero, x, y, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER | (topmost ? 0 : NativeMethods.SWP_NOZORDER));
    }

    public static void Move(IntPtr h, int x, int y)
    {
        if (h == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOOWNERZORDER);
    }

    public static void AssertTopmost(IntPtr h)
    {
        if (h == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOOWNERZORDER);
    }

    public static void ClearTopmost(IntPtr h)
    {
        if (h == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(h, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOOWNERZORDER);
    }

    /// <summary>The DPI scale WPF is currently rendering this window at.</summary>
    public static double WindowScale(Visual v)
    {
        try
        {
            return VisualTreeHelper.GetDpi(v).DpiScaleX;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>Rounded corners on Windows 11 for normal (non-layered) windows.</summary>
    public static void RoundCorners(Window w)
    {
        try
        {
            var h = Handle(w);
            var pref = 2; // DWMWCP_ROUND
            NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
            // Windows 10: square corners are fine.
        }
    }

    /// <summary>Places a DIP-sized window at a physical pixel position (top-left) on the right monitor.</summary>
    public static void PlaceWindowPx(Window w, double pxX, double pxY, double monitorScale)
    {
        var h = Handle(w);
        if (h == IntPtr.Zero)
        {
            w.Left = pxX / monitorScale;
            w.Top = pxY / monitorScale;
            return;
        }
        var widthPx = (int)Math.Round(w.Width * monitorScale);
        var heightPx = (int)Math.Round((double.IsNaN(w.Height) ? w.ActualHeight : w.Height) * monitorScale);
        NativeMethods.SetWindowPos(h, IntPtr.Zero, (int)Math.Round(pxX), (int)Math.Round(pxY), widthPx, heightPx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER);
    }
}
