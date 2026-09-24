using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HoodieCompanion.Geometry;
using HoodieCompanion.Platform;
using HoodieCompanion.Presence;

namespace HoodieCompanion.UI;

/// <summary>
/// Simple visual territory editor: a translucent overlay on each monitor where the user drags rectangles
/// (Allowed / Quiet / Pass through / Never enter), erases them, or clicks to set Home. Deliberately minimal.
/// </summary>
public sealed class TerritoryEditor
{
    private enum Tool { Free, Quiet, PassThrough, NoGo, Erase, Home }

    private readonly AppHost _host;
    private readonly List<Overlay> _overlays = new();
    private Window? _toolbar;
    private Tool _tool = Tool.NoGo;

    public TerritoryEditor(AppHost host) => _host = host;

    public bool IsOpen => _overlays.Count > 0;

    public event Action? Closed;

    public static Color ColorFor(RegionType t) => t switch
    {
        RegionType.Free => Color.FromArgb(0x55, 0x8F, 0xC7, 0x9A),
        RegionType.Quiet => Color.FromArgb(0x55, 0x7A, 0xA7, 0xE0),
        RegionType.PassThrough => Color.FromArgb(0x55, 0xED, 0xB6, 0x5B),
        _ => Color.FromArgb(0x66, 0xE2, 0x73, 0x5F),
    };

    public static string LabelFor(RegionType t) => t switch
    {
        RegionType.Free => "Allowed",
        RegionType.Quiet => "Quiet",
        RegionType.PassThrough => "Pass through",
        _ => "Never enter",
    };

    public void Open()
    {
        if (IsOpen) return;
        foreach (var m in _host.World.Monitors)
        {
            var o = new Overlay(this, m);
            _overlays.Add(o);
            o.Show();
            WindowInterop.SetBounds(WindowInterop.Handle(o), (int)m.Bounds.X, (int)m.Bounds.Y, (int)m.Bounds.Width, (int)m.Bounds.Height, topmost: true);
            o.Redraw();
        }
        _toolbar = BuildToolbar();
        _toolbar.Show();
        var p = _host.World.Primary;
        var s = p.Scale;
        WindowInterop.PlaceWindowPx(_toolbar, p.WorkArea.Center.X - _toolbar.Width * s / 2, p.WorkArea.Top + 24 * s, s);
        _toolbar.Activate();
    }

    public void Close()
    {
        foreach (var o in _overlays) o.Close();
        _overlays.Clear();
        _toolbar?.Close();
        _toolbar = null;
        _host.Territory.NotifyChanged();
        _host.SaveAll();
        Closed?.Invoke();
    }

    private void RedrawAll()
    {
        foreach (var o in _overlays) o.Redraw();
    }

    private Window BuildToolbar()
    {
        var w = new Window
        {
            Title = "Hoodie territory",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Width = 640,
            Height = 170,
            FontFamily = Ui.Font,
        };
        var title = Ui.Text("Where may Hoodie go? Drag on any screen to mark an area.", 14, weight: FontWeights.SemiBold);
        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (tool, label) in new[] { (Tool.Free, "Allowed"), (Tool.Quiet, "Quiet"), (Tool.PassThrough, "Pass through"), (Tool.NoGo, "Never enter"), (Tool.Erase, "Erase"), (Tool.Home, "Set Home (click)") })
        {
            var t = tool;
            chips.Children.Add(Ui.Chip(label, _tool == tool, "tool", () => _tool = t));
        }
        var done = Ui.Button("Done", Close, "AccentButton");
        var clear = Ui.Button("Clear all areas", () => { _host.Territory.Data.Regions.Clear(); RedrawAll(); });
        clear.Margin = new Thickness(0, 0, 8, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(clear);
        buttons.Children.Add(done);
        var hint = Ui.Text("Areas override the per-monitor rule (Settings). Esc closes.", 11.5, dim: true);
        var sp = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        sp.Children.Add(title);
        sp.Children.Add(chips);
        sp.Children.Add(Ui.Row(hint, buttons));
        w.Content = Ui.Chrome(sp);
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        return w;
    }

    private sealed class Overlay : Window
    {
        private readonly TerritoryEditor _ed;
        private readonly MonitorInfo _mon;
        private readonly Canvas _canvas = new();
        private readonly Rectangle _preview = new() { StrokeThickness = 2, Visibility = Visibility.Collapsed, RadiusX = 6, RadiusY = 6 };
        private Point? _start;

