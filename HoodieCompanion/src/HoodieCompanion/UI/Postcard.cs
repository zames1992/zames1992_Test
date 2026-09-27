using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Memory;
using HoodieCompanion.Companion.Rendering;
using static HoodieCompanion.UI.L;

namespace HoodieCompanion.UI;

/// <summary>
/// A shareable postcard of one memorable moment: Hoodie drawn in a pose that fits the moment (with the right item and
/// hoodie colour), the journal line and the date. Only Hoodie itself is drawn — never the screen or anything of the
/// user's — and it is made only when the user asks for it. Saved locally to Pictures\Hoodie.
/// </summary>
public static class Postcard
{
    public const double Width = 1200, Height = 800;

    /// <summary>Where postcards are saved (QA points this at its output folder).</summary>
    public static string Folder { get; set; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Hoodie");

    /// <summary>Renders the postcard and returns the saved file path.</summary>
    public static string Save(MomentMemory moment, string caption, CompanionMemory memory)
    {
        var card = Build(moment, caption, memory);
        var stem = System.IO.Path.Combine(Folder, $"hoodie-{DateTime.Now:yyyyMMdd-HHmmss}");
        var path = stem + ".png";
        for (var n = 2; File.Exists(path); n++) path = $"{stem}-{n}.png";
        PoseSheet.Save(card, path, 1);
        return path;
    }

    public static FrameworkElement Build(MomentMemory moment, string caption, CompanionMemory memory)
    {
        var root = new Canvas
        {
            Width = Width,
            Height = Height,
            Background = new LinearGradientBrush(Color.FromRgb(0xF6, 0xCF, 0x82), Color.FromRgb(0xEE, 0xAE, 0x4E), 90),
        };

        // A soft floor (the rig draws its own shadow).
        var floor = new Rectangle { Width = Width, Height = 90, Fill = new SolidColorBrush(Color.FromArgb(40, 0x6B, 0x3E, 0x10)) };
        Canvas.SetTop(floor, Height - 90);
        root.Children.Add(floor);
        // Hoodie, posed for the moment.
        var (clip, item) = PoseFor(moment.Key);
        var info = AnimationCatalog.Get(clip);
        var t = info.Loop ? 1.3 : info.Duration * 0.55;
        var ctx = new AnimContext { Time = t + 0.7, HeldItem = item };
        var pose = ProceduralAnimator.Evaluate(clip, t, ctx).Pose;
        var rig = new CharacterRig();
        rig.SetHoodieColor(memory.Doc.HoodieColor);
        rig.Apply(pose);
        const double scale = 0.62;
        var m = Matrix.Identity;
        m.Scale(scale, scale);
        // Rig reference (559x923): horizontal centre 272; its ground line (where the shadow sits) is y 808.
        m.Translate(330 - 272 * scale, Height - 70 - 808 * scale);
        rig.RenderTransform = new MatrixTransform(m);
        root.Children.Add(rig);

        // Text on the right.
        var ink = new SolidColorBrush(Color.FromRgb(0x2B, 0x22, 0x18));
        var text = new StackPanel { Width = 560 };
        text.Children.Add(new TextBlock
        {
            Text = caption,
            FontFamily = Ui.Font,
            FontSize = 46,
            FontWeight = FontWeights.SemiBold,
            Foreground = ink,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 56,
        });
        text.Children.Add(new TextBlock
        {
            Text = moment.At.ToString("d MMMM yyyy", L.Culture),
            FontFamily = Ui.Font,
            FontSize = 26,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 0x2B, 0x22, 0x18)),
            Margin = new Thickness(0, 22, 0, 0),
        });
        Canvas.SetLeft(text, 580);
        Canvas.SetTop(text, 190);
        root.Children.Add(text);

        var days = Math.Max(1, memory.DaysTogether);
        var footer = new TextBlock
        {
            Text = "Hoodie · " + (days == 1 ? T("1 day together") : F("{0} days together", days)),
            FontFamily = Ui.Font,
            FontSize = 22,
            Foreground = new SolidColorBrush(Color.FromArgb(170, 0x2B, 0x22, 0x18)),
        };
        Canvas.SetLeft(footer, 580);
        Canvas.SetTop(footer, Height - 70);
        root.Children.Add(footer);
        return root;
    }

    /// <summary>A pose (and item) that tells the moment at a glance.</summary>
    public static (AnimClip Clip, WorldItem Item) PoseFor(string key)
    {
        static WorldItem Item(string id) => id switch
        {
            "mug" => WorldItem.Mug,
            "ball" => WorldItem.Ball,
            "fan" => WorldItem.Fan,
            "blanket" => WorldItem.Blanket,
            _ => WorldItem.None,
        };
        if (key.StartsWith("found-again:", StringComparison.Ordinal)) return (AnimClip.ShowItem, Item(key[12..]));
        if (key.StartsWith("unlock:", StringComparison.Ordinal))
        {
            var id = key[7..];
            return Item(id) != WorldItem.None ? (AnimClip.ShowItem, Item(id))
                : id == "dance" ? (AnimClip.Dance, WorldItem.None)
                : id == "spin" ? (AnimClip.Spin, WorldItem.None)
                : (AnimClip.Proud, WorldItem.None);
        }
        return key switch
        {
            "first-fan" => (AnimClip.FanSelf, WorldItem.Fan),
            "first-break-hint" => (AnimClip.SipMug, WorldItem.Mug),
            "first-ball-game" => (AnimClip.PlayBall, WorldItem.Ball),
            "window-closed-under-me" => (AnimClip.Scared, WorldItem.None),
            "rode-window" => (AnimClip.Balance, WorldItem.None),
            "first-wall-climb" or "first-window-top" => (AnimClip.Proud, WorldItem.None),
            "first-welcome-back" => (AnimClip.Wave, WorldItem.None),
            "first-grab" or "first-big-throw" => (AnimClip.Happy, WorldItem.None),
            "first-gift" => (AnimClip.InspectSelf, WorldItem.None),
            _ => (AnimClip.Happy, WorldItem.None),
        };
    }
}
