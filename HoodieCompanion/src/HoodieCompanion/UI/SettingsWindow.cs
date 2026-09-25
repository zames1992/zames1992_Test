using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HoodieCompanion.Platform;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;
using static HoodieCompanion.UI.L;

namespace HoodieCompanion.UI;

/// <summary>Layer 4: settings. Changes apply immediately and are saved locally.</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly StackPanel _root = new() { Margin = new Thickness(22, 18, 22, 22) };
    /// <summary>Where rows are added (the root, or the body of a collapsible group).</summary>
    private StackPanel _content;
    private static readonly HashSet<string> Expanded = new();
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<(TextBlock Block, Func<string> Text)> _liveTexts = new();

    public SettingsWindow(AppHost host)
    {
        _host = host;
        Title = T("Hoodie Companion — Settings");
        Width = 520;
        Height = 720;
        MinWidth = 440;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Brush("Bg");
        Foreground = Ui.Brush("Text");
        FontFamily = Ui.Font;
        _content = _root;
        Content = Ui.Scroll(_root);
        _live.Tick += (_, _) => { foreach (var (b, t) in _liveTexts) if (b.IsVisible) b.Text = t(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) _live.Start(); else _live.Stop(); };
        SourceInitialized += (_, _) => WindowInterop.RoundCorners(this);
        // Closing only hides the window: it is reused next time (opens instantly).
        Closing += (_, e) =>
        {
            _host.SaveAll();
            if (_host.IsExiting) return;
            e.Cancel = true;
            Hide();
        };
        Build();
    }

    public void Build()
    {
        var s = _host.Settings;
        _root.Children.Clear();
        _liveTexts.Clear();
        _content = _root;
        var title = Ui.Text(T("Settings"), 22, weight: FontWeights.SemiBold);
        _content.Children.Add(title);
        _content.Children.Add(Ui.Text(T("Everything is stored locally on this PC. Nothing is sent anywhere."), 12, dim: true));

        // ---- simple, everyday settings
        Section(T("Companion"));
        SliderRow(T("Size"), s.Scale, 0.5, 2.0, v => { s.Scale = v; _host.SettingsChanged(); }, v => $"{v * 100:0}%");
        SliderRow(T("Walking speed"), s.WalkSpeed, 0.4, 2.5, v => { s.WalkSpeed = v; _host.SettingsChanged(); }, v => $"{v:0.0}×");
        Check(T("Autonomous behavior (wanders, rests and explores on its own)"), s.AutonomousBehavior, v => s.AutonomousBehavior = v);
        var modes = Enum.GetValues<PresenceMode>().Where(m => m != PresenceMode.Alone).ToArray();
        Combo(T("Default presence mode"), modes.Select(QuickPanel.ModeName).ToArray(), Array.IndexOf(modes, s.DefaultPresenceMode),
            i => s.DefaultPresenceMode = modes[Math.Max(0, i)]);
        Check(T("Allow grabbing and throwing"), s.GrabThrow, v => s.GrabThrow = v);
        Check(T("Reduced motion (no big jumps, gentle transitions)"), s.ReducedMotion, v => s.ReducedMotion = v);
        Check(T("Sounds (soft, only for items, reminders and timers)"), s.Sounds, v => s.Sounds = v);
        Check(T("Start with Windows"), StartupService.IsEnabled(), v => { s.StartWithWindows = v; StartupService.Set(v); });
        var langs = new[] { "auto", "en", "ru" };
        Combo(T("Interface language"), new[] { T("Automatic (Windows)"), "English", "Русский" }, Math.Max(0, Array.IndexOf(langs, s.Language)), i =>
        {
            s.Language = langs[Math.Max(0, i)];
            L.Set(s.Language);
            Dispatcher.BeginInvoke(Build);
        });

        // ---- deeper: collapsed until opened
        Group("territory", T("Territory and apps"), T("Where Hoodie may go, and how it behaves around specific apps."), BuildTerritory);
        Group("privacy", T("Privacy and data"), T("What Hoodie notices, what it remembers, and what it never touches."), BuildPrivacy);
        Group("advanced", T("Advanced"), T("Fine-tuning for the curious."), BuildAdvanced);
        Group("debug", T("Performance and debug"), T("What Hoodie is thinking, and what it costs your PC."), BuildDebug);
    }

    private void BuildTerritory()
    {
        var s = _host.Settings;
        Section(T("Territory"));
        var home = _host.Territory.Data.Home;
        var homeMon = home is null ? null : _host.World.FindById(home.MonitorId);
        _content.Children.Add(Ui.Text(home is null ? T("No Home set — Hoodie rests near the bottom-right of your main monitor.") :
            F("Home: {0}, {1:0}% from the left.", homeMon is null ? T("a monitor that is not connected") : MonitorName(homeMon), home.RelX * 100), 12.5, dim: true));
        var homeButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        homeButtons.Children.Add(Btn(T("Set Home to Hoodie's current spot"), () => { _host.SetHomeHere(); Build(); }));
        homeButtons.Children.Add(Btn(T("Clear Home"), () => { _host.Territory.Data.Home = null; _host.Territory.NotifyChanged(); Build(); }));
        _content.Children.Add(homeButtons);

        var monCaption = Ui.Text(T("Per-monitor rules"), 13, weight: FontWeights.SemiBold);
        monCaption.Margin = new Thickness(0, 12, 0, 4);
        _content.Children.Add(monCaption);
        var ruleTypes = new[] { RegionType.Free, RegionType.Quiet, RegionType.PassThrough, RegionType.NoGo };
        var ruleNames = new[] { T("Allowed"), T("Quiet"), T("Pass through only"), T("Never enter") };
        foreach (var m in _host.World.Monitors)
        {
            var mon = m;
            var cur = _host.Territory.MonitorRule(mon.Id);
            var idx = Array.IndexOf(ruleTypes, cur);
            Combo($"{MonitorName(mon)} — {mon.Bounds.Width:0}×{mon.Bounds.Height:0} @ {mon.Scale * 100:0}%", ruleNames, idx < 0 ? 0 : idx,
                i => _host.Territory.SetMonitorRule(mon.Id, ruleTypes[Math.Max(0, i)]));
        }
        var regions = _host.Territory.Data.Regions.Count;
        _content.Children.Add(Ui.Text(regions == 0 ? T("No restricted areas drawn.") : F("{0} area(s) drawn.", regions), 12.5, dim: true));
        var terr = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        terr.Children.Add(Btn(T("Open territory editor…"), () => _host.ShowTerritoryEditor(), accent: true));
        terr.Children.Add(Btn(T("Clear drawn areas"), () => { _host.Territory.Data.Regions.Clear(); _host.Territory.NotifyChanged(); Build(); }));
        _content.Children.Add(terr);

        Section(T("App rules"));
        _content.Children.Add(Ui.Text(T("How Hoodie behaves while a specific app is in front. Fullscreen games, videos and presentations are handled automatically."), 12.5, dim: true));
        Check(T("Hide or step aside during fullscreen apps"), s.HideOnFullscreen, v => s.HideOnFullscreen = v);
        var appModes = Enum.GetValues<AppPresenceMode>();
        foreach (var rule in _host.Territory.Data.AppRules.ToList())
        {
            var r = rule;
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var del = Btn(T("Remove"), () => { _host.Territory.Data.AppRules.Remove(r); _host.Territory.NotifyChanged(); Build(); });
            DockPanel.SetDock(del, Dock.Right);
            row.Children.Add(del);
            var cb = new ComboBox { ItemsSource = appModes.Select(AppModeName).ToList(), SelectedIndex = Array.IndexOf(appModes, r.PresenceMode), Margin = new Thickness(8, 0, 8, 0), MinWidth = 100 };
            cb.SelectionChanged += (_, _) => { r.PresenceMode = appModes[Math.Max(0, cb.SelectedIndex)]; _host.Territory.NotifyChanged(); };
            DockPanel.SetDock(cb, Dock.Right);
            row.Children.Add(cb);
            var name = Ui.Text(r.ProcessName, 13);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            _content.Children.Add(row);
        }
        var addRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var nameBox = new ComboBox { IsEditable = true, MinWidth = 180, ItemsSource = _host.RecentProcesses.ToList() };
        nameBox.Resources[SystemColors.WindowBrushKey] = Ui.Brush("Surface");
        var modeBox = new ComboBox { ItemsSource = appModes.Select(AppModeName).ToList(), SelectedIndex = 1, Margin = new Thickness(8, 0, 8, 0), MinWidth = 100 };
        var add = Btn(T("Add rule"), () =>
        {
            var n = (nameBox.Text ?? "").Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
            if (n.Length == 0) return;
            _host.Territory.Data.AppRules.RemoveAll(x => string.Equals(x.ProcessName, n, StringComparison.OrdinalIgnoreCase));
            _host.Territory.Data.AppRules.Add(new AppPresenceRule { ProcessName = n, PresenceMode = appModes[Math.Max(0, modeBox.SelectedIndex)] });
            _host.Territory.NotifyChanged();
            Build();
        }, accent: true);
        DockPanel.SetDock(add, Dock.Right);
        addRow.Children.Add(add);
        DockPanel.SetDock(modeBox, Dock.Right);
        addRow.Children.Add(modeBox);
        addRow.Children.Add(nameBox);
        _content.Children.Add(addRow);
        _content.Children.Add(Ui.Text(T("Quiet: calm down · Avoid: stay off that app's monitor · Hide: disappear while it is in front."), 11.5, dim: true));

    }

    private void BuildAdvanced()
    {
        var s = _host.Settings;
        Check(T("Remember the last presence mode on start"), s.RestorePresenceOnStart, v => s.RestorePresenceOnStart = v);
        Check(T("React to the cursor"), s.CursorReactions, v => s.CursorReactions = v);
        Check(T("React to heavy PC load (rarely, with long cooldowns)"), s.PcStatusReactions, v => s.PcStatusReactions = v);
        Check(T("Hoodie may climb onto windows and desktop icons"), s.ClimbOnWindows, v => s.ClimbOnWindows = v);
        Check(T("Always on top"), s.AlwaysOnTop, v => { s.AlwaysOnTop = v; _host.SettingsChanged(); });
        Check(T("Hoodie in the desktop right-click menu"), s.DesktopMenu, v => { s.DesktopMenu = v; _host.ApplyDesktopMenu(); });
        var sys = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        sys.Children.Add(Btn(T("Emergency hide: Ctrl+Alt+H") + (_host.HotkeyRegistered ? "" : " " + T("(unavailable — use the tray)")), () => { }, enabled: false));
        _content.Children.Add(sys);
    }

    private void BuildPrivacy()
    {
        var s = _host.Settings;
        Sub(T("What Hoodie notices, and why"));
        Bullet(T("How long since you last used the mouse or keyboard: to know whether you are here, away or back."));
        Bullet(T("That keys are being pressed, never which ones: to stay quiet and out of the way while you type."));
        Bullet(T("The name of the app in front (like \"chrome\") and where its window is: to walk on windows, keep off your work and notice new apps."));
        Bullet(T("CPU, GPU and network load: to notice when the PC is working hard."));
        Bullet(T("The time of day and your monitor layout: to be calmer at night and to know where it can walk."));
        Sub(T("What Hoodie remembers (on this PC only)"));
        Bullet(T("Time spent together, which days, and at what hours you are usually around."));
        Bullet(T("App names it has seen and roughly how long each was in front."));
        Bullet(T("Its favourite spots, the things it found, and memorable moments (like the first time you threw it)."));
        Bullet(T("How often things happened, so it gets used to them."));
        Sub(T("What Hoodie never stores or reads"));
        Bullet(T("The text you type, passwords, messages, documents, window titles, web pages or clipboard."));
        Bullet(T("Screenshots: Hoodie never looks at your screen contents."));
        Bullet(T("Nothing is sent over the internet. There is no account and no telemetry."));

        Sub(T("Your choices"));
        Check(T("Remember the apps I use (names and minutes only)"), s.LearnFromApps, v => s.LearnFromApps = v);
        Check(T("Notice when I'm typing (to stay out of the way)"), s.NoticeTyping, v => s.NoticeTyping = v);
        var mem = _host.Memory;
        _content.Children.Add(Ui.Text(F("Hoodie remembers {0} apps, {1} spots and {2} moments.", mem.Doc.Apps.Count, mem.Doc.Places.Count, mem.Doc.Moments.Count), 12.5, dim: true));
        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(Btn(T("Forget everything Hoodie learned"), () =>
        {
            var ok = MessageBox.Show(this, T("Hoodie will forget the apps, places, moments and things it found. Its personality stays. Continue?"),
                T("Forget memories"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK) return;
            _host.ForgetMemories();
            Build();
        }));
        row.Children.Add(Btn(T("Open data folder"), () => _host.OpenDataFolder()));
        _content.Children.Add(row);
        _content.Children.Add(Ui.Text(T("Data location: ") + _host.Storage.Root, 12, dim: true));
    }

    private void BuildDebug()
    {
        var pet = _host.Pet;
        Sub(T("What Hoodie is thinking"));
        LiveLine(() => F("Doing: {0}", QuickPanel.Describe(pet.State, pet.Mode, pet.ActivityName)));
        LiveLine(() => pet.Mind.CurrentIntent is { } i ? F("Last decision: {0} — {1}", i, pet.Mind.CurrentReason ?? "") : T("No decision yet."));
        LiveLine(() => F("Noticing: {0}{1}{2}", pet.Perception.ForegroundProcess ?? "—",
            pet.Perception.Typing ? " · " + T("typing") : "", pet.Perception.PcHot ? " · " + T("PC is hot") : ""));
        LiveLine(() => pet.Mind.ToString());
        LiveLine(() => T("Character: ") + pet.Mind.Traits);

        Sub(T("Hoodie's own footprint"));
        LiveLine(() => _host.Performance.Latest is { } l ? T("Now: ") + PerformanceWatch.Describe(l) : T("Measuring…"));
        LiveLine(() => _host.Performance.Latest is null ? "" : T("Peak: ") + PerformanceWatch.Describe(_host.Performance.Peak));
        LiveLine(() => _host.Performance.First is { } f ? F("Running for {0:0.0} h", (DateTime.Now - f.At).TotalHours) : "");
        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(Btn(T("Measure now"), () => _host.Performance.Take()));
        row.Children.Add(Btn(T("Open log"), () => _host.OpenDataFolder()));
        _content.Children.Add(row);
    }

    private static string MonitorName(HoodieCompanion.Geometry.MonitorInfo m) =>
        F("Monitor {0}", m.Index + 1) + (m.IsPrimary ? " (" + T("main") + ")" : "");

    private static string AppModeName(AppPresenceMode m) => m switch
    {
        AppPresenceMode.Quiet => T("Quiet"),
        AppPresenceMode.Avoid => T("Avoid"),
        AppPresenceMode.Hide => T("Hide"),
        _ => T("Normal"),
    };

    /// <summary>Opens groups (QA snapshots).</summary>
    public void Expand(params string[] keys)
    {
        foreach (var k in keys) Expanded.Add(k);
        Build();
    }

    /// <summary>A collapsible group: the header is always visible, the body is built when opened.</summary>
    private void Group(string key, string title, string subtitle, Action build)
    {
        var body = new StackPanel { Margin = new Thickness(4, 4, 0, 6) };
        var open = Expanded.Contains(key);
        var arrow = Ui.Icon(open ? Ui.Icons.ChevronDown : Ui.Icons.ChevronRight, 14, "TextDim");
        arrow.Margin = new Thickness(0, 3, 8, 0);
        arrow.VerticalAlignment = VerticalAlignment.Top;
        var head = new DockPanel();
        DockPanel.SetDock(arrow, Dock.Left);
        head.Children.Add(arrow);
        var texts = new StackPanel();
        texts.Children.Add(Ui.Text(title, 14, weight: FontWeights.SemiBold));
        texts.Children.Add(Ui.Text(subtitle, 11.5, dim: true));
        head.Children.Add(texts);
        var button = Ui.Button(head, () =>
        {
            if (!Expanded.Remove(key)) Expanded.Add(key);
            Build();
        }, "GhostButton");
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Margin = new Thickness(-6, 14, 0, 0);
        _root.Children.Add(button);
        if (!open) return;
        _root.Children.Add(body);
        var saved = _content;
        _content = body;
        try { build(); } finally { _content = saved; }
    }

    private void Sub(string name)
    {
        var t = Ui.Text(name, 13, weight: FontWeights.SemiBold);
        t.Margin = new Thickness(0, 12, 0, 4);
        _content.Children.Add(t);
    }

    private void Bullet(string text)
    {
        var t = Ui.Text("· " + text, 12.5);
        t.Margin = new Thickness(0, 1, 0, 1);
        _content.Children.Add(t);
    }

    private void LiveLine(Func<string> text)
    {
        var t = Ui.Text(text(), 12, dim: true);
        t.Margin = new Thickness(0, 1, 0, 1);
        _liveTexts.Add((t, text));
        _content.Children.Add(t);
    }

    private void Section(string name)
    {
        var c = Ui.Caption(name);
        c.Margin = new Thickness(0, 20, 0, 6);
        c.Foreground = Ui.Brush("Accent");
        _content.Children.Add(c);
    }

    private void Check(string label, bool value, Action<bool> set)
    {
        var cb = new CheckBox { Content = label, IsChecked = value };
        cb.Click += (_, _) =>
        {
            set(cb.IsChecked == true);
            _host.SettingsChanged();
        };
        _content.Children.Add(cb);
    }

    private void SliderRow(string label, double value, double min, double max, Action<double> set, Func<double, string> format)
    {
        var l = Ui.Text(label, 13);
        var v = Ui.Text(format(value), 13, dim: true);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 200, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            v.Text = format(e.NewValue);
            set(e.NewValue);
        };
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(v, Dock.Right);
        row.Children.Add(v);
        DockPanel.SetDock(slider, Dock.Right);
        row.Children.Add(slider);
        row.Children.Add(l);
        _content.Children.Add(row);
    }

    private void Combo(string label, string[] items, int index, Action<int> set)
    {
        var l = Ui.Text(label, 13);
        l.VerticalAlignment = VerticalAlignment.Center;
        var cb = new ComboBox { ItemsSource = items, SelectedIndex = index, MinWidth = 160 };
        cb.SelectionChanged += (_, _) =>
        {
            set(cb.SelectedIndex);
            _host.SettingsChanged();
        };
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(cb, Dock.Right);
        row.Children.Add(cb);
        row.Children.Add(l);
        _content.Children.Add(row);
    }

    private static Button Btn(string label, Action click, bool accent = false, bool enabled = true)
    {
        var b = Ui.Button(label, click, accent ? "AccentButton" : null);
        b.Margin = new Thickness(0, 0, 8, 6);
        b.IsEnabled = enabled;
        return b;
    }
}
