using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Perception;

/// <summary>Something Hoodie noticed. Perception only reports; the Mind and the intent layer decide what it means.</summary>
public enum PerceptKind
{
    /// <summary>A window appeared (Subject = process name, Where = window centre).</summary>
    WindowOpened,
    /// <summary>A window disappeared or was minimised.</summary>
    WindowClosed,
    /// <summary>The user started dragging a window.</summary>
    WindowDragStarted,
    WindowDragEnded,
    /// <summary>The user switched to another app.</summary>
    AppSwitched,
    /// <summary>First time ever seeing this app (from memory).</summary>
    AppFirstSeen,
    /// <summary>The user started typing (inferred from input timing only; keys are never read).</summary>
    TypingStarted,
    TypingStopped,
    /// <summary>The user has been working without a real break for a long time.</summary>
    LongWorkSession,
    UserAway,
    UserReturned,
    /// <summary>CPU or GPU has been working hard for a while.</summary>
    PcHot,
    PcCooled,
    DownloadRunning,
    GameStarted,
    GameEnded,
    NightFell,
    MorningCame,
}

public readonly record struct Percept(PerceptKind Kind, double Time, string? Subject = null, Vec2? Where = null, double Strength = 1);

public enum WindowEventKind
{
    Opened,
    Closed,
    Minimized,
    MoveStarted,
    MoveEnded,
    Foreground,
}

/// <summary>A top-level window event reported by the platform layer (WinEventHook). No titles, no content.</summary>
public readonly record struct WindowEvent(WindowEventKind Kind, string? Process, RectD? Bounds);

/// <summary>Cheap per-frame / per-second facts from the platform layer.</summary>
public struct EnvironmentSample
{
    public double UserIdleSeconds;
    /// <summary>The last input looked like typing (input happened while the pointer did not move). Keys are never read.</summary>
    public bool KeyboardInput;
    public string? ForegroundProcess;
    public RectD? ForegroundBounds;
    public bool ForegroundFullscreen;
    public double? Cpu;
    public double? Gpu;
    public double? NetDown;
    public int? Hour;
}

/// <summary>
/// Turns raw platform signals into a few meaningful percepts and continuous facts ("the user is typing",
/// "they have been working for two hours", "the PC is hot", "a new app appeared"). Everything is local and
/// transient; only process names and counts ever reach memory.
/// </summary>
public sealed class PerceptionSystem
{
    public const double TypingWindowSeconds = 4;
    public const double LongSessionMinutes = 55;
    public const double HotSustainSeconds = 20;
    public const double AwaySeconds = 300;

    private readonly Queue<Percept> _queue = new();
    private double _time;
    private double _lastKeyboard = -100;
    private bool _typing;
    private double _sessionSeconds;
    private double _breakSeconds;
    private bool _sessionAnnounced;
    private double _hotFor, _coolFor;
    private bool _hot;
    private double _downFor;
    private bool _away;
    private bool _gaming;
    private string? _foreground;
    private int? _hour;

    /// <summary>Asked when an app is seen, returns true if it is new (memory decides).</summary>
    public Func<string, bool>? IsNewApp { get; set; }

    public bool Typing => _typing;
    /// <summary>The user is actively working right now (typing or busy input in the last few seconds).</summary>
    public bool UserBusy => _time - _lastKeyboard < 12;
    public double WorkSessionMinutes => _sessionSeconds / 60;
    public bool PcHot => _hot;
    public bool Gaming => _gaming;
    public string? ForegroundProcess => _foreground;
    public AppCategory ForegroundCategory { get; private set; }
    public RectD? ForegroundBounds { get; private set; }
    public double Cpu { get; private set; }
    public double Gpu { get; private set; }
    public bool Night => _hour is int h && (h >= 23 || h < 6);


    public bool TryDequeue(out Percept p) => _queue.TryDequeue(out p);

    public int Pending => _queue.Count;

    private void Emit(PerceptKind kind, string? subject = null, Vec2? where = null, double strength = 1)
    {
        if (_queue.Count > 64) _queue.Dequeue();
        _queue.Enqueue(new Percept(kind, _time, subject, where, strength));
    }

