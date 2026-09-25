using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Features.Backpack;
using HoodieCompanion.Features.Notes;
using HoodieCompanion.Features.Reminders;
using HoodieCompanion.Features.SystemMonitor;
using HoodieCompanion.Features.Timers;
using HoodieCompanion.Platform;
using HoodieCompanion.Settings;
using static HoodieCompanion.UI.L;

namespace HoodieCompanion.UI;

public enum PanelPage
{
    Home,
    Backpack,
    Notes,
    Reminder,
    Timer,
    PcStatus,
    Commands,
}

/// <summary>
/// Layer 2: the compact panel that opens beside Hoodie (left click). Pages are Layer 3 context cards.
/// Each page is built once when shown; only small "live" parts (countdowns, meters) refresh every second,
/// so text boxes keep what the user typed.
/// </summary>
public sealed class QuickPanel : Window
{
    public const double PanelWidth = 360;
    public const double PanelHeight = 540;
    private const double ChromeMargin = 14;

    private readonly AppHost _host;
    private readonly Border _chrome;
    private readonly ContentControl _body = new();
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer _tick;
    private readonly List<(ContentControl Host, Func<UIElement> Build)> _live = new();
    private bool _closing;
    private PanelPage _page = PanelPage.Home;
    private string _search = "";
    private int _sideSign = -1;

