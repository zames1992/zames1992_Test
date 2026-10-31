using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Memory;
using HoodieCompanion.Companion.Rendering;
using HoodieCompanion.Platform;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;
using static HoodieCompanion.UI.L;

namespace HoodieCompanion.UI;

/// <summary>
/// Layer 4: settings. A sidebar of sections on the left, one section at a time on the right.
/// Changes apply immediately and are saved locally.
/// </summary>
public sealed class SettingsWindow : Window
{
    public const string Character = "character", Appearance = "appearance", Control = "control", Territory = "territory", Privacy = "privacy", System = "system";
    private static readonly string[] Sections = { Character, Appearance, Control, Territory, Privacy, System };
    private static string _current = Character;

    private readonly AppHost _host;
    private readonly StackPanel _nav = new() { Margin = new Thickness(10, 14, 10, 10) };
    private readonly StackPanel _content = new() { Margin = new Thickness(26, 18, 26, 26) };
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<(TextBlock Block, Func<string> Text)> _liveTexts = new();
    private readonly List<CharacterRig> _previews = new();
    private double _previewTime;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        Title = T("Hoodie Companion — Settings");
        Width = 780;
        Height = 640;
        MinWidth = 640;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Brush("Bg");
        Foreground = Ui.Brush("Text");
        FontFamily = Ui.Font;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var side = new Border { Background = Ui.Brush("Surface"), BorderBrush = Ui.Brush("BorderBrush"), BorderThickness = new Thickness(0, 0, 1, 0), Child = _nav };
        grid.Children.Add(side);
        _scroll = Ui.Scroll(_content);
        Grid.SetColumn(_scroll, 1);
        grid.Children.Add(_scroll);
        Content = grid;

        _live.Tick += (_, _) =>
        {
            foreach (var (b, t) in _liveTexts) if (b.IsVisible) b.Text = t();
            AnimatePreviews();
        };
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

    /// <summary>Rebuilds the sidebar and the current section.</summary>
    public void Build() => Show(_current);

    /// <summary>Shows one section (QA snapshots call this too). Unknown keys fall back to the last one.</summary>
    public void Show(string section)
    {
        if (section == "debug" || section == "advanced") section = System;
        if (Array.IndexOf(Sections, section) >= 0) _current = section;
        BuildNav();
        BuildContent();
        _scroll.ScrollToTop();
    }

    /// <summary>Compatibility with older QA scripts: opens the last of the given sections.</summary>
    public void Expand(params string[] keys)
    {
        if (keys.Length > 0) Show(keys[^1]);
    }

    // ------------------------------------------------------------------ sidebar

    private void BuildNav()
    {
        _nav.Children.Clear();
        var head = new StackPanel { Margin = new Thickness(8, 4, 0, 14) };
        head.Children.Add(Ui.Text(T("Settings"), 19, weight: FontWeights.SemiBold));
        head.Children.Add(Ui.Text(T("Stored on this PC only."), 11.5, dim: true));
        _nav.Children.Add(head);
        foreach (var s in Sections) _nav.Children.Add(NavItem(s));
        var footer = Ui.Text("v" + AppVersion, 11, dim: true);
        footer.Margin = new Thickness(8, 18, 0, 0);
        _nav.Children.Add(footer);
    }

