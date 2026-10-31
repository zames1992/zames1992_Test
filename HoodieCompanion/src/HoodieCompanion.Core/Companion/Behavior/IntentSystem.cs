using HoodieCompanion.Companion.Perception;
using HoodieCompanion.Settings;

namespace HoodieCompanion.Companion.Behavior;

/// <summary>Everything the intent layer weighs, beyond the basic decision context.</summary>
public readonly record struct IntentContext(
    DecisionContext Basics,
    Personality Traits,
    Mind Mind,
    bool UserBusy,
    bool Typing,
    AppCategory Foreground,
    bool Gaming,
    bool PcHot,
    bool Night,
    double WorkSessionMinutes,
    bool CuriousAboutWindow,
    bool BreakDue,
    bool HasFavoritePlace,
    Func<string, bool> Unlocked,
    Func<string, bool> HasItem);

/// <summary>One option Hoodie considers, with the reason it would do it.</summary>
public readonly record struct IntentOption(Activity Activity, double Score, string Reason);

/// <summary>
/// Intent layer: turns (mind + traits + perception + memory) into scored options, each with a reason, and picks
/// one. Doing nothing is always an option and often wins, especially while the user is busy. The same trait
/// nudges many options (curiosity makes exploring, peeking, climbing and investigating all more likely), so the
/// character shows through the pattern of choices rather than through any single mechanic.
/// </summary>
public sealed class IntentSystem
{
    private readonly BehaviorController _base;
    private readonly Random _rng;

    public IntentSystem(BehaviorController baseWeights, Random rng)
    {
        _base = baseWeights;
        _rng = rng;
    }

    private static readonly Dictionary<Activity, string> DefaultReason = new()
    {
        [Activity.Wander] = "stretching its legs",
        [Activity.Explore] = "wants to see the other screen",
        [Activity.Sit] = "wants to rest a bit",
        [Activity.Sleep] = "tired",
        [Activity.Stretch] = "stiff from standing",
        [Activity.Yawn] = "a little sleepy",
        [Activity.LookAround] = "wondering what's going on",
        [Activity.PeekEdge] = "what's over the edge?",
        [Activity.InspectBackpack] = "checking its things",
        [Activity.Hop] = "feels bouncy",
        [Activity.Run] = "full of energy",
        [Activity.StayNearUser] = "wants to be near you",
        [Activity.ChaseCursor] = "the pointer looks catchable",
        [Activity.GoRestSpot] = "going somewhere quiet",
        [Activity.ReadBook] = "in the mood for a story",
        [Activity.UseLaptop] = "has something to look up",
        [Activity.WriteNotes] = "had an idea",
        [Activity.Dance] = "happy",
        [Activity.JumpForJoy] = "happy",
        [Activity.SitEdge] = "likes dangling its legs",
        [Activity.LieAround] = "lazy",
        [Activity.VisitSurface] = "curious what's up there",
        [Activity.ClimbWall] = "wants to climb",
        [Activity.HopDown] = "done up here",
        [Activity.Nothing] = "content just being here",
    };

