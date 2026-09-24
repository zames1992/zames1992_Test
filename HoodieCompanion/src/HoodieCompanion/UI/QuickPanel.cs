using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Features.Backpack;
using HoodieCompanion.Features.Reminders;
using HoodieCompanion.Features.SystemMonitor;
using HoodieCompanion.Features.Timers;
using HoodieCompanion.Geometry;
using HoodieCompanion.Platform;
using HoodieCompanion.Settings;

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
/// Everything stays small (≈330 DIP wide) and visually attached to the character.
/// </summary>
public sealed class QuickPanel : Window
{
    public const double PanelWidth = 330;
    public const double PanelHeight = 500;
    private const double ChromeMargin = 14;

    private readonly AppHost _host;
    private readonly Border _chrome;
    private readonly ContentControl _body = new();
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer _tick;
    private bool _closing;
    private PanelPage _page = PanelPage.Home;
    private string _search = "";
    private string? _expandedItem;
    private string? _renamingItem;
    private bool _showAddRow;

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

        _chrome = Ui.Chrome(_body);
        _chrome.RenderTransform = _slide;
        Content = _chrome;

        SourceInitialized += (_, _) => WindowInterop.MakeToolWindow(this, noActivate: false);
        Deactivated += (_, _) =>
        {
            if (!SuppressAutoClose) Close(animated: true);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_page != PanelPage.Home) Show(PanelPage.Home);
                else Close(animated: true);
                e.Handled = true;
            }
        };

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => RefreshLive();

        _host.Inventory.Changed += () => { if (IsVisible && _page is PanelPage.Backpack or PanelPage.Home) Rebuild(); };
        _host.Notes.Changed += () => { if (IsVisible && _page is PanelPage.Notes) Rebuild(); };
        _host.Reminders.Changed += () => { if (IsVisible && _page is PanelPage.Reminder) Rebuild(); };
        _host.Timers.Changed += () => { if (IsVisible && _page is PanelPage.Timer or PanelPage.Home) Rebuild(); };
        _host.SystemStatusUpdated += _ => { if (IsVisible && _page is PanelPage.PcStatus) Rebuild(); };
    }

    public bool SuppressAutoClose { get; set; }
    public PanelPage Page => _page;
    public bool IsOpen => IsVisible && !_closing;

    // ------------------------------------------------------------------ open / close / placement

    public void Open(PanelPage page)
    {
        _page = page;
        _expandedItem = null;
        _renamingItem = null;
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
        _host.Pet.BackpackOpened(page == PanelPage.Backpack);
    }

    public void Show(PanelPage page)
    {
        var wasBackpack = _page == PanelPage.Backpack;
        _page = page;
        _expandedItem = null;
        _renamingItem = null;
        _showAddRow = false;
        Rebuild();
        var fade = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(140));
        _body.BeginAnimation(OpacityProperty, fade);
        if (wasBackpack != (page == PanelPage.Backpack)) _host.Pet.BackpackOpened(page == PanelPage.Backpack);
    }

    public void Close(bool animated)
    {
        if (!IsVisible || _closing) return;
        _closing = true;
        _tick.Stop();
        _host.Pet.BackpackOpened(false);
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
            var slide = new DoubleAnimation(open ? from : 0, open ? 0 : from, dur) { EasingFunction = ease };
            _slide.BeginAnimation(TranslateTransform.XProperty, slide);
        }
    }

    private int _sideSign = -1;

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

    private void RefreshLive()
    {
        if (!IsVisible) return;
        if (_page is PanelPage.Timer or PanelPage.Home or PanelPage.Reminder) Rebuild();
    }

    // ------------------------------------------------------------------ pages

    private void Rebuild()
    {
        // Keep keyboard focus in a text box across rebuilds (e.g. search) when possible.
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

    private UIElement Frame(string title, UIElement content, bool back = true)
    {
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };
        var close = Ui.Button(Ui.Icon(Ui.Icons.Close, 14, "TextDim"), () => Close(true), "GhostButton", "Close (Esc)");
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        if (back)
        {
            var b = Ui.Button(Ui.Icon(Ui.Icons.Back, 14, "TextDim"), () => Show(PanelPage.Home), "GhostButton", "Back");
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
        root.Children.Add(content);
        return root;
    }

    private static string Describe(BehaviorState s, PresenceMode mode) => s switch
    {
        BehaviorState.Sleeping => "asleep",
        BehaviorState.Sitting => "resting",
        BehaviorState.Walking => "wandering about",
        BehaviorState.Grabbed => "being held",
        BehaviorState.Airborne => "flying!",
        BehaviorState.ShowingBackpack => "showing you its pocket",
        BehaviorState.ReceivingItem => "tucking something away",
        BehaviorState.Hidden => "away",
        _ => mode == PresenceMode.Focus ? "doing its own work" : "hanging around",
    };

    private UIElement HomePage()
    {
        var pet = _host.Pet;
        var sub = Ui.Text($"{pet.Mode} · {Describe(pet.State, pet.Mode)}" + (_host.Territory.Anchor is null ? "" : " · staying here"), 12, dim: true);

        var timer = _host.Timers.Active.FirstOrDefault();
        var status = _host.LatestStatus;
        var tiles = Ui.Grid(3,
            Tile(Ui.Icons.Backpack, "Backpack", _host.Inventory.Items.Count == 0 ? "empty" : $"{_host.Inventory.Items.Count} items", () => Show(PanelPage.Backpack)),
            Tile(Ui.Icons.Note, "Notes", _host.Notes.OpenCount == 0 ? "—" : $"{_host.Notes.OpenCount} open", () => Show(PanelPage.Notes)),
            Tile(Ui.Icons.Bell, "Reminder", _host.Reminders.Pending().FirstOrDefault() is { } r ? r.DueAt.ToString("HH:mm") : "—", () => Show(PanelPage.Reminder)),
            Tile(Ui.Icons.Timer, "Timer", timer is null ? "—" : TimerService.FormatRemaining(timer.Remaining(DateTime.Now)), () => Show(PanelPage.Timer)),
            Tile(Ui.Icons.Pc, "PC Status", status is null ? "…" : $"CPU {status.CpuUsage:0}%", () => Show(PanelPage.PcStatus)),
            Tile(Ui.Icons.Gear, "Settings", "", () => { Close(false); _host.ShowSettings(); }));

        var chips = new WrapPanel();
        foreach (var (mode, label, tip) in new[]
                 {
                     (PresenceMode.Normal, "Normal", "Balanced: lives its own life"),
                     (PresenceMode.Company, "Company", "Stays near you, quietly"),
                     (PresenceMode.Play, "Play", "Playful: chases your cursor"),
                     (PresenceMode.Focus, "Focus", "You're working: Hoodie goes home and keeps busy quietly"),
                     (PresenceMode.Quiet, "Quiet", "Visible but calm"),
                     (PresenceMode.Alone, "Alone", "Leaves the screen until you call it back"),
                 })
        {
            var m = mode;
            chips.Children.Add(Ui.Chip(label, pet.Mode == mode, "presence", () =>
            {
                if (_host.Pet.Mode != m) _host.SetMode(m);
                if (m == PresenceMode.Alone) Close(true);
            }, tip));
        }

        var anchored = _host.Territory.Anchor is not null;
        var cmds = Ui.Grid(2,
            Cmd(anchored ? "You're free" : "Stay here", () => _host.Command(anchored ? PetCommand.YoureFree : PetCommand.StayHere)),
            Cmd("Go home", () => _host.Command(PetCommand.GoHome)),
            Cmd("Set this as Home", () => _host.SetHomeHere()),
            Cmd("Leave me alone", () => { _host.Command(PetCommand.LeaveMeAlone); Close(true); }));

        var content = Ui.Stack(Orientation.Vertical, 10,
            sub,
            tiles,
            Ui.Caption("Presence"),
            chips,
            Ui.Caption("Ask Hoodie"),
            cmds);
        return Frame("Hoodie", Ui.Scroll(content), back: false);
    }

    private Button Tile(string icon, string label, string value, Action click)
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Icon(icon, 20));
        var l = Ui.Text(label, 12, weight: FontWeights.SemiBold, wrap: TextWrapping.NoWrap);
        l.Margin = new Thickness(0, 6, 0, 0);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(l);
        var v = Ui.Text(value, 11, dim: true, wrap: TextWrapping.NoWrap);
        v.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(v);
        ((FrameworkElement)sp.Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
        var b = Ui.Button(sp, click, "TileButton");
        b.Margin = new Thickness(0, 0, 6, 6);
        b.Height = 78;
        return b;
    }

    private Button Cmd(string label, Action click)
    {
        var b = Ui.Button(label, click);
        b.Margin = new Thickness(0, 0, 6, 6);
        return b;
    }

    // ---------------- Backpack

    private UIElement BackpackPage()
    {
        var list = new StackPanel();
        var search = Ui.Input("Search backpack");
        search.Text = _search;
        search.TextChanged += (_, _) =>
        {
            _search = search.Text;
            ListInto(list);
        };
        var add = Ui.Button(Ui.Icon(Ui.Icons.Plus, 16), () => { _showAddRow = !_showAddRow; Rebuild(); }, null, "Give Hoodie something");
        add.Margin = new Thickness(6, 0, 0, 0);
        var top = Ui.Row(Ui.WithPlaceholder(search), add, 0);

        ListInto(list);

        var stack = new StackPanel();
        stack.Children.Add(top);
        if (_showAddRow)
        {
            var row = Ui.Grid(3,
                Cmd("File…", AddFiles),
                Cmd("Folder…", AddFolder),
                Cmd("Link…", () => { _pendingLink = true; Rebuild(); }));
            row.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(row);
            if (_pendingLink)
            {
                var link = Ui.Input("https://…", url => AddLink(url));
                var go = Ui.Button("Add", () => AddLink(link.Text), "AccentButton");
                var lr = Ui.Row(Ui.WithPlaceholder(link), go);
                ((FrameworkElement)lr).Margin = new Thickness(0, 2, 0, 0);
                stack.Children.Add(lr);
                Dispatcher.BeginInvoke(() => link.Focus(), DispatcherPriority.Input);
            }
        }
        var scroll = Ui.Scroll(list);
        scroll.Margin = new Thickness(0, 10, 0, 0);
        var dock = new DockPanel();
        DockPanel.SetDock(stack, Dock.Top);
        dock.Children.Add(stack);
        dock.Children.Add(scroll);
        if (string.IsNullOrEmpty(_search) && !_showAddRow) Dispatcher.BeginInvoke(() => search.Focus(), DispatcherPriority.Input);
        return Frame("Backpack", dock);
    }

    private bool _pendingLink;

    private void ListInto(StackPanel list)
    {
        list.Children.Clear();
        var items = _host.Inventory.Search(_search).ToList();
        if (_host.Inventory.Items.Count == 0)
        {
            var empty = Ui.Text("Nothing in here yet.\n\nDrag a file, folder, app or link onto Hoodie and it will keep it in its pocket for you. Removing it later only removes Hoodie's reference - your files are never moved or deleted.", 12.5, dim: true);
            empty.Margin = new Thickness(4, 8, 4, 0);
            list.Children.Add(empty);
            return;
        }
        if (items.Count == 0)
        {
            list.Children.Add(Ui.Text("No matches.", 12.5, dim: true));
            return;
        }
        var pinned = items.Where(i => i.IsPinned).ToList();
        if (pinned.Count > 0)
        {
            list.Children.Add(Section("Pinned"));
            foreach (var i in pinned) list.Children.Add(ItemRow(i));
            list.Children.Add(Section("Items"));
        }
        foreach (var i in items.Where(i => !i.IsPinned)) list.Children.Add(ItemRow(i));
    }

    private static UIElement Section(string text)
    {
        var c = Ui.Caption(text);
        c.Margin = new Thickness(2, 8, 0, 4);
        return c;
    }

    private UIElement ItemRow(InventoryItem item)
    {
        var exists = _host.Inventory.Exists(item);
        var icon = _host.Icons.Get(item);
        UIElement iconEl = icon is not null
            ? new Image { Source = icon, Width = 24, Height = 24 }
            : Ui.Icon(item.Type switch { InventoryItemType.Folder => Ui.Icons.Folder, InventoryItemType.Url => Ui.Icons.Link, _ => Ui.Icons.Note }, 20, "TextDim");

        var name = Ui.Text(item.DisplayName, 13, weight: FontWeights.SemiBold, wrap: TextWrapping.NoWrap);
        var sub = Ui.Text(exists ? item.Type switch
        {
            InventoryItemType.Url => item.Target,
            InventoryItemType.Folder => "Folder · " + item.Target,
            _ => item.Type + " · " + System.IO.Path.GetDirectoryName(item.Target),
        } : "Missing · " + item.Target, 11, dim: true, wrap: TextWrapping.NoWrap);
        if (!exists) sub.Foreground = Ui.Brush("Danger");
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };

        if (_renamingItem == item.Id)
        {
            var box = new TextBox { Text = item.DisplayName };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { _host.Inventory.Rename(item.Id, box.Text); _renamingItem = null; Rebuild(); e.Handled = true; }
                if (e.Key == Key.Escape) { _renamingItem = null; Rebuild(); e.Handled = true; }
            };
            texts.Children.Add(box);
            Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
        }
        else
        {
            texts.Children.Add(name);
        }
        texts.Children.Add(sub);

        var pin = Ui.Button(Ui.Icon(Ui.Icons.Pin, 14, item.IsPinned ? "Accent" : "TextDim"), () => _host.Inventory.TogglePin(item.Id), "GhostButton", item.IsPinned ? "Unpin" : "Pin");
        var moreIcon = Ui.Icon(Ui.Icons.More, 14, "TextDim");
        moreIcon.StrokeThickness = 3;
        var more = Ui.Button(moreIcon, () => { _expandedItem = _expandedItem == item.Id ? null : item.Id; Rebuild(); }, "GhostButton", "More actions");
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(pin);
        right.Children.Add(more);

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ((FrameworkElement)iconEl).VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(iconEl);
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);

        var rowButton = new Button { Content = g, Style = Ui.Style("TileButton"), Padding = new Thickness(10, 7, 4, 7), Margin = new Thickness(0, 0, 0, 6), ToolTip = item.Target };
        rowButton.Click += (_, _) =>
        {
            if (_renamingItem is null) _host.OpenItem(item);
        };

        if (_expandedItem != item.Id) return rowButton;

        var actions = Ui.Grid(3,
            Cmd("Rename", () => { _renamingItem = item.Id; _expandedItem = null; Rebuild(); }),
            Cmd("Show", () => ShortcutService.ShowInFolder(item)),
            Cmd("Remove", () => { _host.RemoveItem(item); _expandedItem = null; }));
        actions.Margin = new Thickness(6, 0, 0, 8);
        var wrap = new StackPanel();
        wrap.Children.Add(rowButton);
        wrap.Children.Add(actions);
        return wrap;
    }

    private void AddFiles()
    {
        SuppressAutoClose = true;
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Give Hoodie files or apps" };
            if (dlg.ShowDialog(this) == true) _host.GiveItems(dlg.FileNames);
        }
        finally
        {
            SuppressAutoClose = false;
            Activate();
        }
        _showAddRow = false;
        Rebuild();
    }

    private void AddFolder()
    {
        SuppressAutoClose = true;
        try
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Give Hoodie a folder" };
            if (dlg.ShowDialog(this) == true) _host.GiveItems(new[] { dlg.FolderName });
        }
        finally
        {
            SuppressAutoClose = false;
            Activate();
        }
        _showAddRow = false;
        Rebuild();
    }

    private void AddLink(string url)
    {
        url = url.Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        if (!InventoryService.LooksLikeUrl(url)) return;
        _host.GiveItems(new[] { url });
        _pendingLink = false;
        _showAddRow = false;
        Rebuild();
    }

    // ---------------- Notes

    private UIElement NotesPage()
    {
        TextBox input = null!;
        void Add(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _host.AddNote(text);
            input.Text = "";
        }
        input = Ui.Input("Remember this for me…", Add);
        var add = Ui.Button("Keep", () => Add(input.Text), "AccentButton");
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var notes = _host.Notes.Ordered().ToList();
        if (notes.Count == 0) list.Children.Add(Ui.Text("Hoodie will hold on to small things for you: an idea, a number, a to-do.", 12.5, dim: true));
        foreach (var n in notes)
        {
            var id = n.Id;
            var cb = new CheckBox { IsChecked = n.IsCompleted, Content = Ui.Text(n.Text, 13) };
            if (n.IsCompleted && cb.Content is TextBlock tb) { tb.TextDecorations = TextDecorations.Strikethrough; tb.Foreground = Ui.Brush("TextDim"); }
            cb.Click += (_, _) => _host.Notes.Toggle(id);
            var del = Ui.Button(Ui.Icon(Ui.Icons.Trash, 14, "TextDim"), () => _host.Notes.Delete(id), "GhostButton", "Forget");
            var row = Ui.Row(cb, del, 4);
            ((FrameworkElement)row).Margin = new Thickness(0, 0, 0, 4);
            list.Children.Add(row);
        }
        var dock = new DockPanel();
        var top = Ui.Row(Ui.WithPlaceholder(input), add);
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(Ui.Scroll(list));
        Dispatcher.BeginInvoke(() => input.Focus(), DispatcherPriority.Input);
        return Frame("Notes", dock);
    }

    // ---------------- Reminders

    private bool _reminderAtTime;

    private UIElement ReminderPage()
    {
        var what = Ui.Input("Remind me to…");
        var minutes = new TextBox { Text = "10", Width = 56, HorizontalContentAlignment = HorizontalAlignment.Center };
        var at = new TextBox { Text = DateTime.Now.AddHours(1).ToString("HH:00"), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Center };
        var error = Ui.Text("", 12);
        error.Foreground = Ui.Brush("Danger");

        var inRow = new StackPanel { Orientation = Orientation.Horizontal };
        var inChip = Ui.Chip("in", !_reminderAtTime, "when", () => _reminderAtTime = false);
        inRow.Children.Add(inChip);
        inRow.Children.Add(minutes);
        var minLabel = Ui.Text("minutes", 13, dim: true);
        minLabel.VerticalAlignment = VerticalAlignment.Center;
        minLabel.Margin = new Thickness(8, 0, 14, 0);
        inRow.Children.Add(minLabel);
        inRow.Children.Add(Ui.Chip("at", _reminderAtTime, "when", () => _reminderAtTime = true));
        inRow.Children.Add(at);

        void Set()
        {
            var now = DateTime.Now;
            DateTime due;
            if (_reminderAtTime)
            {
                var d = ReminderService.NextOccurrence(at.Text, now);
                if (d is null) { error.Text = "Use a time like 18:30."; return; }
                due = d.Value;
            }
            else
            {
                if (!double.TryParse(minutes.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var m) &&
                    !double.TryParse(minutes.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out m) || m <= 0 || m > 60 * 24 * 7)
                {
                    error.Text = "Minutes should be a number like 10.";
                    return;
                }
                due = now.AddMinutes(m);
            }
            _host.AddReminder(string.IsNullOrWhiteSpace(what.Text) ? "Reminder" : what.Text, due);
        }
        what.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Set(); e.Handled = true; } };

        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var pending = _host.Reminders.Pending().ToList();
        if (pending.Count > 0) list.Children.Add(Section("Hoodie will remember"));
        foreach (var r in pending)
        {
            var id = r.Id;
            var left = new StackPanel();
            left.Children.Add(Ui.Text(r.Text, 13, weight: FontWeights.SemiBold));
            var mins = (r.DueAt - DateTime.Now).TotalMinutes;
            left.Children.Add(Ui.Text(r.DueAt.ToString(r.DueAt.Date == DateTime.Today ? "HH:mm" : "ddd HH:mm") +
                                      (mins > 0 ? $" · in {FormatMinutes(mins)}" : " · now"), 11.5, dim: true));
            var del = Ui.Button(Ui.Icon(Ui.Icons.Trash, 14, "TextDim"), () => _host.Reminders.Delete(id), "GhostButton", "Cancel reminder");
            var row = Ui.Row(left, del, 4);
            ((FrameworkElement)row).Margin = new Thickness(0, 0, 0, 6);
            list.Children.Add(row);
        }

        var content = Ui.Stack(Orientation.Vertical, 10,
            Ui.WithPlaceholder(what),
            inRow,
            Ui.Button("Remember this", Set, "AccentButton"),
            error,
            list);
        Dispatcher.BeginInvoke(() => what.Focus(), DispatcherPriority.Input);
        return Frame("Reminder", Ui.Scroll(content));
    }

    private static string FormatMinutes(double m) => m < 60 ? $"{Math.Ceiling(m)} min" : $"{Math.Floor(m / 60)} h {Math.Ceiling(m % 60)} min";

    // ---------------- Timer

    private UIElement TimerPage()
    {
        var presets = Ui.Grid(4, TimerService.PresetMinutes.Select(m => (UIElement)Cmd($"{m} min", () => _host.StartTimer(TimeSpan.FromMinutes(m)))).ToArray());
        var custom = new TextBox { Text = "10", Width = 60, HorizontalContentAlignment = HorizontalAlignment.Center };
        var start = Ui.Button("Start", () =>
        {
            if (double.TryParse(custom.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var m) && m > 0 && m <= 24 * 60)
                _host.StartTimer(TimeSpan.FromMinutes(m));
        }, "AccentButton");
        var customRow = new StackPanel { Orientation = Orientation.Horizontal };
        var label = Ui.Text("Custom", 13, dim: true);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 10, 0);
        customRow.Children.Add(label);
        customRow.Children.Add(custom);
        var minLabel = Ui.Text("min", 13, dim: true);
        minLabel.VerticalAlignment = VerticalAlignment.Center;
        minLabel.Margin = new Thickness(8, 0, 10, 0);
        customRow.Children.Add(minLabel);
        customRow.Children.Add(start);

        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var active = _host.Timers.Active.ToList();
        if (active.Count > 0) list.Children.Add(Section("Hoodie is watching the clock"));
        foreach (var t in active)
        {
            var id = t.Id;
            var left = new StackPanel();
            left.Children.Add(Ui.Text(TimerService.FormatRemaining(t.Remaining(DateTime.Now)), 22, weight: FontWeights.SemiBold));
            left.Children.Add(Ui.Text(t.Label + " · ends " + t.DueAt.ToString("HH:mm"), 11.5, dim: true));
            var cancel = Ui.Button("Cancel", () => _host.Timers.Cancel(id));
            cancel.VerticalAlignment = VerticalAlignment.Center;
            var row = Ui.Row(left, cancel);
            ((FrameworkElement)row).Margin = new Thickness(0, 0, 0, 8);
            list.Children.Add(Ui.Card(row, new Thickness(12, 8, 8, 8)));
        }
        if (active.Count == 0) list.Children.Add(Ui.Text("Pick a length and Hoodie will keep an eye on the clock for you.", 12.5, dim: true));

        return Frame("Timer", Ui.Scroll(Ui.Stack(Orientation.Vertical, 10, presets, customRow, list)));
    }

    // ---------------- PC status

    private UIElement PcStatusPage()
    {
        var s = _host.LatestStatus;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        void Line(string k, string v) => LineEl(k, Ui.Text(v, 15, weight: FontWeights.SemiBold));
        void LineEl(string k, FrameworkElement vt)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var kt = Ui.Caption(k);
            kt.Margin = new Thickness(0, 5, 0, 5);
            kt.VerticalAlignment = VerticalAlignment.Center;
            vt.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(kt, row);
            Grid.SetRow(vt, row);
            Grid.SetColumn(vt, 1);
            grid.Children.Add(kt);
            grid.Children.Add(vt);
            row++;
        }
        if (s is null)
        {
            Line("CPU", "…");
        }
        else
        {
            Line("CPU", $"{s.CpuUsage:0}%");
            Line("RAM", $"{s.MemoryUsed / 1073741824.0:0.0} / {s.MemoryTotal / 1073741824.0:0} GB");
            Line("Disk", s.DiskActivityOptional is double d ? $"{d:0}%" : "—");
            var net = new StackPanel { Orientation = Orientation.Horizontal };
            net.Children.Add(Ui.Icon(Ui.Icons.Down, 12, "Accent"));
            var down = Ui.Text(SystemStatus.Rate(s.NetworkDownloadOptional), 15, weight: FontWeights.SemiBold, wrap: TextWrapping.NoWrap);
            down.Margin = new Thickness(4, 0, 14, 0);
            net.Children.Add(down);
            net.Children.Add(Ui.Icon(Ui.Icons.Up, 12, "TextDim"));
            var up = Ui.Text(SystemStatus.Rate(s.NetworkUploadOptional), 15, weight: FontWeights.SemiBold, wrap: TextWrapping.NoWrap);
            up.Margin = new Thickness(4, 0, 0, 0);
            net.Children.Add(up);
            LineEl("Net", net);
            Line("GPU", s.GpuUsageOptional is double g ? $"{g:0}%" : "not measured");
            Line("Uptime", SystemStatus.FormatUptime(s.Uptime));
        }
        var self = s is null ? "" : $"{s.SelfCpu:0.0}% CPU · {SystemStatus.Bytes(s.SelfMemory)}";
        var mood = _host.EnvironmentBusy ? "Hoodie feels the PC working hard." : "The world feels calm.";
        var content = Ui.Stack(Orientation.Vertical, 10,
            Ui.Caption("This PC"),
            Ui.Card(grid),
            Ui.Caption("Hoodie itself"),
            Ui.Text(self, 13, dim: true),
            Ui.Text(mood, 12, dim: true),
            Ui.Text("Measured locally once per second. Nothing leaves this computer.", 11, dim: true));
        return Frame("PC Status", Ui.Scroll(content));
    }

    // ---------------- Commands (right click)

    private UIElement CommandsPage()
    {
        var anchored = _host.Territory.Anchor is not null;
        var list = Ui.Stack(Orientation.Vertical, 6,
            Ui.Caption("Ask Hoodie"),
            Wide(anchored ? "You're free" : "Stay here", () => _host.Command(anchored ? PetCommand.YoureFree : PetCommand.StayHere)),
            Wide("Go home", () => _host.Command(PetCommand.GoHome)),
            Wide("Set this as Home", () => _host.SetHomeHere()),
            Wide("Show backpack", () => Show(PanelPage.Backpack), keepOpen: true),
            Wide("Be quiet", () => _host.SetMode(PresenceMode.Quiet)),
            Wide("Let's play", () => _host.SetMode(PresenceMode.Play)),
            Wide("Leave me alone", () => _host.SetMode(PresenceMode.Alone)),
            Ui.Caption("More"),
            Wide("Territory editor…", () => _host.ShowTerritoryEditor()),
            Wide("Settings…", () => _host.ShowSettings()),
            Wide("Hide Hoodie (Ctrl+Alt+H)", () => _host.SetEmergencyHidden(true)));
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
