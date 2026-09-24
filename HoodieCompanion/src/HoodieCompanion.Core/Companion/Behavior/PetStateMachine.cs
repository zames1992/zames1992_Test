namespace HoodieCompanion.Companion.Behavior;

/// <summary>What Hoodie is doing (behavior). Separate from which animation clip is playing.</summary>
public enum BehaviorState
{
    Idle,
    Walking,
    Turning,
    Sitting,
    Sleeping,
    Emote,
    Grabbed,
    Airborne,
    Jumping,
    Landing,
    Recovering,
    ReceivingItem,
    ShowingBackpack,
    Alert,
    Leaving,
    Hidden,
    Returning,
    Vanishing,
    Appearing,
}

/// <summary>Why Hoodie is currently hidden (several may apply; the app shows it only when none do).</summary>
[Flags]
public enum HiddenReason
{
    None = 0,
    Alone = 1,
    Fullscreen = 2,
    AppRule = 4,
    NoPlace = 8,
}

/// <summary>
/// Small explicit state machine: current state, time in state, and a guarded transition.
/// Physical states (Grabbed/Airborne) can only be left through physics or release.
/// </summary>
public sealed class PetStateMachine
{
    private readonly Queue<string> _log = new();

    public BehaviorState State { get; private set; } = BehaviorState.Idle;
    public BehaviorState Previous { get; private set; } = BehaviorState.Idle;
    public double TimeInState { get; private set; }

    /// <summary>Sub-phase counter for multi-step states (receiving an item, leaving, etc.).</summary>
    public int Phase { get; set; }

    public IEnumerable<string> RecentTransitions => _log;

    public bool IsPhysical => State is BehaviorState.Grabbed or BehaviorState.Airborne or BehaviorState.Jumping or BehaviorState.Landing;

    public bool IsGroundedCalm => State is BehaviorState.Idle or BehaviorState.Sitting or BehaviorState.Sleeping or BehaviorState.Emote or BehaviorState.Walking or BehaviorState.Turning;

    public void Tick(double dt) => TimeInState += dt;

    public bool TransitionTo(BehaviorState next, string reason, bool force = false)
    {
        if (!force && State == BehaviorState.Grabbed && next is not (BehaviorState.Airborne or BehaviorState.Hidden))
            return false;
        if (!force && State == BehaviorState.Airborne && next is not (BehaviorState.Landing or BehaviorState.Grabbed or BehaviorState.Hidden or BehaviorState.Appearing))
            return false;
        Previous = State;
        State = next;
        TimeInState = 0;
        Phase = 0;
        _log.Enqueue($"{Previous} -> {next} ({reason})");
        while (_log.Count > 40) _log.Dequeue();
        return true;
    }
}