    public QuickPanel(AppHost host)
    {
        _host = host;
        Title = "Hoodie";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = PanelWidth + ChromeMargin * 2;
        Height = PanelHeight + ChromeMargin * 2;
        FontFamily = Ui.Font;
        UseLayoutRounding = true;
        AllowDrop = true;

        _chrome = Ui.Chrome(_body);
        _chrome.RenderTransform = _slide;
        Content = _chrome;

        SourceInitialized += (_, _) => WindowInterop.MakeToolWindow(this, noActivate: false);
        // Only the menu-like pages close when you click elsewhere; working pages (Backpack, Notes...)
        // stay open so you can drag files in from Explorer or type.
        Deactivated += (_, _) =>
        {
            if (!SuppressAutoClose && _page is PanelPage.Home or PanelPage.Commands) Close(animated: true);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_page == PanelPage.Notes && _noteEdit is not null) LeaveEditor();
                else if (_page != PanelPage.Home) Show(PanelPage.Home);
                else Close(animated: true);
                e.Handled = true;
            }
        };

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        Drop += (_, e) =>
        {
            e.Handled = true;
            var items = DropHandler.Extract(e.Data);
            if (items.Count == 0) return;
            _host.GiveItems(items);
            if (_page != PanelPage.Backpack) Show(PanelPage.Backpack);
        };

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => RefreshLive();

        _host.Inventory.Changed += () => { if (IsVisible && _page == PanelPage.Backpack) RebuildBackpackGrid(); };
        _host.Notes.Changed += () => { if (IsVisible && _page == PanelPage.Notes && _noteEdit is null) _noteBoardFill?.Invoke(); };
        _host.Reminders.Changed += () => { if (IsVisible && _page == PanelPage.Reminder) RefreshLive(); };
        _host.Timers.Changed += () => { if (IsVisible) RefreshLive(); };
        _host.SystemStatusUpdated += _ => { if (IsVisible && _page == PanelPage.PcStatus) RefreshLive(); };
        L.Changed += () => { if (IsVisible) Rebuild(); };
    }

    public bool SuppressAutoClose { get; set; }
    public PanelPage Page => _page;
    public bool IsOpen => IsVisible && !_closing;

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ItemDragFormat)) return;
        e.Effects = DropHandler.CanAccept(e.Data) ? DragDropEffects.Copy | DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    // ------------------------------------------------------------------ open / close / placement

    public void Open(PanelPage page)
    {
        _page = page;
        Rebuild();
        _closing = false;
        if (!IsVisible)
        {
            _chrome.Opacity = 0;
            Show();
        }
        PlaceBesideHoodie();
        Activate();
        Animate(open: true);
        _tick.Start();
        _host.Pet.SetPanelActivity(ActivityFor(page));
    }

    public void Show(PanelPage page)
    {
        if (page != PanelPage.Notes && _noteEdit is not null) LeaveEditor(showBoard: false);
        _page = page;
        Rebuild();
        var fade = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(140));
        _body.BeginAnimation(OpacityProperty, fade);
        _host.Pet.SetPanelActivity(ActivityFor(page));
    }

    private static PanelActivity ActivityFor(PanelPage page) => page switch
    {
        PanelPage.Backpack => PanelActivity.Backpack,
        PanelPage.Notes or PanelPage.Reminder => PanelActivity.Notes,
        PanelPage.PcStatus => PanelActivity.Laptop,
        _ => PanelActivity.None,
    };

    public void Close(bool animated)
    {
        if (!IsVisible || _closing) return;
        _closing = true;
        _tick.Stop();
        if (_noteEdit is not null) LeaveEditor(showBoard: false);
        _host.Pet.SetPanelActivity(PanelActivity.None);
        if (!animated || _host.Settings.ReducedMotion)
        {
            Hide();
            _closing = false;
            return;
        }
        Animate(open: false);
    }

    private void Animate(bool open)
    {
        var reduced = _host.Settings.ReducedMotion;
        var dur = TimeSpan.FromMilliseconds(open ? 190 : 150);
        var ease = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
        var fade = new DoubleAnimation(open ? 0 : 1, open ? 1 : 0, dur) { EasingFunction = ease };
        if (!open)
        {
            fade.Completed += (_, _) =>
            {
                if (_closing)
                {
                    Hide();
                    _closing = false;
                }
            };
        }
        _chrome.BeginAnimation(OpacityProperty, fade);
        if (!reduced)
        {
            var from = _sideSign * 14;
            _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(open ? from : 0, open ? 0 : from, dur) { EasingFunction = ease });
        }
    }

    /// <summary>Places the panel next to Hoodie's body, on the side with room, bottom near the floor.</summary>
    public void PlaceBesideHoodie()
    {
        var pet = _host.PetBoundsPx;
        var mon = _host.World.NearestMonitor(pet.Center);
        var s = mon.Scale;
        var w = Width * s;
        var h = Height * s;
        var wa = mon.WorkArea;
        var gap = 2 * s;
        double x;
        if (pet.Right + w + gap <= wa.Right) { x = pet.Right + gap - ChromeMargin * s * 0.6; _sideSign = -1; }
        else { x = pet.Left - w - gap + ChromeMargin * s * 0.6; _sideSign = 1; }
        var y = pet.Bottom - h + ChromeMargin * s;
        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - w));
        y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - h));
        WindowInterop.PlaceWindowPx(this, x, y, s);
    }

    // ------------------------------------------------------------------ building

    private void Rebuild()
    {
        _live.Clear();
        _liveTexts.Clear();
        _body.Content = _page switch
        {
            PanelPage.Backpack => BackpackPage(),
            PanelPage.Notes => NotesPage(),
            PanelPage.Reminder => ReminderPage(),
            PanelPage.Timer => TimerPage(),
            PanelPage.PcStatus => PcStatusPage(),
            PanelPage.Commands => CommandsPage(),
            _ => HomePage(),
        };
    }

    /// <summary>A part of the page that is rebuilt every second (never contains text boxes).</summary>
    private ContentControl Live(Func<UIElement> build)
    {
        var host = new ContentControl { Content = build() };
        _live.Add((host, build));
        return host;
    }

    private void RefreshLive()
    {
        if (!IsVisible) return;
        // Never rebuild a part while the user is pressing a button in it: the click would be lost
        // (the button under the finger would be replaced between mouse-down and mouse-up).
        var pressing = Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed;
        foreach (var (host, build) in _live)
        {
            if (pressing && host.IsMouseOver) continue;
            host.Content = build();
        }
        foreach (var (tb, text) in _liveTexts)
        {
            var t = text();
            if (tb.Text != t) tb.Text = t;
        }
    }

    private readonly List<(TextBlock Block, Func<string> Text)> _liveTexts = new();

    /// <summary>A text that refreshes every second without rebuilding the controls around it.</summary>
    private TextBlock LiveText(TextBlock tb, Func<string> text)
    {
        tb.Text = text();
        _liveTexts.Add((tb, text));
        return tb;
    }

    private UIElement Frame(string title, UIElement content, bool back = true, UIElement? footer = null, Action? onBack = null)
    {
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };
        var close = Ui.Button(Ui.Icon(Ui.Icons.Close, 14, "TextDim"), () => Close(true), "GhostButton", T("Close (Esc)"));
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        if (back)
        {
            var b = Ui.Button(Ui.Icon(Ui.Icons.Back, 14, "TextDim"), onBack ?? (() => Show(PanelPage.Home)), "GhostButton", T("Back"));
            b.Margin = new Thickness(-6, 0, 4, 0);
            DockPanel.SetDock(b, Dock.Left);
            header.Children.Add(b);
        }
        var t = Ui.Title(title);
        t.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(t);

        var root = new DockPanel { Margin = new Thickness(16, 12, 16, 14) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        if (footer is not null)
        {
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
        }
        root.Children.Add(content);
        return root;
    }

    public static string Describe(BehaviorState s, PresenceMode mode, string? activity) => s switch
    {
        BehaviorState.Sleeping => T("asleep"),
        BehaviorState.Sitting => T("resting"),
        BehaviorState.Walking => T("wandering about"),
        BehaviorState.Grabbed => T("being held"),
        BehaviorState.Airborne => T("flying!"),
        BehaviorState.Climbing => T("climbing"),
        BehaviorState.Activity => activity switch
        {
            "laptop" => T("working on its laptop"),
            "read" => T("reading a book"),
            "notes" => T("writing in its notebook"),
            "backpack" => T("showing you its backpack"),
            _ => T("busy"),
        },
        BehaviorState.ReceivingItem => T("putting something away"),
        BehaviorState.Hidden => T("away"),
        _ => mode == PresenceMode.Focus ? T("doing its own work") : T("hanging around"),
    };

    public static string ModeName(PresenceMode m) => m switch
    {
        PresenceMode.Company => T("Company"),
        PresenceMode.Play => T("Play"),
        PresenceMode.Focus => T("Focus"),
        PresenceMode.Quiet => T("Quiet"),
        PresenceMode.Alone => T("Alone"),
        _ => T("Normal"),
    };

    // ------------------------------------------------------------------ Home

    private UIElement HomePage()
    {
        var sub = Live(() =>
        {
            var pet = _host.Pet;
            return Ui.Text($"{ModeName(pet.Mode)} · {Describe(pet.State, pet.Mode, pet.ActivityName)}" +
                           (_host.Territory.Anchor is null ? "" : " · " + T("staying here")), 12, dim: true);
        });

        // Built once; only the small values under the titles refresh (rebuilding the buttons every second
        // made clicks get lost when they landed on a rebuild).
        var tiles = Ui.Grid(3,
            Tile(Ui.Icons.Backpack, T("Backpack"), () => _host.Inventory.Items.Count == 0 ? T("empty") : F("{0} items", _host.Inventory.Items.Count), () => Show(PanelPage.Backpack)),
            Tile(Ui.Icons.Note, T("Notes"), () => _host.Notes.OpenCount == 0 ? "—" : F("{0} open", _host.Notes.OpenCount), () => Show(PanelPage.Notes)),
            Tile(Ui.Icons.Bell, T("Reminder"), () => _host.Reminders.Pending().FirstOrDefault() is { } r ? r.DueAt.ToString("HH:mm") : "—", () => Show(PanelPage.Reminder)),
            Tile(Ui.Icons.Timer, T("Timer"), () => _host.Timers.Active.FirstOrDefault() is { } timer ? TimerService.FormatRemaining(timer.Remaining(DateTime.Now)) : "—", () => Show(PanelPage.Timer)),
            Tile(Ui.Icons.Pc, T("PC Status"), () => _host.LatestStatus is { } status ? $"CPU {status.CpuUsage:0}%" : "…", () => Show(PanelPage.PcStatus)),
            Tile(Ui.Icons.Gear, T("Settings"), () => "", () => { Close(false); _host.ShowSettings(); }));

        var chips = new WrapPanel();
        foreach (var (mode, tip) in new[]
                 {
                     (PresenceMode.Normal, "Balanced: lives its own life"),
                     (PresenceMode.Company, "Stays near you, quietly"),
                     (PresenceMode.Play, "Playful: chases your cursor"),
                     (PresenceMode.Focus, "You're working: Hoodie goes home and keeps busy quietly"),
                     (PresenceMode.Quiet, "Visible but calm"),
                     (PresenceMode.Alone, "Leaves the screen until you call it back"),
                 })
        {
            var m = mode;
            chips.Children.Add(Ui.Chip(ModeName(mode), _host.Pet.Mode == mode, "presence", () =>
            {
                if (_host.Pet.Mode != m) _host.SetMode(m);
                if (m == PresenceMode.Alone) Close(true);
            }, T(tip)));
        }

        var stayLabel = LiveText(Ui.Text("", 13, wrap: TextWrapping.NoWrap), () => _host.Territory.Anchor is not null ? T("You're free") : T("Stay here"));
        var stay = Ui.Button(stayLabel, () => _host.Command(_host.Territory.Anchor is not null ? PetCommand.YoureFree : PetCommand.StayHere));
        stay.Margin = new Thickness(0, 0, 6, 6);
        var cmds = Ui.Grid(2,
            stay,
            Cmd(T("Go home"), () => _host.Command(PetCommand.GoHome)),
            Cmd(T("Set this as Home"), () => _host.SetHomeHere()),
            Cmd(T("Leave me alone"), () => { _host.Command(PetCommand.LeaveMeAlone); Close(true); }));

        var content = Ui.Stack(Orientation.Vertical, 10, sub, tiles, Ui.Caption(T("Presence")), chips, Ui.Caption(T("Ask Hoodie")), cmds);
        return Frame("Hoodie", Ui.Scroll(content), back: false);
    }

    private Button Tile(string icon, string label, Func<string> value, Action click)
    {
        var b = Tile(icon, label, "", click);
        var v = ((StackPanel)b.Content).Children.OfType<TextBlock>().Last();
        LiveText(v, value);
        return b;
    }

    private Button Tile(string icon, string label, string value, Action click)
    {
        var sp = new StackPanel();
        var ic = Ui.Icon(icon, 20);
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(ic);
        var l = Ui.Text(label, 11.5, weight: FontWeights.SemiBold);
        l.Margin = new Thickness(0, 5, 0, 0);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.TextAlignment = TextAlignment.Center;
        l.LineHeight = 13;
        sp.Children.Add(l);
        var v = Ui.Text(value, 11, dim: true, wrap: TextWrapping.NoWrap);
        v.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(v);
        var b = Ui.Button(sp, click, "TileButton");
        b.Margin = new Thickness(0, 0, 6, 6);
        b.Height = 88;
        b.Padding = new Thickness(4, 8, 4, 6);
        return b;
    }

    private static Button Cmd(string label, Action click)
    {
        var b = Ui.Button(label, click);
        b.Margin = new Thickness(0, 0, 6, 6);
        return b;
    }

    // ------------------------------------------------------------------ Backpack (inventory grid)

    private WrapPanel? _grid;
    private string? _renaming;
    private bool _addMenu;
    private bool _linkInput;
    private bool _appSearch;
    private const string ItemDragFormat = "HoodieBackpackItem";
    private Point _slotPress;
    private string? _slotPressId;
    private bool _slotDragged;

    private UIElement BackpackPage()
    {
        _renaming = null;
        _addMenu = false;
        _linkInput = false;
        _appSearch = false;
        var search = Ui.Input(T("Search backpack"));
        search.Text = _search;
        search.TextChanged += (_, _) =>
        {
            _search = search.Text;
            RebuildBackpackGrid();
        };
        var add = Ui.Button(Ui.Icon(Ui.Icons.Plus, 16), () => { _addMenu = !_addMenu; _linkInput = false; RebuildAddRow(); }, null, T("Give Hoodie something"));
        add.Margin = new Thickness(6, 0, 0, 0);
        var top = Ui.Row(Ui.WithPlaceholder(search), add, 0);

        _addRow = new ContentControl();
        _grid = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        RebuildBackpackGrid();
        RebuildAddRow();

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_addRow);
        var dock = new DockPanel();
        DockPanel.SetDock(stack, Dock.Top);
        dock.Children.Add(stack);
        dock.Children.Add(Ui.Scroll(_grid));
        var hint = Ui.Text(T("Drop files, folders, apps or links here or onto Hoodie. Right-click a slot for more."), 11, dim: true);
        hint.Margin = new Thickness(0, 8, 0, 0);
        return Frame(T("Backpack"), dock, footer: hint);
    }

    private ContentControl _addRow = new();

    private void RebuildAddRow()
    {
        if (!_addMenu)
        {
            _addRow.Content = null;
            return;
        }
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        sp.Children.Add(Ui.Grid(4, Cmd(T("File…"), AddFiles), Cmd(T("Folder…"), AddFolder),
            Cmd(T("App…"), () => { _appSearch = true; _linkInput = false; RebuildAddRow(); }),
            Cmd(T("Link…"), () => { _linkInput = true; _appSearch = false; RebuildAddRow(); })));
        if (_appSearch)
        {
            var q = Ui.Input(T("Find an app, e.g. Calculator"));
            var results = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            void Fill()
            {
                results.Children.Clear();
                if (!_host.Apps.IsLoaded)
                {
                    results.Children.Add(Ui.Text(T("Looking through your apps…"), 12, dim: true));
                    return;
                }
                var found = _host.Apps.Search(q.Text, 8);
                if (found.Count == 0) results.Children.Add(Ui.Text(T("No matches."), 12, dim: true));
                foreach (var (name, target) in found)
                {
                    var icon = _host.Icons.Preview(target);
                    FrameworkElement ic = icon is not null ? new Image { Source = icon, Width = 20, Height = 20 } : Ui.Icon(Ui.Icons.Plus, 14, "TextDim");
                    ic.Margin = new Thickness(0, 0, 8, 0);
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(ic);
                    row.Children.Add(Ui.Text(name, 12.5));
                    var b = new Button { Content = row, Style = Ui.Style("GhostButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 4, 6, 4), ToolTip = T("Put it in the backpack") };
                    var t = target;
                    b.Click += (_, _) =>
                    {
                        _host.GiveItems(new[] { t });
                        _appSearch = false;
                        _addMenu = false;
                        RebuildAddRow();
                    };
                    results.Children.Add(b);
                }
            }
            q.TextChanged += (_, _) => Fill();
            q.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                var first = _host.Apps.Search(q.Text, 1);
                if (first.Count == 0) return;
                _host.GiveItems(new[] { first[0].Target });
                _appSearch = false;
                _addMenu = false;
                RebuildAddRow();
                e.Handled = true;
            };
            sp.Children.Add(Ui.WithPlaceholder(q));
            sp.Children.Add(results);
            Fill();
            if (!_host.Apps.IsLoaded) _host.Apps.LoadAsync().ContinueWith(_ => Dispatcher.BeginInvoke(() => { if (_appSearch) Fill(); }));
            Dispatcher.BeginInvoke(() => q.Focus(), DispatcherPriority.Input);
        }
        if (_linkInput)
        {
            var link = Ui.Input("https://…", url => AddLink(url));
            var go = Ui.Button(T("Add"), () => AddLink(link.Text), "AccentButton");
            sp.Children.Add(Ui.Row(Ui.WithPlaceholder(link), go));
            Dispatcher.BeginInvoke(() => link.Focus(), DispatcherPriority.Input);
        }
        _addRow.Content = sp;
    }

    private const double SlotW = 76, SlotH = 88;

    private void RebuildBackpackGrid()
    {
        if (_grid is null) return;
        _grid.Children.Clear();
        var items = _host.Inventory.Search(_search).ToList();
        foreach (var item in items) _grid.Children.Add(Slot(item));
        // Always show a few empty slots so it reads as an inventory.
        var filled = items.Count;
        var total = string.IsNullOrEmpty(_search) ? Math.Max(16, (filled / 4 + 1) * 4) : filled;
        for (var i = filled; i < total; i++) _grid.Children.Add(EmptySlot());
        if (items.Count == 0 && !string.IsNullOrEmpty(_search))
            _grid.Children.Add(Ui.Text(T("No matches."), 12.5, dim: true));
    }

    private UIElement EmptySlot()
    {
        var b = new Border
        {
            Width = SlotW - 6,
            Height = SlotH - 6,
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(10),
            BorderBrush = Ui.Brush("BorderBrush"),
            BorderThickness = new Thickness(1.2),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x2B, 0x2F, 0x36)),
            Cursor = Cursors.Hand,
            ToolTip = T("Empty slot — drop something here, or click to add a file"),
        };
        var plus = Ui.Icon(Ui.Icons.Plus, 16, "BorderBrush");
        plus.HorizontalAlignment = HorizontalAlignment.Center;
        plus.VerticalAlignment = VerticalAlignment.Center;
        plus.Opacity = 0.6;
        b.Child = plus;
        b.MouseEnter += (_, _) => b.BorderBrush = Ui.Brush("Accent");
        b.MouseLeave += (_, _) => b.BorderBrush = Ui.Brush("BorderBrush");
        b.MouseLeftButtonUp += (_, _) => AddFiles();
        AcceptReorder(b, () => int.MaxValue);
        return b;
    }

    private UIElement Slot(InventoryItem item)
    {
        var exists = _host.Inventory.Exists(item);
        var icon = _host.Icons.Get(item);
        FrameworkElement iconEl = icon is not null
            ? new Image { Source = icon, Width = 40, Height = 40, Stretch = Stretch.Uniform }
            : Ui.Icon(item.Type switch { InventoryItemType.Folder => Ui.Icons.Folder, InventoryItemType.Url => Ui.Icons.Link, _ => Ui.Icons.Note }, 28, "TextDim");
        iconEl.HorizontalAlignment = HorizontalAlignment.Center;
        iconEl.Margin = new Thickness(0, 7, 0, 3);
        if (!exists) iconEl.Opacity = 0.4;

        var sp = new StackPanel();
        sp.Children.Add(iconEl);
        if (_renaming == item.Id)
        {
            var box = new TextBox { Text = item.DisplayName, FontSize = 11, Padding = new Thickness(2), Margin = new Thickness(4, 0, 4, 0) };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { _host.Inventory.Rename(item.Id, box.Text); _renaming = null; RebuildBackpackGrid(); e.Handled = true; }
                if (e.Key == Key.Escape) { _renaming = null; RebuildBackpackGrid(); e.Handled = true; }
            };
            box.LostKeyboardFocus += (_, _) => { if (_renaming == item.Id) { _host.Inventory.Rename(item.Id, box.Text); _renaming = null; RebuildBackpackGrid(); } };
            sp.Children.Add(box);
            Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
        }
        else
        {
            var name = Ui.Text(item.DisplayName, 11, weight: FontWeights.SemiBold);
            name.TextAlignment = TextAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxHeight = 30;
            name.Margin = new Thickness(4, 0, 4, 0);
            if (!exists) name.Foreground = Ui.Brush("Danger");
            sp.Children.Add(name);
        }

        var grid = new Grid();
        grid.Children.Add(sp);
        if (item.IsPinned)
        {
            var pin = new Ellipse { Width = 8, Height = 8, Fill = Ui.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0) };
            grid.Children.Add(pin);
        }

        var slot = new Button
        {
            Content = grid,
            Style = Ui.Style("TileButton"),
            Padding = new Thickness(0),
            Width = SlotW - 6,
            Height = SlotH - 6,
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = item.DisplayName + "\n" + item.Target + (exists ? "" : "\n" + T("(missing)")),
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        if (item.IsPinned) slot.BorderBrush = Ui.Brush("Accent");
        slot.Click += (_, _) =>
        {
            if (_slotDragged) { _slotDragged = false; return; }
            if (_renaming is null) _host.OpenItem(item);
        };
        slot.ContextMenu = SlotMenu(item);
        // Drag a slot onto another one to arrange the backpack yourself.
        slot.PreviewMouseLeftButtonDown += (_, e) => { _slotPress = e.GetPosition(this); _slotPressId = item.Id; _slotDragged = false; };
        slot.PreviewMouseMove += (_, e) =>
        {
            if (_slotPressId != item.Id || e.LeftButton != MouseButtonState.Pressed || _renaming is not null) return;
            var d = e.GetPosition(this) - _slotPress;
            if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
            _slotPressId = null;
            _slotDragged = true;
            slot.Opacity = 0.45;
            try { DragDrop.DoDragDrop(slot, new DataObject(ItemDragFormat, item.Id), DragDropEffects.Move); }
            finally { slot.Opacity = 1; }
        };
        AcceptReorder(slot, () => _host.Inventory.Ordered().ToList().FindIndex(i => i.Id == item.Id));
        return slot;
    }

    private void AcceptReorder(UIElement target, Func<int> index)
    {
        target.AllowDrop = true;
        target.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(ItemDragFormat)) return;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        target.Drop += (_, e) =>
        {
            if (e.Data.GetData(ItemDragFormat) is not string id) return;
            e.Handled = true;
            var i = index();
            _host.Inventory.Move(id, i == int.MaxValue ? _host.Inventory.Items.Count : i);
        };
    }

    private ContextMenu SlotMenu(InventoryItem item)
    {
        var menu = new ContextMenu();
        void Add(string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => a();
            menu.Items.Add(mi);
        }
        Add(T("Open"), () => _host.OpenItem(item));
        Add(item.IsPinned ? T("Unpin") : T("Pin to the front"), () => _host.Inventory.TogglePin(item.Id));
        Add(T("Rename"), () => { _renaming = item.Id; RebuildBackpackGrid(); });
        if (item.Type is not (InventoryItemType.Url or InventoryItemType.ShellItem)) Add(T("Show in folder"), () => ShortcutService.ShowInFolder(item));
        var order = _host.Inventory.Ordered().ToList();
        var idx = order.FindIndex(i => i.Id == item.Id);
        if (idx > 0) Add(T("Move left"), () => _host.Inventory.Move(item.Id, idx - 1));
        if (idx >= 0 && idx < order.Count - 1) Add(T("Move right"), () => _host.Inventory.Move(item.Id, idx + 1));
        menu.Items.Add(new Separator());
        Add(T("Remove from Backpack"), () => _host.RemoveItem(item));
        menu.Opened += (_, _) => SuppressAutoClose = true;
        menu.Closed += (_, _) => SuppressAutoClose = false;
        return menu;
    }

    private void AddFiles()
    {
        SuppressAutoClose = true;
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = T("Give Hoodie files or apps") };
            if (dlg.ShowDialog(this) == true) _host.GiveItems(dlg.FileNames);
        }
        finally
        {
            SuppressAutoClose = false;
            Activate();
        }
        _addMenu = false;
        RebuildAddRow();
    }

    private void AddFolder()
    {
        SuppressAutoClose = true;
        try
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = T("Give Hoodie a folder") };
            if (dlg.ShowDialog(this) == true) _host.GiveItems(new[] { dlg.FolderName });
        }
        finally
        {
            SuppressAutoClose = false;
            Activate();
        }
        _addMenu = false;
        RebuildAddRow();
    }

    private void AddLink(string url)
    {
        url = url.Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        if (!InventoryService.LooksLikeUrl(url)) return;
        _host.GiveItems(new[] { url });
        _addMenu = false;
        _linkInput = false;
        RebuildAddRow();
    }

    // ------------------------------------------------------------------ Notes

    // Notes work like a small Sticky Notes board: colourful cards laid out like an inventory; a card opens
    // an editor; notes can be pinned to the desktop. Hoodie writes while you type, thinks when you pause,
    // finishes neatly when you keep a note and tears the page out when you throw one away.
    private string? _noteEdit;          // note id, "new" for a draft, null for the board
    private string _noteSearch = "";
    private string _noteColor = NoteColors.Default;
    private TextBox? _noteBox;
    private TextBox? _noteLabel;

    private UIElement NotesPage() => _noteEdit is null ? NotesBoard() : NoteEditor();

    private UIElement NotesBoard()
    {
        var search = Ui.Input(T("Search notes"));
        search.Text = _noteSearch;
        var add = Ui.Button(Ui.Row(Ui.Icon(Ui.Icons.Plus, 14, "AccentText"), Ui.Text(T("New note"), 12.5, weight: FontWeights.SemiBold), 4),
            () => OpenNote("new"), "AccentButton", T("Write a new note"));
        add.Padding = new Thickness(10, 5, 10, 5);
        var board = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Fill()
        {
            board.Children.Clear();
            var notes = _host.Notes.Search(_noteSearch).ToList();
            foreach (var n in notes) board.Children.Add(NoteCard(n));
            if (notes.Count == 0)
            {
                var hint = Ui.Text(string.IsNullOrEmpty(_noteSearch)
                    ? T("Hoodie writes small things down for you: an idea, a number, a to-do. Pin a note to keep it on the desktop.")
                    : T("No matches."), 12.5, dim: true);
                hint.Width = PanelWidth - 40;
                board.Children.Add(hint);
            }
        }
        search.TextChanged += (_, _) => { _noteSearch = search.Text; Fill(); };
        _noteBoardFill = Fill;
        Fill();
        var dock = new DockPanel();
        var top = Ui.Row(Ui.WithPlaceholder(search), add);
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(Ui.Scroll(board));
        return Frame(T("Notes"), dock);
    }

    private Action? _noteBoardFill;

    private UIElement NoteCard(Note n)
    {
        var (back, header, text) = NoteColors.Palette(n.Color);
        var fg = Hex(text);
        var body = new TextBlock
        {
            Text = n.Text,
            FontSize = 12.5,
            FontFamily = Ui.Font,
            Foreground = fg,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(9, 6, 9, 4),
            MaxHeight = 68,
        };
        if (n.IsCompleted) { body.TextDecorations = TextDecorations.Strikethrough; body.Opacity = 0.6; }
        var foot = new DockPanel { Margin = new Thickness(9, 0, 7, 5), LastChildFill = false };
        if (n.Label is { } label)
        {
            var chip = new Border { Background = Hex(header), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock { Text = label, FontSize = 10.5, Foreground = fg, FontFamily = Ui.Font } };
            foot.Children.Add(chip);
        }
        if (n.IsPinned)
        {
            var pin = Ui.Icon(Ui.Icons.Pin, 12);
            pin.Stroke = fg;
            DockPanel.SetDock(pin, Dock.Right);
            foot.Children.Add(pin);
        }
        var stack = new DockPanel();
        var strip = new Border { Height = 7, Background = Hex(header), CornerRadius = new CornerRadius(6, 6, 0, 0) };
        DockPanel.SetDock(strip, Dock.Top);
        stack.Children.Add(strip);
        DockPanel.SetDock(foot, Dock.Bottom);
        stack.Children.Add(foot);
        stack.Children.Add(body);
        var card = new Border
        {
            Width = (PanelWidth - 40) / 2 - 6,
            Height = 116,
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(6),
            Background = Hex(back),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Child = stack,
            Cursor = Cursors.Hand,
            ToolTip = n.Text.Length > 300 ? n.Text[..300] + "…" : n.Text,
        };
        var id = n.Id;
        card.MouseLeftButtonUp += (_, _) => OpenNote(id);
        var menu = new ContextMenu();
        void Item(string t, Action a)
        {
            var mi = new MenuItem { Header = t };
            mi.Click += (_, _) => a();
            menu.Items.Add(mi);
        }
        Item(T("Open"), () => OpenNote(id));
        Item(n.IsPinned ? T("Unpin from the desktop") : T("Pin to the desktop"), () => _host.Notes.SetPinned(id, !n.IsPinned));
        Item(n.IsCompleted ? T("Mark as not done") : T("Mark as done"), () => _host.Notes.Toggle(id));
        var colors = new MenuItem { Header = T("Colour") };
        foreach (var c in NoteColors.All)
        {
            var mi = new MenuItem { Header = ColorName(c), Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Hex(NoteColors.Palette(c).Back), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) } };
            var cc = c;
            mi.Click += (_, _) => _host.Notes.SetColor(id, cc);
            colors.Items.Add(mi);
        }
        menu.Items.Add(colors);
        menu.Items.Add(new Separator());
        Item(T("Throw away"), () => { _host.Notes.Delete(id); _host.Pet.NoteFinished(saved: false); });
        menu.Opened += (_, _) => SuppressAutoClose = true;
        menu.Closed += (_, _) => SuppressAutoClose = false;
        card.ContextMenu = menu;
        return card;
    }

    /// <summary>QA helper: opens the editor for a new note with some text.</summary>
    internal void QaNewNote(string text, string color)
    {
        OpenNote("new");
        _noteColor = color;
        Show(PanelPage.Notes);
        if (_noteBox is not null) _noteBox.Text = text;
    }

    /// <summary>QA helper: presses Keep in the editor. Returns the saved note id.</summary>
    internal string? QaKeepNote()
    {
        var n = SaveNote();
        _host.Pet.NoteFinished(saved: n is not null);
        CloseEditor();
        return n?.Id;
    }

    private static string ColorName(string c) => c switch
    {
        "green" => T("Green"),
        "pink" => T("Pink"),
        "purple" => T("Purple"),
        "blue" => T("Blue"),
        "gray" => T("Gray"),
        "charcoal" => T("Charcoal"),
        _ => T("Yellow"),
    };

    private static SolidColorBrush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private void OpenNote(string id)
    {
        _noteEdit = id;
        _noteColor = id == "new" ? NoteColors.Default : _host.Notes.Find(id)?.Color ?? NoteColors.Default;
        Show(PanelPage.Notes);
    }

    private UIElement NoteEditor()
    {
        var existing = _noteEdit is { } eid && eid != "new" ? _host.Notes.Find(eid) : null;
        if (_noteEdit != "new" && existing is null) _noteEdit = "new";
        var (back, _, text) = NoteColors.Palette(_noteColor);

        var box = new TextBox
        {
            Text = existing?.Text ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 14,
            Padding = new Thickness(10),
            Background = Hex(back),
            Foreground = Hex(text),
            CaretBrush = Hex(text),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        box.TextChanged += (_, _) => _host.Pet.NoteTyping();
        _noteBox = box;

        var colors = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in NoteColors.All)
        {
            var cc = c;
            var dot = new Border
            {
                Width = 20, Height = 20, Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(10),
                Background = Hex(NoteColors.Palette(c).Back),
                BorderBrush = c == _noteColor ? Ui.Brush("Accent") : new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)),
                BorderThickness = new Thickness(c == _noteColor ? 2.5 : 1),
                Cursor = Cursors.Hand,
                ToolTip = ColorName(c),
            };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                _noteColor = cc;
                var (b2, _, t2) = NoteColors.Palette(cc);
                box.Background = Hex(b2);
                box.Foreground = Hex(t2);
                box.CaretBrush = Hex(t2);
                foreach (Border d in colors.Children) { d.BorderThickness = new Thickness(1); d.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)); }
                dot.BorderThickness = new Thickness(2.5);
                dot.BorderBrush = Ui.Brush("Accent");
            };
            colors.Children.Add(dot);
        }

        var label = Ui.Input(T("Label (optional), e.g. work"));
        label.Text = existing?.Label ?? "";
        label.Margin = new Thickness(0, 8, 0, 8);
        _noteLabel = label;

        var keep = Ui.Button(T("Keep"), () => FinishNote(save: true), "AccentButton", T("Hoodie writes it down"));
        var pin = Ui.Button(Ui.Row(Ui.Icon(Ui.Icons.Pin, 13), Ui.Text(existing?.IsPinned == true ? T("Unpin") : T("Pin to desktop"), 12), 4), () =>
        {
            var n = SaveNote();
            if (n is null) return;
            _host.Notes.SetPinned(n.Id, !n.IsPinned);
            _host.Pet.NoteFinished(saved: true);
            CloseEditor();
        }, null, T("Keep this note on top of the desktop"));
        var toss = Ui.Button(Ui.Row(Ui.Icon(Ui.Icons.Trash, 13, "TextDim"), Ui.Text(T("Throw away"), 12, dim: true), 4), () => FinishNote(save: false), "GhostButton",
            T("Hoodie tears the page out"));
        var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        DockPanel.SetDock(keep, Dock.Right);
        buttons.Children.Add(keep);
        DockPanel.SetDock(pin, Dock.Right);
        pin.Margin = new Thickness(0, 0, 6, 0);
        buttons.Children.Add(pin);
        buttons.Children.Add(toss);

        var dock = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(colors);
        top.Children.Add(Ui.WithPlaceholder(label));
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(box);
        Dispatcher.BeginInvoke(() => { box.Focus(); box.CaretIndex = box.Text.Length; }, DispatcherPriority.Input);
        return Frame(existing is null ? T("New note") : T("Note"), dock, onBack: () => LeaveEditor());
    }

    /// <summary>Saves the editor content (creating the note if needed). Empty text removes an existing note.</summary>
    private Note? SaveNote()
    {
        if (_noteBox is null || _noteEdit is null) return null;
        var text = _noteBox.Text;
        var label = _noteLabel?.Text;
        if (_noteEdit == "new")
        {
            var n = _host.Notes.Add(text, _noteColor, label);
            if (n is not null) _noteEdit = n.Id;
            return n;
        }
        var id = _noteEdit;
        _host.Notes.Update(id, text);
        _host.Notes.SetColor(id, _noteColor);
        _host.Notes.SetLabel(id, label);
        return _host.Notes.Find(id);
    }

    private void FinishNote(bool save)
    {
        if (save)
        {
            var n = SaveNote();
            _host.Pet.NoteFinished(saved: n is not null);
        }
        else
        {
            if (_noteEdit is { } id && id != "new") _host.Notes.Delete(id);
            _host.Pet.NoteFinished(saved: false);
        }
        CloseEditor();
    }

    /// <summary>Back / Esc / closing the panel: an existing note is kept; a new draft that was never kept is abandoned.</summary>
    private void LeaveEditor(bool showBoard = true)
    {
        if (_noteEdit is null) return;
        if (_noteEdit == "new")
        {
            if (!string.IsNullOrWhiteSpace(_noteBox?.Text)) _host.Pet.NoteFinished(saved: false);
        }
        else
        {
            SaveNote();
        }
        if (showBoard) CloseEditor();
        else ResetEditor();
    }

    private void ResetEditor()
    {
        _noteEdit = null;
        _noteBox = null;
        _noteLabel = null;
    }

    private void CloseEditor()
    {
        ResetEditor();
        if (_page == PanelPage.Notes && IsVisible) Show(PanelPage.Notes);
    }

    // ------------------------------------------------------------------ Reminders

    private bool _reminderAtTime;

    private UIElement ReminderPage()
    {
        var what = Ui.Input(T("Remind me to…"));
        var minutes = new TextBox { Text = "10", Width = 56, HorizontalContentAlignment = HorizontalAlignment.Center };
        var at = new TextBox { Text = DateTime.Now.AddHours(1).ToString("HH:00"), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Center };
        var error = Ui.Text("", 12);
        error.Foreground = Ui.Brush("Danger");

        var inRow = new WrapPanel();
        inRow.Children.Add(Ui.Chip(T("in"), !_reminderAtTime, "when", () => _reminderAtTime = false));
        inRow.Children.Add(minutes);
        var minLabel = Ui.Text(T("minutes"), 13, dim: true);
        minLabel.VerticalAlignment = VerticalAlignment.Center;
        minLabel.Margin = new Thickness(8, 0, 14, 0);
        inRow.Children.Add(minLabel);
        inRow.Children.Add(Ui.Chip(T("at"), _reminderAtTime, "when", () => _reminderAtTime = true));
        inRow.Children.Add(at);
        minutes.GotKeyboardFocus += (_, _) => { _reminderAtTime = false; SelectChip(inRow, 0); };
        at.GotKeyboardFocus += (_, _) => { _reminderAtTime = true; SelectChip(inRow, 3); };

        void Set()
        {
            var now = DateTime.Now;
            DateTime due;
            error.Text = "";
            if (_reminderAtTime)
            {
                var d = ReminderService.NextOccurrence(at.Text, now);
                if (d is null) { error.Text = T("Use a time like 18:30."); return; }
                due = d.Value;
            }
            else
            {
                if (!TryNumber(minutes.Text, out var m) || m <= 0 || m > 60 * 24 * 7)
                {
                    error.Text = T("Minutes should be a number like 10.");
                    return;
                }
                due = now.AddMinutes(m);
            }
            _host.AddReminder(string.IsNullOrWhiteSpace(what.Text) ? T("Reminder") : what.Text, due);
            what.Text = "";
        }
        what.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Set(); e.Handled = true; } };
        minutes.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Set(); e.Handled = true; } };
        at.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Set(); e.Handled = true; } };

        var list = Live(() =>
        {
            var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var pending = _host.Reminders.Pending().ToList();
            if (pending.Count > 0) sp.Children.Add(Section(T("Hoodie will remember")));
            foreach (var r in pending)
            {
                var id = r.Id;
                var left = new StackPanel();
                left.Children.Add(Ui.Text(r.Text, 13, weight: FontWeights.SemiBold));
                var mins = (r.DueAt - DateTime.Now).TotalMinutes;
                left.Children.Add(Ui.Text(r.DueAt.ToString(r.DueAt.Date == DateTime.Today ? "HH:mm" : "ddd HH:mm") +
                                          (mins > 0 ? " · " + F("in {0}", FormatMinutes(mins)) : " · " + T("now")), 11.5, dim: true));
                var del = Ui.Button(Ui.Icon(Ui.Icons.Trash, 14, "TextDim"), () => _host.Reminders.Delete(id), "GhostButton", T("Cancel reminder"));
                var row = Ui.Row(left, del, 4);
                ((FrameworkElement)row).Margin = new Thickness(0, 0, 0, 6);
                sp.Children.Add(row);
            }
            return sp;
        });

        var content = Ui.Stack(Orientation.Vertical, 10, Ui.WithPlaceholder(what), inRow, Ui.Button(T("Remember this"), Set, "AccentButton"), error, list);
        Dispatcher.BeginInvoke(() => what.Focus(), DispatcherPriority.Input);
        return Frame(T("Reminder"), Ui.Scroll(content));
    }

    private static void SelectChip(Panel row, int index)
    {
        if (row.Children[index] is RadioButton rb) rb.IsChecked = true;
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string FormatMinutes(double m) =>
        m < 60 ? F("{0} min", Math.Ceiling(m)) : F("{0} h {1} min", Math.Floor(m / 60), Math.Ceiling(m % 60));

    private static UIElement Section(string text)
    {
        var c = Ui.Caption(text);
        c.Margin = new Thickness(2, 8, 0, 4);
        return c;
    }

    // ------------------------------------------------------------------ Timer

    private UIElement TimerPage()
    {
        var presets = Ui.Grid(4, TimerService.PresetMinutes.Select(m => (UIElement)Cmd(F("{0} min", m), () => _host.StartTimer(TimeSpan.FromMinutes(m)))).ToArray());
        var custom = new TextBox { Text = "10", Width = 64, HorizontalContentAlignment = HorizontalAlignment.Center };
        var error = Ui.Text("", 12);
        error.Foreground = Ui.Brush("Danger");
        void StartCustom()
        {
            if (TryNumber(custom.Text, out var m) && m > 0 && m <= 24 * 60)
            {
                error.Text = "";
                _host.StartTimer(TimeSpan.FromMinutes(m));
            }
            else
            {
                error.Text = T("Enter minutes, e.g. 10 or 2.5.");
            }
        }
        custom.KeyDown += (_, e) => { if (e.Key == Key.Enter) { StartCustom(); e.Handled = true; } };
        var start = Ui.Button(T("Start"), StartCustom, "AccentButton");
        var customRow = new StackPanel { Orientation = Orientation.Horizontal };
        var label = Ui.Text(T("Custom"), 13, dim: true);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 10, 0);
        customRow.Children.Add(label);
        customRow.Children.Add(custom);
        var minLabel = Ui.Text(T("min"), 13, dim: true);
        minLabel.VerticalAlignment = VerticalAlignment.Center;
        minLabel.Margin = new Thickness(8, 0, 10, 0);
        customRow.Children.Add(minLabel);
        customRow.Children.Add(start);

        var list = Live(() =>
        {
            var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var active = _host.Timers.Active.ToList();
            if (active.Count > 0) sp.Children.Add(Section(T("Hoodie is watching the clock")));
            foreach (var t in active)
            {
                var id = t.Id;
                var left = new StackPanel();
                left.Children.Add(Ui.Text(TimerService.FormatRemaining(t.Remaining(DateTime.Now)), 24, weight: FontWeights.SemiBold));
                var progress = t.Duration.TotalSeconds <= 0 ? 1 : 1 - t.Remaining(DateTime.Now).TotalSeconds / t.Duration.TotalSeconds;
                left.Children.Add(Bar(progress, "Accent", 180));
                left.Children.Add(Ui.Text(F("{0} · ends {1}", t.Label, t.DueAt.ToString("HH:mm")), 11.5, dim: true));
                var cancel = Ui.Button(T("Cancel"), () => _host.Timers.Cancel(id));
                cancel.VerticalAlignment = VerticalAlignment.Center;
                var row = Ui.Row(left, cancel);
                sp.Children.Add(Ui.Card(row, new Thickness(12, 8, 8, 8)));
            }
            if (active.Count == 0) sp.Children.Add(Ui.Text(T("Pick a length and Hoodie will keep an eye on the clock for you."), 12.5, dim: true));
            return sp;
        });
        return Frame(T("Timer"), Ui.Scroll(Ui.Stack(Orientation.Vertical, 10, presets, customRow, error, list)));
    }

    // ------------------------------------------------------------------ PC status

    private UIElement PcStatusPage()
    {
        var body = Live(() =>
        {
            var s = _host.LatestStatus;
            var h = _host.History;
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(T("Hoodie is checking the PC on its laptop."), 12, dim: true));
            if (s is null)
            {
                sp.Children.Add(Ui.Text("…", 15));
                return sp;
            }
            sp.Children.Add(Meter(T("CPU"), $"{s.CpuUsage:0}%", s.CpuUsage / 100, h.Cpu, 100, "Accent"));
            sp.Children.Add(Meter(T("Memory"), $"{s.MemoryUsed / 1073741824.0:0.0} / {s.MemoryTotal / 1073741824.0:0} GB", s.MemoryPercent / 100, h.Ram, 100, "Accent"));
            if (s.DiskActivityOptional is double d) sp.Children.Add(Meter(T("Disk"), $"{d:0}%", d / 100, h.Disk, 100, "Accent"));
            var maxNet = Math.Max(1024 * 64, Math.Max(h.Down.DefaultIfEmpty(0).Max(), h.Up.DefaultIfEmpty(0).Max()));
            sp.Children.Add(Meter(T("Download"), SystemStatus.Rate(s.NetworkDownloadOptional), (s.NetworkDownloadOptional ?? 0) / maxNet, h.Down, maxNet, "Accent"));
            sp.Children.Add(Meter(T("Upload"), SystemStatus.Rate(s.NetworkUploadOptional), (s.NetworkUploadOptional ?? 0) / maxNet, h.Up, maxNet, "TextDim"));

            var facts = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            facts.ColumnDefinitions.Add(new ColumnDefinition());
            facts.ColumnDefinitions.Add(new ColumnDefinition());
            var up = Fact(T("Uptime"), Uptime(s.Uptime));
            var self = Fact(T("Hoodie itself"), $"{s.SelfCpu:0.0}% · {SystemStatus.Bytes(s.SelfMemory)}");
            Grid.SetColumn(self, 1);
            facts.Children.Add(up);
            facts.Children.Add(self);
            sp.Children.Add(facts);
            var mood = _host.EnvironmentBusy ? T("The PC is working hard — Hoodie feels the heat.") : T("Everything is calm.");
            var m = Ui.Text(mood, 12, dim: true);
            m.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(m);
            return sp;
        });
        var foot = Ui.Text(T("Measured locally once per second. Nothing leaves this computer."), 11, dim: true);
        foot.Margin = new Thickness(0, 8, 0, 0);
        return Frame(T("PC Status"), Ui.Scroll(body), footer: foot);
    }

    private static string Uptime(TimeSpan t) =>
        t.TotalDays >= 1 ? F("{0} d {1} h", (int)t.TotalDays, t.Hours)
        : t.TotalHours >= 1 ? F("{0} h {1} min", (int)t.TotalHours, t.Minutes)
        : F("{0} min", t.Minutes);

    private static UIElement Fact(string k, string v)
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Caption(k));
        var t = Ui.Text(v, 14, weight: FontWeights.SemiBold);
        t.Margin = new Thickness(0, 2, 0, 0);
        sp.Children.Add(t);
        return sp;
    }

    /// <summary>Label, value, a progress bar and a 60-second sparkline.</summary>
    private static UIElement Meter(string label, string value, double fraction, IReadOnlyList<double> history, double max, string brush)
    {
        var head = Ui.Row(Ui.Caption(label), Ui.Text(value, 14, weight: FontWeights.SemiBold, wrap: TextWrapping.NoWrap));
        var spark = Sparkline(history, max, brush);
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        sp.Children.Add(head);
        sp.Children.Add(Bar(fraction, brush, double.NaN));
        sp.Children.Add(spark);
        return Ui.Card(sp, new Thickness(12, 8, 12, 8));
    }

    private static FrameworkElement Bar(double fraction, string brush, double width)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var g = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 6) };
        if (!double.IsNaN(width)) g.Width = width;
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) });
        var back = new Border { CornerRadius = new CornerRadius(3), Background = Ui.Brush("SurfaceHi") };
        Grid.SetColumnSpan(back, 2);
        g.Children.Add(back);
        g.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Ui.Brush(brush) });
        return g;
    }

    private static FrameworkElement Sparkline(IReadOnlyList<double> values, double max, string brush)
    {
        const double w = 300, h = 28;
        var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        if (values.Count >= 2 && max > 0)
        {
            var pts = new PointCollection();
            var n = SystemHistory.Capacity;
            var offset = n - values.Count;
            for (var i = 0; i < values.Count; i++)
            {
                var x = (offset + i) * w / (n - 1);
                var y = h - 2 - Math.Clamp(values[i] / max, 0, 1) * (h - 4);
                pts.Add(new Point(x, y));
            }
            var area = new PointCollection(pts) { new Point(pts[^1].X, h), new Point(pts[0].X, h) };
            var fillColor = ((SolidColorBrush)Ui.Brush(brush)).Color;
            canvas.Children.Add(new Polygon { Points = area, Fill = new SolidColorBrush(Color.FromArgb(0x30, fillColor.R, fillColor.G, fillColor.B)) });
            canvas.Children.Add(new Polyline { Points = pts, Stroke = Ui.Brush(brush), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round });
        }
        return canvas;
    }

    // ------------------------------------------------------------------ Commands (right click)

    private UIElement CommandsPage()
    {
        var anchored = _host.Territory.Anchor is not null;
        var list = Ui.Stack(Orientation.Vertical, 6,
            Ui.Caption(T("Ask Hoodie")),
            Wide(anchored ? T("You're free") : T("Stay here"), () => _host.Command(anchored ? PetCommand.YoureFree : PetCommand.StayHere)),
            Wide(T("Go home"), () => _host.Command(PetCommand.GoHome)),
            Wide(T("Set this as Home"), () => _host.SetHomeHere()),
            Wide(T("Show backpack"), () => Show(PanelPage.Backpack), keepOpen: true),
            Wide(T("Be quiet"), () => _host.SetMode(PresenceMode.Quiet)),
            Wide(T("Let's play"), () => _host.SetMode(PresenceMode.Play)),
            Wide(T("Leave me alone"), () => _host.SetMode(PresenceMode.Alone)),
            Ui.Caption(T("More")),
            Wide(T("Territory editor…"), () => _host.ShowTerritoryEditor()),
            Wide(T("Settings…"), () => _host.ShowSettings()),
            Wide(T("Hide Hoodie (Ctrl+Alt+H)"), () => _host.SetEmergencyHidden(true)));
        return Frame("Hoodie", Ui.Scroll(list), back: false);
    }

    private Button Wide(string label, Action click, bool keepOpen = false)
    {
        var b = Ui.Button(label, () =>
        {
            if (!keepOpen) Close(true);
            click();
        }, "TileButton");
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        b.Padding = new Thickness(12, 8, 12, 8);
        return b;
    }
}

/// <summary>Rolling 60-sample history of PC metrics for the sparklines.</summary>
public sealed class SystemHistory
{
    public const int Capacity = 60;
    public List<double> Cpu { get; } = new();
    public List<double> Ram { get; } = new();
    public List<double> Disk { get; } = new();
    public List<double> Down { get; } = new();
    public List<double> Up { get; } = new();

    public void Add(SystemStatus s)
    {
        Push(Cpu, s.CpuUsage);
        Push(Ram, s.MemoryPercent);
        Push(Disk, s.DiskActivityOptional ?? 0);
        Push(Down, s.NetworkDownloadOptional ?? 0);
        Push(Up, s.NetworkUploadOptional ?? 0);
    }

    private static void Push(List<double> list, double v)
    {
        list.Add(v);
        if (list.Count > Capacity) list.RemoveAt(0);
    }
}
