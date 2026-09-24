namespace HoodieCompanion.Settings;

/// <summary>Explicit user preference for how present Hoodie should be. Never inferred from emotions.</summary>
public enum PresenceMode
{
    Normal,
    Company,
    Play,
    Focus,
    Quiet,
    Alone,
}

public sealed class SavedPosition
{
    public string? MonitorId { get; set; }
    public double RelX { get; set; }
}

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // COMPANION
    public double Scale { get; set; } = 1.0;
    public double WalkSpeed { get; set; } = 1.0;

    // BEHAVIOR
    public bool AutonomousBehavior { get; set; } = true;
    public PresenceMode DefaultPresenceMode { get; set; } = PresenceMode.Normal;
    public PresenceMode CurrentPresenceMode { get; set; } = PresenceMode.Normal;
    public bool RestorePresenceOnStart { get; set; } = true;

    // INTERACTION
    public bool CursorReactions { get; set; } = true;
    public bool GrabThrow { get; set; } = true;
    public bool ReducedMotion { get; set; }

    // APPEARANCE
    public bool AlwaysOnTop { get; set; } = true;

    // UTILITIES
    public bool PcStatusReactions { get; set; } = true;
    public bool Sounds { get; set; } = true;

    // PRESENCE
    public bool HideOnFullscreen { get; set; } = true;

    // SYSTEM
    /// <summary>"auto" (Windows display language), "en" or "ru".</summary>
    public string Language { get; set; } = "auto";
    public bool StartWithWindows { get; set; }
    public bool FirstRunDone { get; set; }

    public SavedPosition? LastPosition { get; set; }

    public void Normalize()
    {
        Scale = Math.Clamp(double.IsFinite(Scale) ? Scale : 1, 0.5, 2.0);
        WalkSpeed = Math.Clamp(double.IsFinite(WalkSpeed) ? WalkSpeed : 1, 0.4, 2.5);
        if (!Enum.IsDefined(DefaultPresenceMode)) DefaultPresenceMode = PresenceMode.Normal;
        if (!Enum.IsDefined(CurrentPresenceMode)) CurrentPresenceMode = PresenceMode.Normal;
        if (Language is not ("auto" or "en" or "ru")) Language = "auto";
    }
}
