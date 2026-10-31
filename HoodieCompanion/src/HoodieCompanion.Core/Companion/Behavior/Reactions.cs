using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Settings;

namespace HoodieCompanion.Companion.Behavior;

/// <summary>Things that happen to Hoodie or around it. Each maps to a reaction through <see cref="ReactionSystem"/>.</summary>
public enum PetEvent
{
    CursorApproached,
    CursorRushed,
    CursorHovered,
    CursorLost,
    Clicked,
    Grabbed,
    GrabbedWhileAsleep,
    Released,
    LandedHard,
    ItemOffered,
    ItemStored,
    ItemOpened,
    ItemMissing,
    BackpackFull,
    ReminderDue,
    AlertIgnored,
    TimerDone,
    PcBusy,
    PcCalm,
    DownloadActive,
    TaskSucceeded,
    TaskFailed,
    BoundaryHit,
    UserReturned,
    NightTime,
    HourChime,
    NoteSaved,
    NoteAbandoned,
}

/// <summary>
/// Priority tiers, highest first. A reaction may interrupt whatever runs at a lower tier; equal or
/// higher tiers are never interrupted by a reaction (they finish, or the reaction is dropped).
/// </summary>
public enum ReactionPriority
{
    /// <summary>Emergency hide, fullscreen, Alone mode (handled by presence; never overridden).</summary>
    Safety = 0,
    /// <summary>Grab, throw, fall, landing: physics always wins.</summary>
    Physical = 1,
    /// <summary>Explicit user commands (Come here, Go home, panel actions).</summary>
    Command = 2,
    /// <summary>Reminders and timers.</summary>
    Alert = 3,
    /// <summary>Utility feedback: backpack, notes, open/missing items.</summary>
    Utility = 4,
    /// <summary>Contextual reactions to events (cursor, PC load, downloads, clock).</summary>
    Contextual = 5,
    /// <summary>Idle director micro-actions (look, scratch, sigh...).</summary>
    Idle = 6,
    /// <summary>Base loops (breathing variants, sitting, lying).</summary>
    Base = 7,
}

/// <summary>One possible reaction: a clip (or short chain) with a weight and an optional condition.</summary>
public sealed record ReactionOption(AnimClip[] Clips, double Weight, Func<Mind, PresenceMode, bool>? When = null);

public sealed record ReactionRule(PetEvent Event, ReactionPriority Priority, double CooldownSeconds, bool CalmModesToo, ReactionOption[] Options);

/// <summary>The chosen reaction for an event.</summary>
public readonly record struct Reaction(PetEvent Event, ReactionPriority Priority, AnimClip[] Clips);

/// <summary>
/// Event → Reaction table. Pure and deterministic given the random source, so it is unit-testable.
/// Rules decide *what* expresses the event; <see cref="PetController"/> decides *whether* it may play now
/// (state, interruption rules, presence mode).
/// </summary>
public sealed class ReactionSystem
{
    private readonly Random _rng;
    private readonly Dictionary<PetEvent, double> _lastFired = new();

    public ReactionSystem(Random rng) => _rng = rng;

    private static ReactionOption O(double w, params AnimClip[] clips) => new(clips, w);
    private static ReactionOption O(double w, Func<Mind, PresenceMode, bool> when, params AnimClip[] clips) => new(clips, w, when);

