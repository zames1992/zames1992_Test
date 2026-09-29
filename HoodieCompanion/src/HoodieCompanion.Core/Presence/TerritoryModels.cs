namespace HoodieCompanion.Presence;

public enum RegionType
{
    Free,
    Quiet,
    PassThrough,
    NoGo,
    Home,
    Anchor,
}

public enum AppPresenceMode
{
    Normal,
    Quiet,
    Avoid,
    Hide,
}

/// <summary>A user-drawn region, stored relative to its monitor's working area so it survives resolution changes.</summary>
public sealed class TerritoryRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public RegionType Type { get; set; } = RegionType.NoGo;
    public string MonitorId { get; set; } = "";
    public double RelX { get; set; }
    public double RelY { get; set; }
    public double RelW { get; set; }
    public double RelH { get; set; }
}

public sealed class HomeSpot
{
    public string MonitorId { get; set; } = "";
    /// <summary>Horizontal position as a fraction of the monitor working area width.</summary>
    public double RelX { get; set; }
    public double AllowedRadiusDip { get; set; } = 160;
}

public sealed class AppPresenceRule
{
    public string ProcessName { get; set; } = "";
    public AppPresenceMode PresenceMode { get; set; } = AppPresenceMode.Quiet;
    public bool WindowInteractionAllowed { get; set; }
}

public sealed class TerritoryData
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public HomeSpot? Home { get; set; }
    /// <summary>Default rule per monitor id (regions override it).</summary>
    public Dictionary<string, RegionType> MonitorRules { get; set; } = new();
    public List<TerritoryRegion> Regions { get; set; } = new();
    public List<AppPresenceRule> AppRules { get; set; } = new();
}
