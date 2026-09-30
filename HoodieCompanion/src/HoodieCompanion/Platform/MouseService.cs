using System;
using HoodieCompanion.Geometry;

namespace HoodieCompanion.Platform;

/// <summary>Cursor position (physical pixels), button state and user idle time.</summary>
public static class MouseService
{
    public static Vec2 Cursor()
    {
        return NativeMethods.GetCursorPos(out var p) ? new Vec2(p.X, p.Y) : Vec2.Zero;
    }

    /// <summary>
    /// State of the *logical* primary button. GetAsyncKeyState reports physical buttons, so when the user
    /// swapped buttons (left-handed setup) the primary button is the physical right one.
    /// </summary>
    public static bool LeftButtonDown()
    {
        var vk = System.Windows.SystemParameters.SwapButtons ? 0x02 : NativeMethods.VK_LBUTTON;
        return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    /// <summary>Tick of the last user input (any device). Used to tell typing from pointer use without reading keys.</summary>
    public static uint LastInputTick()
    {
        var info = new NativeMethods.LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
        return NativeMethods.GetLastInputInfo(ref info) ? info.dwTime : 0;
    }

    public static double UserIdleSeconds()
    {
        var info = new NativeMethods.LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
        if (!NativeMethods.GetLastInputInfo(ref info)) return 0;
        var now = (uint)Environment.TickCount;
        return Math.Max(0, (now - info.dwTime) / 1000.0);
    }
}
