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

    public static bool LeftButtonDown() => (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;

    public static double UserIdleSeconds()
    {
        var info = new NativeMethods.LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
        if (!NativeMethods.GetLastInputInfo(ref info)) return 0;
        var now = (uint)Environment.TickCount;
        return Math.Max(0, (now - info.dwTime) / 1000.0);
    }
}