    public List<IntentOption> Evaluate(in IntentContext c)
    {
        var t = c.Traits;
        var m = c.Mind;
        var basics = c.Basics;
        var w = _base.Weights(basics);
        var options = new Dictionary<Activity, (double Score, string Reason)>();
        foreach (var (a, v) in w) options[a] = (v, DefaultReason.TryGetValue(a, out var r) ? r : a.ToString());

        void Mul(Activity a, double k, string? reason = null)
        {
            if (!options.TryGetValue(a, out var o)) return;
            options[a] = (o.Score * k, reason ?? o.Reason);
        }

        void Add(Activity a, double v, string reason)
        {
            if (v <= 0) return;
            options.TryGetValue(a, out var o);
            options[a] = (o.Score + v, o.Score >= v ? o.Reason ?? reason : reason);
        }

        // Traits shape everything a little.
        foreach (var a in new[] { Activity.Explore, Activity.PeekEdge, Activity.VisitSurface, Activity.ClimbWall, Activity.LookAround })
            Mul(a, 0.6 + t.Curiosity);
        foreach (var a in new[] { Activity.Dance, Activity.Hop, Activity.Run, Activity.ChaseCursor, Activity.JumpForJoy })
            Mul(a, 0.5 + t.Playfulness);
        foreach (var a in new[] { Activity.VisitSurface, Activity.ClimbWall, Activity.Hop })
            Mul(a, 0.7 + (t.Confidence - t.Caution) * 0.8);
        foreach (var a in new[] { Activity.Sit, Activity.ReadBook, Activity.LieAround, Activity.SitEdge })
            Mul(a, 0.7 + t.Comfort * 0.6 + (1 - t.Energy) * 0.3);
        Mul(Activity.StayNearUser, 0.6 + t.Attachment);

        // Doing nothing is a real choice.
        var calmMode = basics.Mode is PresenceMode.Focus or PresenceMode.Quiet;
        Add(Activity.Nothing, 1.4 + t.Comfort * 0.6 + (calmMode ? 2 : 0) + (1 - m.Energy) * 0.6, "content just being here");

        // Unlockable flourishes appear over time.
        if (!c.Unlocked("dance")) options.Remove(Activity.Dance);
        if (!c.Unlocked("climb-wall")) options.Remove(Activity.ClimbWall);

        // Being considerate: while the user works, Hoodie keeps to quiet things (or keeps them company quietly).
        if (c.UserBusy)
        {
            foreach (var a in new[] { Activity.Wander, Activity.Run, Activity.Explore, Activity.Hop, Activity.Dance, Activity.JumpForJoy,
                         Activity.ChaseCursor, Activity.VisitSurface, Activity.ClimbWall, Activity.PeekEdge, Activity.StayNearUser })
                Mul(a, 0.2);
            Add(Activity.Nothing, 3, "you're busy, so it keeps quiet");
            if (c.Typing && c.WorkSessionMinutes > 3)
                Add(Activity.WorkAlongside, 1.5 + t.Attachment * 2 + (AppCategories.IsWork(c.Foreground) ? 1 : 0), "working alongside you");
        }
        if (c.Gaming)
        {
            foreach (var k in options.Keys.ToList()) if (k is not (Activity.Nothing or Activity.Sit or Activity.WatchUser)) Mul(k, 0.15);
            Add(Activity.WatchUser, 2 + t.Attachment * 2, "watching you play");
        }
        if (c.Night)
        {
            foreach (var a in new[] { Activity.Run, Activity.Hop, Activity.Dance, Activity.ChaseCursor, Activity.ClimbWall }) Mul(a, 0.4);
            Mul(Activity.Sleep, 1.6, "it's late");
            Mul(Activity.LieAround, 1.5, "it's late");
        }

        // Perception-driven intents.
        if (c.CuriousAboutWindow && !c.UserBusy && basics.Mode is not (PresenceMode.Focus or PresenceMode.Quiet))
            Add(Activity.InvestigateWindow, 1.5 + t.Curiosity * 4, "something new appeared");
        if (c.PcHot)
            Add(Activity.CoolDown, 3 + t.Comfort * 2, c.HasItem("fan") ? "the computer is hot, fetch the fan" : "the computer is working hard");
        if (c.BreakDue && basics.Mode is not (PresenceMode.Focus or PresenceMode.Alone))
            Add(Activity.SuggestBreak, 4 + t.Attachment * 3, "you've been working for ages");
        if (c.HasFavoritePlace && !c.UserBusy)
            Add(Activity.FavoritePlace, 0.4 + t.Comfort * 0.8, "going to its favourite spot");
        if (c.HasItem("ball") && !c.UserBusy && !calmMode && !basics.ReducedMotion)
            Add(Activity.PlayBall, 0.3 + t.Playfulness * 1.2 + m.Boredom, "bored, kicks the ball around");
        if (!c.UserBusy && basics.Mode is PresenceMode.Normal or PresenceMode.Company && basics.UserIdleSeconds < 20)
            Add(Activity.WatchUser, 0.3 + t.Attachment * 0.8 + t.Curiosity * 0.3, "curious what you're doing");

        return options.Where(o => o.Value.Score > 0).Select(o => new IntentOption(o.Key, o.Value.Score, o.Value.Reason))
                      .OrderByDescending(o => o.Score).ToList();
    }

    /// <summary>Weighted pick among the options (the best ones dominate, but Hoodie is not a clock).</summary>
    public IntentOption Choose(in IntentContext c)
    {
        var options = Evaluate(c);
        if (options.Count == 0) return new IntentOption(Activity.Nothing, 1, "nothing to do");
        // Sharpen: squares favour clear preferences without making the choice deterministic.
        var total = options.Sum(o => o.Score * o.Score);
        var r = _rng.NextDouble() * total;
        foreach (var o in options)
        {
            r -= o.Score * o.Score;
            if (r <= 0) return o;
        }
        return options[0];
    }

    /// <summary>Seconds to the next decision: calmer modes and a busy user mean longer pauses.</summary>
    public double NextDecisionDelay(PresenceMode mode, bool userBusy)
    {
        var d = mode switch
        {
            PresenceMode.Play => 2.0 + _rng.NextDouble() * 4,
            PresenceMode.Focus => 25 + _rng.NextDouble() * 35,
            PresenceMode.Quiet => 12 + _rng.NextDouble() * 18,
            PresenceMode.Company => 7 + _rng.NextDouble() * 10,
            _ => 5 + _rng.NextDouble() * 10,
        };
        return userBusy ? d * 1.8 : d;
    }
}
