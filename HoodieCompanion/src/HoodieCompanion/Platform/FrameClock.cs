using System;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;

namespace HoodieCompanion.Platform;

public enum FrameRateMode
{
    /// <summary>Synchronised with the compositor (monitor refresh) for smooth motion.</summary>
    Active,
    /// <summary>~24 fps for slow, calm animation (sitting, sleeping) to save CPU.</summary>
    Calm,
    /// <summary>~4 fps when nothing is visible.</summary>
    Idle,
}

/// <summary>
/// Frame-synchronised clock. Uses CompositionTarget.Rendering while Hoodie moves (so motion lands
/// exactly on display frames) and falls back to a low-rate timer when nothing moves much.
/// Always reports real delta time from a Stopwatch; simulation is frame-rate independent.
/// </summary>
public sealed class FrameClock : IDisposable
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
    private double _last;
    private bool _renderingHooked;
    private bool _running;

    public FrameClock()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(42) };
        _timer.Tick += (_, _) => Fire();
    }

    public event Action<double>? Tick;

    public FrameRateMode Mode { get; private set; } = FrameRateMode.Active;

    public double FramesPerSecond { get; private set; }

    private int _frames;
    private double _fpsWindowStart;

    public void Start()
    {
        _running = true;
        _last = _watch.Elapsed.TotalSeconds;
        Apply();
    }

    public void Stop()
    {
        _running = false;
        Apply();
    }

    public void SetMode(FrameRateMode mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        Apply();
    }

    private void Apply()
    {
        var wantRendering = _running && Mode == FrameRateMode.Active;
        if (wantRendering && !_renderingHooked)
        {
            CompositionTarget.Rendering += OnRendering;
            _renderingHooked = true;
        }
        else if (!wantRendering && _renderingHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }

        if (_running && Mode != FrameRateMode.Active)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(Mode == FrameRateMode.Calm ? 42 : 250);
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can fire more than once per frame; only step once per distinct frame.
        if (e is RenderingEventArgs r)
        {
            if (r.RenderingTime == _lastRenderingTime) return;
            _lastRenderingTime = r.RenderingTime;
        }
        Fire();
    }

    private void Fire()
    {
        var now = _watch.Elapsed.TotalSeconds;
        var dt = now - _last;
        _last = now;
        if (dt <= 0) return;
        _frames++;
        if (now - _fpsWindowStart >= 1)
        {
            FramesPerSecond = _frames / (now - _fpsWindowStart);
            _frames = 0;
            _fpsWindowStart = now;
        }
        Tick?.Invoke(Math.Min(dt, 0.1));
    }

    public void Dispose()
    {
        Stop();
        _timer.Stop();
    }
}
