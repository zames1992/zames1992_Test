using HoodieCompanion.Companion.Animation;

namespace HoodieCompanion.Companion.Behavior;

/// <summary>What the open Quick Panel page means to Hoodie.</summary>
public enum PanelActivity
{
    None,
    /// <summary>Backpack page: swings the backpack round, opens it and rummages.</summary>
    Backpack,
    /// <summary>Notes / Reminder pages: writes in a little notebook.</summary>
    Notes,
    /// <summary>PC Status page: sits down with a laptop and types.</summary>
    Laptop,
}

public sealed partial class PetController
{
    private sealed class ActivityPlan
    {
        public required string Name;
        public WorldItem Item;
        public required AnimClip Loop;
        public required List<(AnimClip Clip, double? Duration)> Exit;
        public readonly Queue<(AnimClip Clip, double? Duration)> Queue = new();
        public double? LoopSeconds;
        public double LoopUntil = double.MaxValue;
        public bool Sitting;
        public bool FromPanel;
        public int Phase; // 0 enter, 1 loop, 2 exit
        public bool StopRequested;
        public AnimClip? Interjection;
        public double StepDuration;
    }

    private ActivityPlan? _activity;

    /// <summary>The world item currently in use (activity item, or the blanket while sleeping).</summary>
    public WorldItem HeldItem => Machine.State == BehaviorState.Activity && _activity is { } a ? a.Item
        : Machine.State == BehaviorState.Sleeping && _sleepWithBlanket ? WorldItem.Blanket : WorldItem.None;
    private PanelActivity _panelActivity;

    public PanelActivity CurrentPanelActivity => _panelActivity;
    public string? ActivityName => Machine.State == BehaviorState.Activity ? _activity?.Name : null;

    /// <summary>
    /// Starts an activity with an accessory. Sitting activities sit down first (or stay seated);
    /// standing activities stand up first.
    /// </summary>
    private void StartActivity(string name, bool sitting, List<(AnimClip, double?)> enter, AnimClip loop,
        List<(AnimClip, double?)> exit, double? loopSeconds, bool fromPanel = false, WorldItem item = WorldItem.None)
    {
        if (!CanReact) return;
        if (Machine.State == BehaviorState.Sleeping)
        {
            WakeUp(() => StartActivity(name, sitting, enter, loop, exit, loopSeconds, fromPanel, item));
            return;
        }
        if (!sitting && Machine.State == BehaviorState.Sitting)
        {
            StandUp(() => StartActivity(name, sitting, enter, loop, exit, loopSeconds, fromPanel, item));
            return;
        }

        var plan = new ActivityPlan { Item = item, Name = name, Loop = loop, Exit = new List<(AnimClip, double?)>(exit), LoopSeconds = loopSeconds, Sitting = sitting, FromPanel = fromPanel };
        var alreadySeated = Machine.State == BehaviorState.Sitting || (Machine.State == BehaviorState.Activity && _activity is { Sitting: true });
        if (sitting && !alreadySeated) plan.Queue.Enqueue((AnimClip.SitDown, null));
        foreach (var e in enter) plan.Queue.Enqueue(e);
        _activity = plan;
        _walkTargetX = null;
        _travel = null;
        Go(BehaviorState.Activity, "activity " + name, force: true);
        NextActivityStep();
    }

    private void NextActivityStep()
    {
        var a = _activity;
        if (a is null) return;
        if (a.Queue.Count > 0)
        {
            var (clip, dur) = a.Queue.Dequeue();
            a.StepDuration = dur ?? AnimationCatalog.Get(clip).Duration;
            Animation.Play(clip, force: true, restart: true);
            return;
        }
        if (a.Phase == 0)
        {
            a.Phase = 1;
            a.LoopUntil = a.LoopSeconds is double s ? _time + s : double.MaxValue;
            Animation.Play(a.Loop, force: true);
            return;
        }
        if (a.Phase == 2)
        {
            // Done.
            _activity = null;
            if (a.Sitting)
            {
                Go(BehaviorState.Sitting, "activity done", force: true);
                _sitUntil = _time + 2 + _rng.NextDouble() * 4;
                _sleepAfterSit = false;
                Animation.Play(AnimClip.SitIdle);
            }
            else
            {
                Go(BehaviorState.Idle, "activity done", force: true);
                Animation.Play(AnimClip.IdleBreathing);
            }
            ScheduleDecision(2 + _rng.NextDouble() * 2);
        }
    }

