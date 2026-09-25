using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Companion.Memory;
using HoodieCompanion.Companion.Perception;
using HoodieCompanion.Features.Backpack;
using HoodieCompanion.Features.Notes;
using HoodieCompanion.Features.Reminders;
using HoodieCompanion.Features.SystemMonitor;
using HoodieCompanion.Features.Timers;
using HoodieCompanion.Geometry;
using HoodieCompanion.Platform;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;
using HoodieCompanion.Storage;
using HoodieCompanion.UI;
using Microsoft.Win32;

namespace HoodieCompanion;

/// <summary>
/// Composition root: owns services, windows and the frame loop, and translates system events into
/// world events for the companion (see the diegetic mapping in README.md).
/// </summary>
public sealed class AppHost : IDisposable
{
    public const string SettingsFile = "settings.json";
    public const string TerritoryFile = "territory.json";

    private readonly Application _app;
    private readonly FrameClock _clock = new();
    private readonly DispatcherTimer _housekeeping;
    private readonly FullscreenService _fullscreen = new();
    private readonly EnvironmentInterpreter _environment = new();
    private readonly HashSet<string> _recentProcesses = new(StringComparer.OrdinalIgnoreCase);
    private PetWindow _petWindow = null!;
    private WorldPropWindow _propWindow = null!;
    private QuickPanel _panel = null!;
    private AlertCard _alerts = null!;
    private TrayIcon? _tray;
    private HotkeyService? _hotkey;
    private SystemMonitorService? _monitor;
    private SettingsWindow? _settingsWindow;
    private TerritoryEditor? _territoryEditor;
    private ForegroundInfo _foreground = new(null, null, null, null);
    private AppPresenceMode _foregroundRule;
    private double _userIdle;
    private CommandChannel? _commands;
    private SurfaceScanner? _surfaceScanner;
    private WindowEvents? _windowEvents;
    private uint _lastInputTick;
    private Vec2 _lastCursor;
    private int _housekeepingTicks;
    private RenderState _last;
    private bool _disposed;

    public AppHost(Application app, AppStorage storage)
    {
        _app = app;
        Storage = storage;
        Settings = storage.Load(SettingsFile, () => new AppSettings());
        Settings.Normalize();
        var territoryData = storage.Load(TerritoryFile, () => new TerritoryData());
        World = MonitorService.Query();
        Territory = new TerritoryService(territoryData, World);
        Pet = new PetController(World, Territory, Settings);
        Memory = new CompanionMemory(storage);
        Pet.Memory = Memory;
        Inventory = new InventoryService(storage);
        Notes = new NoteService(storage);
        Reminders = new ReminderService(storage);
        Timers = new TimerService(storage);
        Icons = new ShellIconService(storage.Root);
        Sound = new SoundService(() => Settings.Sounds);
        Pet.HasItems = Inventory.Items.Count > 0;

        _housekeeping = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _housekeeping.Tick += (_, _) => Housekeeping();
    }

    // ------------------------------------------------------------------ services & state used by the UI

    public AppStorage Storage { get; }
    public AppSettings Settings { get; }
    public WorldGeometry World { get; private set; }
    public TerritoryService Territory { get; }
    public PetController Pet { get; }
    public CompanionMemory Memory { get; }

    /// <summary>Hoodie's own resource use over time (debug page and log).</summary>
    public PerformanceWatch Performance { get; } = new();
    public InventoryService Inventory { get; }
    public NoteService Notes { get; }
    public ReminderService Reminders { get; }
    public TimerService Timers { get; }
    public ShellIconService Icons { get; }
    public AppCatalog Apps { get; } = new();
    public StickyNotes? StickyNotes { get; private set; }
    public SoundService Sound { get; }
    public SystemStatus? LatestStatus => _monitor?.Latest;
    public bool EnvironmentBusy => _environment.IsBusy;
    public IEnumerable<string> RecentProcesses => _recentProcesses.OrderBy(p => p);
    public bool HotkeyRegistered => _hotkey?.IsRegistered ?? false;
    public bool EmergencyHidden { get; private set; }
    public RectD PetBoundsPx { get; private set; }
    public event Action<SystemStatus>? SystemStatusUpdated;
    public SystemHistory History { get; } = new();
    public QuickPanel Panel => _panel;
    public PetWindow PetWindow => _petWindow;
    public FrameClock Clock => _clock;

