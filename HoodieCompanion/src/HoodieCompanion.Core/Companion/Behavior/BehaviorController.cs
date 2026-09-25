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
    ReadBook,
    UseLaptop,
    WriteNotes,
    Dance,
    JumpForJoy,
    /// <summary>Sit on the edge of the floor (e.g. the top of the taskbar) and swing the legs.</summary>
    SitEdge,
    /// <summary>Lie down on the floor for a while, kicking a foot.</summary>
    LieAround,
    /// <summary>Jump or climb onto a window top / desktop icon.</summary>
    VisitSurface,
    /// <summary>Climb up the side of the screen and slide back down.</summary>
    ClimbWall,
    /// <summary>Hop down from the platform Hoodie stands on.</summary>
    HopDown,
    /// <summary>Do nothing on purpose: just be there, breathe, look around a little.</summary>
    Nothing,
    /// <summary>Go and have a look at a window / app that just appeared (curiosity).</summary>
    InvestigateWindow,
    /// <summary>The PC is working hard: fetch the fan (or haul the crate) and cool things down.</summary>
    CoolDown,
    /// <summary>The user has worked for a long time: come closer with a mug, stretch, suggest a break.</summary>
    SuggestBreak,
    /// <summary>The user is typing away: sit down with the laptop and "work" alongside, quietly.</summary>
    WorkAlongside,
    /// <summary>Go to a spot Hoodie remembers liking.</summary>
    FavoritePlace,
    /// <summary>Play with the ball.</summary>
    PlayBall,
    /// <summary>Sit where it can see the pointer and watch what the user is doing.</summary>
    WatchUser,
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
    bool ReducedMotion,
    AfkPhase Afk = AfkPhase.Present,
    bool OnLedge = false,
    double Boredom = 0,
    double Sleepiness = 0,
    bool CanVisitSurface = false,
    bool OnSurface = false,
    bool CanClimbWall = false,
    double SurfaceSeconds = 0);

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
        var sleepy = (1 - d.Energy) + c.Sleepiness + (c.Afk >= AfkPhase.Sleepy ? 1.2 : 0);
        switch (c.Mode)
        {
            case PresenceMode.Focus:
                if (!c.AtRestSpot) w[Activity.GoRestSpot] = 6;
                w[Activity.Sit] = 3;
                w[Activity.Sleep] = 0.6 + sleepy;
                w[Activity.InspectBackpack] = c.HasItems ? 0.4 : 0;
                w[Activity.Stretch] = 0.15;
                w[Activity.ReadBook] = c.AtRestSpot ? 2.5 : 0;
                w[Activity.UseLaptop] = c.AtRestSpot ? 2.0 : 0;
                break;
            case PresenceMode.Quiet:
                w[Activity.Sit] = 3;
                w[Activity.Sleep] = 1 + sleepy * 1.5;
                w[Activity.InspectBackpack] = c.HasItems ? 0.6 : 0;
                w[Activity.Stretch] = 0.3;
                w[Activity.Wander] = 0.35;
                w[Activity.LookAround] = 0.5;
                w[Activity.ReadBook] = 2.2;
                w[Activity.UseLaptop] = 1.2;
                w[Activity.WriteNotes] = 0.6;
                break;
            case PresenceMode.Company:
                w[Activity.StayNearUser] = 3 + d.SocialInterest * 2;
                w[Activity.Sit] = 1.8;
                w[Activity.LookAround] = 1.2;
                w[Activity.Stretch] = 0.3;
                w[Activity.Sleep] = sleepy * 0.6;
                w[Activity.ReadBook] = 0.8;
                w[Activity.UseLaptop] = 0.8;
                w[Activity.Dance] = 0.3;
                break;
            case PresenceMode.Play:
                w[Activity.ChaseCursor] = c.CursorNearFloor ? 3.5 : 0;
                w[Activity.Run] = c.ReducedMotion ? 0 : 2 + d.Playfulness * 2;
                w[Activity.Wander] = 1.5;
                w[Activity.Hop] = c.ReducedMotion ? 0 : 1.2 + d.Playfulness;
                w[Activity.Explore] = c.CanExplore ? 0.8 + d.Curiosity : 0;
                w[Activity.PeekEdge] = c.NearEdge ? 0.6 : 0;
                w[Activity.Sit] = 0.3;
                w[Activity.Dance] = c.ReducedMotion ? 0 : 1.2;
                w[Activity.JumpForJoy] = c.ReducedMotion ? 0 : 0.8;
                break;
            default: // Normal
                w[Activity.Wander] = 2.2 + d.Curiosity;
                w[Activity.Sit] = 1.0 + (1 - d.Energy);
                w[Activity.Sleep] = d.Energy < 0.3 || c.Afk >= AfkPhase.Sleepy ? 0.6 + sleepy : 0.05;
                w[Activity.Stretch] = 0.45;
                w[Activity.Yawn] = d.Energy < 0.5 ? 0.5 : 0.1;
                w[Activity.LookAround] = 1.0;
                w[Activity.PeekEdge] = c.NearEdge ? 0.6 + d.Curiosity : 0;
                w[Activity.InspectBackpack] = c.HasItems ? 0.35 : 0;
                w[Activity.Explore] = c.CanExplore ? 0.3 + d.Curiosity * 0.8 : 0;
                w[Activity.Hop] = c.ReducedMotion ? 0 : 0.1 + d.Playfulness * 0.2;
                w[Activity.ReadBook] = 0.7 + (1 - d.Energy) * 0.5;
                w[Activity.UseLaptop] = 0.6 + d.Curiosity * 0.4;
                w[Activity.WriteNotes] = 0.4;
                w[Activity.Dance] = c.ReducedMotion ? 0 : 0.15 + d.Playfulness * 0.3;
                w[Activity.JumpForJoy] = c.ReducedMotion ? 0 : 0.1;
                w[Activity.SitEdge] = c.OnLedge ? 0.35 + c.Boredom * 0.8 : 0;
                w[Activity.LieAround] = 0.15 + c.Boredom * 0.5 + c.Sleepiness * 0.6;
                break;
        }
        if (c.Mode == PresenceMode.Quiet)
        {
            w[Activity.SitEdge] = c.OnLedge ? 0.8 : 0;
            w[Activity.LieAround] = 0.6 + c.Sleepiness;
        }
        if (c.Mode == PresenceMode.Company) w[Activity.SitEdge] = c.OnLedge ? 0.6 : 0;
        if (c.Mode is PresenceMode.Normal or PresenceMode.Play or PresenceMode.Company)
        {
            if (c.CanVisitSurface) Add(w, Activity.VisitSurface, (c.Mode == PresenceMode.Play ? 0.9 : 0.3) + d.Curiosity * 0.4 + (c.Afk == AfkPhase.Exploring ? 2 : 0));
            if (c.CanClimbWall && !c.ReducedMotion) Add(w, Activity.ClimbWall, (c.Mode == PresenceMode.Play ? 0.6 : 0.15) + d.Playfulness * 0.3 + (c.Afk == AfkPhase.Exploring ? 1 : 0));
        }
        if (c.OnSurface)
        {
            // Up on a platform only a few things make sense; after a while (not right after arriving:
            // climbing up just to leave looks random) it hops down again.
            var keep = new[] { Activity.SitEdge, Activity.LookAround, Activity.Wander, Activity.Stretch, Activity.Yawn, Activity.Sit, Activity.ReadBook };
            foreach (var k in w.Keys.ToList()) if (!keep.Contains(k)) w.Remove(k);
            Add(w, Activity.SitEdge, 1.5);
            if (c.SurfaceSeconds >= 20) Add(w, Activity.HopDown, 0.3 + 0.9 * Math.Min(1, (c.SurfaceSeconds - 20) / 40) + c.Boredom * 0.5);
        }
        // Away-from-keyboard timeline biases (the user is not watching; Hoodie entertains itself).
        if (c.Mode is PresenceMode.Normal or PresenceMode.Company or PresenceMode.Play)
        {
            switch (c.Afk)
            {
                case AfkPhase.Relaxed:
                    Scale(w, Activity.Run, 0.3);
                    Scale(w, Activity.Sit, 1.6);
                    Scale(w, Activity.ReadBook, 1.4);
                    break;
                case AfkPhase.Bored:
                    Scale(w, Activity.Sit, 1.5);
                    Add(w, Activity.SitEdge, c.OnLedge ? 1.2 : 0);
                    Add(w, Activity.LieAround, 0.8);
                    Add(w, Activity.LookAround, 0.8);
                    break;
                case AfkPhase.Exploring:
                    Add(w, Activity.Explore, c.CanExplore ? 3 : 0);
                    Add(w, Activity.PeekEdge, c.NearEdge ? 1.5 : 0.4);
                    Add(w, Activity.Wander, 2);
                    Add(w, Activity.SitEdge, c.OnLedge ? 1 : 0);
                    break;
                case AfkPhase.Sleepy:
                    Scale(w, Activity.Run, 0);
                    Scale(w, Activity.Hop, 0);
                    Scale(w, Activity.Dance, 0);
                    Add(w, Activity.Yawn, 1);
                    Add(w, Activity.LieAround, 1.5);
                    Add(w, Activity.Sit, 1.5);
                    break;
                case AfkPhase.Asleep:
                    Add(w, Activity.Sleep, 6);
                    break;
            }
        }
        return w;
    }

    private static void Scale(Dictionary<Activity, double> w, Activity a, double k) { if (w.ContainsKey(a)) w[a] *= k; }

    private static void Add(Dictionary<Activity, double> w, Activity a, double v) => w[a] = (w.TryGetValue(a, out var x) ? x : 0) + v;

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
        PresenceMode.Quiet => 8 + _rng.NextDouble() * 12,
        PresenceMode.Company => 4 + _rng.NextDouble() * 6,
        _ => 2 + _rng.NextDouble() * 4.5,
    };
}
