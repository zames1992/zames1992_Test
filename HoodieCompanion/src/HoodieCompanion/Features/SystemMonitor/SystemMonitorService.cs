using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Threading;
using HoodieCompanion.Platform;

namespace HoodieCompanion.Features.SystemMonitor;

/// <summary>
/// Lightweight local PC monitor sampled at 1 Hz on a thread-pool timer (never per rendered frame).
/// CPU via GetSystemTimes, memory via GlobalMemoryStatusEx, network via interface counters,
/// disk activity via the PhysicalDisk performance counter when available. GPU is not reported
/// (no reliable, dependency-free source) and temperatures are out of scope.
/// </summary>
public sealed class SystemMonitorService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Timer _timer;
    private readonly Process _self = Process.GetCurrentProcess();
    private ulong _prevIdle, _prevKernel, _prevUser;
    private long _prevRx = -1, _prevTx = -1;
    private DateTime _prevNetAt;
    private TimeSpan _prevSelfCpu;
    private DateTime _prevSelfAt;
    private PerformanceCounter? _disk;
    private bool _diskTried;
    private int _busy;

    public SystemMonitorService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new Timer(_ => Sample(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1));
    }

    public event Action<SystemStatus>? Updated;

    public SystemStatus? Latest { get; private set; }

    private void Sample()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var cpu = Cpu();
            var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
            NativeMethods.GlobalMemoryStatusEx(ref mem);
            var (down, up) = Network();
            var disk = Disk();
            var (selfCpu, selfMem) = Self();
            var status = new SystemStatus(cpu, mem.ullTotalPhys - mem.ullAvailPhys, mem.ullTotalPhys, disk, down, up, null,
                TimeSpan.FromMilliseconds(Environment.TickCount64), DateTime.Now, selfCpu, selfMem);
            Latest = status;
            _dispatcher.BeginInvoke(() => Updated?.Invoke(status));
        }
        catch (Exception ex)
        {
            Log.Debug("system sample failed: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private double Cpu()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var di = idle.Value - _prevIdle;
        var dk = kernel.Value - _prevKernel;
        var du = user.Value - _prevUser;
        var first = _prevKernel == 0;
        _prevIdle = idle.Value;
        _prevKernel = kernel.Value;
        _prevUser = user.Value;
        var total = dk + du; // kernel time includes idle time
        if (first || total == 0) return 0;
        return Math.Clamp(100.0 * (total - di) / total, 0, 100);
    }

    private (double? Down, double? Up) Network()
    {
        try
        {
            long rx = 0, tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = ni.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
            var now = DateTime.UtcNow;
            double? down = null, up = null;
            if (_prevRx >= 0)
            {
                var dt = Math.Max(0.2, (now - _prevNetAt).TotalSeconds);
                down = Math.Max(0, (rx - _prevRx) / dt);
                up = Math.Max(0, (tx - _prevTx) / dt);
            }
            _prevRx = rx;
            _prevTx = tx;
            _prevNetAt = now;
            return (down, up);
        }
        catch
        {
            return (null, null);
        }
    }

    private double? Disk()
    {
        try
        {
            if (!_diskTried)
            {
                _diskTried = true;
                if (PerformanceCounterCategory.Exists("PhysicalDisk"))
                {
                    _disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
                    _disk.NextValue();
                }
                return null;
            }
            return _disk is null ? null : Math.Clamp(_disk.NextValue(), 0, 100);
        }
        catch
        {
            _disk = null;
            return null;
        }
    }

    private (double Cpu, ulong Mem) Self()
    {
        _self.Refresh();
        var now = DateTime.UtcNow;
        var cpuTime = _self.TotalProcessorTime;
        double pct = 0;
        if (_prevSelfAt != default)
        {
            var wall = (now - _prevSelfAt).TotalSeconds;
            if (wall > 0) pct = 100 * (cpuTime - _prevSelfCpu).TotalSeconds / (wall * Environment.ProcessorCount);
        }
        _prevSelfCpu = cpuTime;
        _prevSelfAt = now;
        return (Math.Max(0, pct), (ulong)_self.WorkingSet64);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _disk?.Dispose();
    }
}
