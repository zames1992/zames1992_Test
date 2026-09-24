using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Platform;

namespace HoodieCompanion.UI;

/// <summary>
/// Draws world props that are larger than Hoodie itself (the ladder to a monitor above, the rope to a
/// monitor below) in a transparent, click-through window. Same line style as the character.
/// </summary>
public sealed class WorldPropWindow : Window
{
    private static readonly Brush Outline = Frozen(new SolidColorBrush(Color.FromRgb(0x15, 0x18, 0x1D)));
    private static readonly Brush Wood = Frozen(new SolidColorBrush(Color.FromRgb(0xB0, 0x7A, 0x45)));
    private static readonly Brush RopeFill = Frozen(new SolidColorBrush(Color.FromRgb(0xCF, 0xA8, 0x6A)));

    private readonly Canvas _canvas = new();
    private int _x = int.MinValue, _y, _w, _h;
    private WorldProp? _last;

    public WorldPropWindow()
    {
        Title = "Hoodie prop";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Content = _canvas;
        Width = 10;
        Height = 10;
        Left = -10000;
        SourceInitialized += (_, _) => WindowInterop.MakeClickThrough(this);
    }

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    public void Render(WorldProp? prop, bool topmost)
    {
        if (prop is null)
        {
            if (IsVisible) Hide();
            _last = null;
            return;
        }
        if (!IsVisible) Show();
        var p = prop.Value;
        if (_last is { } l && l.Kind == p.Kind && Math.Abs(l.Reveal - p.Reveal) < 0.004 && Math.Abs(l.Alpha - p.Alpha) < 0.01 && l.Top == p.Top && l.Bottom == p.Bottom && Math.Abs(l.Sway - p.Sway) < 0.05)
            return;
        _last = p;

        var s = p.Scale;
        // Room for the sway (ladder pivots at its feet, rope at its knot).
        var halfW = (p.Kind == WorldPropKind.Ladder ? 26 : 10) * s + Math.Abs(p.Bottom.Y - p.Top.Y) * 0.14;
        var x = (int)Math.Floor(p.Top.X - halfW - 4 * s);
        var y = (int)Math.Floor(Math.Min(p.Top.Y, p.Bottom.Y) - 8 * s);
        var w = (int)Math.Ceiling(halfW * 2 + 8 * s);
        var h = (int)Math.Ceiling(Math.Abs(p.Bottom.Y - p.Top.Y) * 1.05 + 16 * s);
        if (x != _x || y != _y || w != _w || h != _h)
        {
            WindowInterop.SetBounds(WindowInterop.Handle(this), x, y, w, h, topmost);
            _x = x; _y = y; _w = w; _h = h;
        }

        var dpi = WindowInterop.WindowScale(this);
        double D(double px) => px / dpi;
        _canvas.Children.Clear();
        _canvas.Opacity = p.Alpha;
        var cx = D(p.Top.X - x);
        var top = D(p.Top.Y - y);
        var bottom = D(p.Bottom.Y - y);
        var len = bottom - top;
        var k = s / dpi;
        _canvas.RenderTransform = new RotateTransform(p.Sway, cx, p.Kind == WorldPropKind.Ladder ? bottom : top);

        if (p.Kind == WorldPropKind.Ladder)
        {
            // Grows upwards from the floor while Hoodie props it up.
            var visTop = Math.Max(0, bottom - len * p.Reveal);
            var rail = 5 * k;
            var half = 20 * k;
            foreach (var sx in new[] { cx - half, cx + half })
            {
                var r = new Rectangle { Width = rail * 2, Height = Math.Max(0, bottom - visTop), Fill = Wood, Stroke = Outline, StrokeThickness = 1.6 * k, RadiusX = rail, RadiusY = rail };
                Canvas.SetLeft(r, sx - rail);
                Canvas.SetTop(r, visTop);
                _canvas.Children.Add(r);
            }
            var step = 26 * k;
            for (var ry = bottom - step * 0.6; ry > visTop + 4 * k; ry -= step)
            {
                var rung = new Rectangle { Width = half * 2, Height = 5 * k, Fill = Wood, Stroke = Outline, StrokeThickness = 1.4 * k, RadiusX = 2 * k, RadiusY = 2 * k };
                Canvas.SetLeft(rung, cx - half);
                Canvas.SetTop(rung, ry);
                _canvas.Children.Add(rung);
            }
        }
        else
        {
            // Unrolls downwards from the knot at the edge.
            var visBottom = Math.Min(_h / dpi, top + len * p.Reveal);
            var rope = new Line { X1 = cx, X2 = cx, Y1 = top, Y2 = visBottom, Stroke = Outline, StrokeThickness = 7 * k, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            var core = new Line { X1 = cx, X2 = cx, Y1 = top, Y2 = visBottom, Stroke = RopeFill, StrokeThickness = 4 * k, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _canvas.Children.Add(rope);
            _canvas.Children.Add(core);
            var knot = new Ellipse { Width = 14 * k, Height = 14 * k, Fill = RopeFill, Stroke = Outline, StrokeThickness = 2 * k };
            Canvas.SetLeft(knot, cx - 7 * k);
            Canvas.SetTop(knot, top - 7 * k);
            _canvas.Children.Add(knot);
        }
    }
}