    private void UpdateActivity()
    {
        var a = _activity;
        if (a is null)
        {
            Go(BehaviorState.Idle, "no activity", force: true);
            return;
        }
        switch (a.Phase)
        {
            case 0:
            case 2:
                if (Animation.ClipTime >= a.StepDuration) NextActivityStep();
                break;
            case 1:
                if (a.Interjection is AnimClip ij)
                {
                    if (Animation.Current != ij || Animation.IsFinished)
                    {
                        a.Interjection = null;
                        Animation.Play(a.Loop, force: true);
                    }
                    break;
                }
                if (a.StopRequested || _time >= a.LoopUntil)
                {
                    a.Phase = 2;
                    foreach (var e in a.Exit) a.Queue.Enqueue(e);
                    NextActivityStep();
                }
                else if (Animation.Current != a.Loop)
                {
                    Animation.Play(a.Loop, force: true);
                }
                else
                {
                    UpdateNoteThinking();
                }
                break;
        }
    }

    /// <summary>Plays a one-shot inside the current activity (e.g. present an item from the open backpack).</summary>
    private void Interject(AnimClip clip)
    {
        if (_activity is null) return;
        _activity.Interjection = clip;
        Animation.Play(clip, force: true, restart: true);
    }

    /// <summary>Stops the current activity gracefully (plays its exit clips).</summary>
    public void StopActivity()
    {
        if (_activity is null || Machine.State != BehaviorState.Activity) return;
        _activity.StopRequested = true;
        if (_activity.Phase == 0)
        {
            // Still entering: skip straight to the exit.
            _activity.Queue.Clear();
            _activity.Phase = 2;
            foreach (var e in _activity.Exit) _activity.Queue.Enqueue(e);
            NextActivityStep();
        }
    }

    /// <summary>The Quick Panel shows a page; Hoodie acts it out with an accessory.</summary>
    public void SetPanelActivity(PanelActivity activity)
    {
        if (activity == _panelActivity) return;
        _panelActivity = activity;
        if (activity == PanelActivity.None)
        {
            // Panel closed: put the accessory away properly.
            if (_activity is not null) StopActivity();
            return;
        }
        // The user's page always wins over whatever Hoodie was doing (reading, laptop, another page), and at
        // once: the old accessory is dropped without its exit animation so the new one comes out immediately.
        if (_activity is { } old && Machine.State == BehaviorState.Activity)
        {
            _activity = null;
            if (old.Sitting)
            {
                Go(BehaviorState.Sitting, "switch activity", force: true);
                _sitUntil = _time + 3;
                _sleepAfterSit = false;
            }
            else
            {
                Go(BehaviorState.Idle, "switch activity", force: true);
            }
        }
        // Start after the previous activity has finished its exit.
        void Begin()
        {
            if (_panelActivity != activity) return;
            switch (activity)
            {
                case PanelActivity.Backpack:
                    StartActivity("backpack", sitting: false, enter: new() { (AnimClip.OpenBackpack, null) }, loop: AnimClip.SearchBackpack,
                        exit: new() { (AnimClip.CloseBackpack, null) }, loopSeconds: null, fromPanel: true);
                    break;
                case PanelActivity.Notes:
                    StartActivity("notes", sitting: false, enter: new(), loop: AnimClip.WriteNotes, exit: new(), loopSeconds: null, fromPanel: true);
                    break;
                case PanelActivity.Laptop:
                    StartActivity("laptop", sitting: true, enter: new() { (AnimClip.LaptopOpen, null) }, loop: AnimClip.LaptopType,
                        exit: new() { (AnimClip.LaptopClose, null) }, loopSeconds: null, fromPanel: true);
                    break;
            }
        }

        // Busy right now (landing, climbing, catching a file...): the page's activity starts as soon as Hoodie can,
        // instead of being dropped.
        if ((Machine.State == BehaviorState.Activity && _activity is not null) || !CanReact) _pendingPanelStart = Begin;
        else Begin();
    }

    private Action? _pendingPanelStart;

    /// <summary>Called every frame: starts a queued panel activity once the previous one has ended.</summary>
    private void PumpPendingActivity()
    {
        if (_pendingPanelStart is null || Machine.State == BehaviorState.Activity || !CanReact) return;
        var p = _pendingPanelStart;
        _pendingPanelStart = null;
        p();
    }

    /// <summary>Kept for compatibility: the Backpack page is open.</summary>
    public void BackpackOpened(bool open) => SetPanelActivity(open ? PanelActivity.Backpack : PanelActivity.None);

    private void DropActivity()
    {
        _activity = null;
        _pendingPanelStart = null;
        // A page is still open (e.g. Hoodie was grabbed while showing its backpack): pick the page's activity up
        // again once it can.
        if (_panelActivity != PanelActivity.None)
        {
            var page = _panelActivity;
            _pendingPanelStart = () =>
            {
                if (_panelActivity != page) return;
                _panelActivity = PanelActivity.None;
                SetPanelActivity(page);
            };
        }
    }
}