    public void Update(double dt, in EnvironmentSample s)
    {
        _time += dt;

        // Typing (inferred): input without pointer movement.
        if (s.KeyboardInput && s.UserIdleSeconds < 1) _lastKeyboard = _time;
        var typing = _time - _lastKeyboard < TypingWindowSeconds;
        if (typing != _typing)
        {
            _typing = typing;
            Emit(typing ? PerceptKind.TypingStarted : PerceptKind.TypingStopped);
        }

        // Presence and work session (a break is 5+ minutes without input).
        if (s.UserIdleSeconds >= AwaySeconds && !_away)
        {
            _away = true;
            Emit(PerceptKind.UserAway);
        }
        else if (s.UserIdleSeconds < 2 && _away)
        {
            _away = false;
            Emit(PerceptKind.UserReturned);
        }
        if (s.UserIdleSeconds < 60)
        {
            _sessionSeconds += dt;
            _breakSeconds = 0;
        }
        else
        {
            _breakSeconds += dt;
            if (_breakSeconds > 300)
            {
                _sessionSeconds = 0;
                _sessionAnnounced = false;
            }
        }
        if (!_sessionAnnounced && _sessionSeconds >= LongSessionMinutes * 60)
        {
            _sessionAnnounced = true;
            Emit(PerceptKind.LongWorkSession, strength: _sessionSeconds / 3600);
        }

        // Foreground app.
        var fg = s.ForegroundProcess;
        ForegroundBounds = s.ForegroundBounds;
        if (fg is not null && !string.Equals(fg, _foreground, StringComparison.OrdinalIgnoreCase))
        {
            _foreground = fg;
            ForegroundCategory = AppCategories.Of(fg, s.ForegroundFullscreen);
            Emit(PerceptKind.AppSwitched, fg, s.ForegroundBounds?.Center);
            if (IsNewApp?.Invoke(fg) == true) Emit(PerceptKind.AppFirstSeen, fg, s.ForegroundBounds?.Center);
        }
        var gaming = s.ForegroundFullscreen && AppCategories.Of(fg, true) is AppCategory.Game;
        if (gaming != _gaming)
        {
            _gaming = gaming;
            Emit(gaming ? PerceptKind.GameStarted : PerceptKind.GameEnded, fg);
        }

        // Load (CPU or GPU working hard for a while).
        if (s.Cpu is double cpu) Cpu = cpu;
        if (s.Gpu is double gpu) Gpu = gpu;
        var load = Math.Max(Cpu, Gpu);
        if (load >= 85) { _hotFor += dt; _coolFor = 0; }
        else { _coolFor += dt; if (load < 60) _hotFor = 0; }
        if (!_hot && _hotFor >= HotSustainSeconds)
        {
            _hot = true;
            Emit(PerceptKind.PcHot, strength: load / 100);
        }
        else if (_hot && _coolFor >= 15)
        {
            _hot = false;
            Emit(PerceptKind.PcCooled);
        }
        _downFor = (s.NetDown ?? 0) > 2 * 1024 * 1024 ? _downFor + dt : 0;
        if (_downFor >= 10 && _downFor - dt < 10) Emit(PerceptKind.DownloadRunning);

        // Clock.
        if (s.Hour is int h)
        {
            if (_hour is int old && old != h)
            {
                if (h == 23) Emit(PerceptKind.NightFell);
                if (h == 7) Emit(PerceptKind.MorningCame);
            }
            _hour = h;
        }
    }

    /// <summary>Window events from the platform (already filtered to other apps' top-level windows).</summary>
    public void OnWindowEvent(in WindowEvent e)
    {
        var where = e.Bounds?.Center;
        switch (e.Kind)
        {
            case WindowEventKind.Opened:
                Emit(PerceptKind.WindowOpened, e.Process, where);
                if (e.Process is { } p && IsNewApp?.Invoke(p) == true) Emit(PerceptKind.AppFirstSeen, p, where);
                break;
            case WindowEventKind.Closed:
            case WindowEventKind.Minimized:
                Emit(PerceptKind.WindowClosed, e.Process, where);
                break;
            case WindowEventKind.MoveStarted:
                Emit(PerceptKind.WindowDragStarted, e.Process, where);
                break;
            case WindowEventKind.MoveEnded:
                Emit(PerceptKind.WindowDragEnded, e.Process, where);
                break;
        }
    }
}
