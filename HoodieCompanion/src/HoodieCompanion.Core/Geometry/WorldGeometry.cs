namespace HoodieCompanion.Geometry;

/// <summary>One physical monitor, in virtual-desktop physical pixels.</summary>
public sealed record MonitorInfo(
    string Id,
    int Index,
    RectD Bounds,
    RectD WorkArea,
    double Scale,
    bool IsPrimary)
{
    public string DisplayName => $"Monitor {Index + 1}" + (IsPrimary ? " (main)" : "");
}

/// <summary>
/// Hoodie's physical world: the union of all monitor working areas in virtual desktop space.
/// Floors are the bottoms of working areas (so Hoodie stands on the taskbar, never behind it).
/// Nothing here assumes monitors are laid out horizontally or at non-negative coordinates.
/// </summary>
public sealed class WorldGeometry
{
    public const double Epsilon = 0.5;

    public WorldGeometry(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        Monitors = monitors;
        var l = monitors.Min(m => m.WorkArea.Left);
        var t = monitors.Min(m => m.WorkArea.Top);
        var r = monitors.Max(m => m.WorkArea.Right);
        var b = monitors.Max(m => m.WorkArea.Bottom);
        Extent = RectD.FromEdges(l, t, r, b);
    }

    public IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>Bounding box of all working areas.</summary>
    public RectD Extent { get; }

    public MonitorInfo Primary => Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors[0];

    public MonitorInfo? FindById(string? id) => id is null ? null : Monitors.FirstOrDefault(m => m.Id == id);

    /// <summary>Monitor whose working area contains the point. Feet standing exactly on a floor count as inside.</summary>
    public MonitorInfo? MonitorAt(Vec2 p)
    {
        // Top edges are exclusive so feet standing on an upper monitor's floor (which is the lower
        // monitor's top edge) belong to the upper monitor. A bare top edge still counts as a fallback.
        foreach (var m in Monitors)
        {
            var w = m.WorkArea;
            if (p.X >= w.Left && p.X < w.Right && p.Y > w.Top + Epsilon && p.Y <= w.Bottom + Epsilon) return m;
        }
        foreach (var m in Monitors)
        {
            var w = m.WorkArea;
            if (p.X >= w.Left && p.X < w.Right && p.Y >= w.Top - Epsilon && p.Y <= w.Bottom + Epsilon) return m;
        }
        return null;
    }

    /// <summary>Monitor containing the point, otherwise the nearest one.</summary>
    public MonitorInfo NearestMonitor(Vec2 p)
    {
        var inside = MonitorAt(p);
        if (inside is not null) return inside;
        MonitorInfo best = Monitors[0];
        var bestD = double.MaxValue;
        foreach (var m in Monitors)
        {
            var d = m.WorkArea.DistanceTo(p);
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    public bool IsInsideWorld(Vec2 p) => MonitorAt(p) is not null;

    /// <summary>
    /// The first floor at or below <paramref name="y"/> for column <paramref name="x"/>,
    /// or null if there is no monitor under that column.
    /// </summary>
    public double? FloorBelow(double x, double y)
    {
        double? best = null;
        foreach (var m in Monitors)
        {
            var w = m.WorkArea;
            if (!w.ContainsX(x)) continue;
            if (w.Bottom + Epsilon < y) continue;
            if (best is null || w.Bottom < best) best = w.Bottom;
        }
        return best;
    }

    /// <summary>Floor of the monitor the feet are standing in, or the closest floor under the column.</summary>
    public double? FloorFor(Vec2 feet)
    {
        var m = MonitorAt(feet);
        if (m is not null) return m.WorkArea.Bottom;
        return FloorBelow(feet.X, feet.Y);
    }

    /// <summary>Highest ceiling (smallest top) for the column, used to bounce thrown bodies.</summary>
    public double CeilingAt(double x)
    {
        double? best = null;
        foreach (var m in Monitors)
        {
            if (!m.WorkArea.ContainsX(x)) continue;
            if (best is null || m.WorkArea.Top < best) best = m.WorkArea.Top;
        }
        return best ?? Extent.Top;
    }

    /// <summary>Is there any monitor covering column x at height y (with feet tolerance)?</summary>
    public bool ColumnOpen(double x, double y) => MonitorAt(new Vec2(x, y)) is not null;

    /// <summary>
    /// Finds the floor a walker at <paramref name="feet"/> would meet when stepping to <paramref name="nextX"/>.
    /// Returns null when the step leads outside every monitor (a wall).
    /// </summary>
    public double? NeighborFloor(Vec2 feet, double nextX)
    {
        double? best = null;
        var bestDelta = double.MaxValue;
        foreach (var m in Monitors)
        {
            var w = m.WorkArea;
            if (!w.ContainsX(nextX)) continue;
            // Hoodie can only walk into a monitor whose vertical span includes the current body height region
            // or whose floor is below (drop) / reasonably above (jump) the feet.
            var delta = Math.Abs(w.Bottom - feet.Y);
            var bodyOverlaps = feet.Y >= w.Top - Epsilon && feet.Y <= w.Bottom + Epsilon;
            var candidate = bodyOverlaps || w.Bottom > feet.Y || delta < w.Height;
            if (!candidate) continue;
            if (delta < bestDelta) { bestDelta = delta; best = w.Bottom; }
        }
        return best;
    }

    /// <summary>Returns the monitor directly above (sharing a horizontal edge) with an overlapping x-range.</summary>
    public MonitorInfo? MonitorAbove(MonitorInfo m) =>
        Monitors.Where(o => o != m && Math.Abs(o.WorkArea.Bottom - m.WorkArea.Top) < 200 &&
                            Overlap(o.WorkArea.Left, o.WorkArea.Right, m.WorkArea.Left, m.WorkArea.Right) > 60)
                .OrderBy(o => Math.Abs(o.WorkArea.Bottom - m.WorkArea.Top)).FirstOrDefault();

    public MonitorInfo? MonitorBelow(MonitorInfo m) =>
        Monitors.Where(o => o != m && Math.Abs(o.Bounds.Top - m.Bounds.Bottom) < 200 &&
                            Overlap(o.WorkArea.Left, o.WorkArea.Right, m.WorkArea.Left, m.WorkArea.Right) > 60)
                .OrderBy(o => Math.Abs(o.Bounds.Top - m.Bounds.Bottom)).FirstOrDefault();

    /// <summary>Monitor touching the given side (dir = -1 left, +1 right) with overlapping vertical span.</summary>
    public MonitorInfo? MonitorBeside(MonitorInfo m, int dir)
    {
        var edge = dir < 0 ? m.WorkArea.Left : m.WorkArea.Right;
        return Monitors.Where(o => o != m &&
                                   Math.Abs((dir < 0 ? o.WorkArea.Right : o.WorkArea.Left) - edge) < 2 &&
                                   Overlap(o.Bounds.Top, o.Bounds.Bottom, m.Bounds.Top, m.Bounds.Bottom) > 0)
                       .FirstOrDefault();
    }

    public static double Overlap(double a0, double a1, double b0, double b1) => Math.Min(a1, b1) - Math.Max(a0, b0);

    /// <summary>A structural fingerprint used to detect display configuration changes.</summary>
    public string Fingerprint => string.Join("|", Monitors.Select(m => $"{m.Id}:{m.Bounds}:{m.WorkArea}:{m.Scale:0.00}"));
}
