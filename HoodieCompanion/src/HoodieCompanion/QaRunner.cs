using HoodieCompanion.Companion.Animation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Companion.Memory;
using HoodieCompanion.Companion.Perception;
using HoodieCompanion.Companion.Physics;
using HoodieCompanion.Geometry;
using HoodieCompanion.Platform;
using HoodieCompanion.Settings;
using HoodieCompanion.UI;

namespace HoodieCompanion;

/// <summary>
/// Automated end-to-end QA scenario for the real (Release) executable:
///   HoodieCompanion.exe --qa &lt;outDir&gt; [--data &lt;tempDataDir&gt;]
/// Drives the companion through life, walking, grab/throw, the Backpack, every panel page, a reminder,
/// Leave me alone / Come back and Quiet mode; saves snapshots and a PASS/FAIL report, then exits.
/// The cursor is simulated so the user's real mouse is never touched.
/// </summary>
public sealed class QaRunner
{
    private readonly AppHost _host;
    private readonly string _out;
    private readonly bool _keep;
    private readonly List<(double At, string Name, Action Act)> _steps = new();
    private readonly List<string> _checks = new();
    private readonly List<double> _fps = new();
    private readonly HashSet<BehaviorState> _seenStates = new();
    private readonly Stopwatch _watch = new();
    private readonly DispatcherTimer _timer;
    private readonly Process _self = Process.GetCurrentProcess();
    private int _next;
    private bool _failed;
    private Vec2 _cursorFrom, _cursorTo;
    private double _cursorStart = -1, _cursorDur;
    private double _calmCpu = -1;
    private long _peakMem;

    public QaRunner(AppHost host, string outDir, bool keep)
    {
        _host = host;
        _out = Path.GetFullPath(outDir);
        _keep = keep;
        Directory.CreateDirectory(_out);
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += (_, _) => Tick();
    }

    private double Now => _watch.Elapsed.TotalSeconds;

    private void At(double t, string name, Action act) => _steps.Add((t, name, act));

    private void Check(bool ok, string what)
    {
        _checks.Add($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok) _failed = true;
        Log.Info("QA " + (ok ? "PASS " : "FAIL ") + what);
    }

