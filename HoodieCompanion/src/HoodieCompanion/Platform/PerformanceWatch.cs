using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HoodieCompanion.Platform;

/// <summary>
/// Watches Hoodie's own footprint over long sessions (8-12 h): CPU, working set, managed heap, handles,
/// GDI and USER objects. Sampled once a minute, logged every 10 minutes, with a warning when something only
/// ever grows (a leak) so it shows up in the log instead of slowly hurting the PC.
/// </summary>
public sealed class PerformanceWatch
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

    public readonly record struct Sample(DateTime At, double CpuPercent, long WorkingSet, long Managed, int Handles, uint Gdi, uint User);

    private readonly Process _self = Process.GetCurrentProcess();
    private TimeSpan _prevCpu;
    private DateTime _prevAt;
    private int _samples;

    public Sample? First { get; private set; }
    public Sample? Latest { get; private set; }
    public Sample Peak { get; private set; }

    /// <summary>Takes a sample (call about once a minute).</summary>
    public Sample Take()
    {
        _self.Refresh();
        var now = DateTime.Now;
        var cpu = _self.TotalProcessorTime;
        var wall = (now - _prevAt).TotalSeconds;
        var pct = _prevAt == default || wall <= 0 ? 0 : 100 * (cpu - _prevCpu).TotalSeconds / (wall * Environment.ProcessorCount);
        _prevCpu = cpu;
        _prevAt = now;
        uint gdi = 0, user = 0;
        try
        {
            gdi = GetGuiResources(_self.Handle, 0);
            user = GetGuiResources(_self.Handle, 1);
        }
        catch
        {
            // not available (e.g. under some compatibility layers)
        }
        var s = new Sample(now, pct, _self.WorkingSet64, GC.GetTotalMemory(false), _self.HandleCount, gdi, user);
        First ??= s;
        Latest = s;
        Peak = new Sample(now, Math.Max(Peak.CpuPercent, s.CpuPercent), Math.Max(Peak.WorkingSet, s.WorkingSet), Math.Max(Peak.Managed, s.Managed),
            Math.Max(Peak.Handles, s.Handles), Math.Max(Peak.Gdi, s.Gdi), Math.Max(Peak.User, s.User));
        _samples++;
        if (_samples % 10 == 1) Log.Info("perf " + Describe(s));
        if (First is { } f && (now - f.At).TotalHours > 1)
        {
            // Rough leak guard: after the first hour, growth far beyond the warm-up level is suspicious.
            if (s.Handles > f.Handles * 3 + 500 || s.Gdi > f.Gdi * 3 + 300 || s.User > f.User * 3 + 300 || s.WorkingSet > f.WorkingSet * 4 + 400_000_000)
                if (_samples % 10 == 1) Log.Info("WARN perf growth since start: " + Describe(f) + " -> " + Describe(s));
        }
        return s;
    }

    public static string Describe(in Sample s) =>
        $"cpu {s.CpuPercent:0.0}% ram {s.WorkingSet / 1048576.0:0} MB heap {s.Managed / 1048576.0:0} MB handles {s.Handles} gdi {s.Gdi} user {s.User}";
}
