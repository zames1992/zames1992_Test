using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using HoodieCompanion.Geometry;
using HoodieCompanion.Platform;
using static HoodieCompanion.UI.L;

namespace HoodieCompanion.UI;

/// <summary>
/// Layer 2 (alternative): a ring of round buttons that opens around Hoodie on a click. Hoodie stays in the
/// middle and looks at the button under the cursor. Near a screen edge the ring becomes an arc on the side
/// with room. Selecting an item opens that page of the <see cref="QuickPanel"/>; Esc, a click elsewhere or
/// a click on Hoodie closes it. Transparent pixels pass clicks through, so Hoodie stays clickable.
/// </summary>
public sealed class RadialMenu : Window
{
    private const double Ring = 96;       // DIP from Hoodie's centre to a button's centre
    private const double ButtonSize = 48; // DIP
    private const double Box = 2 * (Ring + ButtonSize) + 40;

    private readonly AppHost _host;
    private readonly Canvas _canvas = new() { Width = Box, Height = Box };
    private readonly List<(PanelPage Page, Border Button, Point Center)> _items = new();
    private readonly Border _label;
    private readonly TextBlock _labelText;
    private bool _closing;
    private Vec2 _anchorPx;
    private double _scale = 1;

    private static readonly (PanelPage Page, string Icon)[] Entries =
    {
        (PanelPage.Backpack, Ui.Icons.Backpack),
        (PanelPage.Notes, Ui.Icons.Note),
        (PanelPage.Reminder, Ui.Icons.Bell),
        (PanelPage.Timer, Ui.Icons.Timer),
        (PanelPage.PcStatus, Ui.Icons.Pc),
        (PanelPage.Memories, Ui.Icons.Heart),
    };