    public void Start()
    {
        var pet = _host.Pet;
        var world = _host.World;
        var prim = world.Primary.WorkArea;
        var far = new Vec2(prim.Left + 10, prim.Top + 10);
        _host.CursorOverride = far;
        _host.ButtonOverride = false;
        _host.Settings.FirstRunDone = true;
        // The QA machine has no real user: keep the away-from-keyboard timeline out of the scripted scenario.
        pet.Mind.AfkScale = 10000;
        var testFile = Path.Combine(_out, "test.txt");
        File.WriteAllText(testFile, "Hello from the Hoodie QA run.");
        var testFolder = Directory.CreateDirectory(Path.Combine(_out, "Test Folder")).FullName;

        At(1.6, "appear", () =>
        {
            Check(_host.LastRender.Visible, "character appears after launch");
            SnapPet("01-appear");
            SnapDesktop("01-desktop");
        });
        At(2.0, "walk", () => pet.TravelTo(new Vec2(prim.Left + prim.Width * 0.45, prim.Bottom), false, null));
        At(3.2, "walk-snap", () =>
        {
            Check(_seenStates.Contains(BehaviorState.Walking) || _seenStates.Contains(BehaviorState.Turning), "walks autonomously / on request");
            SnapPet("02-walk");
        });
        At(5.0, "prepare grab", () => pet.Place(new Vec2(prim.Left + prim.Width * 0.35, prim.Bottom), appear: false));
        At(5.6, "grab", () =>
        {
            var hood = pet.Transform.LocalToWorld(new Vec2(272, 140));
            _host.CursorOverride = hood;
            _host.ButtonOverride = true;
            Check(pet.BeginGrab(hood), "grab by the hood starts");
            MoveCursor(hood, hood + new Vec2(-world.Primary.Scale * 60, -world.Primary.Scale * 200), 0.6);
        });
        At(6.0, "grab-snap", () =>
        {
            Check(pet.State == BehaviorState.Grabbed, "held state while dragging");
            SnapPet("03-grabbed");
        });
        At(6.3, "swing", () =>
        {
            var c = _host.CursorOverride!.Value;
            MoveCursor(c, c + new Vec2(world.Primary.Scale * 520, -world.Primary.Scale * 60), 0.2);
        });
        At(6.47, "release", () =>
        {
            _host.ButtonOverride = false;
            pet.EndGrab(_host.CursorOverride!.Value);
            Check(pet.State == BehaviorState.Airborne && pet.Physics.Velocity.Length > 200, $"release gives throw velocity ({pet.Physics.Velocity.Length:0} px/s)");
            _host.CursorOverride = far;
        });
        At(6.62, "air-snap", () => SnapPet("04-thrown"));
        At(9.0, "landed", () =>
        {
            Check(_seenStates.Contains(BehaviorState.Landing), "lands after the throw");
            Check(world.MonitorAt(pet.Feet) is not null, "still inside the world after the throw");
            SnapPet("05-landed");
        });
        At(9.3, "sticky-grab", () =>
        {
            // Regression: a lost button-up must never leave Hoodie stuck to the cursor.
            var hood = pet.Transform.LocalToWorld(new Vec2(272, 140));
            _host.CursorOverride = hood;
            _host.ButtonOverride = true;
            pet.BeginGrab(hood);
        });
        At(9.55, "button-up", () => _host.ButtonOverride = false);
        At(9.85, "sticky-check", () =>
        {
            Check(pet.State != BehaviorState.Grabbed, "releasing the mouse button always drops Hoodie (no sticking)");
            _host.CursorOverride = far;
        });
        At(10.0, "give file", () =>
        {
            pet.Place(new Vec2(prim.Left + prim.Width * 0.5, prim.Bottom), appear: false);
            _host.GiveItems(new[] { testFile });
        });
        At(10.35, "catch-snap", () => SnapPet("06-catch"));
        At(10.8, "inspect-snap", () => SnapPet("07-inspect"));
        At(11.6, "stored", () =>
        {
            Check(_host.Inventory.Items.Any(i => i.Target == testFile), "dropped file stored in Backpack");
            var saved = File.ReadAllText(_host.Storage.PathFor("inventory.json"));
            Check(saved.Contains("test.txt"), "Backpack persisted to inventory.json");
            _host.GiveItems(new[] { testFolder, "https://example.com" });
        });
        At(13.0, "panel home", () =>
        {
            // A few things Hoodie found and remembered, so the Memories page has content.
            _host.Memory.GiveItem("mug");
            _host.Memory.GiveItem("ball");
            _host.Memory.Remember("unlock:mug");
            _host.Memory.Remember("first-grab");
            _host.OpenPanel(PanelPage.Home);
        });
        At(13.6, "panel-snap", () => SnapWindow(_host.Panel, "08-panel-home"));
        var pages = new[] { PanelPage.Backpack, PanelPage.Notes, PanelPage.Reminder, PanelPage.Timer, PanelPage.PcStatus, PanelPage.Commands, PanelPage.Memories };
        for (var p = 0; p < pages.Length; p++)
        {
            var page = pages[p];
            var t = 14.0 + p * 1.2;
            At(t, "page " + page, () => _host.Panel.Show(page));
            At(t + (page == PanelPage.PcStatus ? 1.1 : 0.6), "snap " + page, () => SnapWindow(_host.Panel, $"{9 + Array.IndexOf(pages, page):00}-panel-{page.ToString().ToLowerInvariant()}"));
        }
        At(15.15, "backpack-prop", () =>
        {
            Check(_maxBackpack > 0.5, "Backpack page: Hoodie opens its backpack");
            SnapPet("09b-backpack-pet");
        });
        At(17.75, "timer-input", () =>
        {
            _timerBox = FindAll<System.Windows.Controls.TextBox>(_host.Panel).FirstOrDefault(t => t.Text == "10");
            if (_timerBox is not null) _timerBox.Text = "3";
        });
        At(18.7, "timer-input-check", () =>
            Check(_timerBox is not null && _timerBox.Text == "3" && _timerBox.IsVisible, "timer: typed minutes are kept while the countdown refreshes"));
        At(20.1, "laptop", () =>
        {
            Check(_sawLaptop, "PC Status page: Hoodie sits down with its laptop");
            SnapPet("13b-laptop-pet");
        });
        At(22.0, "close panel", () => _host.Panel.Close(animated: false));
        At(22.1, "remove", () =>
        {
            var item = _host.Inventory.Items.First(i => i.Target == testFile);
            _host.RemoveItem(item);
            Check(File.Exists(testFile) && File.ReadAllText(testFile).StartsWith("Hello"), "removing from Backpack keeps the original file");
        });
        At(22.5, "reminder", () => _host.AddReminder("Stretch your legs", DateTime.Now.AddSeconds(1.5)));
        At(26.2, "reminder-snap", () =>
        {
            Check(pet.State == BehaviorState.Alert && _host.Alerts.IsShowing, "reminder alert: Hoodie holds up the note + card");
            SnapPet("15-reminder-pet");
            SnapWindow(_host.Alerts, "15-reminder-card");
        });
        At(26.8, "ack", () => _host.CloseAlert(dismissed: true));
        At(27.4, "alone", () => _host.SetMode(PresenceMode.Alone));
        At(28.3, "leaving-snap", () => SnapPet("16-leaving"));
        At(42.0, "alone-check", () =>
        {
            Check(pet.State == BehaviorState.Hidden && !_host.LastRender.Visible, "Leave me alone: Hoodie exits and stays hidden");
            SnapDesktop("17-alone-desktop");
            _host.Command(PetCommand.ComeBack);
        });
        At(47.0, "back-check", () =>
        {
            Check(_host.LastRender.Visible && world.MonitorAt(pet.Feet) is not null, "Come back: Hoodie returns");
            SnapPet("18-back");
        });
        At(47.5, "quiet", () => _host.SetMode(PresenceMode.Quiet));
        At(51.0, "quiet-snap", () =>
        {
            Check(pet.State is BehaviorState.Sitting or BehaviorState.Sleeping, "Quiet mode: sits calmly");
            SnapPet("19-quiet");
            _cpuStart = (_self.TotalProcessorTime, Now);
        });
        At(56.0, "calm cpu", () =>
        {
            _self.Refresh();
            var used = (_self.TotalProcessorTime - _cpuStart.Cpu).TotalSeconds / (Now - _cpuStart.At) / Environment.ProcessorCount * 100;
            _calmCpu = used;
            Check(used < 15, $"calm CPU usage is low ({used:0.0}% of all cores)");
        });
        At(56.5, "settings", () => _host.ShowSettings());
        At(57.5, "settings-snap", () =>
        {
            var w = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
            if (w is not null)
            {
                SnapWindow(w, "20-settings");
                w.Expand("privacy", "debug");
                w.UpdateLayout();
            }
        });
        At(58.0 - 0.3, "settings-privacy-snap", () =>
        {
            var w = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
            if (w is not null)
            {
                SnapWindow(w, "20b-settings-privacy");
                w.Close();
            }
        });
        At(58.0, "territory", () => _host.ShowTerritoryEditor());
        At(59.0, "territory-snap", () =>
        {
            SnapDesktop("21-territory-editor");
            var tb = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "Hoodie territory");
            Check(tb?.Owner is not null, "territory toolbar stays above the overlays (owned window)");
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\DesktopBackground\Shell\HoodieCompanion\shell");
            Check(key is not null && key.GetSubKeyNames().Length >= 4, "desktop right-click menu registered");
        });
        At(59.5, "territory-close", () =>
        {
            foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w.Title is "Territory" or "Hoodie territory").ToList()) w.Close();
        });
        At(60.0, "normal", () => _host.SetMode(PresenceMode.Normal));
        if (world.Monitors.Count > 1)
        {
            var other = world.Monitors.First(m => m != world.Primary);
            At(60.5, "travel monitor", () => pet.TravelTo(new Vec2(other.WorkArea.Center.X, other.WorkArea.Bottom), true, null));
            At(75.0, "travel-check", () => Check(world.MonitorAt(pet.Feet) == other, "travels to the other monitor"));
        }
        // v1.2: grab anywhere, ledge, notes board + pinned note, lying sleep.
        var groundX = prim.Left + prim.Width * 0.5;
        At(77.0, "hand-grab", () =>
        {
            _host.SetMode(PresenceMode.Normal);
            pet.Place(new Vec2(groundX, prim.Bottom), appear: false);
        });
        At(77.6, "hand-grab-start", () =>
        {
            var hand = pet.Transform.LocalToWorld(PosedRig.HandLeft(pet.Animation.LastPose));
            _host.CursorOverride = hand;
            _host.ButtonOverride = true;
            pet.BeginGrab(hand);
            MoveCursor(hand, hand + new Vec2(0, -world.Primary.Scale * 180), 0.5);
        });
        At(79.0, "hand-grab-check", () =>
        {
            Check(pet.State == BehaviorState.Grabbed && _host.LastRender.Clip is AnimClip.HangHandL or AnimClip.Struggle or AnimClip.RelaxedCarry,
                $"held by the hand: hangs from the hand ({_host.LastRender.Clip})");
            SnapPet("22-hang-hand");
            _host.ButtonOverride = false;
            pet.EndGrab(_host.CursorOverride!.Value);
        });
        At(81.0, "foot-grab", () =>
        {
            pet.Place(new Vec2(groundX, prim.Bottom), appear: false);
        });
        At(81.5, "foot-grab-start", () =>
        {
            var foot = pet.Transform.LocalToWorld(PosedRig.FootLeft(pet.Animation.LastPose));
            _host.CursorOverride = foot;
            _host.ButtonOverride = true;
            pet.BeginGrab(foot);
            MoveCursor(foot, foot + new Vec2(0, -world.Primary.Scale * 260), 0.5);
        });
        At(83.2, "foot-grab-check", () =>
        {
            Check(pet.State == BehaviorState.Grabbed && Math.Abs(pet.Transform.Tilt) > 120, $"held by a foot: hangs upside down (tilt {pet.Transform.Tilt:0})");
            SnapPet("23-hang-foot");
            _host.ButtonOverride = false;
            pet.EndGrab(_host.CursorOverride!.Value);
        });
        At(86.0, "ledge", () => pet.Place(new Vec2(groundX, prim.Bottom), appear: false));
        At(86.5, "ledge-grab", () =>
        {
            var hood = pet.Transform.LocalToWorld(new Vec2(272, 140));
            _host.CursorOverride = hood;
            _host.ButtonOverride = true;
            pet.BeginGrab(hood);
            MoveCursor(hood, hood + new Vec2(0, world.Primary.Scale * 175), 0.5);
        });
        At(87.6, "ledge-release", () =>
        {
            _host.ButtonOverride = false;
            pet.EndGrab(_host.CursorOverride!.Value);
            _host.CursorOverride = far;
        });
        At(88.1, "ledge-snap", () =>
        {
            Check(_host.LastRender.Clip is AnimClip.HangEdge or AnimClip.ClimbEdge, $"dropped below the taskbar edge: grabs the edge ({_host.LastRender.Clip})");
            SnapPet("24-ledge");
        });
        At(91.0, "ledge-check", () => Check(Math.Abs(pet.Feet.Y - (world.MonitorAt(pet.Feet)?.WorkArea.Bottom ?? -1)) < 1 && pet.State != BehaviorState.Hidden,
            "climbs back up onto the floor (no teleport)"));
        At(91.5, "note-editor", () =>
        {
            _host.OpenPanel(PanelPage.Notes);
            _host.Panel.QaNewNote("Buy oat milk\nCall the dentist", "green");
        });
        At(92.3, "note-editor-snap", () => SnapWindow(_host.Panel, "25-note-editor"));
        At(92.6, "note-keep", () =>
        {
            var id = _host.Panel.QaKeepNote();
            if (id is not null) _host.Notes.SetPinned(id, true);
            _host.AddNote("Idea: a hoodie for the cat", "pink", "ideas");
            _host.AddNote("Wi-Fi: hoodie-guest", "blue");
        });
        At(93.4, "notes-board-snap", () =>
        {
            Check(_host.Notes.All.Count(n => n.IsPinned) == 1 && (_host.StickyNotes?.Count ?? 0) == 1, "notes: kept note pinned to the desktop");
            SnapWindow(_host.Panel, "26-notes-board");
            var sticky = Application.Current.Windows.OfType<StickyNoteWindow>().FirstOrDefault();
            if (sticky is not null) SnapWindow(sticky, "27-pinned-note");
            _host.Panel.Close(animated: false);
        });
        At(94.0, "sleep", () =>
        {
            pet.Place(new Vec2(groundX, prim.Bottom), appear: false);
            pet.WorldSleep(true);
        });
        At(97.0, "sleep-snap", () =>
        {
            Check(pet.State == BehaviorState.Sleeping && _host.LastRender.Clip is AnimClip.SleepLying or AnimClip.DreamTwitch, "sleeps lying down");
            SnapPet("28-sleeping");
            pet.WorldSleep(false);
        });
        // Regression: clicks on a moving Hoodie were swallowed when a frame saw the button already up before
        // the WM_LBUTTONUP message was handled.
        At(97.3, "click-while-walking", () =>
        {
            pet.WorldSleep(false);
            pet.TravelTo(new Vec2(prim.Left + prim.Width * 0.2, prim.Bottom), false, null);
        });
        At(97.6, "click-press", () =>
        {
            _host.ButtonOverride = false; // the real button is already up again...
            _host.PetWindow.QaPress();     // ...but the mouse-down is only now being handled
        });
        At(97.75, "click-release", () =>
        {
            var delivered = _host.PetWindow.QaRelease();
            Check(delivered && _host.Panel.IsOpen, "a quick click on a walking Hoodie opens the panel");
            _host.Panel.Close(animated: false);
        });
        At(98.0, "platform", () =>
        {
            pet.Place(new Vec2(groundX, prim.Bottom), appear: false);
            var s = world.Primary.Scale;
            var surface = new Surface("qa-window", SurfaceKind.Window, groundX - 260 * s, groundX + 260 * s, prim.Bottom - 120 * s);
            _host.SurfaceOverride = new[] { surface };
            pet.SetSurfaces(_host.SurfaceOverride);
        });
        At(98.4, "platform-visit", () => Check(pet.DebugVisitSurface(), "finds a window top to climb onto"));
        At(105.0, "platform-check", () =>
        {
            Check(pet.StandingOn == "qa-window", $"stands on the window top ({pet.StandingOn ?? "floor"})");
            SnapDesktop("29-on-window");
            _host.SurfaceOverride = Array.Empty<Surface>();
        });
        At(108.0, "platform-gone", () => Check(pet.StandingOn is null && Math.Abs(pet.Feet.Y - prim.Bottom) < 1, "falls back to the floor when the window closes"));
        At(108.5, "wall", () =>
        {
            pet.Place(new Vec2(prim.Left + prim.Width * 0.07, prim.Bottom), appear: false);
        });
        At(109.0, "wall-start", () => Check(pet.DebugClimbWall(), "starts climbing the side of the screen"));
        At(114.0, "wall-snap", () =>
        {
            Check(pet.State == BehaviorState.Climbing && pet.Feet.Y < prim.Bottom - 20, "climbs up the screen side");
            SnapDesktop("30-screen-side");
        });
        At(122.0, "wall-done", () => Check(Math.Abs(pet.Feet.Y - prim.Bottom) < 1, "slides back down to the floor"));
        // v1.3: the living character.
        At(123.0, "v13-window", () =>
        {
            pet.Place(new Vec2(groundX, prim.Bottom), appear: false);
            pet.Perception.OnWindowEvent(new WindowEvent(WindowEventKind.Opened, "qa-new-app", new RectD(prim.Left + 100, prim.Top + 100, 600, 400)));
        });
        At(124.0, "v13-window-check", () =>
        {
            Check(_host.Memory.Doc.Apps.ContainsKey("qa-new-app") && _host.Memory.HasMoment("first-new-app"), "notices a new app (by name only) and remembers it");
            _host.Memory.GiveItem("fan");
            Check(pet.DebugIntent(HoodieCompanion.Companion.Behavior.Activity.CoolDown), "hot PC: Hoodie gets its fan out");
        });
        At(127.5, "v13-fan-snap", () =>
        {
            Check(_host.LastRender.Pose.PropFan > 0.5, $"the fan is in Hoodie's hand ({_host.LastRender.Clip})");
            SnapPet("26-fan");
            pet.Execute(PetCommand.Normal);
        });
        At(128.0, "v13-ball", () => Check(pet.DebugIntent(HoodieCompanion.Companion.Behavior.Activity.PlayBall), "plays with its ball"));
        At(130.0, "v13-ball-snap", () =>
        {
            Check(_host.LastRender.Pose.PropBall > 0.5, "the ball is out");
            SnapPet("27-ball");
        });
        At(131.0, "v13-memory", () =>
        {
            _host.SaveAll();
            var path = _host.Storage.PathFor(CompanionMemory.FileName);
            var json = File.Exists(path) ? File.ReadAllText(path) : "";
            Check(json.Contains("qa-new-app") && json.Contains("first-new-app"), "memory saved locally to memory.json");
            var sample = _host.Performance.Take();
            Check(sample.Handles > 0 && sample.WorkingSet > 0, $"own footprint is measured ({PerformanceWatch.Describe(sample)})");
            Check(pet.Mind.CurrentIntent is not null, $"decisions have reasons ({pet.Mind.CurrentIntent}: {pet.Mind.CurrentReason})");
        });
        At(131.5, "report", Finish);

        // Steps run in time order regardless of the order they were declared in.
        var ordered = _steps.Select((st, i) => (st, i)).OrderBy(x => x.st.At).ThenBy(x => x.i).Select(x => x.st).ToList();
        _steps.Clear();
        _steps.AddRange(ordered);
        _watch.Start();
        _timer.Start();
        Log.Info("QA scenario started, output: " + _out);
    }

    private (TimeSpan Cpu, double At) _cpuStart;
    private double _maxBackpack;
    private bool _sawLaptop;
    private System.Windows.Controls.TextBox? _timerBox;

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var x in FindAll<T>(child)) yield return x;
        }
    }

    private void MoveCursor(Vec2 from, Vec2 to, double seconds)
    {
        _cursorFrom = from;
        _cursorTo = to;
        _cursorStart = Now;
        _cursorDur = seconds;
    }

    private void Tick()
    {
        var now = Now;
        _seenStates.Add(_host.Pet.State);
        if (_host.Panel.IsOpen && _host.Panel.Page == PanelPage.Backpack) _maxBackpack = Math.Max(_maxBackpack, _host.LastRender.Pose.PropBackpack);
        if (_host.Panel.IsOpen && _host.Panel.Page == PanelPage.PcStatus && _host.Pet.ActivityName == "laptop") _sawLaptop = true;
        if (_cursorStart >= 0)
        {
            var k = Math.Min(1, (now - _cursorStart) / _cursorDur);
            _host.CursorOverride = Vec2.Lerp(_cursorFrom, _cursorTo, k);
            if (k >= 1) _cursorStart = -1;
        }
        if ((int)(now * 4) != (int)((now - 0.015) * 4))
        {
            _fps.Add(_host.Clock.FramesPerSecond);
            _self.Refresh();
            _peakMem = Math.Max(_peakMem, _self.WorkingSet64);
        }
        while (_next < _steps.Count && _steps[_next].At <= now)
        {
            var step = _steps[_next++];
            try
            {
                step.Act();
            }
            catch (Exception ex)
            {
                Check(false, $"step '{step.Name}' threw {ex.GetType().Name}: {ex.Message}");
                Log.Error("QA step " + step.Name, ex);
            }
        }
    }

    private void SnapPet(string name)
    {
        var w = _host.PetWindow;
        if (w.Content is FrameworkElement fe) Save(fe, name, withBackground: true);
    }

    private void SnapWindow(Window w, string name)
    {
        if (w.Content is FrameworkElement fe && fe.ActualWidth > 0) Save(fe, name, withBackground: false);
        else Check(false, $"window for {name} had no content");
    }

    private void Save(FrameworkElement fe, string name, bool withBackground)
    {
        try
        {
            var width = Math.Max(1, fe.ActualWidth > 0 ? fe.ActualWidth : fe.Width);
            var height = Math.Max(1, fe.ActualHeight > 0 ? fe.ActualHeight : fe.Height);
            const double scale = 2;
            var bmp = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            if (withBackground)
            {
                // Background first, then the element itself (RenderTargetBitmap accumulates).
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF2, 0xBD, 0x60)), null, new Rect(0, 0, width, height));
                }
                bmp.Render(dv);
            }
            bmp.Render(fe);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(_out, name + ".png"));
            enc.Save(fs);
        }
        catch (Exception ex)
        {
            Check(false, $"snapshot {name} failed: {ex.Message}");
        }
    }

    private void SnapDesktop(string name)
    {
        try
        {
            var e = _host.World.Monitors.Select(m => m.Bounds).Aggregate((a, b) => RectD.FromEdges(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom)));
            using var bmp = new System.Drawing.Bitmap((int)e.Width, (int)e.Height);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen((int)e.Left, (int)e.Top, 0, 0, bmp.Size);
            }
            var w = Math.Min(1600, bmp.Width);
            var h = (int)(bmp.Height * (w / (double)bmp.Width));
            using var small = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(w, h));
            small.Save(Path.Combine(_out, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex)
        {
            Log.Info($"desktop snapshot {name} unavailable: {ex.Message}");
        }
    }

    private void Finish()
    {
        _timer.Stop();
        Check(Log.ErrorCount == 0, $"no errors logged ({Log.ErrorCount})");
        var validFps = _fps.Where(f => f > 0).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Hoodie Companion — automated QA report");
        sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"OS: {Environment.OSVersion}  .NET: {Environment.Version}  64-bit: {Environment.Is64BitProcess}");
        sb.AppendLine($"Monitors: {_host.World.Fingerprint}");
        sb.AppendLine($"Frame rate while active: avg {(validFps.Count > 0 ? validFps.Average() : 0):0.0} fps, max {(validFps.Count > 0 ? validFps.Max() : 0):0.0}");
        sb.AppendLine($"Calm CPU (Quiet, sitting): {_calmCpu:0.00}% of all cores");
        sb.AppendLine($"Frame logic (simulation + scene update, excl. WPF render): {_host.FrameLogicMs:0.000} ms/frame");
        sb.AppendLine($"Peak working set: {_peakMem / 1048576.0:0} MB");
        sb.AppendLine($"States seen: {string.Join(", ", _seenStates.OrderBy(s => s.ToString()))}");
        sb.AppendLine();
        foreach (var c in _checks) sb.AppendLine(c);
        sb.AppendLine();
        sb.AppendLine(_failed ? "RESULT: FAIL" : "RESULT: PASS");
        sb.AppendLine();
        sb.AppendLine("Recent transitions:");
        foreach (var t in _host.Pet.Machine.RecentTransitions) sb.AppendLine("  " + t);
        File.WriteAllText(Path.Combine(_out, "qa-report.txt"), sb.ToString());
        Log.Info("QA finished: " + (_failed ? "FAIL" : "PASS"));
        if (!_keep)
        {
            _host.CursorOverride = null;
            _host.ButtonOverride = null;
            _host.Exit();
        }
        else
        {
            _host.CursorOverride = null;
            _host.ButtonOverride = null;
        }
    }
}
