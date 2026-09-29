using System;
using System.Collections.Generic;
using System.Linq;
using HoodieCompanion.Geometry;

namespace HoodieCompanion.Platform;

/// <summary>Enumerates monitors in physical virtual-desktop pixels with their per-monitor DPI scale.</summary>
public static class MonitorService
{
    public static WorldGeometry Query()
    {
        var list = new List<(string Id, RectD Bounds, RectD Work, double Scale, bool Primary)>();
        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref NativeMethods.RECT r, IntPtr d) =>
            {
                var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
                if (!NativeMethods.GetMonitorInfo(h, ref info)) return true;
                double scale = 1;
                try
                {
                    if (NativeMethods.GetDpiForMonitor(h, 0, out var dx, out _) == 0 && dx > 0) scale = dx / 96.0;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                var b = info.rcMonitor;
                var w = info.rcWork;
                list.Add((info.szDevice ?? $"MON{list.Count}", new RectD(b.Left, b.Top, b.Width, b.Height),
                    new RectD(w.Left, w.Top, w.Width, w.Height), scale, (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error("EnumDisplayMonitors failed", ex);
        }

        if (list.Count == 0)
        {
            // Fallback: WPF's idea of the primary work area (in DIP == px at 100%).
            var wa = System.Windows.SystemParameters.WorkArea;
            var sw = System.Windows.SystemParameters.PrimaryScreenWidth;
            var sh = System.Windows.SystemParameters.PrimaryScreenHeight;
            list.Add(("PRIMARY", new RectD(0, 0, sw, sh), new RectD(wa.X, wa.Y, wa.Width, wa.Height), 1, true));
        }

        var ordered = list.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
        var monitors = ordered.Select((m, i) => new MonitorInfo(m.Id, i, m.Bounds, m.Work.IsEmpty ? m.Bounds : m.Work, m.Scale, m.Primary)).ToList();
        return new WorldGeometry(monitors);
    }
}