    /// <summary>Optional cursor override used by the automated QA script.</summary>
    public Vec2? CursorOverride { get; set; }
    public bool? ButtonOverride { get; set; }
    /// <summary>QA: fixed platforms instead of the real window tops / icons.</summary>
    public IReadOnlyList<Surface>? SurfaceOverride { get; set; }

    // ------------------------------------------------------------------ lifecycle

    public void Start()
    {
        L.Set(Settings.Language);
        _petWindow = new PetWindow();
        _petWindow.Rig.SetHoodieColor(Memory.Doc.HoodieColor);
        _propWindow = new WorldPropWindow();
        _panel = new QuickPanel(this);
        _alerts = new AlertCard(this);

        _petWindow.TryBeginGrab = cursor =>
        {
            _panel.Close(animated: false);
            return Pet.BeginGrab(CursorOverride ?? cursor);
        };
        _petWindow.Released += cursor => Pet.EndGrab(CursorOverride ?? cursor);
        _petWindow.Clicked += OnPetClicked;
        _petWindow.RightClicked += () => OpenPanel(PanelPage.Commands);
        _petWindow.ItemDragEnter += () => Pet.DragEntered();
        _petWindow.ItemDragLeave += () => Pet.DragLeft();
        _petWindow.ItemsDropped += items => GiveItems(items);
        _petWindow.Show();
        StickyNotes = new StickyNotes(Notes);
        StickyNotes.Sync();

        _hotkey = new HotkeyService(_petWindow.Hwnd, () => SetEmergencyHidden(!EmergencyHidden));
        try
        {
            _tray = new TrayIcon(this);
        }
        catch (Exception ex)
        {
            Log.Error("tray icon failed", ex);
        }

        Pet.Log += msg => Log.Debug(msg);
        Pet.ModeChanged += _ => { SaveSettings(); _tray?.Refresh(); };
        Territory.Changed += SaveTerritory;
        Reminders.Due += OnReminderDue;
        Timers.Finished += OnTimerFinished;
        Inventory.Changed += () => { Pet.HasItems = Inventory.Items.Count > 0; Pet.Mind.BackpackItems = Inventory.Items.Count; };

        _monitor = new SystemMonitorService(_app.Dispatcher);
        _monitor.Updated += OnSystemStatus;

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        Pet.Place(InitialPosition(), appear: true);
        _commands = new CommandChannel((c, x, y) => _app.Dispatcher.BeginInvoke(() => DesktopCommand(c, x, y)));
        ApplyDesktopMenu();
        _windowEvents = new WindowEvents(e => Pet.Perception.OnWindowEvent(e));
        _surfaceScanner = new SurfaceScanner(_app.Dispatcher, s => Pet.SetSurfaces(SurfaceOverride ?? (Settings.ClimbOnWindows && !EmergencyHidden ? s : Array.Empty<Surface>())));
        _windowEvents.Changed += () => _surfaceScanner?.Poke();
        _surfaceScanner.FastMode = () => _windowEvents.Dragging;
        _surfaceScanner.ExcludeProcess = p =>
        {
            if (p is null) return false;
            var rule = Territory.Data.AppRules.FirstOrDefault(r => string.Equals(r.ProcessName, p, StringComparison.OrdinalIgnoreCase));
            return rule is { PresenceMode: AppPresenceMode.Avoid or AppPresenceMode.Hide or AppPresenceMode.Quiet } || AppCategories.Of(p, false) == AppCategory.Game;
        };
        _clock.Tick += OnFrame;
        _clock.Start();
        _housekeeping.Start();

        if (!Settings.FirstRunDone)
        {
            var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            once.Tick += (_, _) =>
            {
                once.Stop();
                ShowWelcome();
            };
            once.Start();
        }
        Log.Info($"started: {World.Monitors.Count} monitor(s): {World.Fingerprint}");
    }