        public Overlay(TerritoryEditor ed, MonitorInfo mon)
        {
            _ed = ed;
            _mon = mon;
            Title = "Territory";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x10, 0x12, 0x16));
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Cursor = Cursors.Cross;
            Content = _canvas;
            _canvas.Background = Brushes.Transparent;
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            KeyDown += (_, e) => { if (e.Key == Key.Escape) _ed.Close(); };
            SourceInitialized += (_, _) => WindowInterop.MakeToolWindow(this, noActivate: false);
        }

        private double Scale => WindowInterop.WindowScale(this);

        private Vec2 ToPx(Point dip) => new(_mon.Bounds.X + dip.X * Scale, _mon.Bounds.Y + dip.Y * Scale);

        private Point ToDip(Vec2 px) => new((px.X - _mon.Bounds.X) / Scale, (px.Y - _mon.Bounds.Y) / Scale);

        public void Redraw()
        {
            _canvas.Children.Clear();
            var t = _ed._host.Territory;
            var rule = t.MonitorRule(_mon.Id);

            var label = Ui.Text($"{_mon.DisplayName} · whole monitor: {LabelFor(rule)}", 13, weight: FontWeights.SemiBold);
            label.Foreground = Brushes.White;
            Canvas.SetLeft(label, 18);
            Canvas.SetTop(label, 14);
            _canvas.Children.Add(label);

            // The floor Hoodie walks on.
            var floorY = ToDip(new Vec2(0, _mon.WorkArea.Bottom)).Y;
            var floor = new Line { X1 = 0, X2 = _mon.Bounds.Width / Scale, Y1 = floorY, Y2 = floorY, Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), StrokeDashArray = new DoubleCollection { 4, 4 }, StrokeThickness = 1.5 };
            _canvas.Children.Add(floor);

            foreach (var r in t.Data.Regions.Where(r => r.MonitorId == _mon.Id))
            {
                var abs = t.ResolveRegion(r);
                if (abs is null) continue;
                var tl = ToDip(new Vec2(abs.Value.Left, abs.Value.Top));
                var br = ToDip(new Vec2(abs.Value.Right, abs.Value.Bottom));
                var c = ColorFor(r.Type);
                var rect = new Rectangle
                {
                    Width = Math.Max(1, br.X - tl.X),
                    Height = Math.Max(1, br.Y - tl.Y),
                    Fill = new SolidColorBrush(c),
                    Stroke = new SolidColorBrush(Color.FromArgb(0xE0, c.R, c.G, c.B)),
                    StrokeThickness = 2,
                    RadiusX = 6,
                    RadiusY = 6,
                    Tag = r.Id,
                };
                Canvas.SetLeft(rect, tl.X);
                Canvas.SetTop(rect, tl.Y);
                _canvas.Children.Add(rect);
                var l = Ui.Text(LabelFor(r.Type), 12, weight: FontWeights.SemiBold);
                l.Foreground = Brushes.White;
                l.IsHitTestVisible = false;
                Canvas.SetLeft(l, tl.X + 8);
                Canvas.SetTop(l, tl.Y + 6);
                _canvas.Children.Add(l);
            }

            if (t.HomeFeet() is Vec2 home && _ed._host.World.MonitorAt(home) == _mon)
            {
                var hp = ToDip(home);
                var icon = Ui.Icon(Ui.Icons.Home, 26, "Accent");
                Canvas.SetLeft(icon, hp.X - 13);
                Canvas.SetTop(icon, hp.Y - 34);
                _canvas.Children.Add(icon);
            }
            _canvas.Children.Add(_preview);
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (_ed._tool == Tool.Erase)
            {
                var px = ToPx(p);
                var t = _ed._host.Territory;
                var hit = t.Data.Regions.LastOrDefault(r => r.MonitorId == _mon.Id && t.ResolveRegion(r) is RectD rr && rr.Contains(px));
                if (hit is not null)
                {
                    t.Data.Regions.Remove(hit);
                    _ed.RedrawAll();
                }
                return;
            }
            if (_ed._tool == Tool.Home)
            {
                _ed._host.Territory.SetHome(_mon, new Vec2(ToPx(p).X, _mon.WorkArea.Bottom));
                _ed.RedrawAll();
                return;
            }
            _start = p;
            CaptureMouse();
            var c = ColorFor(Current);
            _preview.Fill = new SolidColorBrush(c);
            _preview.Stroke = new SolidColorBrush(Color.FromArgb(0xFF, c.R, c.G, c.B));
            _preview.Visibility = Visibility.Visible;
            Update(p);
        }

        private RegionType Current => _ed._tool switch
        {
            Tool.Free => RegionType.Free,
            Tool.Quiet => RegionType.Quiet,
            Tool.PassThrough => RegionType.PassThrough,
            _ => RegionType.NoGo,
        };

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_start is null) return;
            Update(e.GetPosition(_canvas));
        }

        private void Update(Point p)
        {
            var s = _start!.Value;
            Canvas.SetLeft(_preview, Math.Min(s.X, p.X));
            Canvas.SetTop(_preview, Math.Min(s.Y, p.Y));
            _preview.Width = Math.Abs(p.X - s.X);
            _preview.Height = Math.Abs(p.Y - s.Y);
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (_start is null) return;
            var s = _start.Value;
            var p = e.GetPosition(_canvas);
            _start = null;
            ReleaseMouseCapture();
            _preview.Visibility = Visibility.Collapsed;
            if (Math.Abs(p.X - s.X) < 8 || Math.Abs(p.Y - s.Y) < 8) return;
            var a = ToPx(s);
            var b = ToPx(p);
            var rect = RectD.FromEdges(a.X, a.Y, b.X, b.Y);
            var t = _ed._host.Territory;
            t.Data.Regions.Add(t.MakeRegion(Current, _mon, rect));
            _ed.RedrawAll();
        }
    }
}
