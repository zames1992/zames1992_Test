using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Geometry;
using HoodieCompanion.Settings;

namespace HoodieCompanion.Companion.Behavior;

/// <summary>
/// Glue between the <see cref="Mind"/>, the <see cref="ReactionSystem"/> and the behavior states:
/// event reactions with the interruption rule, the away-from-keyboard timeline, time-of-day moods and
/// alerts that escalate politely (knocking on the glass).
/// </summary>
public sealed partial class PetController
{
    private int _lastHour = -1;
    private double _nextNightMood;
    private double _nextKnock;

    /// <summary>Tier of what is running now (an emote knows which tier started it).</summary>
    public ReactionPriority RunningTier => Machine.State == BehaviorState.Emote ? _emoteTier : ReactionSystem.TierOf(Machine.State);

    /// <summary>
    /// Fires an event. The reaction table picks how Hoodie expresses it; it plays only if it outranks what is
    /// running (see <see cref="ReactionSystem.MayInterrupt"/>). Returns true when something started.
    /// </summary>
    public bool React(PetEvent e)
    {
        var tier = RunningTier;
        var rule = ReactionSystem.RuleFor(e);
        if (!ReactionSystem.MayInterrupt(rule.Priority, tier)) return false;
        // Resting Hoodie only gets up for things that concern the user directly.
        if (Machine.State is BehaviorState.Sitting or BehaviorState.Sleeping && rule.Priority >= ReactionPriority.Contextual && e != PetEvent.UserReturned)
            return false;
        if (Reactions.Resolve(e, Mind, EffectiveMode, _time) is not { } r) return false;
        Log?.Invoke($"react {e} -> {string.Join(", ", r.Clips)}");
        return PlayChain(r.Clips, r.Priority);
    }

    private bool PlayChain(IReadOnlyList<AnimClip> clips, ReactionPriority tier)
    {
        if (clips.Count == 0) return false;
        if (Machine.State is BehaviorState.Sitting or BehaviorState.Sleeping)
        {
            EnsureStanding(() => PlayChain(clips, tier));
            return true;
        }
        if (Machine.State == BehaviorState.Activity && _activity is not null)
        {
            Interject(clips[0]);
            return true;
        }
        if (!PlayEmoteAt(clips[0], null, tier)) return false;
        _emoteChain.Clear();
        for (var i = 1; i < clips.Count; i++) _emoteChain.Enqueue(clips[i]);
        return true;
    }

    private double AfkDecisionFactor => Mind.Afk switch
    {
        AfkPhase.Relaxed => 1.5,
        AfkPhase.Bored => 1.3,
        AfkPhase.Exploring => 0.9,
        AfkPhase.Sleepy => 2.0,
        _ => 1.0,
    };

    /// <summary>
    /// The away-from-keyboard timeline: 5 min relaxed → 15 bored → 30 explores the desktop → 60 sleepy →
    /// 90 finds a spot and sleeps. When the user returns: wake → notice the cursor → recognise → greet.
    /// </summary>
    private void OnAfkPhaseChanged(AfkPhase old, AfkPhase now)
    {
        Log?.Invoke($"afk {old} -> {now}");
        if (now == AfkPhase.Present)
        {
            if (old >= AfkPhase.Relaxed) GreetReturningUser(old);
            return;
        }
        if (IsHiddenMode || !CanReact && Machine.State != BehaviorState.Sleeping) return;
        switch (now)
        {
            case AfkPhase.Relaxed:
                // Slower decisions (AfkDecisionFactor); nothing visible changes abruptly.
                break;
            case AfkPhase.Bored:
                Mind.Boredom = Math.Max(Mind.Boredom, 0.6);
                if (Machine.State == BehaviorState.Idle) PlayEmote(AnimClip.Sigh);
                break;
            case AfkPhase.Exploring:
                Mind.Boredom = Math.Max(Mind.Boredom, 0.7);
                Drives.Curiosity = Math.Max(Drives.Curiosity, 0.85);
                ScheduleDecision(1);
                break;
            case AfkPhase.Sleepy:
                Mind.Sleepiness = Math.Max(Mind.Sleepiness, 0.7);
                if (Machine.State == BehaviorState.Idle) PlayEmote(AnimClip.Yawn);
                break;
            case AfkPhase.Asleep:
                if (Machine.State == BehaviorState.Sleeping) break;
                _sleptBecauseUserAway = true;
                EnsureStanding(() => GoToRestSpot(then: () =>
                {
                    _sleptBecauseUserAway = true;
                    BeginSit(sleepAfter: true);
                }));
                break;
        }
    }