    private static string AppVersion => typeof(SettingsWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    private Button NavItem(string section)
    {
        var selected = section == _current;
        var row = new DockPanel();
        var icon = Ui.Icon(IconOf(section), 17, selected ? "Accent" : "TextDim");
        icon.Margin = new Thickness(0, 0, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        var text = Ui.Text(NameOf(section), 13.5, weight: selected ? FontWeights.SemiBold : FontWeights.Normal, wrap: TextWrapping.NoWrap);
        text.Foreground = Ui.Brush(selected ? "Text" : "TextDim");
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        var b = Ui.Button(row, () => Show(section), "GhostButton");
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.Padding = new Thickness(10, 8, 10, 8);
        b.Margin = new Thickness(0, 1, 0, 1);
        if (selected) b.Background = Ui.Brush("SurfaceHi");
        return b;
    }

    private static string IconOf(string s) => s switch
    {
        Character => Ui.Icons.User,
        Appearance => Ui.Icons.Palette,
        Control => Ui.Icons.Gamepad,
        Territory => Ui.Icons.Map,
        Privacy => Ui.Icons.Shield,
        _ => Ui.Icons.Sliders,
    };

    private static string NameOf(string s) => s switch
    {
        Character => T("Character"),
        Appearance => T("Appearance"),
        Control => T("Control"),
        Territory => T("Territory"),
        Privacy => T("Privacy"),
        _ => T("System"),
    };

    // ------------------------------------------------------------------ sections

    private void BuildContent()
    {
        _content.Children.Clear();
        _liveTexts.Clear();
        _previews.Clear();
        var title = Ui.Text(NameOf(_current), 22, weight: FontWeights.SemiBold);
        _content.Children.Add(title);
        switch (_current)
        {
            case Character: BuildCharacter(); break;
            case Appearance: BuildAppearance(); break;
            case Control: BuildControl(); break;
            case Territory: BuildTerritory(); break;
            case Privacy: BuildPrivacy(); break;
            default: BuildSystem(); break;
        }
    }

    private void BuildCharacter()
    {
        var s = _host.Settings;
        var mem = _host.Memory;
        Lead(T("Who lives on your desktop, and what they wear."));

        Section(T("Who"));
        var who = new WrapPanel();
        who.Children.Add(CharacterCard("boy", T("Boy"), T("The original: hood up, hands in pockets.")));
        who.Children.Add(CharacterCard("girl", T("Girl"), T("Hair out of the hood and round glasses.")));
        _content.Children.Add(who);

        Section(T("Hoodie colour"));
        _content.Children.Add(Ui.Text(T("New colours are found over time spent together; locked ones show when they unlock."), 12.5, dim: true));
        var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var c in Progression.Colors)
        {
            var color = c;
            var ok = Progression.ColorAvailable(mem, color);
            var current = mem.Doc.HoodieColor == color;
            var dot = new Ellipse
            {
                Width = 34, Height = 34,
                Fill = new SolidColorBrush(QuickPanel.SwatchColor(color)) { Opacity = ok ? 1 : 0.3 },
                Stroke = Ui.Brush(current ? "Accent" : "BorderBrush"),
                StrokeThickness = current ? 3 : 1,
            };
            var cell = new Grid { Width = 44, Height = 44 };
            cell.Children.Add(dot);
            if (!ok)
            {
                var lock_ = Ui.Icon(Ui.Icons.Lock, 14, "TextDim");
                lock_.HorizontalAlignment = HorizontalAlignment.Center;
                lock_.VerticalAlignment = VerticalAlignment.Center;
                cell.Children.Add(lock_);
            }
            var stack = new StackPanel { Width = 74, Margin = new Thickness(0, 0, 6, 6) };
            stack.Children.Add(cell);
            var name = Ui.Text(QuickPanel.HoodieColorName(color), 11.5, dim: !ok, wrap: TextWrapping.NoWrap);
            name.TextAlignment = TextAlignment.Center;
            name.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(name);
            var tip = ok ? QuickPanel.HoodieColorName(color) : UnlockHint(color);
            var b = Ui.Button(stack, () =>
            {
                if (!ok) return;
                _host.SetHoodieColor(color);
                Build();
            }, "GhostButton", tip);
            b.Padding = new Thickness(4);
            swatches.Children.Add(b);
        }
        _content.Children.Add(swatches);

        Section(T("Body"));
        SliderRow(T("Size"), s.Scale, 0.5, 2.0, v => { s.Scale = v; _host.SettingsChanged(); }, v => $"{v * 100:0}%");
        SliderRow(T("Walking speed"), s.WalkSpeed, 0.4, 2.5, v => { s.WalkSpeed = v; _host.SettingsChanged(); }, v => $"{v:0.0}×");

        Section(T("Temperament"));
        Check(T("Autonomous behavior (wanders, rests and explores on its own)"), s.AutonomousBehavior, v => s.AutonomousBehavior = v);
        var modes = Enum.GetValues<PresenceMode>().Where(m => m != PresenceMode.Alone).ToArray();
        Combo(T("Default presence mode"), modes.Select(QuickPanel.ModeName).ToArray(), Array.IndexOf(modes, s.DefaultPresenceMode),
            i => s.DefaultPresenceMode = modes[Math.Max(0, i)]);
        Check(T("Remember the last presence mode on start"), s.RestorePresenceOnStart, v => s.RestorePresenceOnStart = v);
        Check(T("React to the cursor"), s.CursorReactions, v => s.CursorReactions = v);
        Check(T("React to heavy PC load (rarely, with long cooldowns)"), s.PcStatusReactions, v => s.PcStatusReactions = v);
        Check(T("Hoodie may climb onto windows and desktop icons"), s.ClimbOnWindows, v => s.ClimbOnWindows = v);
        LiveLine(() => T("Character: ") + _host.Pet.Mind.Traits);
    }

    private string UnlockHint(string color)
    {
        var u = Progression.Schedule.FirstOrDefault(x => x.Id == "color-" + color);
        return u is null ? T("Not found yet") : F("Unlocks after {0} h together over {1} day(s).", u.Hours, u.Days);
    }

    /// <summary>A live preview card for one character; the chosen one is outlined with the accent.</summary>
    private UIElement CharacterCard(string id, string name, string note)
    {
        var selected = _host.Settings.Character == id;
        const double scale = 0.17;
        var holder = new Canvas { Width = 110, Height = 150, ClipToBounds = true };
        var rig = new CharacterRig();
        rig.SetCharacter(id);
        rig.SetHoodieColor(_host.Memory.Doc.HoodieColor);
        rig.Apply(ProceduralAnimator.Evaluate(AnimClip.IdleBreathing, 0.7, new AnimContext { Time = 0.7 }).Pose);
        var m = Matrix.Identity;
        m.Scale(scale, scale);
        m.Translate(holder.Width / 2 - 272 * scale, 4 - 40 * scale);
        rig.RenderTransform = new MatrixTransform(m);
        holder.Children.Add(rig);
        _previews.Add(rig);

        var stack = new StackPanel { Width = 150 };
        stack.Children.Add(holder);
        var title = Ui.Text(name, 14, weight: FontWeights.SemiBold, wrap: TextWrapping.NoWrap);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(title);
        var sub = Ui.Text(note, 11.5, dim: true);
        sub.TextAlignment = TextAlignment.Center;
        sub.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(sub);
        var card = new Border
        {
            Background = Ui.Brush(selected ? "SurfaceHi" : "Surface"),
            BorderBrush = Ui.Brush(selected ? "Accent" : "BorderBrush"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 12),
            Child = stack,
        };
        var b = Ui.Button(card, () => { _host.SetCharacter(id); Build(); }, "GhostButton", name);
        b.Padding = new Thickness(0);
        b.Margin = new Thickness(0, 0, 12, 8);
        return b;
    }

    private void AnimatePreviews()
    {
        if (_previews.Count == 0 || _host.Settings.ReducedMotion) return;
        _previewTime += 1;
        var pose = ProceduralAnimator.Evaluate(AnimClip.IdleBreathing, _previewTime, new AnimContext { Time = _previewTime }).Pose;
        foreach (var r in _previews) r.Apply(pose);
    }

    private void BuildAppearance()
    {
        var s = _host.Settings;
        Lead(T("How Hoodie and its windows look and sound."));
        Section(T("Interface"));
        var langs = new[] { "auto", "en", "ru" };
        Combo(T("Interface language"), new[] { T("Automatic (Windows)"), "English", "Русский" }, Math.Max(0, Array.IndexOf(langs, s.Language)), i =>
        {
            s.Language = langs[Math.Max(0, i)];
            L.Set(s.Language);
            Dispatcher.BeginInvoke(Build);
        });
        Check(T("Always on top"), s.AlwaysOnTop, v => { s.AlwaysOnTop = v; _host.SettingsChanged(); });
        Check(T("Hoodie in the desktop right-click menu"), s.DesktopMenu, v => { s.DesktopMenu = v; _host.ApplyDesktopMenu(); });
        Section(T("Motion and sound"));
        Check(T("Reduced motion (no big jumps, gentle transitions)"), s.ReducedMotion, v => s.ReducedMotion = v);
        Check(T("Sounds (soft, only for items, reminders and timers)"), s.Sounds, v => s.Sounds = v);
    }

    private void BuildControl()
    {
        var s = _host.Settings;
        Lead(T("How you talk to Hoodie: clicks, the ring menu and driving it yourself."));

        Section(T("Click on Hoodie"));
        Check(T("Ring of round buttons around Hoodie (off: the side panel opens instead)"), s.RadialMenu, v => s.RadialMenu = v);
        Check(T("Allow grabbing and throwing"), s.GrabThrow, v => s.GrabThrow = v);

        Section(T("Manual control"));
        _content.Children.Add(Ui.Text(T("Take the wheel: Hoodie stops deciding for itself and walks where you say. Your territory rules still apply."), 12.5, dim: true));
        Check(T("Allow manual control (Ctrl+Alt+P)"), s.ManualControlEnabled, v => { s.ManualControlEnabled = v; if (!v) _host.SetManualControl(false); });
        SliderRow(T("Ends by itself after no keys for"), s.ManualControlAutoExitSeconds, 0, 300, v => { s.ManualControlAutoExitSeconds = (int)Math.Round(v / 10) * 10; _host.SettingsChanged(); },
            v => v < 5 ? T("never") : F("{0} s", (int)Math.Round(v / 10) * 10));
        var keys = Ui.Card(KeyTable(
            ("← →  /  A D", T("walk")),
            ("Shift", T("run")),
            (T("Space"), T("jump")),
            ("↑  /  W", T("climb onto a window top or the screen side")),
            ("↓  /  S", T("hop down")),
            ("E", T("sit / stand up")),
            ("Esc", T("done"))), new Thickness(14, 10, 14, 10));
        keys.Margin = new Thickness(0, 8, 0, 0);
        _content.Children.Add(keys);
        _content.Children.Add(Ui.Text(T("Keys are read only by Hoodie's own small window while it has focus — never from other apps. Clicking elsewhere ends the mode."), 11.5, dim: true));
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(Btn(T("Start driving now"), () => { _host.SetManualControl(true); }, accent: true, enabled: s.ManualControlEnabled));
        _content.Children.Add(row);

        Section(T("Hotkeys"));
        Bullet(T("Ctrl+Alt+H — hide / show Hoodie instantly") + (_host.HotkeyRegistered ? "" : " " + T("(unavailable — use the tray)")));
        Bullet(T("Ctrl+Alt+P — manual control on / off") + (_host.ManualHotkeyRegistered ? "" : " " + T("(unavailable — use the tray)")));
    }

    private static UIElement KeyTable(params (string Key, string What)[] rows)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var key = new Border
            {
                Background = Ui.Brush("SurfaceHi"),
                BorderBrush = Ui.Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 3),
                Margin = new Thickness(0, 2, 12, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = Ui.Text(rows[i].Key, 12, weight: FontWeights.SemiBold, wrap: TextWrapping.NoWrap),
            };
            Grid.SetRow(key, i);
            g.Children.Add(key);
            var what = Ui.Text(rows[i].What, 12.5);
            what.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(what, i);
            Grid.SetColumn(what, 1);
            g.Children.Add(what);
        }
        return g;
    }

    private void BuildTerritory()
    {
        var s = _host.Settings;
        Lead(T("Where Hoodie may go, and how it behaves around specific apps."));
        Section(T("Home"));
        var home = _host.Territory.Data.Home;
        var homeMon = home is null ? null : _host.World.FindById(home.MonitorId);
        _content.Children.Add(Ui.Text(home is null ? T("No Home set — Hoodie rests near the bottom-right of your main monitor.") :
            F("Home: {0}, {1:0}% from the left.", homeMon is null ? T("a monitor that is not connected") : MonitorName(homeMon), home.RelX * 100), 12.5, dim: true));
        var homeButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        homeButtons.Children.Add(Btn(T("Set Home to Hoodie's current spot"), () => { _host.SetHomeHere(); Build(); }));
        homeButtons.Children.Add(Btn(T("Clear Home"), () => { _host.Territory.Data.Home = null; _host.Territory.NotifyChanged(); Build(); }));
        _content.Children.Add(homeButtons);

        Section(T("Per-monitor rules"));
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

    private void BuildPrivacy()
    {
        var s = _host.Settings;
        Lead(T("What Hoodie notices, what it remembers, and what it never touches."));
        Sub(T("What Hoodie notices, and why"));
        Bullet(T("How long since you last used the mouse or keyboard: to know whether you are here, away or back."));
        Bullet(T("That keys are being pressed, never which ones: to stay quiet and out of the way while you type."));
        Bullet(T("The name of the app in front (like \"chrome\") and where its window is: to walk on windows, keep off your work and notice new apps."));
        Bullet(T("CPU, GPU and network load: to notice when the PC is working hard."));
        Bullet(T("The time of day and your monitor layout: to be calmer at night and to know where it can walk."));
        Bullet(T("In manual control: the arrow / WASD keys you press in Hoodie's own small window, used for that moment only and never stored."));
        Sub(T("What Hoodie remembers (on this PC only)"));
        Bullet(T("Time spent together, which days, and at what hours you are usually around."));
        Bullet(T("App names it has seen and roughly how long each was in front."));
        Bullet(T("Its favourite spots, the things it found, and memorable moments (like the first time you threw it)."));
        Bullet(T("How often things happened, so it gets used to them."));
        Sub(T("What Hoodie never stores or reads"));
        Bullet(T("The text you type, passwords, messages, documents, window titles, web pages or clipboard."));
        Bullet(T("Keys pressed in other apps: Hoodie only ever sees its own two hotkeys (Ctrl+Alt+H, Ctrl+Alt+P)."));
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

    private void BuildSystem()
    {
        var s = _host.Settings;
        var pet = _host.Pet;
        Lead(T("Startup, data, and what Hoodie costs your PC."));
        Section(T("Startup"));
        Check(T("Start with Windows"), StartupService.IsEnabled(), v => { s.StartWithWindows = v; StartupService.Set(v); });

        Section(T("Data"));
        _content.Children.Add(Ui.Text(T("Data location: ") + _host.Storage.Root, 12, dim: true));
        var row0 = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        row0.Children.Add(Btn(T("Open data folder"), () => _host.OpenDataFolder()));
        _content.Children.Add(row0);

        Section(T("Debug"));
        Sub(T("What Hoodie is thinking"));
        LiveLine(() => F("Doing: {0}", QuickPanel.Describe(pet.State, pet.Mode, pet.ActivityName)));
        LiveLine(() => pet.Mind.CurrentIntent is { } i ? F("Last decision: {0} — {1}", i, pet.Mind.CurrentReason ?? "") : T("No decision yet."));
        LiveLine(() => F("Noticing: {0}{1}{2}", pet.Perception.ForegroundProcess ?? "—",
            pet.Perception.Typing ? " · " + T("typing") : "", pet.Perception.PcHot ? " · " + T("PC is hot") : ""));
        LiveLine(() => pet.Mind.ToString());

        Sub(T("Hoodie's own footprint"));
        LiveLine(() => _host.Performance.Latest is { } l ? T("Now: ") + PerformanceWatch.Describe(l) : T("Measuring…"));
        LiveLine(() => _host.Performance.Latest is null ? "" : T("Peak: ") + PerformanceWatch.Describe(_host.Performance.Peak));
        LiveLine(() => _host.Performance.First is { } f ? F("Running for {0:0.0} h", (DateTime.Now - f.At).TotalHours) : "");
        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(Btn(T("Measure now"), () => _host.Performance.Take()));
        row.Children.Add(Btn(T("Open log"), () => _host.OpenDataFolder()));
        _content.Children.Add(row);
    }

    // ------------------------------------------------------------------ helpers

    private static string MonitorName(HoodieCompanion.Geometry.MonitorInfo m) =>
        F("Monitor {0}", m.Index + 1) + (m.IsPrimary ? " (" + T("main") + ")" : "");

    private static string AppModeName(AppPresenceMode m) => m switch
    {
        AppPresenceMode.Quiet => T("Quiet"),
        AppPresenceMode.Avoid => T("Avoid"),
        AppPresenceMode.Hide => T("Hide"),
        _ => T("Normal"),
    };

    private void Lead(string text)
    {
        var t = Ui.Text(text, 12.5, dim: true);
        t.Margin = new Thickness(0, 2, 0, 0);
        _content.Children.Add(t);
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
        v.MinWidth = 48;
        v.TextAlignment = TextAlignment.Right;
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
