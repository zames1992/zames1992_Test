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
    private Action? _releaseMidSwing;
    private bool _swinging;
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

    private readonly List<(double Deadline, string Name, Func<bool> Poll)> _polls = new();

    /// <summary>Runs <paramref name="poll"/> every tick until it returns true (FAIL after <paramref name="timeout"/> s).</summary>
    private void Until(string name, double timeout, Func<bool> poll) => _polls.Add((Now + timeout, name, poll));

    // ------------------------------------------------------------------ real clicks (v1.4)

    private int _clicks;
    private ClickTarget _lastClickTarget;
    private ClickResult _lastClick;

    /// <summary>
    /// A real click through Windows (cursor moved there, SendInput button down + up), so the layered window's
    /// own per-pixel hit testing decides whether it reaches Hoodie, exactly as for the user.
    /// </summary>
    private static void RealClick(Vec2 px, bool release = true)
    {
        int x = (int)Math.Round(px.X), y = (int)Math.Round(px.Y);
        NativeMethods.MouseAt(x, y, null);
        NativeMethods.MouseAt(x, y, true);
        if (release) NativeMethods.MouseAt(x, y, false);
        // A trailing move at the same spot: some input stacks (Wine) only deliver queued events on the next one.
        NativeMethods.MouseAt(x, y, null);
    }

    /// <summary>Hands the pointer and the button back to the real mouse for the real-click checks.</summary>
    private void UseRealMouse()
    {
        _host.CursorOverride = null;
        _host.ButtonOverride = null;
    }

    /// <summary>Thrown a little (simulated pointer), then clicked for real while it lands.</summary>
    private void RealLandingCase(Action next)
    {
        var pet = _host.Pet;
        var prim = _host.World.Primary.WorkArea;
        pet.StopActivity();
        pet.Place(new Vec2(prim.Left + prim.Width * 0.5, prim.Bottom), appear: false);
        var start = Now;
        var phase = 0;
        var before = 0;
        Until("real click on a landing Hoodie", 8, () =>
        {
            switch (phase)
            {
                case 0:
                    if (Now - start < 0.5) return false;
                    var hood = pet.Transform.LocalToWorld(new Vec2(272, 140));
                    _host.CursorOverride = hood;
                    _host.ButtonOverride = true;
                    pet.BeginGrab(hood);
                    MoveCursor(hood, hood + new Vec2(0, -_host.World.Primary.Scale * 160), 0.25);
                    phase = 1;
                    return false;
                case 1:
                    if (_cursorStart >= 0) return false;
                    _host.ButtonOverride = false;
                    pet.EndGrab(_host.CursorOverride!.Value);
                    UseRealMouse();
                    before = _clicks;
                    phase = 2;
                    return false;
                case 2:
                    if (pet.State is not (BehaviorState.Landing or BehaviorState.Recovering) || _host.PetWindow.ScreenPointOf("torso") is not { } p) return false;
                    RealClick(p);
                    phase = 3;
                    return false;
                default:
                    if (_clicks <= before) return false;
                    Check(_lastClick.Response == ClickResponse.ImOkay, $"real click on a landing Hoodie is answered ({_lastClick.Response})");
                    _host.Panel.Close(animated: false);
                    next();
                    return true;
            }
        });
    }

    /// <summary>In the air, pressing on Hoodie catches it at once (no drag threshold, no menu).</summary>
    private void RealCatchCase(Action next)
    {
        var pet = _host.Pet;
        var prim = _host.World.Primary.WorkArea;
        _host.Panel.Close(animated: false);
        pet.Place(new Vec2(prim.Left + prim.Width * 0.5, prim.Bottom), appear: false);
        var start = Now;
        var phase = 0;
        Until("catching Hoodie in the air", 8, () =>
        {
            switch (phase)
            {
                case 0:
                    if (Now - start < 0.5) return false;
                    var hood = pet.Transform.LocalToWorld(new Vec2(272, 140));
                    _host.CursorOverride = hood;
                    _host.ButtonOverride = true;
                    pet.BeginGrab(hood);
                    MoveCursor(hood, hood + new Vec2(0, -_host.World.Primary.Scale * 260), 0.12);
                    phase = 1;
                    return false;
                case 1:
                    if (_cursorStart >= 0) return false;
                    _host.ButtonOverride = false;
                    pet.EndGrab(_host.CursorOverride!.Value);
                    UseRealMouse();
                    phase = 2;
                    return false;
                case 2:
                    if (pet.State != BehaviorState.Airborne || _host.PetWindow.ScreenPointOf("torso") is not { } p)
                    {
                        if (pet.State is BehaviorState.Landing or BehaviorState.Recovering) { Check(false, "a real press on a flying Hoodie catches it (it landed first)"); next(); return true; }
                        return false;
                    }
                    RealClick(p, release: false);
                    phase = 3;
                    return false;
                default:
                    if (pet.State == BehaviorState.Grabbed)
                    {
                        Check(!_host.Panel.IsOpen, "a real press on a flying Hoodie catches it (no menu)");
                        var c = MouseService.Cursor();
                        NativeMethods.MouseAt((int)c.X, (int)c.Y, false);
                        NativeMethods.MouseAt((int)c.X, (int)c.Y, null);
                        next();
                        return true;
                    }
                    if (pet.State is BehaviorState.Landing or BehaviorState.Recovering)
                    {
                        Check(false, "a real press on a flying Hoodie catches it (it landed instead)");
                        NativeMethods.MouseAt(0, 0, false);
                        next();
                        return true;
                    }
                    return false;
            }
        });
    }

    /// <summary>Empty space next to Hoodie must stay the desktop's.</summary>
    private void RealEmptyCase(Action next)
    {
        var pet = _host.Pet;
        var world = _host.World;
        var prim = world.Primary.WorkArea;
        _host.Panel.Close(animated: false);
        pet.Place(new Vec2(prim.Left + prim.Width * 0.5, prim.Bottom), appear: false);
        pet.SetMode(PresenceMode.Quiet);
        var start = Now;
        var before = 0;
        var sent = false;
        Until("a click beside Hoodie", 6, () =>
        {
            if (!sent)
            {
                if (Now - start < 2) return false;
                before = _clicks;
                // Behind Hoodie (its legs stretch forward when it sits), 30 DIP past the body.
                var body = _host.PetBoundsPx;
                var x = pet.Facing > 0 ? body.Left - 30 * world.Primary.Scale : body.Right + 30 * world.Primary.Scale;
                RealClick(new Vec2(x, body.Top + body.Height * 0.3));
                sent = true;
                start = Now;
                return false;
            }
            if (Now - start < 1) return false;
            Check(_clicks == before, $"a click 30 DIP beside Hoodie goes to the desktop ({_clicks - before} clicks reached Hoodie)");
            pet.SetMode(PresenceMode.Normal);
            _host.CursorOverride = new Vec2(prim.Left + 10, prim.Top + 10);
            _host.ButtonOverride = false;
            next();
            return true;
        });
    }

    private sealed record ClickCaseSpec(string Label, Action Setup, string Group, Func<ClickResult, bool>? Expect, double Settle, Action? Then);

    private readonly List<ClickCaseSpec> _clickCases = new();

    /// <summary>Queues a real click on a rig group of Hoodie (see <see cref="RunClickCases"/>).</summary>
    private void ClickCase(string label, Action setup, string group, Func<ClickResult, bool>? expect = null, double settle = 1.6, Action? then = null) =>
        _clickCases.Add(new ClickCaseSpec(label, setup, group, expect, settle, then));

    /// <summary>
    /// Runs the queued click cases one after another (each waits for the previous one to finish, so a slow
    /// machine cannot make them overlap), then calls <paramref name="done"/>.
    /// </summary>
    private void RunClickCases(Action done)
    {
        var index = -1;
        var phase = 0;
        double started = 0, deadline = 0;
        var before = 0;
        ClickCaseSpec? c = null;
        Until("real click cases", 120, () =>
        {
            if (c is null)
            {
                if (++index >= _clickCases.Count) { done(); return true; }
                c = _clickCases[index];
                UseRealMouse();
                NativeMethods.MouseAt(0, 0, null);
                _host.Panel.Close(animated: false);
                try { c.Setup(); } catch (Exception ex) { Check(false, $"real click on {c.Label}: setup threw {ex.Message}"); c = null; return false; }
                started = Now;
                phase = 0;
                return false;
            }
            switch (phase)
            {
                case 0:
                    if (Now - started < 0.4) return false;
                    c.Then?.Invoke();
                    phase = 1;
                    return false;
                case 1:
                    if (Now - started < c.Settle) return false;
                    // "a|b": the first of these groups that is drawn (e.g. the blanket if Hoodie took it to bed).
                    var drawn = c.Group.Split('|').Select(g => _host.PetWindow.ScreenPointOf(g)).FirstOrDefault(p => p is not null);
                    if (drawn is not { } at)
                    {
                        if (Now - started > c.Settle + 2)
                        {
                            Check(false, $"real click on {c.Label}: '{c.Group}' is not drawn ({_host.Pet.State}, {_host.LastRender.Clip})");
                            c = null;
                        }
                        return false;
                    }
                    before = _clicks;
                    RealClick(at);
                    deadline = Now + 2;
                    phase = 2;
                    return false;
                default:
                    if (_clicks > before)
                    {
                        var ok = c.Expect?.Invoke(_lastClick) ?? _lastClick.Response != ClickResponse.None;
                        Check(ok, $"real click on {c.Label} is answered ({_lastClick.Response}, {_lastClickTarget})");
                        _host.Panel.Close(animated: false);
                        c = null;
                    }
                    else if (Now > deadline)
                    {
                        Check(false, $"real click on {c.Label} is answered (no click)");
                        c = null;
                    }
                    return false;
            }
        });
    }

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
        _host.PetClickHandled += (target, result) => { _clicks++; _lastClickTarget = target; _lastClick = result; };
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
            _swinging = true;
        });
        // Release mid-swing (by swing progress, not by the clock: a late timer tick must not release a pointer
        // that has already stopped).
        _releaseMidSwing = () =>
        {
            _host.ButtonOverride = false;
            pet.EndGrab(_host.CursorOverride!.Value);
            Check(pet.State == BehaviorState.Airborne && pet.Physics.Velocity.Length > 200, $"release gives throw velocity ({pet.Physics.Velocity.Length:0} px/s)");
            _host.CursorOverride = far;
        };
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
            _host.Memory.GiveItem("fan");
            _host.Memory.LoseItem("fan"); // shows as "misplaced" on the Memories page
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
            // The menu opens at once, or right after Hoodie wakes up if it was still asleep.
            Until("a quick click on a walking Hoodie opens the panel", 1.5, () =>
            {
                if (!delivered || !_host.Panel.IsOpen) return false;
                Check(true, "a quick click on a walking Hoodie opens the panel");
                _host.Panel.Close(animated: false);
                return true;
            });
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
        At(131.3, "v13-postcard", () =>
        {
            Postcard.Folder = _out;
            var moment = new MomentMemory { Key = "first-fan", At = DateTime.Now };
            _host.SavePostcard(new MomentMemory { Key = "found-again:ball", At = DateTime.Now }, "Found its ball again after misplacing it.", reveal: false);
            var path = _host.SavePostcard(moment, "Fanned itself while your PC was working hard.", reveal: false);
            var ok = path is not null && File.Exists(path) && new FileInfo(path).Length > 20_000;
            if (ok) File.Copy(path!, Path.Combine(_out, "28-postcard.png"), true);
            Check(ok, "a moment can be saved as a postcard (only Hoodie is drawn)");
        });
        // v1.4: real clicks in every kind of animation (through Windows' own hit testing, not QaPress).
        var mid = prim.Left + prim.Width * 0.5;
        ClickCase("sitting Hoodie", () => { pet.Place(new Vec2(mid, prim.Bottom), appear: false); pet.SetMode(PresenceMode.Quiet); }, "torso",
            r => r.Response == ClickResponse.SeatedWave);
        ClickCase("the laptop", () => { pet.SetMode(PresenceMode.Normal); pet.SetPanelActivity(PanelActivity.Laptop); }, "laptop",
            r => r.Response == ClickResponse.PropUse, settle: 2.2);
        ClickCase("Hoodie at its laptop", () => pet.SetPanelActivity(PanelActivity.Laptop), "head", r => r.Response == ClickResponse.Glance, settle: 2.2);
        ClickCase("the notebook", () => pet.SetPanelActivity(PanelActivity.Notes), "notebook",
            r => r.Response == ClickResponse.PropUse, settle: 2.4, then: () => pet.NoteTyping());
        ClickCase("the blanket (asleep)", () =>
        {
            pet.SetPanelActivity(PanelActivity.None);
            _host.Memory.GiveItem("blanket");
        }, "blanket|torso", r => r.Response == ClickResponse.Wake, settle: 4.0, then: () => pet.WorldSleep(true));
        ClickCase("walking Hoodie", () =>
        {
            pet.WorldSleep(false);
            pet.Place(new Vec2(prim.Left + prim.Width * 0.7, prim.Bottom), appear: false);
        }, "torso", r => r.Response == ClickResponse.Boop, settle: 1.0,
            then: () => pet.TravelTo(new Vec2(prim.Left + prim.Width * 0.2, prim.Bottom), false, null));
        ClickCase("running Hoodie", () =>
        {
            pet.Place(new Vec2(prim.Left + prim.Width * 0.2, prim.Bottom), appear: false);
        }, "torso", r => r.Response == ClickResponse.Boop, settle: 0.9,
            then: () => pet.TravelTo(new Vec2(prim.Left + prim.Width * 0.8, prim.Bottom), true, null));
        ClickCase("Hoodie on the screen side", () =>
        {
            pet.Place(new Vec2(prim.Left + prim.Width * 0.07, prim.Bottom), appear: false);
        }, "torso", r => r.Response == ClickResponse.HoldWave, settle: 2.6, then: () => Check(pet.DebugClimbWall(), "starts climbing for the real click"));
        ClickCase("the ball", () =>
        {
            pet.StopActivity();
            pet.Place(new Vec2(mid, prim.Bottom), appear: false);
        }, "ball", r => r.Response == ClickResponse.KickBall && !_host.Panel.IsOpen, settle: 2.0,
            then: () => Check(pet.DebugIntent(HoodieCompanion.Companion.Behavior.Activity.PlayBall), "the ball game starts for the real click"));

        At(132.0, "real clicks", () => RunClickCases(() => RealLandingCase(() => RealCatchCase(() => RealEmptyCase(Finish)))));
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
            if (_releaseMidSwing is { } release && _swinging && k >= 0.8)
            {
                _host.CursorOverride = Vec2.Lerp(_cursorFrom, _cursorTo, k);
                _releaseMidSwing = null;
                _swinging = false;
                _cursorStart = -1;
                release();
                return;
            }
            _host.CursorOverride = Vec2.Lerp(_cursorFrom, _cursorTo, k);
            if (k >= 1) _cursorStart = -1;
        }
        if ((int)(now * 4) != (int)((now - 0.015) * 4))
        {
            _fps.Add(_host.Clock.FramesPerSecond);
            _self.Refresh();
            _peakMem = Math.Max(_peakMem, _self.WorkingSet64);
        }
        for (var i = _polls.Count - 1; i >= 0; i--)
        {
            var (deadline, name, poll) = _polls[i];
            bool done;
            try { done = poll(); }
            catch (Exception ex) { done = true; Check(false, $"poll '{name}' threw {ex.GetType().Name}: {ex.Message}"); }
            if (done) _polls.RemoveAt(i);
            else if (now > deadline) { _polls.RemoveAt(i); Check(false, $"{name}: timed out"); }
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
        var (median, p95) = _host.FrameLogicPercentiles();
        sb.AppendLine($"Frame logic (simulation + scene update, excl. WPF render): median {median:0.000} ms, p95 {p95:0.000} ms, max {_host.FrameLogicMaxMs:0.0} ms, frames over 8 ms: {_host.SlowFrames} (recent average {_host.FrameLogicMs:0.000} ms)");
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
