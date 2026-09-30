using System;
using System.Collections.Generic;
using System.IO;
using Path = System.Windows.Shapes.Path;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HoodieCompanion.Companion.Animation;

namespace HoodieCompanion.Companion.Rendering;

/// <summary>
/// The 2D cutout puppet built from Assets/rig.json. Every part is a vector Path in reference-image
/// coordinates; groups (legs, arms, torso, head, face, eyes...) are animated only through pivot
/// transforms, so the silhouette, limb count and clothing can never drift between poses.
/// </summary>
public sealed class CharacterRig : Canvas
{
    private sealed class Group
    {
        public required string Name;
        public required Point Pivot;
        public Group? Parent;
        public readonly MatrixTransform Transform = new();
        public Matrix World = Matrix.Identity;
        public double Dx, Dy, Rot, Sx = 1, Sy = 1;
        public double Opacity = 1;
    }

    private readonly Dictionary<string, Group> _groups = new();
    private readonly List<Group> _order = new();
    private readonly Dictionary<string, List<Path>> _partsByGroup = new();
    private readonly Dictionary<Path, double> _baseOpacity = new();
    private readonly Dictionary<Path, string> _groupOf = new();
    private readonly Dictionary<string, List<Path>> _halosByGroup = new();
    private readonly List<Path> _halos = new();
    private double _haloThickness = -1;

