using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using HoodieCompanion.Geometry;
using HoodieCompanion.Platform;

namespace HoodieCompanion.Presence;

/// <summary>What is in the foreground right now (sampled at ~1 Hz, never per frame).</summary>
public sealed record ForegroundInfo(string? ProcessName, string? MonitorId, string? FullscreenMonitorId, RectD? Bounds);

/// <summary>
/// Detects fullscreen apps (games, video, presentations) and the foreground process for per-app rules.
/// </summary>
public sealed class FullscreenService
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    private readonly int _ownPid = Environment.ProcessId;
    private readonly Dictionary<uint, string> _names = new();

    public ForegroundInfo Current { get; private set; } = new(null, null, null, null);

    public ForegroundInfo Sample(WorldGeometry world)
    {
        try
        {
            var h = NativeMethods.GetForegroundWindow();
            if (h == IntPtr.Zero || h == NativeMethods.GetShellWindow() || h == NativeMethods.GetDesktopWindow())
                return Current = new ForegroundInfo(null, null, null, null);

            NativeMethods.GetWindowThreadProcessId(h, out var pid);
            if (pid == _ownPid) return Current;

            var cls = new StringBuilder(128);
            NativeMethods.GetClassName(h, cls, cls.Capacity);
            var isShell = ShellClasses.Contains(cls.ToString());

            if (NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT r, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0)
                NativeMethods.GetWindowRect(h, out r);
            var rect = new RectD(r.Left, r.Top, r.Width, r.Height);
            var center = rect.Center;
            var mon = world.Monitors.FirstOrDefault(m => m.Bounds.Contains(center)) ?? world.NearestMonitor(center);

            string? fullscreen = null;
            if (!isShell && NativeMethods.IsWindowVisible(h) && !NativeMethods.IsIconic(h) && !IsCloaked(h))
            {
                var b = mon.Bounds;
                if (rect.Left <= b.Left + 1 && rect.Top <= b.Top + 1 && rect.Right >= b.Right - 1 && rect.Bottom >= b.Bottom - 1)
                    fullscreen = mon.Id;
            }

            return Current = new ForegroundInfo(isShell ? null : ProcessName(pid), mon.Id, fullscreen, rect);
        }
        catch (Exception ex)
        {
            Log.Debug("foreground sample failed: " + ex.Message);
            return Current;
        }
    }

    private static bool IsCloaked(IntPtr h)
    {
        try
        {
            return NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private string? ProcessName(uint pid)
    {
        if (_names.TryGetValue(pid, out var n)) return n;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            n = p.ProcessName;
        }
        catch
        {
            n = null;
        }
        if (_names.Count > 256) _names.Clear();
        if (n is not null) _names[pid] = n;
        return n;
    }
}
