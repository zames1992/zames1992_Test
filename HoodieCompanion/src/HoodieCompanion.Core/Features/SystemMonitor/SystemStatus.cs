namespace HoodieCompanion.Features.SystemMonitor;

/// <summary>A 1 Hz snapshot of local PC load. Never transmitted anywhere.</summary>
public sealed record SystemStatus(
    double CpuUsage,
    ulong MemoryUsed,
    ulong MemoryTotal,
    double? DiskActivityOptional,
    double? NetworkDownloadOptional,
    double? NetworkUploadOptional,
    double? GpuUsageOptional,
    TimeSpan Uptime,
    DateTime UpdatedAt,
    double SelfCpu,
    ulong SelfMemory)
{
    public double MemoryPercent => MemoryTotal == 0 ? 0 : 100.0 * MemoryUsed / MemoryTotal;

    public static string Bytes(double b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        var i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return i >= 3 ? $"{b:0.0} {u[i]}" : $"{b:0} {u[i]}";
    }

    public static string Rate(double? bytesPerSec) => bytesPerSec is double v ? Bytes(v) + "/s" : "—";

    public static string FormatUptime(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h" : t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{t.Minutes} min";
}

public enum EnvironmentMood
{
    Calm,
    Busy,
    Downloading,
    CalmedDown,
}

/// <summary>
/// Turns raw metrics into rare, cooled-down world events ("the environment is working hard").
/// Hoodie never nags: each reaction has a long cooldown and needs sustained load.
/// </summary>
public sealed class EnvironmentInterpreter
{
    public double BusyThreshold { get; init; } = 85;
    public double BusySustainSeconds { get; init; } = 20;
    public double DownloadThreshold { get; init; } = 2 * 1024 * 1024;
    public double DownloadSustainSeconds { get; init; } = 10;
    public double Cooldown { get; init; } = 600;

    private double _busyFor;
    private double _downFor;
    private double _sinceBusy = double.MaxValue;
    private double _sinceDownload = double.MaxValue;
    private bool _wasBusy;
    private double _calmFor;

    public bool IsBusy => _busyFor >= BusySustainSeconds;

    /// <summary>Feed one sample; dt is the time since the previous one.</summary>
    public EnvironmentMood Feed(SystemStatus s, double dt)
    {
        _sinceBusy += dt;
        _sinceDownload += dt;
        _busyFor = s.CpuUsage >= BusyThreshold ? _busyFor + dt : 0;
        _downFor = (s.NetworkDownloadOptional ?? 0) >= DownloadThreshold ? _downFor + dt : 0;

        if (_busyFor >= BusySustainSeconds && _sinceBusy >= Cooldown)
        {
            _sinceBusy = 0;
            _wasBusy = true;
            _calmFor = 0;
            return EnvironmentMood.Busy;
        }
        if (_downFor >= DownloadSustainSeconds && _sinceDownload >= Cooldown)
        {
            _sinceDownload = 0;
            return EnvironmentMood.Downloading;
        }
        if (_wasBusy)
        {
            _calmFor = s.CpuUsage < 40 ? _calmFor + dt : 0;
            if (_calmFor > 30)
            {
                _wasBusy = false;
                return EnvironmentMood.CalmedDown;
            }
        }
        return EnvironmentMood.Calm;
    }
}