    private void GreetReturningUser(AfkPhase wasPhase)
    {
        Mind.OnUserInteraction();
        if (IsHiddenMode) return;
        _sleptBecauseUserAway = false;
        void Greet()
        {
            // Notice the cursor → recognise the user → greet (chain from the reaction table).
            _lookScriptUntil = 0;
            React(PetEvent.UserReturned);
        }
        if (Machine.State == BehaviorState.Sleeping)
        {
            WakeUp(() => StandUp(Greet));
            return;
        }
        if (Machine.State == BehaviorState.Sitting)
        {
            StandUp(Greet);
            return;
        }
        if (Machine.State == BehaviorState.Activity && _activity is { FromPanel: false }) StopActivity();
        if (CanReact && Machine.State is BehaviorState.Idle or BehaviorState.Walking) Greet();
    }

    private void UpdateClock(int hour)
    {
        Mind.Hour = hour;
        if (_lastHour >= 0 && hour != _lastHour && Machine.State == BehaviorState.Idle) React(PetEvent.HourChime);
        _lastHour = hour;
        if (Mind.IsNight && _time >= _nextNightMood && Machine.State == BehaviorState.Idle)
        {
            _nextNightMood = _time + 600 + _rng.NextDouble() * 900;
            React(PetEvent.NightTime);
        }
    }

    /// <summary>An unacknowledged reminder: after a while Hoodie knocks on the glass (never nags more than every ~20 s).</summary>
    private void UpdateAlert()
    {
        if (_alert != AlertKind.Reminder) return;
        var alertClip = AnimClip.ReminderAlert;
        if (Animation.Current is AnimClip.Knock or AnimClip.Point)
        {
            if (Animation.IsFinished) Animation.Play(alertClip, force: true, restart: true);
            return;
        }
        if (Machine.TimeInState < 12) { _nextKnock = _time + 12; return; }
        if (_time < _nextKnock) return;
        _nextKnock = _time + 20;
        if (Reactions.Resolve(PetEvent.AlertIgnored, Mind, EffectiveMode, _time) is { } r)
            Animation.Play(r.Clips[0], force: true, restart: true);
    }

    /// <summary>The note editor closed. Saved: Hoodie finishes writing; abandoned: it tears the page out and tosses it.</summary>
    public void NoteFinished(bool saved)
    {
        var e = saved ? PetEvent.NoteSaved : PetEvent.NoteAbandoned;
        if (Reactions.Resolve(e, Mind, EffectiveMode, _time) is not { } r) return;
        if (Machine.State == BehaviorState.Activity && _activity is not null)
        {
            Interject(r.Clips[0]);
            return;
        }
        if (Machine.State is BehaviorState.Idle or BehaviorState.Walking or BehaviorState.Emote) PlayChain(r.Clips, r.Priority);
    }

    private double _lastTyping = -100;
    private bool _thoughtSinceTyping = true;

    /// <summary>The user types a note: Hoodie keeps writing; when they pause for a while it stops to think.</summary>
    public void NoteTyping()
    {
        _lastTyping = _time;
        _thoughtSinceTyping = false;
    }

    private void UpdateNoteThinking()
    {
        if (_thoughtSinceTyping || _activity is not { Name: "notes", Phase: 1, Interjection: null }) return;
        if (_time - _lastTyping < 3.5) return;
        _thoughtSinceTyping = true;
        Interject(AnimClip.Thinking);
    }

    /// <summary>Standing on a floor with open space below it (the taskbar or a lower monitor)?</summary>
    private bool OnLedge()
    {
        var mon = World.MonitorAt(Feet);
        if (mon is null) return false;
        if (mon.Bounds.Bottom - mon.WorkArea.Bottom > Dip(20)) return true;
        return World.MonitorBelow(mon) is not null;
    }
}