    /// <summary>
    /// Almost transparent (alpha 1/255): invisible, but Windows counts the pixel as part of the layered window,
    /// so a click there reaches Hoodie instead of falling through to the desktop.
    /// </summary>
    private static readonly Brush HaloBrush = Freeze(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

    public CharacterRig()
    {
        Width = 559;
        Height = 895;
        IsHitTestVisible = true;
        Build(LoadRigJson());
    }

    /// <summary>Paths that accept mouse input: the body and every visible thing Hoodie holds (not the ground shadow).</summary>
    public IEnumerable<Path> HitParts => _partsByGroup.Where(k => IsHitGroup(k.Key)).SelectMany(k => k.Value);

    private static readonly HashSet<string> PropGroups = new() { "item", "backpack", "backpackMouth", "backpackLid", "laptop", "laptopLid", "book", "notebook", "pencil", "crate", "fan", "mug", "ball", "blanket" };

    public static bool IsPropGroup(string name) => PropGroups.Contains(name);

    private static bool IsHitGroup(string name) => name != "shadow";

    /// <summary>
    /// Sets the width of the invisible hit halo around the silhouette, in reference pixels (the stroke is
    /// centred on the outline, so the halo reaches half of it outside the drawn edge).
    /// </summary>
    public void SetHaloWidth(double referencePixels)
    {
        if (Math.Abs(referencePixels - _haloThickness) < 2) return;
        _haloThickness = referencePixels;
        var grow = referencePixels / 2;
        foreach (var h in _halos)
        {
            var r = _haloBase[h];
            r.Inflate(grow, grow);
            var radius = Math.Min(r.Width, r.Height) * 0.35;
            var g = new RectangleGeometry(r, radius, radius);
            g.Freeze();
            h.Data = g;
        }
    }

    /// <summary>Screen bounds (DIPs of <paramref name="ancestor"/>) of a group's visible parts, or null.</summary>
    public Rect? GroupBounds(string group, Visual ancestor)
    {
        if (!_partsByGroup.TryGetValue(group, out var parts)) return null;
        Rect? r = null;
        foreach (var path in parts)
        {
            if (path.Visibility != Visibility.Visible) continue;
            var b = path.TransformToAncestor(ancestor).TransformBounds(path.Data.Bounds);
            r = r is Rect acc ? Rect.Union(acc, b) : b;
        }
        return r;
    }

    /// <summary>Name of the rig group drawn at a point (rig reference coordinates), or null for empty space.</summary>
    public string? GroupAt(Point local)
    {
        string? found = null;
        // Hidden or faded-out parts (a prop that is put away) must not count.
        VisualTreeHelper.HitTest(this, d => d is UIElement { IsVisible: false } or UIElement { Opacity: < 0.05 }
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue, r =>
        {
            if (r.VisualHit is Path path && _groupOf.TryGetValue(path, out var g))
            {
                found = g;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(local));
        return found;
    }

    public static string LoadRigJson()
    {
        var asm = typeof(CharacterRig).Assembly;
        using var s = asm.GetManifestResourceStream("HoodieCompanion.rig.json")
                      ?? throw new InvalidOperationException("rig.json resource missing");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private void Build(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var outline = (Color)ColorConverter.ConvertFromString(root.GetProperty("outline").GetString()!);
        var outlineWidth = root.GetProperty("outlineWidth").GetDouble();
        var outlineBrush = Freeze(new SolidColorBrush(outline));

        foreach (var g in root.GetProperty("groups").EnumerateObject())
        {
            var p = g.Value.GetProperty("pivot");
            _groups[g.Name] = new Group { Name = g.Name, Pivot = new Point(p[0].GetDouble(), p[1].GetDouble()) };
        }
        foreach (var g in root.GetProperty("groups").EnumerateObject())
        {
            var parent = g.Value.GetProperty("parent");
            if (parent.ValueKind == JsonValueKind.String) _groups[g.Name].Parent = _groups[parent.GetString()!];
        }
        // Parents before children so world matrices can be composed in one pass.
        var visited = new HashSet<string>();
        void Visit(Group g)
        {
            if (!visited.Add(g.Name)) return;
            if (g.Parent is not null) Visit(g.Parent);
            _order.Add(g);
        }
        foreach (var g in _groups.Values) Visit(g);

        foreach (var part in root.GetProperty("parts").EnumerateArray())
        {
            var groupName = part.GetProperty("group").GetString()!;
            var group = _groups[groupName];
            System.Windows.Media.Geometry geometry;
            if (part.TryGetProperty("ellipse", out var e))
            {
                geometry = new EllipseGeometry(new Point(e[0].GetDouble(), e[1].GetDouble()), e[2].GetDouble(), e[3].GetDouble());
            }
            else
            {
                geometry = System.Windows.Media.Geometry.Parse(part.GetProperty("path").GetString()!);
            }
            geometry.Freeze();

            var fill = part.TryGetProperty("fill", out var f) ? f.GetString() : "none";
            var stroke = part.TryGetProperty("stroke", out var st) ? st.GetString() : null;
            var width = part.TryGetProperty("strokeWidth", out var sw) ? sw.GetDouble() : outlineWidth;
            var opacity = part.TryGetProperty("opacity", out var op) ? op.GetDouble() : 1.0;

            var path = new Path
            {
                Data = geometry,
                Fill = fill is null or "none" ? null : Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill))),
                Stroke = stroke == "none" ? null : stroke is null ? outlineBrush : Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(stroke))),
                StrokeThickness = width,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = opacity,
                RenderTransform = group.Transform,
                SnapsToDevicePixels = false,
            };
            if (!IsHitGroup(groupName)) path.IsHitTestVisible = false;
            else
            {
                // Collected per group: one merged outline per group becomes its hit halo (see BuildHalos).
                System.Windows.Media.Geometry haloShape = fill is null or "none"
                    ? new RectangleGeometry(geometry.GetRenderBounds(new Pen(Brushes.Black, width)))
                    : geometry;
                if (!_haloShapes.TryGetValue(groupName, out var shapes)) _haloShapes[groupName] = shapes = new List<System.Windows.Media.Geometry>();
                shapes.Add(haloShape);
            }
            _groupOf[path] = groupName;
            if (fill is "#34363E" or "#262930" or "#25282E" && groupName is "head" or "torso" or "armL" or "armR") _baseFill[path] = fill!;
            _baseOpacity[path] = opacity;
            if (!_partsByGroup.TryGetValue(groupName, out var list)) _partsByGroup[groupName] = list = new List<Path>();
            list.Add(path);
            Children.Add(path);
        }
        BuildHalos();
    }

    private readonly Dictionary<string, List<System.Windows.Media.Geometry>> _haloShapes = new();
    private readonly Dictionary<Path, Rect> _haloBase = new();

    /// <summary>
    /// One invisible hit halo per group: a rounded box around the group's parts (grown by the halo width),
    /// filled with an almost transparent brush. Simple shapes keep the layered window's per-frame cost low.
    /// </summary>
    private void BuildHalos()
    {
        foreach (var (groupName, shapes) in _haloShapes)
        {
            var bounds = Rect.Empty;
            foreach (var g in shapes) bounds.Union(g.Bounds);
            if (bounds.IsEmpty) continue;
            var halo = new Path
            {
                Fill = HaloBrush,
                RenderTransform = _groups[groupName].Transform,
                SnapsToDevicePixels = false,
            };
            _haloBase[halo] = bounds;
            _groupOf[halo] = groupName;
            _halos.Add(halo);
            _halosByGroup[groupName] = new List<Path> { halo };
            Children.Insert(_halos.Count - 1, halo);
        }
        _haloShapes.Clear();
        SetHaloWidth(40);
    }

    // ------------------------------------------------------------------ hoodie colour (cosmetic, unlocked over time)

    private readonly Dictionary<Path, string> _baseFill = new();

    private static readonly Dictionary<string, Dictionary<string, string>> Palettes = new()
    {
        ["navy"] = new() { ["#34363E"] = "#2F3B56", ["#262930"] = "#232C42", ["#25282E"] = "#212A3E" },
        ["forest"] = new() { ["#34363E"] = "#2F4538", ["#262930"] = "#233529", ["#25282E"] = "#21311F" },
        ["maroon"] = new() { ["#34363E"] = "#56323A", ["#262930"] = "#40252B", ["#25282E"] = "#3A2227" },
        ["sand"] = new() { ["#34363E"] = "#A08C6E", ["#262930"] = "#7E6C52", ["#25282E"] = "#766548" },
    };

    /// <summary>Recolours the hoodie (hood, body, sleeves, hem). Everything else stays: the character is the same.</summary>
    public void SetHoodieColor(string color)
    {
        Palettes.TryGetValue(color, out var map);
        foreach (var (path, fill) in _baseFill)
        {
            var target = map is not null && map.TryGetValue(fill, out var c) ? c : fill;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(target));
            brush.Freeze();
            path.Fill = brush;
        }
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private void Set(string name, double dx = 0, double dy = 0, double rot = 0, double sx = 1, double sy = 1)
    {
        var g = _groups[name];
        g.Dx = dx;
        g.Dy = dy;
        g.Rot = rot;
        g.Sx = sx;
        g.Sy = sy;
    }

    /// <summary>Applies a pose: maps pose values onto group transforms (see Pose for units).</summary>
    public void Apply(in Pose p)
    {
        var s = p.Scale;
        Set("body", p.RootDx, p.RootDy, p.RootRot, p.BodySx * s, p.BodySy * s);
        Set("shadow", 0, 0, 0, p.ShadowScale * s, p.ShadowScale * s);
        Set("legL", 0, p.LegLDy, p.LegLRot, 1, p.LegLSy);
        Set("legR", 0, p.LegRDy, p.LegRRot, 1, p.LegRSy);
        Set("torso", 0, p.TorsoDy, p.TorsoRot);
        Set("strings", 0, 0, p.StringsRot);
        Set("armL", 0, p.ArmLDy, p.ArmLRot);
        Set("armR", 0, p.ArmRDy, p.ArmRRot);
        Set("head", p.HeadDx, p.HeadDy, p.HeadRot);
        Set("face", p.LookX * 14, p.LookY * 9);
        var eyeOpen = Math.Max(0.08, p.EyeOpen);
        Set("eyes", p.LookX * 6, p.LookY * 5, 0, p.EyeScale, p.EyeScale * eyeOpen);
        Set("item", p.ItemDx, p.ItemDy, p.ItemRot, p.ItemScale, p.ItemScale);
        // Accessories grow slightly as they are pulled out.
        static double Pop(double a) => 0.7 + 0.3 * Math.Clamp(a, 0, 1);
        Set("backpack", 0, 10, 0, 1.25 * Pop(p.PropBackpack), 1.25 * Pop(p.PropBackpack));
        Set("backpackMouth", 0, 0, 0, 1, Math.Clamp(p.BackpackLid, 0.05, 1));
        Set("backpackLid", 0, 0, 0, 1, 1 - 2 * Math.Clamp(p.BackpackLid, 0, 1));
        Set("laptop", 0, 0, 0, Pop(p.PropLaptop), Pop(p.PropLaptop));
        Set("laptopLid", 0, 0, 0, 1, Math.Clamp(p.LaptopLid, 0.05, 1));
        Set("book", 0, 0, 0, Pop(p.PropBook), Pop(p.PropBook));
        Set("notebook", 0, 0, 0, Pop(p.PropNotebook), Pop(p.PropNotebook));
        Set("pencil", 0, 0, 0, Pop(p.PropPencil), Pop(p.PropPencil));
        Set("fan", 0, 0, 0, Pop(p.PropFan), Pop(p.PropFan));
        // The mug stays upright whatever the arm does (plus a sip tilt).
        Set("mug", 0, 0, -p.ArmLRot + p.MugTilt, Pop(p.PropMug), Pop(p.PropMug));
        Set("ball", p.BallDx, p.BallDy, p.BallRot, Pop(p.PropBall), Pop(p.PropBall));
        Set("blanket", 0, 0, 0, 1, 1);
        Set("crate", 0, 24 - 40 * (1 - Math.Clamp(p.PropCrate, 0, 1)), 0, Pop(p.PropCrate), Pop(p.PropCrate));

        foreach (var g in _order)
        {
            // local = T(-pivot) * S * R * T(pivot + offset); world = local * parent.world
            var m = Matrix.Identity;
            m.Translate(-g.Pivot.X, -g.Pivot.Y);
            m.Scale(g.Sx, g.Sy);
            m.Rotate(g.Rot);
            m.Translate(g.Pivot.X + g.Dx, g.Pivot.Y + g.Dy);
            if (g.Parent is not null) m.Append(g.Parent.World);
            g.World = m;
            g.Transform.Matrix = m;
        }

        SetOpacity("item", p.ItemAlpha);
        SetOpacity("shadow", p.ShadowAlpha);
        SetOpacity("backpack", p.PropBackpack);
        SetOpacity("backpackLid", p.PropBackpack);
        SetOpacity("backpackMouth", p.PropBackpack * Math.Clamp(p.BackpackLid * 1.5, 0, 1));
        SetOpacity("laptop", p.PropLaptop);
        SetOpacity("laptopLid", p.PropLaptop);
        SetOpacity("book", p.PropBook);
        SetOpacity("notebook", p.PropNotebook);
        SetOpacity("pencil", p.PropPencil);
        SetOpacity("crate", p.PropCrate);
        SetOpacity("fan", p.PropFan);
        SetOpacity("mug", p.PropMug);
        SetOpacity("ball", p.PropBall);
        SetOpacity("blanket", p.PropBlanket);
        Opacity = p.Opacity;
    }

    private void SetOpacity(string group, double alpha)
    {
        if (!_partsByGroup.TryGetValue(group, out var parts)) return;
        foreach (var path in parts)
        {
            var o = _baseOpacity[path] * alpha;
            if (Math.Abs(path.Opacity - o) > 0.001) path.Opacity = o;
            var vis = o > 0.005 ? Visibility.Visible : Visibility.Hidden;
            if (path.Visibility != vis) path.Visibility = vis;
        }
        // A hidden prop must not catch clicks through its halo either.
        if (_halosByGroup.TryGetValue(group, out var halos))
        {
            var vis = alpha > 0.3 ? Visibility.Visible : Visibility.Hidden;
            foreach (var h in halos)
                if (h.Visibility != vis) h.Visibility = vis;
        }
    }

    /// <summary>Head top / body centre in reference coordinates after the current pose (for effects).</summary>
    public Point HeadTop => _groups["head"].World.Transform(new Point(272, 80));
    public Point Feet => _groups["body"].World.Transform(new Point(272, 830));
    public Point FaceSide => _groups["head"].World.Transform(new Point(150, 250));
}
