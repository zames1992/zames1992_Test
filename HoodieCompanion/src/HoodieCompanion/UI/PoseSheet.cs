using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Rendering;

namespace HoodieCompanion.UI;

/// <summary>
/// Renders every animation clip at several moments into one PNG contact sheet.
/// Used to check the rig visually: complete body, stable identity, no missing parts.
/// Run: HoodieCompanion.exe --render-poses poses.png
/// </summary>
public static class PoseSheet
{
    public static void Render(string path)
    {
        const double scale = 0.21;
        const int cellW = 128, cellH = 210, cols = 12;
        var clips = Enum.GetValues<AnimClip>();
        var samples = new[] { 0.2, 0.5, 0.85 };
        var cells = clips.Length * samples.Length;
        var rows = (int)Math.Ceiling(cells / (double)cols);

        var root = new Canvas { Width = cols * cellW, Height = rows * cellH, Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xBD, 0x60)) };
        var i = 0;
        foreach (var clip in clips)
        {
            var info = AnimationCatalog.Get(clip);
            foreach (var k in samples)
            {
                var t = info.Loop ? k * 2.4 : k * info.Duration;
                var ctx = new AnimContext { Time = t + 0.7, WalkPhase = k, AirVy = k * 2 - 1, SwingAngle = 25 * Math.Sin(k * 6), SwingSpeed = 200 * Math.Cos(k * 6) };
                var pose = ProceduralAnimator.Evaluate(clip, t, ctx).Pose;
                var rig = new CharacterRig();
                rig.Apply(pose);
                var holder = new Canvas { Width = cellW, Height = cellH, ClipToBounds = true };
                var m = Matrix.Identity;
                m.Scale(scale, scale);
                m.Translate(cellW / 2.0 - 272 * scale, 6 - 40 * scale);
                rig.RenderTransform = new MatrixTransform(m);
                holder.Children.Add(rig);
                var label = new TextBlock { Text = $"{clip} {k:0.0#}", FontSize = 9, Foreground = Brushes.Black, Width = cellW, TextAlignment = TextAlignment.Center };
                Canvas.SetTop(label, cellH - 16);
                holder.Children.Add(label);
                Canvas.SetLeft(holder, i % cols * cellW);
                Canvas.SetTop(holder, i / cols * cellH);
                root.Children.Add(holder);
                i++;
            }
        }
        Save(root, path, 1);
    }

    public static void Save(FrameworkElement element, string path, double pixelScale)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(element.Width * pixelScale), (int)Math.Ceiling(element.Height * pixelScale), 96 * pixelScale, 96 * pixelScale, PixelFormats.Pbgra32);
        bmp.Render(element);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