    public static readonly IReadOnlyList<ReactionRule> Rules = new List<ReactionRule>
    {
        new(PetEvent.CursorApproached, ReactionPriority.Contextual, 8, false, new[]
        {
            O(3, AnimClip.Curious), O(2, AnimClip.HeadTilt), O(1.2, AnimClip.NoticeMovement),
            O(1.5, (m, _) => m.Mood > 0.6, AnimClip.Wave),
            O(1.5, (_, p) => p == PresenceMode.Play, AnimClip.ReachCursor),
            O(1, (m, _) => m.Stress > 0.5, AnimClip.Suspicious),
        }),
        new(PetEvent.CursorRushed, ReactionPriority.Contextual, 6, false, new[]
        {
            O(3, AnimClip.Surprised), O(2, AnimClip.Dodge), O(1, (m, _) => m.Stress > 0.4, AnimClip.Scared),
        }),
        new(PetEvent.CursorHovered, ReactionPriority.Contextual, 20, false, new[]
        {
            O(2, AnimClip.ReachCursor), O(1.5, AnimClip.LookUp), O(1, (_, p) => p == PresenceMode.Play, AnimClip.CatchCursor),
            O(1, (m, _) => m.Stress > 0.5, AnimClip.Annoyed),
        }),
        new(PetEvent.CursorLost, ReactionPriority.Contextual, 30, false, new[] { O(1, AnimClip.SearchCursor) }),
        new(PetEvent.Clicked, ReactionPriority.Command, 0, true, new[]
        {
            O(3, AnimClip.Wave), O(1.2, (m, _) => m.Affection > 0.5, AnimClip.Happy), O(0.8, AnimClip.HeadTilt),
        }),
        new(PetEvent.Grabbed, ReactionPriority.Physical, 0, true, new[] { O(1, AnimClip.GrabReaction) }),
        new(PetEvent.GrabbedWhileAsleep, ReactionPriority.Physical, 0, true, new[] { O(1, AnimClip.WakeStartled) }),
        new(PetEvent.Released, ReactionPriority.Physical, 0, true, new[] { O(1, AnimClip.RecoverFromThrow) }),
        new(PetEvent.LandedHard, ReactionPriority.Physical, 0, true, new[]
        {
            O(3, AnimClip.Recover, AnimClip.RecoverFromThrow),
            O(1, (m, _) => m.Stress > 0.5, AnimClip.Recover, AnimClip.Angry),
            O(1, (m, _) => m.Mood > 0.6, AnimClip.Recover, AnimClip.Embarrassed),
        }),
        new(PetEvent.ItemOffered, ReactionPriority.Utility, 0, true, new[] { O(1, AnimClip.NoticeItem) }),
        new(PetEvent.ItemStored, ReactionPriority.Utility, 0, true, new[]
        {
            O(3, AnimClip.CheckResult), O(1, (m, _) => m.BackpackItems > 24, AnimClip.BackpackHeavy), O(1, AnimClip.ThumbsUp),
        }),
        new(PetEvent.ItemOpened, ReactionPriority.Utility, 0, true, new[] { O(1, AnimClip.PresentItem) }),
        new(PetEvent.ItemMissing, ReactionPriority.Utility, 0, true, new[] { O(3, AnimClip.MissingItem), O(1, AnimClip.MissingItem, AnimClip.Confused) }),
        new(PetEvent.BackpackFull, ReactionPriority.Contextual, 600, false, new[] { O(1, AnimClip.BackpackHeavy) }),
        new(PetEvent.ReminderDue, ReactionPriority.Alert, 0, true, new[] { O(1, AnimClip.ReminderAlert) }),
        new(PetEvent.AlertIgnored, ReactionPriority.Alert, 15, true, new[] { O(3, AnimClip.Knock), O(1, AnimClip.Point) }),
        new(PetEvent.TimerDone, ReactionPriority.Alert, 0, true, new[] { O(1, AnimClip.TimerAlert) }),
        new(PetEvent.PcBusy, ReactionPriority.Contextual, 600, false, new[]
        {
            O(3, AnimClip.CarryLoad), O(1, AnimClip.PCBusy),
        }),
        new(PetEvent.PcCalm, ReactionPriority.Contextual, 600, false, new[] { O(2, AnimClip.PCIdle), O(1, AnimClip.Sigh) }),
        new(PetEvent.DownloadActive, ReactionPriority.Contextual, 600, false, new[]
        {
            O(3, AnimClip.CatchPackage), O(1, AnimClip.DownloadWatching),
        }),
        new(PetEvent.TaskSucceeded, ReactionPriority.Utility, 0, true, new[]
        {
            O(3, AnimClip.Success), O(1.5, AnimClip.ThumbsUp), O(1, (m, _) => m.Mood > 0.6, AnimClip.Proud),
            O(0.8, (_, p) => p == PresenceMode.Play, AnimClip.JumpForJoy),
        }),
        new(PetEvent.TaskFailed, ReactionPriority.Utility, 0, true, new[]
        {
            O(3, AnimClip.Error), O(1.5, AnimClip.Facepalm), O(1, AnimClip.Confused), O(0.6, (m, _) => m.Stress > 0.5, AnimClip.Frustrated),
        }),
        new(PetEvent.BoundaryHit, ReactionPriority.Contextual, 5, true, new[] { O(1, AnimClip.BoundaryBump) }),
        new(PetEvent.UserReturned, ReactionPriority.Contextual, 60, true, new[]
        {
            O(3, AnimClip.NoticeMovement, AnimClip.HeadTilt, AnimClip.Wave),
            O(1.5, (m, _) => m.Affection > 0.5, AnimClip.NoticeMovement, AnimClip.Happy),
            O(1, AnimClip.NoticeMovement, AnimClip.Wave, AnimClip.Excited),
        }),
        new(PetEvent.NightTime, ReactionPriority.Contextual, 1800, false, new[] { O(2, AnimClip.Shiver), O(2, AnimClip.Yawn) }),
        new(PetEvent.HourChime, ReactionPriority.Contextual, 1800, false, new[] { O(1, AnimClip.CheckTime) }),
        new(PetEvent.NoteSaved, ReactionPriority.Utility, 0, true, new[] { O(2, AnimClip.CheckResult), O(1, AnimClip.ThumbsUp) }),
        new(PetEvent.NoteAbandoned, ReactionPriority.Utility, 0, true, new[] { O(1, AnimClip.TearPage) }),
    };

