using HoodieCompanion.Settings;

namespace HoodieCompanion.Companion.Behavior;

/// <summary>Autonomous activities Hoodie may choose when nothing else is going on.</summary>
public enum Activity
{
    Wander,
    Explore,
    Sit,
    Sleep,
    Stretch,
    Yawn,
    LookAround,
    PeekEdge,
    InspectBackpack,
    Hop,
    Run,
    StayNearUser,
    ChaseCursor,
    GoRestSpot,
}

public readonly record struct DecisionContext(
    PresenceMode Mode,
    CharacterDrives Drives,
    bool HasItems,
    bool NearEdge,
    bool CanExplore,
    double UserIdleSeconds,
    bool CursorNearFloor,
    bool AtRestSpot,
    bool ReducedMotion);

/// <summary>
/// Weighted choice of the next autonomous activity. Presence mode shapes the weights; drives vary them.
/// Explicit user restrictions are enforced elsewhere (TerritoryService) and always win.
/// </summary>
public sealed class BehaviorController
{
    private readonly Random _rng;

    public BehaviorController(Random rng) => _rng = rng;

    public Dictionary<Activity, double> Weights(in DecisionContext c)
    {
        var d = c.Drives;
        var w = new Dictionary<Activity, double>();
        var sleepy = (1 - d.Energy) + (c.UserIdleSeconds > 240 ? 1.2 : 0);
        switch (c.Mode)
        {
            case PresenceMode.Focus:
                if (!c.AtRestSpot) w[Activity.GoRestSpot] = 6;
                w[Activity.Sit] = 3;
                w[Activity.Sleep] = 0.6 + sleepy;
                w[Activity.InspectBackpack] = c.HasItems ? 0.4 : 0;
                w[Activity.Stretch] = 0.15;
                break;
            case PresenceMode.Quiet:
                w[Activity.Sit] = 3;
                w[Activity.Sleep] = 1 + sleepy * 1.5;
                w[Activity.InspectBackpack] = c.HasItems ? 0.6 : 0;
                w[Activity.Stretch] = 0.3;
                w[Activity.Wander] = 0.35;
                w[Activity.LookAround] = 0.5;
                break;
            case PresenceMode.Company:
                w[Activity.StayNearUser] = 3 + d.SocialInterest * 2;
                w[Activity.Sit] = 1.8;
                w[Activity.LookAround] = 1.2;
                w[Activity.Stretch] = 0.3;
                w[Activity.Sleep] = sleepy * 0.6;
                break;
            case PresenceMode.Play:
                w[Activity.ChaseCursor] = c.CursorNearFloor ? 3.5 : 0;
                w[Activity.Run] = c.ReducedMotion ? 0 : 2 + d.Playfulness * 2;
                w[Activity.Wander] = 1.5;
                w[Activity.Hop] = c.ReducedMotion ? 0 : 1.2 + d.Playfulness;
                w[Activity.Explore] = c.CanExplore ? 0.8 + d.Curiosity : 0;
                w[Activity.PeekEdge] = c.NearEdge ? 0.6 : 0;
                w[Activity.Sit] = 0.3;
                break;
            default: // Normal
                w[Activity.Wander] = 2.2 + d.Curiosity;
                w[Activity.Sit] = 1.0 + (1 - d.Energy);
                w[Activity.Sleep] = d.Energy < 0.3 || c.UserIdleSeconds > 240 ? 0.6 + sleepy : 0.05;
                w[Activity.Stretch] = 0.45;
                w[Activity.Yawn] = d.Energy < 0.5 ? 0.5 : 0.1;
                w[Activity.LookAround] = 1.0;
                w[Activity.PeekEdge] = c.NearEdge ? 0.6 + d.Curiosity : 0;
                w[Activity.InspectBackpack] = c.HasItems ? 0.35 : 0;
                w[Activity.Explore] = c.CanExplore ? 0.3 + d.Curiosity * 0.8 : 0;
                w[Activity.Hop] = c.ReducedMotion ? 0 : 0.1 + d.Playfulness * 0.2;
                break;
        }
        return w;
    }

    public Activity Choose(in DecisionContext c)
    {
        var w = Weights(c);
        var total = w.Values.Sum();
        if (total <= 0) return Activity.LookAround;
        var r = _rng.NextDouble() * total;
        foreach (var (k, v) in w)
        {
            r -= v;
            if (r <= 0) return k;
        }
        return w.Keys.Last();
    }

    /// <summary>Seconds to wait before the next autonomous decision.</summary>
    public double NextDecisionDelay(PresenceMode mode) => mode switch
    {
        PresenceMode.Play => 1.0 + _rng.NextDouble() * 2.5,
        PresenceMode.Focus => 20 + _rng.NextDouble() * 30,
        PresenceMode.Quiet => 12 + _rng.NextDouble() * 20,
        PresenceMode.Company => 4 + _rng.NextDouble() * 6,
        _ => 3 + _rng.NextDouble() * 7,
    };
}
