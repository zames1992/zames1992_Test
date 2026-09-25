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
    private readonly Path[] _stars = new Path[3];
    private readonly Path[] _hearts = new Path[2];
    private Path _sweat = null!, _anger = null!, _paper = null!;
    private readonly Path[] _notes = new Path[2];
    private readonly Path[] _knock = new Path[2];
    private PoseEffect _current = PoseEffect.None;

    private static readonly Brush Blue = Frozen(new SolidColorBrush(Color.FromRgb(0x9C, 0xCB, 0xEB)));
    private static readonly Brush Red = Frozen(new SolidColorBrush(Color.FromRgb(0xE0, 0x6A, 0x5A)));
    private static readonly Brush Gold = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xBD, 0x60)));

    private Path Shape(string data, Brush? fill, Brush? stroke, double width)
    {
        var g = System.Windows.Media.Geometry.Parse(data);
        g.Freeze();
        var p = new Path
        {
            Data = g, Fill = fill, Stroke = stroke, StrokeThickness = width, Visibility = Visibility.Hidden,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        Children.Add(p);
        return p;
    }

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
        BuildExtras();
    }

    private void BuildExtras()
    {
        const string star = "M 0 -22 L 6 -6 L 22 0 L 6 6 L 0 22 L -6 6 L -22 0 L -6 -6 Z";
        for (var i = 0; i < _stars.Length; i++) _stars[i] = Shape(star, Gold, Ink, 4);
        const string heart = "M 0 18 C -30 -2 -26 -26 -10 -26 C -2 -26 0 -18 0 -14 C 0 -18 2 -26 10 -26 C 26 -26 30 -2 0 18 Z";
        for (var i = 0; i < _hearts.Length; i++) _hearts[i] = Shape(heart, Red, Ink, 4);
        _sweat = Shape("M 0 -24 C 8 -10 16 0 16 8 C 16 18 8 24 0 24 C -8 24 -16 18 -16 8 C -16 0 -8 -10 0 -24 Z", Blue, Ink, 4);
        _anger = Shape("M -22 -8 C -8 -8 -8 -8 -8 -22 M 8 -22 C 8 -8 8 -8 22 -8 M 22 8 C 8 8 8 8 8 22 M -8 22 C -8 8 -8 8 -22 8", null, Red, 7);
        for (var i = 0; i < _notes.Length; i++) _notes[i] = Shape("M 0 0 L 0 -40 L 22 -46 L 22 -8 M -10 0 A 10 8 0 1 0 10 0 A 10 8 0 1 0 -10 0 M 12 -8 A 10 8 0 1 0 32 -8 A 10 8 0 1 0 12 -8", Ink, Ink, 5);
        for (var i = 0; i < _knock.Length; i++) _knock[i] = Shape("M 0 -26 C 14 -14 14 14 0 26", null, Ink, 7);
        _paper = Shape("M -18 -6 L -8 -18 L 6 -16 L 18 -6 L 16 10 L 4 18 L -10 16 L -18 6 Z M -8 -6 L 6 2 M -4 8 L 8 -8", Cream, Ink, 4);
    }

    private static void Place(UIElement e, double x, double y, double opacity = 1, double scale = 1, double rot = 0)
    {
        e.Visibility = opacity > 0.02 ? Visibility.Visible : Visibility.Hidden;
        e.Opacity = Math.Clamp(opacity, 0, 1);
        var tg = new TransformGroup();
        tg.Children.Add(new ScaleTransform(scale, scale));
        tg.Children.Add(new RotateTransform(rot));
        e.RenderTransform = tg;
        SetLeft(e, x);
        SetTop(e, y);
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
                    // Lying down the head is low: start the z's above the body, never on the face.
                    SetTop(z, Math.Min(headTop.Y - 20, feet.Y - 330) - phase * 170);
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
            case PoseEffect.Stars:
                for (var i = 0; i < _stars.Length; i++)
                {
                    var a = (reducedMotion ? 0 : t * 3.2) + i * 2 * Math.PI / _stars.Length;
                    Place(_stars[i], headTop.X + Math.Cos(a) * 130, headTop.Y + 30 + Math.Sin(a) * 34, 0.95, 0.8 + 0.25 * Math.Sin(a));
                }
                break;
            case PoseEffect.Sweat:
            {
                var k = (t * 0.9) % 1.0;
                Place(_sweat, headTop.X + 120, headTop.Y + 60 + k * 50, Math.Sin(k * Math.PI));
                break;
            }
            case PoseEffect.Heart:
                for (var i = 0; i < _hearts.Length; i++)
                {
                    var k = (t * 0.7 + i * 0.5) % 1.0;
                    Place(_hearts[i], headTop.X + 60 + i * 90 + Math.Sin(k * 7) * 10, headTop.Y - 10 - k * 120, Math.Sin(k * Math.PI), 0.7 + 0.4 * k);
                }
                break;
            case PoseEffect.Anger:
            {
                var pulse = 1 + 0.15 * Math.Sin(t * 10);
                Place(_anger, headTop.X + 110, headTop.Y + 20, 1, pulse);
                break;
            }
            case PoseEffect.Music:
                for (var i = 0; i < _notes.Length; i++)
                {
                    var k = (t * 0.5 + i * 0.5) % 1.0;
                    Place(_notes[i], headTop.X + 80 + i * 60 + Math.Sin(k * 6) * 14, headTop.Y + 10 - k * 110, Math.Sin(k * Math.PI), 0.9, 10 * Math.Sin(k * 5));
                }
                break;
            case PoseEffect.Knock:
                for (var i = 0; i < _knock.Length; i++)
                {
                    var k = Math.Min(1, t / 0.3);
                    Place(_knock[i], headTop.X - 190 - i * 28 - k * 20, headTop.Y + 250, 1 - k, 1 + 0.3 * i);
                }
                break;
            case PoseEffect.PaperBall:
            {
                // Crumpled in the hands, then tossed over the shoulder in an arc (clip time 0.5 .. 1.7 s).
                var k = Math.Clamp((t - 1.1) / 0.5, 0, 1);
                var x = headTop.X - 10 + 420 * k;
                var y = headTop.Y + 330 - 380 * Math.Sin(Math.PI * k) + 260 * k * k;
                Place(_paper, x, y, 1 - Math.Clamp((k - 0.85) / 0.15, 0, 1), 1.3 - 0.3 * k, t * (k > 0 ? 600 : 90));
                break;
            }
        }
    }

    private void HideAll()
    {
        foreach (UIElement c in Children) c.Visibility = Visibility.Hidden;
    }
}