    private Vec2 InitialPosition()
    {
        if (Settings.LastPosition is { MonitorId: not null } lp && World.FindById(lp.MonitorId) is { } m)
            return new Vec2(m.WorkArea.Left + Math.Clamp(lp.RelX, 0.02, 0.98) * m.WorkArea.Width, m.WorkArea.Bottom);
        return Territory.HomeFeet() ?? Pet.DefaultHome();
    }

    private void ShowWelcome()
    {
        _alerts.Enqueue(new AlertRequest(
            L.T("Hi, I'm Hoodie."),
            L.T("I live down here now. Click me for my panel, pick me up by the hood (I don't mind being thrown), or drop files on me and I'll keep them in my backpack. Right-click for commands. Ctrl+Alt+H hides me instantly."),
            new[] { new AlertAction(L.T("Got it"), () => { }, true) },
            Icon: Ui.Icons.Hand));
        Settings.FirstRunDone = true;
        SaveSettings();
    }

    // ------------------------------------------------------------------ frame loop

    private void OnFrame(double dt)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (EmergencyHidden) return;
            // Safety net: if the real button is up, no press or grab may survive (lost button-up messages,
            // capture stolen by another window, etc.). This is what keeps Hoodie from sticking to the cursor.
            var buttonDown = ButtonOverride ?? MouseService.LeftButtonDown();
            _buttonUpFor = buttonDown ? 0 : _buttonUpFor + dt;
            if (!buttonDown)
            {
                // A drag ends as soon as the button is up. A plain press (a click in progress) is left alone:
                // its WM_LBUTTONUP is normally just a few milliseconds behind, and cancelling it here used to
                // swallow clicks whenever a frame ran in between (i.e. almost always while Hoodie was moving).
                // Only a press whose button-up really got lost is cancelled.
                if (_petWindow.IsDragging || (_petWindow.IsPressed && _petWindow.PressAge > 0.4 && _buttonUpFor > 0.2)) _petWindow.CancelPress(notifyRelease: true);
                if (Pet.State == BehaviorState.Grabbed) Pet.EndGrab(CursorOverride ?? MouseService.Cursor());
            }
            // Typing is inferred without reading keys: input happened but the pointer did not move.
            var cursorNow = CursorOverride ?? MouseService.Cursor();
            var inputTick = MouseService.LastInputTick();
            var keyboardish = inputTick != _lastInputTick && Vec2.Distance(cursorNow, _lastCursor) < 0.5 && !buttonDown && CursorOverride is null;
            _lastInputTick = inputTick;
            _lastCursor = cursorNow;
            var status = LatestStatus;
            var input = new PetInput
            {
                Dt = dt,
                Env = new EnvironmentSample
                {
                    KeyboardInput = keyboardish,
                    ForegroundProcess = _foreground.ProcessName,
                    ForegroundBounds = _foreground.Bounds,
                    ForegroundFullscreen = _foreground.FullscreenMonitorId is not null,
                    Cpu = status?.CpuUsage,
                    Gpu = status?.GpuUsageOptional,
                    NetDown = status?.NetworkDownloadOptional,
                },
                Cursor = cursorNow,
                LeftButtonDown = buttonDown,
                UserIdleSeconds = CursorOverride is null ? MouseService.UserIdleSeconds() : _userIdle,
                FullscreenMonitorId = _foreground.FullscreenMonitorId,
                ForegroundRule = _foregroundRule,
                ForegroundMonitorId = _foreground.MonitorId,
                LocalHour = DateTime.Now.Hour,
            };
            var rs = Pet.Update(input);
            _last = rs;
            PetBoundsPx = rs.Visible ? rs.Transform.Bounds(RigTransform.BodyLocal) : FallbackBounds();
            _propWindow.Render(rs.Visible ? rs.Prop : null, Settings.AlwaysOnTop);
            _petWindow.Render(rs, Settings.Scale, Settings.AlwaysOnTop, Settings.ReducedMotion);
            if (_alerts.IsShowing && (_frameCount++ % 6) == 0) _alerts.Place();

