using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HoodieCompanion.Platform;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;
using static HoodieCompanion.UI.L;

namespace HoodieCompanion.UI;

/// <summary>Layer 4: settings. Changes apply immediately and are saved locally.</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly StackPanel _content = new() { Margin = new Thickness(22, 18, 22, 22) };

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
        Content = Ui.Scroll(_content);
        SourceInitialized += (_, _) => WindowInterop.RoundCorners(this);
        Closed += (_, _) => _host.SaveAll();
        Build();
    }

    public void Build()
    {
        var s = _host.Settings;
        _content.Children.Clear();
        var title = Ui.Text(T("Settings"), 22, weight: FontWeights.SemiBold);
        _content.Children.Add(title);
        _content.Children.Add(Ui.Text(T("Everything is stored locally on this PC. Nothing is sent anywhere."), 12, dim: true));

        Section(T("Language"));
        var langs = new[] { "auto", "en", "ru" };
        Combo(T("Interface language"), new[] { T("Automatic (Windows)"), "English", "Русский" }, Math.Max(0, Array.IndexOf(langs, s.Language)), i =>
        {
            s.Language = langs[Math.Max(0, i)];
            L.Set(s.Language);
            Dispatcher.BeginInvoke(Build);
        });

        Section(T("Companion"));
        SliderRow(T("Size"), s.Scale, 0.5, 2.0, v => { s.Scale = v; _host.SettingsChanged(); }, v => $"{v * 100:0}%");
        SliderRow(T("Walking speed"), s.WalkSpeed, 0.4, 2.5, v => { s.WalkSpeed = v; _host.SettingsChanged(); }, v => $"{v:0.0}×");

        Section(T("Behavior"));
        Check(T("Autonomous behavior (wanders, rests and explores on its own)"), s.AutonomousBehavior, v => s.AutonomousBehavior = v);
        var modes = Enum.GetValues<PresenceMode>().Where(m => m != PresenceMode.Alone).ToArray();
        Combo(T("Default presence mode"), modes.Select(QuickPanel.ModeName).ToArray(), Array.IndexOf(modes, s.DefaultPresenceMode),
            i => s.DefaultPresenceMode = modes[Math.Max(0, i)]);
        Check(T("Remember the last presence mode on start"), s.RestorePresenceOnStart, v => s.RestorePresenceOnStart = v);

        Section(T("Interaction"));
        Check(T("React to the cursor"), s.CursorReactions, v => s.CursorReactions = v);
        Check(T("Allow grabbing and throwing"), s.GrabThrow, v => s.GrabThrow = v);
        Check(T("Reduced motion (no big jumps, gentle transitions)"), s.ReducedMotion, v => s.ReducedMotion = v);

        Section(T("Appearance"));
        Check(T("Always on top"), s.AlwaysOnTop, v => { s.AlwaysOnTop = v; _host.SettingsChanged(); });

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

        Section(T("Utilities"));
        Check(T("React to heavy PC load (rarely, with long cooldowns)"), s.PcStatusReactions, v => s.PcStatusReactions = v);
        Check(T("Sounds (soft, only for items, reminders and timers)"), s.Sounds, v => s.Sounds = v);

        Section(T("System"));
        Check(T("Start with Windows"), StartupService.IsEnabled(), v => { s.StartWithWindows = v; StartupService.Set(v); });
        Check(T("Hoodie in the desktop right-click menu"), s.DesktopMenu, v => { s.DesktopMenu = v; _host.ApplyDesktopMenu(); });
        _content.Children.Add(Ui.Text(T("Data location: ") + _host.Storage.Root, 12, dim: true));
        var sys = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        sys.Children.Add(Btn(T("Open data folder"), () => _host.OpenDataFolder()));
        sys.Children.Add(Btn(T("Emergency hide: Ctrl+Alt+H") + (_host.HotkeyRegistered ? "" : " " + T("(unavailable — use the tray)")), () => { }, enabled: false));
        _content.Children.Add(sys);
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