    private static readonly Dictionary<PetEvent, ReactionRule> ByEvent = Rules.ToDictionary(r => r.Event);

    public static ReactionRule RuleFor(PetEvent e) => ByEvent[e];

    /// <summary>
    /// Picks a reaction for the event, or null when it is on cooldown or the presence mode keeps Hoodie calm
    /// (Focus / Quiet only let non-contextual reactions through).
    /// </summary>
    public Reaction? Resolve(PetEvent e, Mind mind, PresenceMode mode, double now)
    {
        var rule = ByEvent[e];
        if (!rule.CalmModesToo && mode is PresenceMode.Focus or PresenceMode.Quiet or PresenceMode.Alone) return null;
        if (rule.CooldownSeconds > 0 && _lastFired.TryGetValue(e, out var at) && now - at < rule.CooldownSeconds) return null;
        var options = rule.Options.Where(o => o.When is null || o.When(mind, mode)).ToList();
        if (options.Count == 0) return null;
        var total = options.Sum(o => o.Weight);
        var r = _rng.NextDouble() * total;
        var pick = options[^1];
        foreach (var o in options)
        {
            r -= o.Weight;
            if (r <= 0) { pick = o; break; }
        }
        _lastFired[e] = now;
        return new Reaction(e, rule.Priority, pick.Clips);
    }

    /// <summary>Tier of what Hoodie is doing right now, for the interruption rule.</summary>
    public static ReactionPriority TierOf(BehaviorState s) => s switch
    {
        BehaviorState.Hidden or BehaviorState.Leaving or BehaviorState.Returning or BehaviorState.Vanishing or BehaviorState.Appearing => ReactionPriority.Safety,
        BehaviorState.Grabbed or BehaviorState.Airborne or BehaviorState.Jumping or BehaviorState.Landing or BehaviorState.Recovering
            or BehaviorState.Climbing => ReactionPriority.Physical,
        BehaviorState.Alert => ReactionPriority.Alert,
        BehaviorState.ReceivingItem or BehaviorState.Activity => ReactionPriority.Utility,
        BehaviorState.Emote => ReactionPriority.Contextual,
        BehaviorState.Walking or BehaviorState.Turning => ReactionPriority.Idle,
        _ => ReactionPriority.Base,
    };

    /// <summary>The interruption rule: a reaction may start only if it outranks what is running.</summary>
    public static bool MayInterrupt(ReactionPriority reaction, ReactionPriority running) => reaction < running;
}