            var mode = !rs.Visible ? FrameRateMode.Idle
                : rs.Calm && !_petWindow.IsDragging && !_panel.IsOpen ? FrameRateMode.Calm
                : FrameRateMode.Active;
            _clock.SetMode(mode);
        }
        catch (Exception ex)
        {
            Log.Error("frame failed", ex);
        }
        var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        FrameLogicMs = FrameLogicMs * 0.98 + ms * 0.02;
        _frameSamples[_frameSampleCount++ % _frameSamples.Length] = ms;
        if (ms > FrameLogicMaxMs) FrameLogicMaxMs = ms;
        if (ms > 8)
        {
            SlowFrames++;
            if (SlowFrames <= 20 || SlowFrames % 100 == 0) Log.Info($"slow frame: {ms:0.0} ms logic ({Pet.State}, {_last.Clip})");
        }
    }

    private readonly double[] _frameSamples = new double[4096];
    private int _frameSampleCount;

    /// <summary>Worst single frame of simulation + scene update since start.</summary>
    public double FrameLogicMaxMs { get; private set; }

    /// <summary>Frames whose logic took more than 8 ms.</summary>
    public int SlowFrames { get; private set; }

    /// <summary>Median and 95th percentile of the recent frame logic times (ms).</summary>
    public (double Median, double P95) FrameLogicPercentiles()
    {
        var n = Math.Min(_frameSampleCount, _frameSamples.Length);
        if (n == 0) return (0, 0);
        var a = _frameSamples.Take(n).OrderBy(x => x).ToArray();
        return (a[n / 2], a[Math.Min(n - 1, (int)(n * 0.95))]);
    }

    /// <summary>Smoothed time spent in simulation + scene update per frame (excludes WPF rendering).</summary>
    public double FrameLogicMs { get; private set; }

    private int _frameCount;
    private double _buttonUpFor;

    private RectD FallbackBounds()
    {
        var home = Territory.HomeFeet() ?? Pet.DefaultHome();
        var s = World.NearestMonitor(home).Scale;
        return new RectD(home.X - 40 * s, home.Y - 150 * s, 80 * s, 150 * s);
    }

    // ------------------------------------------------------------------ housekeeping (1 Hz)

    private void Housekeeping()
    {
        try
        {
            _housekeepingTicks++;
            var now = DateTime.Now;
            Reminders.Tick(now);
            Timers.Tick(now);
            _userIdle = MouseService.UserIdleSeconds();

            _foreground = _fullscreen.Sample(World);
            // Nothing to climb while asleep or away: the scanner rests too.
            if (_surfaceScanner is not null)
                _surfaceScanner.Enabled = Settings.ClimbOnWindows && !EmergencyHidden && Pet.State is not (BehaviorState.Sleeping or BehaviorState.Hidden);
            if (_foreground.ProcessName is { } pn)
            {
                if (_recentProcesses.Count < 64) _recentProcesses.Add(pn);
                var rule = Territory.Data.AppRules.FirstOrDefault(r => string.Equals(r.ProcessName, pn, StringComparison.OrdinalIgnoreCase));
                _foregroundRule = rule?.PresenceMode ?? AppPresenceMode.Normal;
            }
            else
            {
                _foregroundRule = AppPresenceMode.Normal;
            }

            if (_housekeepingTicks % 2 == 0) RefreshMonitors();
            if (_housekeepingTicks % 2 == 1 && Settings.AlwaysOnTop && !EmergencyHidden) WindowInterop.AssertTopmost(_petWindow.Hwnd);
            if (_housekeepingTicks % 30 == 0) RememberPosition();
            if (_housekeepingTicks == 20) _ = Apps.LoadAsync();
            if (_housekeepingTicks == 6) PrewarmSettings();
            if (_housekeepingTicks % 60 == 5) Performance.Take();
        }
        catch (Exception ex)
        {
            Log.Error("housekeeping failed", ex);
        }
    }

    private void RefreshMonitors()
    {
        var w = MonitorService.Query();
        if (w.Fingerprint == World.Fingerprint) return;
        Log.Info("display configuration changed: " + w.Fingerprint);
        World = w;
        Pet.UpdateWorld(w);
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => _app.Dispatcher.BeginInvoke(RefreshMonitors);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        _app.Dispatcher.BeginInvoke(() =>
        {
            if (e.Mode == PowerModes.Suspend) Pet.WorldSleep(true);
            else if (e.Mode == PowerModes.Resume) { Pet.WorldSleep(false); RefreshMonitors(); }
        });
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _app.Dispatcher.BeginInvoke(() =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock) Pet.WorldSleep(true);
            else if (e.Reason is SessionSwitchReason.SessionUnlock) Pet.WorldSleep(false);
        });
    }

    private void OnSystemStatus(SystemStatus s)
    {
        History.Add(s);
        // PC load, downloads etc. now reach Hoodie through its perception (see OnFrame → PetInput.Env).
        _environment.Feed(s, 1);
        SystemStatusUpdated?.Invoke(s);
    }

    // ------------------------------------------------------------------ interaction

    private void OnPetClicked()
    {
        if (_panel.IsOpen)
        {
            _panel.Close(animated: true);
            return;
        }
        Pet.Clicked();
        OpenPanel(PanelPage.Home);
    }

    public void OpenPanel(PanelPage page)
    {
        if (EmergencyHidden) SetEmergencyHidden(false);
        _panel.Open(page);
    }

    public void SetMode(PresenceMode mode)
    {
        Pet.SetMode(mode);
        SaveSettings();
    }

    public void Command(PetCommand command)
    {
        if (EmergencyHidden && command is PetCommand.ComeHere or PetCommand.ComeBack) SetEmergencyHidden(false);
        Pet.Execute(command);
        SaveSettings();
    }

    /// <summary>A command from the desktop right-click menu, with the point where the user clicked.</summary>
    public void DesktopCommand(string command, double x, double y)
    {
        var at = new Vec2(x, y);
        Log.Info($"desktop command {command} at {x:0},{y:0}");
        switch (command)
        {
            case "comehere":
            case "stayhere":
                if (EmergencyHidden) SetEmergencyHidden(false);
                Pet.ComeTo(at, stay: command == "stayhere");
                SaveSettings();
                break;
            case "sethome":
                if (EmergencyHidden) SetEmergencyHidden(false);
                Pet.SetHomeAt(at);
                SaveTerritory();
                break;
            case "panel":
                OpenPanel(PanelPage.Home);
                break;
            case "hide":
                SetEmergencyHidden(!EmergencyHidden);
                break;
        }
    }

    public void ApplyDesktopMenu()
    {
        if (Settings.DesktopMenu) DesktopMenuService.Register(L.T);
        else DesktopMenuService.Unregister();
    }

    public void SetHomeHere()
    {
        Pet.SetHomeHere();
        SaveTerritory();
    }

    public void SetEmergencyHidden(bool hidden)
    {
        EmergencyHidden = hidden;
        if (hidden)
        {
            _panel.Close(animated: false);
            _alerts.Hide();
            _petWindow.Hide();
            _propWindow.Render(null, false);
            _clock.SetMode(FrameRateMode.Idle);
            Log.Info("emergency hide");
        }
        else
        {
            _petWindow.Show();
            if (_alerts.IsShowing) _alerts.Show();
            _clock.SetMode(FrameRateMode.Active);
        }
        _tray?.Refresh();
    }

    // ---------------- Backpack

    public void GiveItems(IEnumerable<string> targets)
    {
        var any = false;
        var allAlready = true;
        foreach (var t in targets)
        {
            try
            {
                var name = InventoryService.IsShellName(t) ? ShellInterop.DisplayName(t) : null;
                var r = Inventory.Add(t, name);
                any = true;
                allAlready &= r.AlreadyPresent;
                Icons.Get(r.Item);
                if (r.Item.IconCache is not null) Inventory.SetIconCache(r.Item.Id, r.Item.IconCache);
            }
            catch (Exception ex)
            {
                Log.Error("could not add " + t, ex);
            }
        }
        if (!any)
        {
            Pet.DragLeft();
            return;
        }
        Pet.ItemReceived(allAlready);
        Sound.Play(SoundCue.ItemReceived);
    }

    public void OpenItem(InventoryItem item)
    {
        if (!Inventory.Exists(item))
        {
            Pet.ItemMissing();
            _alerts.Enqueue(new AlertRequest(
                L.T("It's not there anymore"),
                L.F("\"{0}\" isn't where Hoodie left it:\n{1}", item.DisplayName, item.Target),
                new[]
                {
                    new AlertAction(L.T("Cancel"), () => { }),
                    new AlertAction(L.T("Remove reference"), () => RemoveItem(item)),
                    new AlertAction(L.T("Locate…"), () => Locate(item), true),
                }, Icon: Ui.Icons.Backpack));
            return;
        }
        // Hoodie hands it over at once; Windows opens it in the background.
        Inventory.MarkOpened(item.Id);
        Pet.ItemPresented();
        ShortcutService.OpenAsync(item, error =>
        {
            Pet.Feedback(AnimClip.Error);
            _alerts.Enqueue(new AlertRequest(L.T("Couldn't open it"), string.IsNullOrEmpty(error) ? L.T("Windows refused to open this item.") : error,
                new[] { new AlertAction(L.T("OK"), () => { }, true) }, Icon: Ui.Icons.Backpack));
        });
    }

    private void Locate(InventoryItem item)
    {
        string? path = null;
        if (item.Type == InventoryItemType.Folder)
        {
            var dlg = new OpenFolderDialog { Title = L.F("Where is {0}?", item.DisplayName) };
            if (dlg.ShowDialog() == true) path = dlg.FolderName;
        }
        else
        {
            var dlg = new OpenFileDialog { Title = L.F("Where is {0}?", item.DisplayName), FileName = Path.GetFileName(item.Target) };
            if (dlg.ShowDialog() == true) path = dlg.FileName;
        }
        if (path is null) return;
        Inventory.Relocate(item.Id, path);
        Icons.Forget(item.Id);
        Pet.Feedback(AnimClip.Success);
    }

    /// <summary>Removes Hoodie's reference only; the original is never touched.</summary>
    public void RemoveItem(InventoryItem item)
    {
        Inventory.Remove(item.Id);
        Icons.Forget(item.Id);
    }

    // ---------------- Notes, reminders, timers

    public Note? AddNote(string text, string? color = null, string? label = null)
    {
        var n = Notes.Add(text, color, label);
        if (n is not null) Pet.NoteFinished(saved: true);
        return n;
    }

    public void AddReminder(string text, DateTime due)
    {
        Reminders.Add(text, due);
        Pet.Feedback(AnimClip.Success);
    }

    public void StartTimer(TimeSpan duration)
    {
        Timers.Start(duration, DateTime.Now, L.F("{0} timer", FormatDuration(duration)));
        Pet.Feedback(AnimClip.Success);
    }

    public static string FormatDuration(TimeSpan d) =>
        d.TotalHours >= 1 ? L.F("{0} h {1} min", (int)d.TotalHours, d.Minutes)
        : d.TotalMinutes >= 1 ? L.F("{0} min", Math.Round(d.TotalMinutes, 1))
        : L.F("{0} s", (int)d.TotalSeconds);

    private void OnReminderDue(Reminder r)
    {
        Pet.StartAlert(AlertKind.Reminder);
        Sound.Play(SoundCue.Reminder);
        _alerts.Enqueue(new AlertRequest(L.T("You asked me to remind you"), r.Text,
            new[]
            {
                new AlertAction(L.T("Snooze 10 min"), () => { Reminders.Snooze(r.Id, TimeSpan.FromMinutes(10), DateTime.Now); Pet.EndAlert(); }),
                new AlertAction(L.T("Done"), () => { Reminders.Complete(r.Id); Pet.EndAlert(); }, true),
            }, OnDismissed: () => Pet.EndAlert()));
    }

    private void OnTimerFinished(CountdownTimer t)
    {
        Pet.StartAlert(AlertKind.Timer);
        Sound.Play(SoundCue.Timer);
        _alerts.Enqueue(new AlertRequest(L.T("Time's up"), L.F("Your {0} is done.", t.Label),
            new[]
            {
                new AlertAction(L.T("+5 min"), () => { Timers.Acknowledge(t.Id); Timers.Start(TimeSpan.FromMinutes(5), DateTime.Now, L.F("{0} timer", FormatDuration(TimeSpan.FromMinutes(5)))); Pet.EndAlert(); }),
                new AlertAction(L.T("OK"), () => { Timers.Acknowledge(t.Id); Pet.EndAlert(); }, true),
            }, OnDismissed: () => Pet.EndAlert(), Icon: Ui.Icons.Timer));
    }

    // ---------------- windows

    /// <summary>Changes Hoodie's hoodie (only colours it has found).</summary>
    public void SetHoodieColor(string color)
    {
        if (!Progression.ColorAvailable(Memory, color)) return;
        Memory.Doc.HoodieColor = color;
        Memory.MarkDirty();
        _petWindow?.Rig.SetHoodieColor(color);
        Pet.PlayEmote(AnimClip.InspectSelf);
    }

    /// <summary>Privacy: forget everything Hoodie learned about this PC.</summary>
    public void ForgetMemories()
    {
        Memory.Forget();
        _petWindow?.Rig.SetHoodieColor(Memory.Doc.HoodieColor);
    }

    public void ShowSettings()
    {
        _panel.Close(animated: false);
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        // The window is built once (ahead of time, see PrewarmSettings) and then only shown/hidden,
        // so opening it is instant instead of building and compiling a big window on the spot.
        _settingsWindow ??= new SettingsWindow(this);
        _settingsWindow.Build();
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Builds the settings window in the background a few seconds after start (not shown).</summary>
    private void PrewarmSettings()
    {
        if (_settingsWindow is not null) return;
        _app.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _settingsWindow ??= new SettingsWindow(this);
                // Instantiate its templates and layout once while nobody is waiting for it.
                _settingsWindow.Measure(new Size(_settingsWindow.Width, _settingsWindow.Height));
                _settingsWindow.Arrange(new Rect(0, 0, _settingsWindow.Width, _settingsWindow.Height));
            }
            catch (Exception ex)
            {
                Log.Error("settings prewarm", ex);
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    public void ShowTerritoryEditor()
    {
        _panel.Close(animated: false);
        if (_territoryEditor is null)
        {
            _territoryEditor = new TerritoryEditor(this);
            _territoryEditor.Closed += () => _settingsWindow?.Build();
        }
        _territoryEditor.Open();
    }

    public void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Storage.Root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("open data folder", ex);
        }
    }

    public void CloseAlert(bool dismissed) => _alerts.Finish(dismissed);

    public AlertCard Alerts => _alerts;

    // ------------------------------------------------------------------ persistence

    public void SettingsChanged()
    {
        Settings.Normalize();
        L.Set(Settings.Language);
        ApplyDesktopMenu();
        _petWindow.Topmost = Settings.AlwaysOnTop;
        if (!Settings.AlwaysOnTop) WindowInterop.ClearTopmost(_petWindow.Hwnd);
        SaveSettings();
    }

    private void RememberPosition()
    {
        if (!_last.Visible || Pet.Machine.IsPhysical) return;
        var m = World.MonitorAt(Pet.Feet);
        if (m is null) return;
        Settings.LastPosition = new SavedPosition { MonitorId = m.Id, RelX = (Pet.Feet.X - m.WorkArea.Left) / m.WorkArea.Width };
        SaveSettings();
    }

    public void SaveSettings()
    {
        try
        {
            Storage.Save(SettingsFile, Settings);
        }
        catch (Exception ex)
        {
            Log.Error("save settings", ex);
        }
    }

    public void SaveTerritory()
    {
        try
        {
            Storage.Save(TerritoryFile, Territory.Data);
        }
        catch (Exception ex)
        {
            Log.Error("save territory", ex);
        }
    }

    public void SaveAll()
    {
        Memory.Flush(force: true);
        RememberPosition();
        SaveSettings();
        SaveTerritory();
    }

    public bool IsExiting { get; private set; }

    public void Exit()
    {
        IsExiting = true;
        SaveAll();
        Dispose();
        _app.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _clock.Dispose();
        _housekeeping.Stop();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _monitor?.Dispose();
        StickyNotes?.CloseAll();
        _commands?.Dispose();
        _surfaceScanner?.Dispose();
        _windowEvents?.Dispose();
    }

    public RenderState LastRender => _last;
}
