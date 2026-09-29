namespace HoodieCompanion.Companion.Memory;

public enum UnlockKind
{
    Item,
    Behavior,
    Color,
}

public sealed record Unlock(string Id, UnlockKind Kind, double Hours, int Days);

/// <summary>
/// Passive progression: Hoodie keeps discovering new things simply by living here. No quests, no dailies,
/// no penalty for being away: unlocks depend on hours spent together (while the user is around) and on the
/// number of different days, never on streaks.
/// </summary>
public static class Progression
{
    public static readonly IReadOnlyList<Unlock> Schedule = new List<Unlock>
    {
        new("mug", UnlockKind.Item, 0.75, 1),
        new("spin", UnlockKind.Behavior, 1, 1),
        new("ball", UnlockKind.Item, 2, 1),
        new("dance", UnlockKind.Behavior, 2.5, 1),
        new("color-navy", UnlockKind.Color, 4, 1),
        new("fan", UnlockKind.Item, 3, 2),
        new("climb-wall", UnlockKind.Behavior, 5, 2),
        new("blanket", UnlockKind.Item, 6, 3),
        new("color-forest", UnlockKind.Color, 10, 3),
        new("color-maroon", UnlockKind.Color, 16, 5),
        new("color-sand", UnlockKind.Color, 30, 8),
    };

    /// <summary>Unlocks that are due now (not yet unlocked).</summary>
    public static IEnumerable<Unlock> Due(CompanionMemory m) =>
        Schedule.Where(u => !m.IsUnlocked(u.Id) && m.HoursTogether >= u.Hours && m.DaysTogether >= u.Days);

    public static readonly string[] Colors = { "charcoal", "navy", "forest", "maroon", "sand" };

    public static bool ColorAvailable(CompanionMemory m, string color) => color == "charcoal" || m.IsUnlocked("color-" + color);
}
