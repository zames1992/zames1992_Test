using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Behavior;
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
    public InventoryService Inventory { get; }
    public NoteService Notes { get; }
    public ReminderService Reminders { get; }
    public TimerService Timers { get; }
    public ShellIconService Icons { get; }
    public SoundService Sound { get; }
    public SystemStatus? LatestStatus => _monitor?.Latest;
    public bool EnvironmentBusy => _environment.IsBusy;
    public IEnumerable<string> RecentProcesses => _recentProcesses.OrderBy(p => p);
    public bool HotkeyRegistered => _hotkey?.IsRegistered ?? false;
    public bool EmergencyHidden { get; private set; }
    public RectD PetBoundsPx { get; private set; }
    public event Action<SystemStatus>? SystemStatusUpdated;
    public QuickPanel Panel => _panel;
    public PetWindow PetWindow => _petWindow;
    public FrameClock Clock => _clock;

    /// <summary>Optional cursor override used by the automated QA script.</summary>
    public Vec2? CursorOverride { get; set; }
    public bool? ButtonOverride { get; set; }

    // ------------------------------------------------------------------ lifecycle

    public void Start()
    {
        _petWindow = new PetWindow();
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
        Inventory.Changed += () => Pet.HasItems = Inventory.Items.Count > 0;

        _monitor = new SystemMonitorService(_app.Dispatcher);
        _monitor.Updated += OnSystemStatus;

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        Pet.Place(InitialPosition(), appear: true);
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
            "Hi, I'm Hoodie.",
            "I live down here now. Click me for my panel, pick me up by the hood (I don't mind being thrown), or drop files on me and I'll keep them in my pocket. Right-click for commands. Ctrl+Alt+H hides me instantly.",
            new[] { new AlertAction("Got it", () => { }, true) },
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
            var input = new PetInput
            {
                Dt = dt,
                Cursor = CursorOverride ?? MouseService.Cursor(),
                LeftButtonDown = ButtonOverride ?? MouseService.LeftButtonDown(),
                UserIdleSeconds = _userIdle,
                FullscreenMonitorId = _foreground.FullscreenMonitorId,
                ForegroundRule = _foregroundRule,
                ForegroundMonitorId = _foreground.MonitorId,
            };
            var rs = Pet.Update(input);
            _last = rs;
            PetBoundsPx = rs.Visible ? rs.Transform.Bounds(RigTransform.BodyLocal) : FallbackBounds();
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
    }

    /// <summary>Smoothed time spent in simulation + scene update per frame (excludes WPF rendering).</summary>
    public double FrameLogicMs { get; private set; }

    private int _frameCount;

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
        var mood = _environment.Feed(s, 1);
        if (mood != EnvironmentMood.Calm) Pet.Environment(mood);
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
                var r = Inventory.Add(t);
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
                "It's not there anymore",
                $"\"{item.DisplayName}\" isn't where Hoodie left it:\n{item.Target}",
                new[]
                {
                    new AlertAction("Cancel", () => { }),
                    new AlertAction("Remove reference", () => RemoveItem(item)),
                    new AlertAction("Locate…", () => Locate(item), true),
                }, Icon: Ui.Icons.Backpack));
            return;
        }
        if (ShortcutService.Open(item, out var error))
        {
            Inventory.MarkOpened(item.Id);
            Pet.ItemPresented();
        }
        else
        {
            Pet.Feedback(AnimClip.Error);
            _alerts.Enqueue(new AlertRequest("Couldn't open it", error ?? "Windows refused to open this item.",
                new[] { new AlertAction("OK", () => { }, true) }, Icon: Ui.Icons.Backpack));
        }
    }

    private void Locate(InventoryItem item)
    {
        string? path = null;
        if (item.Type == InventoryItemType.Folder)
        {
            var dlg = new OpenFolderDialog { Title = "Where is " + item.DisplayName + "?" };
            if (dlg.ShowDialog() == true) path = dlg.FolderName;
        }
        else
        {
            var dlg = new OpenFileDialog { Title = "Where is " + item.DisplayName + "?", FileName = Path.GetFileName(item.Target) };
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

    public void AddNote(string text)
    {
        if (Notes.Add(text) is null) return;
        Pet.ItemReceived(alreadyHad: false);
    }

    public void AddReminder(string text, DateTime due)
    {
        Reminders.Add(text, due);
        Pet.Feedback(AnimClip.Success);
    }

    public void StartTimer(TimeSpan duration)
    {
        Timers.Start(duration, DateTime.Now);
        Pet.Feedback(AnimClip.Success);
    }

    private void OnReminderDue(Reminder r)
    {
        Pet.StartAlert(AlertKind.Reminder);
        Sound.Play(SoundCue.Reminder);
        _alerts.Enqueue(new AlertRequest("You asked me to remind you", r.Text,
            new[]
            {
                new AlertAction("Snooze 10 min", () => { Reminders.Snooze(r.Id, TimeSpan.FromMinutes(10), DateTime.Now); Pet.EndAlert(); }),
                new AlertAction("Done", () => { Reminders.Complete(r.Id); Pet.EndAlert(); }, true),
            }, OnDismissed: () => Pet.EndAlert()));
    }

    private void OnTimerFinished(CountdownTimer t)
    {
        Pet.StartAlert(AlertKind.Timer);
        Sound.Play(SoundCue.Timer);
        _alerts.Enqueue(new AlertRequest("Time's up", $"Your {t.Label} is done.",
            new[]
            {
                new AlertAction("+5 min", () => { Timers.Acknowledge(t.Id); Timers.Start(TimeSpan.FromMinutes(5), DateTime.Now, t.Label); Pet.EndAlert(); }),
                new AlertAction("OK", () => { Timers.Acknowledge(t.Id); Pet.EndAlert(); }, true),
            }, OnDismissed: () => Pet.EndAlert(), Icon: Ui.Icons.Timer));
    }

    // ---------------- windows

    public void ShowSettings()
    {
        _panel.Close(animated: false);
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Show();
        _settingsWindow.Activate();
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
        RememberPosition();
        SaveSettings();
        SaveTerritory();
    }

    public void Exit()
    {
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
    }

    public RenderState LastRender => _last;
}
