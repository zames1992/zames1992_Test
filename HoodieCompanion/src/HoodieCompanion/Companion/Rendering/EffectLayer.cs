using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HoodieCompanion.Companion.Animation;

namespace HoodieCompanion.Companion.Rendering;

/// <summary>
/// Small diegetic marks next to Hoodie (Zzz, !, ?, dust puffs, sparkles, heat lines). Drawn in the same
/// reference coordinate space as the rig and never hit-testable. Kept deliberately minimal.
/// </summary>
public sealed class EffectLayer : Canvas
{
    private static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x34)));
    private static readonly Brush Cream = Frozen(new SolidColorBrush(Color.FromRgb(0xFB, 0xF4, 0xE2)));
    private static readonly Brush Dust = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0xC9, 0xC2, 0xB0)));
    private static readonly Brush Warm = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0xE8, 0x8A, 0x4A)));

    private readonly TextBlock[] _zzz = new TextBlock[3];
    private readonly Border _bubble;
    private readonly TextBlock _bubbleText;
    private readonly Path _bubbleArrow;
    private readonly Ellipse[] _dust = new Ellipse[5];
    private readonly Path[] _sparkles = new Path[3];
    private readonly Path[] _heat = new Path[3];
    private PoseEffect _current = PoseEffect.None;

    public EffectLayer()
    {
        Width = 559;
        Height = 895;
        IsHitTestVisible = false;

        for (var i = 0; i < _zzz.Length; i++)
        {
            _zzz[i] = new TextBlock { Text = "z", FontSize = 64 + i * 16, FontWeight = FontWeights.Bold, Foreground = Ink, Visibility = Visibility.Hidden, FontFamily = new FontFamily("Segoe UI, Tahoma, Arial") };
            Children.Add(_zzz[i]);
        }

        _bubbleText = new TextBlock { FontSize = 84, FontWeight = FontWeights.Black, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI, Tahoma, Arial") };
        var arrowGeo = System.Windows.Media.Geometry.Parse("M 0 -30 L 0 26 M -20 6 L 0 26 L 20 6");
        arrowGeo.Freeze();
        _bubbleArrow = new Path { Data = arrowGeo, Stroke = Ink, StrokeThickness = 9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var content = new Grid();
        content.Children.Add(_bubbleText);
        content.Children.Add(_bubbleArrow);
        _bubble = new Border
        {
            Width = 110,
            Height = 110,
            CornerRadius = new CornerRadius(55),
            Background = Cream,
            BorderBrush = Ink,
            BorderThickness = new Thickness(6),
            Child = content,
            Visibility = Visibility.Hidden,
        };
        Children.Add(_bubble);

        for (var i = 0; i < _dust.Length; i++)
        {
            _dust[i] = new Ellipse { Fill = Dust, Visibility = Visibility.Hidden };
            Children.Add(_dust[i]);
        }

        var star = System.Windows.Media.Geometry.Parse("M 0 -26 L 7 -7 L 26 0 L 7 7 L 0 26 L -7 7 L -26 0 L -7 -7 Z");
        star.Freeze();
        for (var i = 0; i < _sparkles.Length; i++)
        {
            _sparkles[i] = new Path { Data = star, Fill = Cream, Stroke = Ink, StrokeThickness = 4, Visibility = Visibility.Hidden };
            Children.Add(_sparkles[i]);
        }

        var wave = System.Windows.Media.Geometry.Parse("M 0 0 C 10 -12 -10 -24 0 -36 C 10 -48 -10 -60 0 -72");
        wave.Freeze();
        for (var i = 0; i < _heat.Length; i++)
        {
            _heat[i] = new Path { Data = wave, Stroke = Warm, StrokeThickness = 7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Hidden };
            Children.Add(_heat[i]);
        }
    }

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    public void Update(PoseEffect effect, double t, Point headTop, Point feet, bool reducedMotion)
    {
        if (effect != _current)
        {
            HideAll();
            _current = effect;
        }
        switch (effect)
        {
            case PoseEffect.Zzz:
                for (var i = 0; i < _zzz.Length; i++)
                {
                    var phase = (t * 0.45 + i / 3.0) % 1.0;
                    var z = _zzz[i];
                    z.Visibility = Visibility.Visible;
                    z.Opacity = Math.Sin(phase * Math.PI) * 0.9;
                    SetLeft(z, headTop.X + 90 + phase * 70 + Math.Sin(phase * 6) * 12);
                    SetTop(z, headTop.Y - 20 - phase * 170);
                }
                break;
            case PoseEffect.Exclaim:
            case PoseEffect.Question:
            case PoseEffect.Dots:
            case PoseEffect.Arrow:
                _bubbleText.Text = effect switch { PoseEffect.Exclaim => "!", PoseEffect.Question => "?", PoseEffect.Dots => "...", _ => "" };
                _bubbleArrow.Visibility = effect == PoseEffect.Arrow ? Visibility.Visible : Visibility.Collapsed;
                _bubble.Visibility = Visibility.Visible;
                var pop = Math.Min(1, t / 0.12);
                _bubble.Opacity = pop;
                _bubble.RenderTransform = new ScaleTransform(0.6 + 0.4 * pop, 0.6 + 0.4 * pop, 55, 55);
                SetLeft(_bubble, headTop.X + 70);
                SetTop(_bubble, headTop.Y - 120 - (reducedMotion ? 0 : Math.Sin(t * 6) * 6));
                break;
            case PoseEffect.Dust:
                for (var i = 0; i < _dust.Length; i++)
                {
                    var d = _dust[i];
                    var k = Math.Min(1, t / 0.7);
                    var side = i % 2 == 0 ? -1 : 1;
                    var r = 26 + i * 6 + k * 30;
                    d.Width = d.Height = r * 2;
                    d.Opacity = (1 - k) * 0.8;
                    d.Visibility = d.Opacity > 0.02 ? Visibility.Visible : Visibility.Hidden;
                    SetLeft(d, feet.X + side * (40 + i * 30 + k * 110) - r);
                    SetTop(d, feet.Y - 30 - i * 8 - k * 30 - r);
                }
                break;
            case PoseEffect.Sparkle:
                for (var i = 0; i < _sparkles.Length; i++)
                {
                    var s = _sparkles[i];
                    var k = (t * 1.6 + i * 0.33) % 1.0;
                    s.Visibility = Visibility.Visible;
                    s.Opacity = Math.Sin(k * Math.PI);
                    var sc = 0.5 + 0.7 * Math.Sin(k * Math.PI);
                    s.RenderTransform = new ScaleTransform(sc, sc);
                    SetLeft(s, headTop.X + (i - 1) * 120 + 20);
                    SetTop(s, headTop.Y + 80 - i * 40 - k * 60);
                }
                break;
            case PoseEffect.Heat:
                for (var i = 0; i < _heat.Length; i++)
                {
                    var h = _heat[i];
                    var k = (t * 0.8 + i / 3.0) % 1.0;
                    h.Visibility = Visibility.Visible;
                    h.Opacity = Math.Sin(k * Math.PI) * 0.8;
                    SetLeft(h, headTop.X - 110 + i * 110);
                    SetTop(h, headTop.Y - 10 - k * 60);
                }
                break;
        }
    }

    private void HideAll()
    {
        foreach (UIElement c in Children) c.Visibility = Visibility.Hidden;
    }
}
