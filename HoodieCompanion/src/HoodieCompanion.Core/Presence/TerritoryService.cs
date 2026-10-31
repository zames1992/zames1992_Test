using HoodieCompanion.Geometry;

namespace HoodieCompanion.Presence;

/// <summary>A temporary "Stay here" circle.</summary>
public readonly record struct Anchor(Vec2 Center, double RadiusPx);

/// <summary>
/// "Hoodie has autonomy, but the user owns the space."
/// Answers where Hoodie may walk, stop and rest. Character curiosity never overrides these answers.
/// </summary>
public sealed class TerritoryService
{
    private WorldGeometry _world;

    public TerritoryService(TerritoryData data, WorldGeometry world)
    {
        Data = data;
        _world = world;
    }

    public TerritoryData Data { get; private set; }
    public Anchor? Anchor { get; private set; }

    public event Action? Changed;

    public void UpdateWorld(WorldGeometry world) => _world = world;

    public void Replace(TerritoryData data)
    {
        Data = data;
        Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();

    public void SetAnchor(Vec2 feet, double radiusPx) => Anchor = new Anchor(feet, radiusPx);
    public void ClearAnchor() => Anchor = null;

    public RegionType MonitorRule(string monitorId) =>
        Data.MonitorRules.TryGetValue(monitorId, out var t) ? t : RegionType.Free;

    public void SetMonitorRule(string monitorId, RegionType type)
    {
        if (type == RegionType.Free) Data.MonitorRules.Remove(monitorId);
        else Data.MonitorRules[monitorId] = type;
        Changed?.Invoke();
    }

    public RectD? ResolveRegion(TerritoryRegion r)
    {
        var m = _world.FindById(r.MonitorId);
        if (m is null) return null;
        var w = m.WorkArea;
        return new RectD(w.X + r.RelX * w.Width, w.Y + r.RelY * w.Height, r.RelW * w.Width, r.RelH * w.Height);
    }

    public TerritoryRegion MakeRegion(RegionType type, MonitorInfo m, RectD absolute)
    {
        var w = m.WorkArea;
        return new TerritoryRegion
        {
            Type = type,
            MonitorId = m.Id,
            RelX = (absolute.X - w.X) / w.Width,
            RelY = (absolute.Y - w.Y) / w.Height,
            RelW = absolute.Width / w.Width,
            RelH = absolute.Height / w.Height,
        };
    }

    /// <summary>
    /// Region type at a point. Explicit regions override the monitor rule; when regions overlap the most
    /// restrictive wins (NoGo &gt; PassThrough &gt; Quiet &gt; Free).
    /// </summary>
    public RegionType TypeAt(Vec2 p)
    {
        var m = _world.MonitorAt(p);
        if (m is null) return RegionType.NoGo;
        RegionType? best = null;
        foreach (var r in Data.Regions)
        {
            if (r.MonitorId != m.Id) continue;
            var rect = ResolveRegion(r);
            if (rect is null || !rect.Value.Contains(p)) continue;
            if (best is null || Rank(r.Type) > Rank(best.Value)) best = r.Type;
        }
        return best ?? MonitorRule(m.Id);
    }

    private static int Rank(RegionType t) => t switch
    {
        RegionType.NoGo => 4,
        RegionType.PassThrough => 3,
        RegionType.Quiet => 2,
        _ => 1,
    };

    /// <summary>Probe point for a standing body: a little above the feet.</summary>
    public static Vec2 Probe(Vec2 feet, double bodyHeightPx) => new(feet.X, feet.Y - Math.Min(40, bodyHeightPx * 0.2));

    /// <summary>May Hoodie intentionally step onto this spot (walking through)?</summary>
    public bool CanTraverse(Vec2 feet, double bodyHeightPx) => TypeAt(Probe(feet, bodyHeightPx)) != RegionType.NoGo;

    /// <summary>May Hoodie stop / rest at this spot?</summary>
    public bool CanStop(Vec2 feet, double bodyHeightPx)
    {
        var t = TypeAt(Probe(feet, bodyHeightPx));
        if (t is RegionType.NoGo or RegionType.PassThrough) return false;
        if (Anchor is { } a && Math.Abs(feet.X - a.Center.X) > a.RadiusPx) return false;
        return true;
    }

    public bool IsQuietAt(Vec2 feet, double bodyHeightPx) => TypeAt(Probe(feet, bodyHeightPx)) == RegionType.Quiet;

    public bool MonitorAllowed(MonitorInfo m) => MonitorRule(m.Id) != RegionType.NoGo || Data.Regions.Any(r => r.MonitorId == m.Id && r.Type is RegionType.Free or RegionType.Quiet);

    public Vec2? HomeFeet()
    {
        var h = Data.Home;
        if (h is null) return null;
        var m = _world.FindById(h.MonitorId);
        if (m is null) return null;
        var w = m.WorkArea;
        return new Vec2(w.X + Math.Clamp(h.RelX, 0, 1) * w.Width, w.Bottom);
    }

    public void SetHome(MonitorInfo m, Vec2 feet, double radiusDip = 160)
    {
        var w = m.WorkArea;
        Data.Home = new HomeSpot { MonitorId = m.Id, RelX = Math.Clamp((feet.X - w.X) / w.Width, 0, 1), AllowedRadiusDip = radiusDip };
        Changed?.Invoke();
    }

    /// <summary>
    /// Finds a standable floor X on the monitor close to <paramref name="preferX"/>.
    /// Returns null if the whole monitor floor is off limits.
    /// </summary>
    public double? NearestStandableX(MonitorInfo m, double preferX, double bodyHeightPx, double margin)
    {
        var w = m.WorkArea;
        var lo = w.Left + margin;
        var hi = w.Right - margin;
        if (hi <= lo) return null;
        preferX = Math.Clamp(preferX, lo, hi);
        var step = Math.Max(8, (hi - lo) / 160);
        for (double d = 0; d <= hi - lo; d += step)
        {
            foreach (var x in new[] { preferX - d, preferX + d })
            {
                if (x < lo || x > hi) continue;
                if (CanStop(new Vec2(x, w.Bottom), bodyHeightPx)) return x;
            }
        }
        return null;
    }

    /// <summary>All monitors where Hoodie can stand somewhere.</summary>
    public IEnumerable<MonitorInfo> StandableMonitors(double bodyHeightPx, double margin) =>
        _world.Monitors.Where(m => NearestStandableX(m, m.WorkArea.Center.X, bodyHeightPx, margin) is not null);
}