    public RadialMenu(AppHost host)
    {
        _host = host;
        Title = "Hoodie";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = Box;
        Height = Box;
        FontFamily = Ui.Font;
        UseLayoutRounding = true;
        Content = _canvas;

        _labelText = Ui.Text("", 12.5, weight: FontWeights.SemiBold, wrap: TextWrapping.NoWrap);
        _label = new Border
        {
            Background = Ui.Brush("Bg"),
            BorderBrush = Ui.Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 4, 10, 5),
            Child = _labelText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.4, Color = Colors.Black },
        };

        SourceInitialized += (_, _) => WindowInterop.MakeToolWindow(this, noActivate: false);
        Deactivated += (_, _) => { if (!_closing) Close(animated: true); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(animated: true); e.Handled = true; }
        };
        Closing += (_, e) =>
        {
            if (_host.IsExiting) return;
            e.Cancel = true;
            Hide();
        };
    }

    public bool IsOpen => IsVisible && !_closing;

    /// <summary>Where the ring is centred, in physical pixels (Hoodie's body centre when it opened).</summary>
    public Vec2 AnchorPx => _anchorPx;

    public static string PageName(PanelPage page) => page switch
    {
        PanelPage.Backpack => T("Backpack"),
        PanelPage.Notes => T("Notes"),
        PanelPage.Reminder => T("Reminder"),
        PanelPage.Timer => T("Timer"),
        PanelPage.PcStatus => T("PC Status"),
        PanelPage.Memories => T("Memories"),
        _ => "Hoodie",
    };

    // ------------------------------------------------------------------ open / close

    public void Open()
    {
        _closing = false;
        var pet = _host.PetBoundsPx;
        var mon = _host.World.NearestMonitor(pet.Center);
        _scale = mon.Scale;
        _anchorPx = new Vec2(pet.Center.X, pet.Center.Y - pet.Height * 0.08);
        var wa = mon.WorkArea;
        var angles = ChooseArc(_anchorPx, wa, _scale);
        BuildButtons(angles);

        // Window centred on Hoodie, kept inside the work area (the buttons were chosen to fit anyway).
        var w = Box * _scale;
        var x = Math.Clamp(_anchorPx.X - w / 2, wa.Left - w / 3, wa.Right - w * 2 / 3);
        var y = Math.Clamp(_anchorPx.Y - w / 2, wa.Top - w / 3, wa.Bottom - w * 2 / 3);
        _windowOriginPx = new Vec2(x, y);
        // Where Hoodie's centre lands inside the canvas (DIP), after clamping.
        _centerDip = new Point((_anchorPx.X - x) / _scale, (_anchorPx.Y - y) / _scale);
        Layout();

        if (!IsVisible) Show();
        WindowInterop.PlaceWindowPx(this, x, y, _scale);
        Activate();
        Animate(open: true);
    }

    private Vec2 _windowOriginPx;
    private Point _centerDip;

    public void Close(bool animated)
    {
        if (!IsVisible || _closing) return;
        _closing = true;
        _label.Visibility = Visibility.Collapsed;
        if (!animated || _host.Settings.ReducedMotion)
        {
            Hide();
            _closing = false;
            return;
        }
        Animate(open: false);
    }

    /// <summary>Keeps the ring on Hoodie if it shifts a little (called by the host while open).</summary>
    public void Follow()
    {
        if (!IsOpen) return;
        var pet = _host.PetBoundsPx;
        var c = new Vec2(pet.Center.X, pet.Center.Y - pet.Height * 0.08);
        if (Vec2.Distance(c, _anchorPx) < 3 * _scale) return;
        // Hoodie walked off (a reaction, a throw...): the ring makes no sense any more.
        if (Vec2.Distance(c, _anchorPx) > 60 * _scale) { Close(animated: true); return; }
        var d = c - _anchorPx;
        _anchorPx = c;
        _windowOriginPx = _windowOriginPx + d;
        WindowInterop.PlaceWindowPx(this, _windowOriginPx.X, _windowOriginPx.Y, _scale);
    }

    /// <summary>QA / keyboard: picks an item as a click would.</summary>
    public void Select(PanelPage page)
    {
        Close(animated: false);
        _host.OpenPanel(page);
    }

    // ------------------------------------------------------------------ geometry

    /// <summary>
    /// Six angles (degrees, clockwise from +x) spread over a 180° arc. The arc opens upwards by default and
    /// swings to whichever side has room when Hoodie stands near an edge or a corner.
    /// </summary>
    private static double[] ChooseArc(Vec2 centre, RectD wa, double s)
    {
        var reach = (Ring + ButtonSize / 2 + 8) * s;
        var centres = new[] { -90.0, -45, -135, 0, 180, 45, 135, 90 };
        foreach (var c in centres)
        {
            var angles = Spread(c);
            var fits = true;
            foreach (var a in angles)
            {
                var r = a * Math.PI / 180;
                var p = new Vec2(centre.X + Math.Cos(r) * reach, centre.Y + Math.Sin(r) * reach);
                if (p.X < wa.Left + ButtonSize / 2 * s || p.X > wa.Right - ButtonSize / 2 * s || p.Y < wa.Top + ButtonSize / 2 * s || p.Y > wa.Bottom - ButtonSize / 2 * s) { fits = false; break; }
            }
            if (fits) return angles;
        }
        return Spread(-90);
    }

    private static double[] Spread(double centreDeg)
    {
        var n = Entries.Length;
        var span = 180.0;
        var step = span / n;
        var r = new double[n];
        for (var i = 0; i < n; i++) r[i] = centreDeg - span / 2 + step / 2 + step * i;
        return r;
    }

    private void BuildButtons(double[] angles)
    {
        _canvas.Children.Clear();
        _items.Clear();
        for (var i = 0; i < Entries.Length; i++)
        {
            var (page, icon) = Entries[i];
            var r = angles[i] * Math.PI / 180;
            var centre = new Point(Math.Cos(r) * Ring, Math.Sin(r) * Ring); // relative to Hoodie's centre
            var ic = Ui.Icon(icon, 22, "Text");
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            ic.VerticalAlignment = VerticalAlignment.Center;
            var b = new Border
            {
                Width = ButtonSize,
                Height = ButtonSize,
                CornerRadius = new CornerRadius(ButtonSize / 2),
                Background = Ui.Brush("Surface"),
                BorderBrush = Ui.Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                Child = ic,
                Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } },
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.45, Color = Colors.Black },
                Focusable = true,
                Tag = page,
            };
            AutomationName(b, PageName(page));
            var p = page;
            var bb = b;
            b.MouseEnter += (_, _) => Hover(bb, p, true);
            b.MouseLeave += (_, _) => Hover(bb, p, false);
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; Select(p); };
            b.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; Select(p); } };
            b.GotKeyboardFocus += (_, _) => Hover(bb, p, true);
            _canvas.Children.Add(b);
            _items.Add((page, b, centre));
        }
        _canvas.Children.Add(_label);
    }

    private static void AutomationName(FrameworkElement e, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(e, name);

    private void Layout()
    {
        foreach (var (_, b, c) in _items)
        {
            Canvas.SetLeft(b, _centerDip.X + c.X - ButtonSize / 2);
            Canvas.SetTop(b, _centerDip.Y + c.Y - ButtonSize / 2);
        }
    }

    private void Hover(Border b, PanelPage page, bool on)
    {
        var reduced = _host.Settings.ReducedMotion;
        b.Background = Ui.Brush(on ? "SurfaceHi" : "Surface");
        b.BorderBrush = Ui.Brush(on ? "Accent" : "BorderBrush");
        if (ScaleOf(b) is { } st)
        {
            var to = on ? 1.12 : 1.0;
            if (reduced) { st.ScaleX = st.ScaleY = to; }
            else
            {
                var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }
        if (!on)
        {
            _label.Visibility = Visibility.Collapsed;
            return;
        }
        // Label just outside the button, away from Hoodie; Hoodie glances at the button.
        _labelText.Text = PageName(page);
        _label.Visibility = Visibility.Visible;
        _label.Measure(new Size(400, 100));
        var lw = _label.DesiredSize.Width;
        var lh = _label.DesiredSize.Height;
        var bx = Canvas.GetLeft(b) + ButtonSize / 2;
        var by = Canvas.GetTop(b) + ButtonSize / 2;
        var dir = new Vector(bx - _centerDip.X, by - _centerDip.Y);
        if (dir.Length > 0) dir.Normalize();
        var lx = bx + dir.X * (ButtonSize / 2 + 10 + lw / 2) - lw / 2;
        var ly = by + dir.Y * (ButtonSize / 2 + 10 + lh / 2) - lh / 2;
        lx = Math.Clamp(lx, 0, Box - lw);
        ly = Math.Clamp(ly, 0, Box - lh);
        Canvas.SetLeft(_label, lx);
        Canvas.SetTop(_label, ly);
        _host.Pet.LookToward(new Vec2(_windowOriginPx.X + bx * _scale, _windowOriginPx.Y + by * _scale), 2);
    }

    private static ScaleTransform? ScaleOf(Border b) => (b.RenderTransform as TransformGroup)?.Children[0] as ScaleTransform;
    private static TranslateTransform? ShiftOf(Border b) => (b.RenderTransform as TransformGroup)?.Children[1] as TranslateTransform;

    private void Animate(bool open)
    {
        var reduced = _host.Settings.ReducedMotion;
        var dur = TimeSpan.FromMilliseconds(open ? 200 : 130);
        var ease = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
        var i = 0;
        foreach (var (_, b, c) in _items)
        {
            b.BeginAnimation(OpacityProperty, null);
            if (reduced)
            {
                b.Opacity = open ? 1 : 0;
                continue;
            }
            var fade = new DoubleAnimation(open ? 0 : 1, open ? 1 : 0, dur) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(open ? i * 18 : 0) };
            b.BeginAnimation(OpacityProperty, fade);
            // Buttons fly out from Hoodie (and fall back in on close).
            if (ShiftOf(b) is { } tx)
            {
                tx.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(open ? -c.X * 0.6 : 0, open ? 0 : -c.X * 0.6, dur) { EasingFunction = ease, BeginTime = fade.BeginTime });
                tx.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(open ? -c.Y * 0.6 : 0, open ? 0 : -c.Y * 0.6, dur) { EasingFunction = ease, BeginTime = fade.BeginTime });
            }
            i++;
        }
        if (!open)
        {
            if (reduced)
            {
                Hide();
                _closing = false;
                return;
            }
            var t = new System.Windows.Threading.DispatcherTimer { Interval = dur + TimeSpan.FromMilliseconds(20) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (_closing)
                {
                    Hide();
                    _closing = false;
                }
            };
            t.Start();
        }
    }
}
