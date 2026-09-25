using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Interaction;
using HoodieCompanion.Companion.Physics;
using HoodieCompanion.Features.SystemMonitor;
using HoodieCompanion.Geometry;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;

namespace HoodieCompanion.Companion.Behavior;

public sealed partial class PetController
{
    private double _nextDecisionAt;
    private double _userIdle;
    private double _sitUntil;
    private bool _sleepAfterSit;
    private double _sleepUntil;
    private bool _worldAsleep;
    private bool _sleptBecauseUserAway;

    private AnimClip _emote;
    private ReactionPriority _emoteTier = ReactionPriority.Contextual;
    private readonly Queue<AnimClip> _emoteChain = new();
    private Action? _afterEmote;
    private BehaviorState _emoteReturn = BehaviorState.Idle;

    private readonly Queue<(AnimClip Clip, double? Duration)> _sequence = new();
    private Action? _afterSequence;

    private AlertKind? _alert;
    private bool _dragHovering;

    public bool IsReceivingDrag => _dragHovering;

    private double _lookWeight;
    private double _lookScriptUntil;
    private double _cursorOtherSide;
    private double _nextStepAside;

    private PresenceMode _modeBeforeAlone;
    private bool _leavingThroughEdge;
    private Vec2 _exitFeet;
    private bool _exitWasEdge;
    private Vec2? _poofTarget;
    private Action? _afterPoof;

    // ------------------------------------------------------------------ idle & decisions

    private void ScheduleDecision(double inSeconds) => _nextDecisionAt = _time + inSeconds;

    private void UpdateIdle()
    {
        var baseLoop = IdleDirector.BaseLoop;
        if (Animation.Current != baseLoop && (Animation.IsFinished || Animation.CurrentInfo.Loop))
            Animation.Play(baseLoop);

        var m = Metrics;
        // Standing somewhere Hoodie is not allowed to stop (e.g. thrown into a NO_GO area)? Walk out.
        if (!Territory.CanStop(Feet, m.HeightPx) && !IsHiddenMode)
        {
            WalkToAllowedSpot();
            return;
        }

        if (_time < _nextDecisionAt)
        {
            // Between decisions the idle director keeps the body alive with small, varied moments.
            if (Machine.TimeInState > 0.8 && IdleDirector.Tick(_time, IdlePosture.Standing, Mind, EffectiveMode, Settings.ReducedMotion, CursorNear, Perception.UserBusy) is AnimClip micro)
                PlayEmoteAt(micro, null, ReactionPriority.Idle);
            return;
        }
        ScheduleDecision(IntentsDelay() * AfkDecisionFactor);
        Decide();
    }

    private bool CursorNear => Vec2.Distance(_cursor, HeadWorld) < Dip(260);

    private bool IsHiddenMode => Mode == PresenceMode.Alone;

    private void WalkToAllowedSpot()
    {
        var m = Metrics;
        var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        var x = Territory.NearestStandableX(mon, Feet.X, m.HeightPx, m.HalfWidthPx);
        if (x is double sx && Math.Abs(sx - Feet.X) > 2)
        {
            StartWalk(sx, run: false, onArrive: null, ignoreTerritory: true);
            return;
        }
        // Nowhere on this monitor: find another monitor, else travel home.
        var other = Territory.StandableMonitors(m.HeightPx, m.HalfWidthPx).FirstOrDefault();
        if (other is not null && other != mon)
        {
            var ox = Territory.NearestStandableX(other, other.WorkArea.Center.X, m.HeightPx, m.HalfWidthPx) ?? other.WorkArea.Center.X;
            TravelTo(new Vec2(ox, other.WorkArea.Bottom), run: false, onArrive: null, ignoreTerritory: true);
            return;
        }
        ScheduleDecision(5);
    }

    private void Decide()
    {
        var m = Metrics;
        var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        var mode = EffectiveMode;

        if (!Settings.AutonomousBehavior && mode != PresenceMode.Focus)
        {
            // Autonomy off: Hoodie stays put and only rests now and then.
            if (Drives.Energy < 0.3 || _userIdle > 300) BeginSit(sleepAfter: true);
            return;
        }

        var distToEdge = Math.Min(Feet.X - mon.WorkArea.Left, mon.WorkArea.Right - Feet.X);
        var otherMonitors = Territory.StandableMonitors(m.HeightPx, m.HalfWidthPx).Where(o => o != mon).ToList();
        var cursorMon = World.MonitorAt(_cursor);
        var ctx = new DecisionContext(
            mode,
            Drives,
            HasItems,
            distToEdge < m.HalfWidthPx * 5,
            otherMonitors.Count > 0 && Territory.Anchor is null,
            _userIdle,
            cursorMon == mon && _cursor.Y > mon.WorkArea.Bottom - Dip(260),
            IsAtRestSpot(),
            Settings.ReducedMotion,
            Mind.Afk,
            OnLedge(),
            Mind.Boredom,
            Mind.Sleepiness,
            _onSurface is null && _surfaces.Count > 0 && PickSurfaceToVisit() is not null,
            _onSurface is not null,
            _onSurface is null && ClimbableWallSide() is not null,
            _onSurface is null ? 0 : _time - _surfaceSince);

        // Intent: a reason first, then the action.
        var intent = Intents.Choose(BuildIntentContext(ctx));
        var act = intent.Activity;
        Mind.CurrentIntent = act.ToString();
        Mind.CurrentReason = intent.Reason;
        Log?.Invoke($"intent {act} because {intent.Reason} (mode {mode})");
        if (DoIntent(act)) return;
        switch (act)
        {
            case Activity.Wander:
            case Activity.Run:
            {
                var run = act == Activity.Run;
                var span = mode == PresenceMode.Quiet ? 160 : run ? 700 : 480;
                for (var i = 0; i < 8; i++)
                {
                    var dist = Dip(80 + _rng.NextDouble() * span) * (_rng.NextDouble() < 0.5 ? -1 : 1);
                    var x = Feet.X + dist;
                    x = Math.Clamp(x, mon.WorkArea.Left + m.HalfWidthPx, mon.WorkArea.Right - m.HalfWidthPx);
                    if (Territory.Anchor is { } a) x = Math.Clamp(x, a.Center.X - a.RadiusPx, a.Center.X + a.RadiusPx);
                    if (_onSurface is { } sid && FindSurface(sid) is { } surf)
                        x = Math.Clamp(x, surf.Left + m.HalfWidthPx * 0.4, Math.Max(surf.Left + m.HalfWidthPx * 0.4, surf.Right - m.HalfWidthPx * 0.4));
                    if (Math.Abs(x - Feet.X) < Dip(30)) continue;
                    if (!Territory.CanStop(new Vec2(x, Feet.Y), m.HeightPx)) continue;
                    StartWalk(x, run, null);
                    return;
                }
                break;
            }
            case Activity.Explore:
            {
                var target = otherMonitors.OrderBy(o => o.WorkArea.Center.X - Feet.X is var d ? Math.Abs(d) : 0).First();
                var x = target.WorkArea.Left + target.WorkArea.Width * (0.2 + _rng.NextDouble() * 0.6);
                if (TravelTo(new Vec2(x, target.WorkArea.Bottom), run: mode == PresenceMode.Play, onArrive: () => PlayEmote(AnimClip.Curious)))
                    Drives.OnExplored();
                break;
            }
            case Activity.Sit:
                BeginSit(sleepAfter: false);
                break;
            case Activity.Sleep:
                _sleptBecauseUserAway = Mind.Afk >= AfkPhase.Sleepy;
                BeginSit(sleepAfter: true);
                break;
            case Activity.VisitSurface:
                if (!VisitSurface()) ScheduleDecision(1);
                break;
            case Activity.ClimbWall:
                if (!ClimbWall()) ScheduleDecision(1);
                break;
            case Activity.HopDown:
                HopDown("done up here");
                break;
            case Activity.SitEdge:
                // Sit on the edge of the floor (the top of the taskbar) and swing the legs.
                StartActivity("edge", sitting: false, enter: new(), loop: AnimClip.SitEdge, exit: new() { (AnimClip.Jump, 0.25) },
                    loopSeconds: 15 + _rng.NextDouble() * 35);
                break;
            case Activity.LieAround:
                if (!RoomToLie()) break;
                StartActivity("lie", sitting: true, enter: new() { (AnimClip.LieDown, null) }, loop: AnimClip.LieIdle,
                    exit: new() { (AnimClip.WakeFromLying, null) }, loopSeconds: 10 + _rng.NextDouble() * 20);
                break;
            case Activity.Stretch:
                PlayEmote(AnimClip.Stretch);
                break;
            case Activity.Yawn:
                PlayEmote(AnimClip.Yawn);
                break;
            case Activity.LookAround:
                _lookScriptUntil = _time + 3.2;
                break;
            case Activity.PeekEdge:
            {
                var dir = Feet.X - mon.WorkArea.Left < mon.WorkArea.Right - Feet.X ? -1 : 1;
                var edgeX = dir < 0 ? mon.WorkArea.Left + m.HalfWidthPx * 0.9 : mon.WorkArea.Right - m.HalfWidthPx * 0.9;
                if (!Territory.CanStop(new Vec2(edgeX, Feet.Y), m.HeightPx)) break;
                StartWalk(edgeX, false, () =>
                {
                    if (Facing != dir) TurnThen(() => PlayEmote(AnimClip.PeekEdge));
                    else PlayEmote(AnimClip.PeekEdge);
                    Drives.OnExplored();
                });
                break;
            }
            case Activity.InspectBackpack:
                RunSequence(BehaviorState.ReceivingItem, new() { (AnimClip.OpenBackpack, null), (AnimClip.SearchBackpack, 2.6), (AnimClip.CloseBackpack, null) }, null);
                break;
            case Activity.ReadBook:
                StartActivity("read", sitting: true, enter: new(), loop: AnimClip.ReadBook, exit: new(), loopSeconds: 18 + _rng.NextDouble() * 30);
                break;
            case Activity.UseLaptop:
                StartActivity("laptop", sitting: true, enter: new() { (AnimClip.LaptopOpen, null) }, loop: AnimClip.LaptopType,
                    exit: new() { (AnimClip.LaptopClose, null) }, loopSeconds: 12 + _rng.NextDouble() * 25);
                break;
            case Activity.WriteNotes:
                StartActivity("notes", sitting: false, enter: new(), loop: AnimClip.WriteNotes, exit: new(), loopSeconds: 5 + _rng.NextDouble() * 7);
                break;
            case Activity.Dance:
                PlayEmote(AnimClip.Dance);
                break;
            case Activity.JumpForJoy:
                PlayEmote(AnimClip.JumpForJoy);
                break;
            case Activity.Hop:
            {
                // A playful hop forward, or straight up when the spot ahead is off limits.
                var ahead = Feet + new Vec2(Facing * Dip(20), 0);
                var ok = mon.WorkArea.ContainsX(ahead.X + Facing * m.HalfWidthPx) && Territory.CanStop(ahead, m.HeightPx);
                JumpTo(ok ? ahead : Feet, extraApexDip: 70);
                break;
            }
            case Activity.StayNearUser:
            {
                if (cursorMon is null) break;
                var side = Feet.X < _cursor.X ? -1 : 1;
                var x = _cursor.X + side * Dip(160 + _rng.NextDouble() * 140);
                if (Math.Abs(x - Feet.X) < Dip(90) && cursorMon == mon)
                {
                    BeginSit(sleepAfter: false);
                    break;
                }
                TravelTo(new Vec2(x, cursorMon.WorkArea.Bottom), run: false, onArrive: () => { if (_rng.NextDouble() < 0.5) BeginSit(false); });
                break;
            }
            case Activity.ChaseCursor:
            {
                var x = _cursor.X + (Feet.X < _cursor.X ? -1 : 1) * Dip(40);
                StartWalk(Math.Clamp(x, mon.WorkArea.Left + m.HalfWidthPx, mon.WorkArea.Right - m.HalfWidthPx), run: true,
                    onArrive: () => { if (!Animation.IsOnCooldown(AnimClip.Wave)) PlayEmote(AnimClip.Wave); }, chase: true);
                break;
            }
            case Activity.GoRestSpot:
                GoToRestSpot(then: () => BeginSit(sleepAfter: false));
                break;
        }
    }

    private bool IsAtRestSpot()
    {
        var m = Metrics;
        if (Territory.IsQuietAt(Feet, m.HeightPx)) return true;
        var home = Territory.HomeFeet();
        if (home is Vec2 h) return Vec2.Distance(h, Feet) < Dip(Territory.Data.Home!.AllowedRadiusDip);
        return false;
    }

    /// <summary>Focus / Quiet: a calm place away from the user's active work.</summary>
    private void GoToRestSpot(Action? then)
    {
        var m = Metrics;
        var home = Territory.HomeFeet();
        if (home is Vec2 h && Territory.CanStop(h, m.HeightPx) && TravelTo(h, false, then)) return;

        // A Quiet region, if the user drew one.
        foreach (var r in Territory.Data.Regions.Where(r => r.Type == RegionType.Quiet))
        {
            var rect = Territory.ResolveRegion(r);
            var mon = World.FindById(r.MonitorId);
            if (rect is null || mon is null) continue;
            if (TravelTo(new Vec2(rect.Value.Center.X, mon.WorkArea.Bottom), false, then)) return;
        }

        // Otherwise the corner of the world farthest from the user's hand.
        var cur = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        var cursorMon = World.MonitorAt(_cursor);
        var candidates = Territory.StandableMonitors(m.HeightPx, m.HalfWidthPx).ToList();
        var target = candidates.FirstOrDefault(c => c != cursorMon && c.Id != _foregroundMonitorId) ?? cur;
        var left = target.WorkArea.Left + m.HalfWidthPx * 1.5;
        var right = target.WorkArea.Right - m.HalfWidthPx * 1.5;
        var x = Math.Abs(_cursor.X - left) > Math.Abs(_cursor.X - right) ? left : right;
        if (!TravelTo(new Vec2(x, target.WorkArea.Bottom), false, then)) then?.Invoke();
    }

    // ------------------------------------------------------------------ rest

    private void BeginSit(bool sleepAfter)
    {
        if (Machine.State == BehaviorState.Sitting)
        {
            if (sleepAfter) _sitUntil = _time;
            _sleepAfterSit = sleepAfter;
            return;
        }
        _sleepAfterSit = sleepAfter;
        var mode = EffectiveMode;
        var (lo, hi) = mode switch
        {
            PresenceMode.Quiet or PresenceMode.Focus => (60.0, 240.0),
            PresenceMode.Company => (20.0, 60.0),
            PresenceMode.Play => (5.0, 12.0),
            _ => (15.0, 60.0),
        };
        _sitUntil = _time + (sleepAfter ? 3 + _rng.NextDouble() * 4 : lo + _rng.NextDouble() * (hi - lo));
        Go(BehaviorState.Sitting, "sit", force: true);
        Animation.Play(AnimClip.SitDown, force: true);
    }

    private void UpdateSitting()
    {
        if (Animation.Current == AnimClip.SitDown && Animation.IsFinished) Animation.Play(AnimClip.SitIdle);
        if (Animation.Current == AnimClip.StandUp)
        {
            if (Animation.IsFinished)
            {
                var then = _afterStand;
                _afterStand = null;
                Go(BehaviorState.Idle, "stood up", force: true);
                Animation.Play(AnimClip.IdleBreathing);
                then?.Invoke();
            }
            return;
        }
        if (_sitMicro is AnimClip micro)
        {
            var info = AnimationCatalog.Get(micro);
            if (Animation.Current != micro || (info.Loop ? _time >= _sitMicroUntil : Animation.IsFinished))
            {
                _sitMicro = null;
                Animation.Play(AnimClip.SitIdle);
            }
        }
        else
        {
            if (Animation.Current is not (AnimClip.SitDown or AnimClip.SitIdle or AnimClip.WakeUp or AnimClip.WakeFromLying)) Animation.Play(AnimClip.SitIdle);
            if (Animation.Current is AnimClip.WakeUp or AnimClip.WakeFromLying && Animation.IsFinished) Animation.Play(AnimClip.SitIdle);
            if (Animation.Current == AnimClip.SitIdle && Machine.TimeInState > 2 &&
                IdleDirector.Tick(_time, IdlePosture.Sitting, Mind, EffectiveMode, Settings.ReducedMotion, CursorNear, Perception.UserBusy) is AnimClip m)
            {
                _sitMicro = m;
                _sitMicroUntil = _time + 5 + _rng.NextDouble() * 7;
                Animation.Play(m);
            }
        }

        if (_time < _sitUntil) return;
        if (_sleepAfterSit || Drives.Energy < 0.2)
        {
            BeginSleep();
            return;
        }
        StandUp(null);
    }

    private Action? _afterStand;
    private AnimClip? _sitMicro;
    private double _sitMicroUntil;

    private void StandUp(Action? then)
    {
        _afterStand = then;
        if (Machine.State != BehaviorState.Sitting) Go(BehaviorState.Sitting, "stand up", force: true);
        Animation.Play(AnimClip.StandUp, force: true);
    }

    /// <summary>Stand before doing something that needs feet.</summary>
    private void EnsureStanding(Action then)
    {
        switch (Machine.State)
        {
            case BehaviorState.Sleeping:
                WakeUp(() => StandUp(then));
                break;
            case BehaviorState.Sitting:
                StandUp(then);
                break;
            default:
                then();
                break;
        }
    }

    private void BeginSleep()
    {
        if (!_worldAsleep && !RoomToLie())
        {
            // Too close to the edge of the screen to lie down: shuffle inwards first.
            var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
            var half = Metrics.HeightPx * 0.55 + Dip(4);
            var x = Math.Clamp(Feet.X, mon.WorkArea.Left + half, mon.WorkArea.Right - half);
            StartWalk(x, run: false, onArrive: () => BeginSit(sleepAfter: true));
            return;
        }
        var mode = EffectiveMode;
        var minutes = mode is PresenceMode.Quiet or PresenceMode.Focus ? 4 + _rng.NextDouble() * 6 : 1.5 + _rng.NextDouble() * 3;
        if (_sleptBecauseUserAway) minutes = Math.Max(minutes, 30);
        _sleepUntil = _time + minutes * 60;
        _nextDream = _time + 25 + _rng.NextDouble() * 40;
        // With a blanket, especially at night.
        _sleepWithBlanket = Memory.HasItem("blanket") && (Perception.Night || Mind.IsNight || _rng.NextDouble() < 0.5);
        if (_sleepWithBlanket) Memory.Remember("first-blanket-nap");
        _sitMicro = null;
        Go(BehaviorState.Sleeping, "fall asleep", force: true);
        // Sleeping is lying down, curled up (sitting upright asleep looked eerie).
        Animation.Play(AnimClip.LieDown, force: true);
    }

    private double _nextDream;
    private bool _sleepWithBlanket;

    /// <summary>Is there room to lie down here without the body poking out of the screen?</summary>
    private bool RoomToLie()
    {
        var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        var half = Metrics.HeightPx * 0.55;
        return Feet.X - mon.WorkArea.Left > half && mon.WorkArea.Right - Feet.X > half;
    }

    private Action? _afterWake;

    private void UpdateSleeping()
    {
        if (Animation.Current is AnimClip.LieDown or AnimClip.DreamTwitch && Animation.IsFinished) Animation.Play(AnimClip.SleepLying);
        if (Animation.Current is AnimClip.SleepStart or AnimClip.SleepLoop) Animation.Play(AnimClip.SleepLying);
        if (Animation.Current == AnimClip.SleepLying && _time >= _nextDream)
        {
            _nextDream = _time + 25 + _rng.NextDouble() * 50;
            Animation.Play(AnimClip.DreamTwitch, force: true, restart: true);
        }
        if (Animation.Current is AnimClip.WakeUp or AnimClip.WakeFromLying)
        {
            if (Animation.IsFinished)
            {
                var then = _afterWake;
                _afterWake = null;
                Go(BehaviorState.Sitting, "awake", force: true);
                _sitUntil = _time + 3 + _rng.NextDouble() * 5;
                _sleepAfterSit = false;
                Animation.Play(AnimClip.SitIdle);
                then?.Invoke();
            }
            return;
        }
        if (_worldAsleep) return;

        // Sleeping because the user is away: keep sleeping until they return (handled by the AFK timeline).
        if (_sleptBecauseUserAway && Mind.Afk != AfkPhase.Present) return;
        if (_time >= _sleepUntil || (Drives.Energy > 0.97 && Machine.TimeInState > 90))
        {
            _sleptBecauseUserAway = false;
            Mind.OnRested();
            WakeUp(null);
        }
    }

    private void WakeUp(Action? then)
    {
        if (Machine.State != BehaviorState.Sleeping)
        {
            then?.Invoke();
            return;
        }
        _afterWake = then;
        Animation.Play(AnimClip.WakeFromLying, force: true);
    }

    // ------------------------------------------------------------------ emotes & sequences

    /// <summary>Plays a one-shot expressive clip, returning to the previous calm state afterwards.</summary>
    public bool PlayEmote(AnimClip clip, Action? after = null) => PlayEmoteAt(clip, after, ReactionPriority.Contextual);

    private bool PlayEmoteAt(AnimClip clip, Action? after, ReactionPriority tier)
    {
        if (Machine.IsPhysical || Machine.State is BehaviorState.Hidden or BehaviorState.Leaving or BehaviorState.Returning
                or BehaviorState.Alert or BehaviorState.Vanishing or BehaviorState.Appearing or BehaviorState.ReceivingItem
                or BehaviorState.Recovering)
            return false;
        if (Animation.IsOnCooldown(clip)) return false;
        if (Machine.State is BehaviorState.Sitting or BehaviorState.Sleeping)
        {
            // Resting Hoodie does not jump up for small things.
            return false;
        }
        if (Machine.State == BehaviorState.Walking) _walkTargetX = null;
        _emote = clip;
        _afterEmote = after;
        _emoteTier = tier;
        _emoteReturn = BehaviorState.Idle;
        Go(BehaviorState.Emote, clip.ToString(), force: true);
        return Animation.Play(clip, force: true, restart: true);
    }

    private void UpdateEmote()
    {
        if (Animation.Current == _emote && !Animation.IsFinished) return;
        if (_emoteChain.Count > 0 && Animation.Current == _emote)
        {
            var next = _emoteChain.Dequeue();
            _emote = next;
            Animation.Play(next, force: true, restart: true);
            return;
        }
        _emoteChain.Clear();
        var after = _afterEmote;
        _afterEmote = null;
        Go(_emoteReturn, "emote done", force: true);
        Animation.Play(AnimClip.IdleBreathing);
        after?.Invoke();
    }

    private void RunSequence(BehaviorState state, List<(AnimClip, double?)> steps, Action? after)
    {
        _sequence.Clear();
        foreach (var s in steps) _sequence.Enqueue(s);
        _afterSequence = after;
        _walkTargetX = null;
        Go(state, "sequence", force: true);
        NextInSequence();
    }

    private double _sequenceStepDuration;

    private void NextInSequence()
    {
        if (_sequence.Count == 0)
        {
            var after = _afterSequence;
            _afterSequence = null;
            if (Machine.State is BehaviorState.ReceivingItem or BehaviorState.Recovering)
            {
                Go(BehaviorState.Idle, "sequence done", force: true);
                Animation.Play(AnimClip.IdleBreathing);
            }
            after?.Invoke();
            return;
        }
        var (clip, dur) = _sequence.Dequeue();
        _sequenceStepDuration = dur ?? AnimationCatalog.Get(clip).Duration;
        Animation.Play(clip, force: true, restart: true);
    }

    private void UpdateSequence()
    {
        if (Animation.ClipTime >= _sequenceStepDuration) NextInSequence();
    }

    // ------------------------------------------------------------------ inventory

    public void DragEntered()
    {
        _dragHovering = true;
        if (!CanReact || Machine.State == BehaviorState.Activity) return;
        if (Machine.State is BehaviorState.Sitting or BehaviorState.Sleeping) return;
        _walkTargetX = null;
        Go(BehaviorState.ReceivingItem, "drag enter", force: true);
        _sequence.Clear();
        _afterSequence = null;
        _sequenceStepDuration = double.MaxValue;
        Animation.Play(AnimClip.NoticeItem, force: true, restart: true);
    }

    public void DragLeft()
    {
        _dragHovering = false;
        if (Machine.State == BehaviorState.ReceivingItem && Animation.Current == AnimClip.NoticeItem)
        {
            Go(BehaviorState.Idle, "drag left", force: true);
            Animation.Play(AnimClip.IdleBreathing);
        }
    }

    /// <summary>An object was given to Hoodie: Notice → Catch → Inspect → Put in Backpack (≈1.3 s).</summary>
    public void ItemReceived(bool alreadyHad)
    {
        _dragHovering = false;
        Mind.OnItemReceived();
        Memory.Interaction("item");
        Memory.Remember("first-gift");
        if (Machine.State == BehaviorState.Activity && _activity is { Name: "backpack" })
        {
            // Backpack is already open: the object goes straight in.
            Interject(AnimClip.PutInBackpack);
            return;
        }
        if (!CanReact && Machine.State != BehaviorState.ReceivingItem) return;
        var steps = new List<(AnimClip, double?)>();
        if (Animation.Current != AnimClip.NoticeItem) steps.Add((AnimClip.NoticeItem, 0.15));
        steps.Add((AnimClip.CatchItem, null));
        steps.Add((AnimClip.InspectItem, alreadyHad ? 0.25 : null));
        steps.Add((AnimClip.PutInBackpack, null));
        steps.Add((AnimClip.CloseBackpack, null));
        RunSequence(BehaviorState.ReceivingItem, steps, null);
    }

    /// <summary>Hands an item back (the user opened it from the Backpack).</summary>
    public void ItemPresented() => Feedback(AnimClip.PresentItem);

    /// <summary>The stored object is gone: search, then shrug.</summary>
    public void ItemMissing() => Feedback(AnimClip.MissingItem);

    /// <summary>Utility feedback (Thinking, Success, Error, PresentItem, MissingItem).</summary>
    public void Feedback(AnimClip clip)
    {
        // Success / Error become varied reactions (thumbs up, proud, facepalm, confused...).
        if (clip is AnimClip.Success or AnimClip.Error)
        {
            if (clip == AnimClip.Error) Mind.OnFailure(); else Mind.OnSmallWin();
            var r = Reactions.Resolve(clip == AnimClip.Success ? PetEvent.TaskSucceeded : PetEvent.TaskFailed, Mind, EffectiveMode, _time);
            if (r is { } rr) clip = rr.Clips[0];
        }
        if (Machine.State == BehaviorState.Activity && _activity is not null)
        {
            Interject(clip);
            return;
        }
        if (Machine.State is BehaviorState.Idle or BehaviorState.Walking or BehaviorState.Emote)
        {
            if (clip is AnimClip.PresentItem or AnimClip.MissingItem)
                RunSequence(BehaviorState.ReceivingItem, new() { (clip, null) }, null);
            else
                PlayEmote(clip);
        }
    }

    private bool CanReact => !Machine.IsPhysical && Machine.State is not (BehaviorState.Hidden or BehaviorState.Leaving
        or BehaviorState.Returning or BehaviorState.Vanishing or BehaviorState.Appearing or BehaviorState.Alert or BehaviorState.Recovering
        or BehaviorState.Climbing);

    // ------------------------------------------------------------------ alerts

    public void StartAlert(AlertKind kind)
    {
        _alert = kind;
        if (Machine.State is BehaviorState.Hidden or BehaviorState.Leaving || Machine.IsPhysical) return;
        _walkTargetX = null;
        _travel = null;
        Go(BehaviorState.Alert, kind.ToString(), force: true);
        Animation.Play(kind == AlertKind.Reminder ? AnimClip.ReminderAlert : AnimClip.TimerAlert, force: true, restart: true);
    }

    public void EndAlert()
    {
        _alert = null;
        if (Machine.State != BehaviorState.Alert) return;
        Go(BehaviorState.Idle, "alert acknowledged", force: true);
        Animation.Play(AnimClip.IdleBreathing, force: true);
        PlayEmote(AnimClip.Success);
    }

    // ------------------------------------------------------------------ environment

    public void Environment(EnvironmentMood mood)
    {
        if (!Settings.PcStatusReactions) return;
        if (EffectiveMode is PresenceMode.Focus || Machine.State != BehaviorState.Idle) return;
        Mind.PcLoad = mood == EnvironmentMood.Busy ? 1 : 0;
        switch (mood)
        {
            case EnvironmentMood.Busy:
                Drives.OnEnvironmentBusy();
                // Diegetic: a busy CPU is heavy lifting, so Hoodie hauls a crate for a while.
                if (Reactions.Resolve(PetEvent.PcBusy, Mind, EffectiveMode, _time) is { } busy)
                {
                    if (busy.Clips[0] == AnimClip.CarryLoad)
                        StartActivity("carry", sitting: false, enter: new(), loop: AnimClip.CarryLoad, exit: new() { (AnimClip.Sigh, null) },
                            loopSeconds: 6 + _rng.NextDouble() * 5);
                    else PlayEmote(busy.Clips[0]);
                }
                break;
            case EnvironmentMood.Downloading:
                // Data arrives as parcels falling from above.
                React(PetEvent.DownloadActive);
                break;
            case EnvironmentMood.CalmedDown:
                React(PetEvent.PcCalm);
                break;
        }
    }

    /// <summary>PC is going to sleep / waking up: Hoodie's world sleeps with it.</summary>
    public void WorldSleep(bool asleep)
    {
        _worldAsleep = asleep;
        if (asleep)
        {
            if (Machine.IsPhysical || Machine.State is BehaviorState.Hidden or BehaviorState.Sleeping) return;
            if (Machine.State != BehaviorState.Sitting) BeginSit(sleepAfter: true);
            BeginSleep();
        }
        else if (Machine.State == BehaviorState.Sleeping)
        {
            WakeUp(null);
        }
    }

    // ------------------------------------------------------------------ cursor

    private Vec2 _cursor;
    private string? _foregroundMonitorId;

    private void UpdateCursor(in PetInput input, double dt)
    {
        _cursor = input.Cursor;
        _foregroundMonitorId = input.ForegroundMonitorId;
        var t = Transform;
        var head = t.LocalToWorld(new Vec2(243, 250));
        var box = t.Bounds(RigTransform.BodyLocal);
        var ev = Cursor.Update(_time, dt, input.Cursor, input.LeftButtonDown, box, head, DipScale);

        var mode = EffectiveMode;
        var reactive = Settings.CursorReactions && Machine.State is BehaviorState.Idle or BehaviorState.Walking or BehaviorState.Sitting
            or BehaviorState.Emote or BehaviorState.Activity or BehaviorState.Alert or BehaviorState.Landing or BehaviorState.Recovering;

        // Look at the user's hand.
        double targetWeight = 0, lx = 0, ly = 0;
        if (_time < _lookAtUntil)
        {
            (lx, ly) = _lookAt;
            targetWeight = 1;
        }
        else if (_time < _lookScriptUntil)
        {
            var k = (_lookScriptUntil - _time) / 3.2;
            lx = Math.Sin(k * Math.PI * 2) * 0.9;
            ly = 0.1;
            targetWeight = 0.9;
        }
        else if (reactive && Machine.State != BehaviorState.Sleeping)
        {
            var d = Vec2.Distance(input.Cursor, head) / DipScale;
            targetWeight = CursorInteractionService.LookWeight(d);
            if (mode is PresenceMode.Focus or PresenceMode.Quiet) targetWeight *= 0.5;
            if (Machine.State is BehaviorState.Landing or BehaviorState.Recovering) targetWeight = Math.Max(targetWeight, 0.6);
            var dir = (input.Cursor - head).Normalized();
            lx = -Facing * dir.X;
            ly = dir.Y;
        }
        _lookWeight = MathUtil.Approach(_lookWeight, targetWeight, 6, dt);
        Animation.SetLook(lx, ly, _lookWeight);

        if (!reactive) return;

        // Occasionally turn to face the user when they are near and behind.
        if (Machine.State == BehaviorState.Idle && mode != PresenceMode.Focus && targetWeight > 0.35 &&
            Math.Sign(input.Cursor.X - Feet.X) == -Facing && Math.Abs(input.Cursor.X - Feet.X) > Dip(30))
        {
            _cursorOtherSide += dt;
            if (_cursorOtherSide > 1.2)
            {
                _cursorOtherSide = 0;
                TurnThen(() => Go(BehaviorState.Idle, "faced the user", force: true));
                return;
            }
        }
        else
        {
            _cursorOtherSide = 0;
        }

        switch (ev)
        {
            case CursorEvent.Surprised when Machine.State is BehaviorState.Idle:
                React(PetEvent.CursorRushed);
                break;
            case CursorEvent.Curious when Machine.State is BehaviorState.Idle:
                React(PetEvent.CursorApproached);
                break;
            case CursorEvent.Obstructing:
                Cursor.MakeShy(_time, 60);
                StepAside();
                break;
        }

        // While shy (the user signalled Hoodie was in the way), keep a respectful distance.
        if (Cursor.IsShy(_time) && _time > _nextStepAside && Machine.State is BehaviorState.Idle or BehaviorState.Sitting)
        {
            var d = Vec2.Distance(input.Cursor, head) / DipScale;
            if (d < 130) StepAside();
        }
    }

    /// <summary>Hoodie realises it is in the way and moves aside (communicated through movement, not dialogs).</summary>
    private void StepAside()
    {
        if (Machine.State is not (BehaviorState.Idle or BehaviorState.Sitting or BehaviorState.Walking or BehaviorState.Emote)) return;
        _nextStepAside = _time + 3;
        var m = Metrics;
        var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        var away = Math.Sign(Feet.X - _cursor.X);
        if (away == 0) away = -Facing;
        var dist = m.HalfWidthPx * 3.2 + Dip(60);
        foreach (var candidate in new[] { Feet.X + away * dist, Feet.X - away * dist * 1.6, Feet.X + away * dist * 2 })
        {
            if (candidate < mon.WorkArea.Left + m.HalfWidthPx || candidate > mon.WorkArea.Right - m.HalfWidthPx) continue;
            if (!Territory.CanStop(new Vec2(candidate, Feet.Y), m.HeightPx)) continue;
            Log?.Invoke("stepping aside");
            StartWalk(candidate, run: false, onArrive: null);
            return;
        }
    }

    // ------------------------------------------------------------------ user interaction

    /// <summary>Left click on Hoodie (the host opens the Quick Panel).</summary>
    public void Clicked()
    {
        Drives.OnUserAttention();
        Mind.OnClicked();
        Memory.Interaction("click");
        if (Machine.State == BehaviorState.Sleeping)
        {
            WakeUp(null);
            return;
        }
        if (Machine.State is BehaviorState.Idle or BehaviorState.Walking or BehaviorState.Emote && !Animation.IsOnCooldown(AnimClip.Wave))
        {
            _walkTargetX = null;
            React(PetEvent.Clicked);
        }
    }

    public void SetMode(PresenceMode mode)
    {
        if (mode == Mode) return;
        var old = Mode;
        Mode = mode;
        if (mode != PresenceMode.Alone) _modeBeforeAlone = mode;
        Settings.CurrentPresenceMode = mode;
        Log?.Invoke($"mode {old} -> {mode}");
        ModeChanged?.Invoke(mode);
        EffectiveMode = mode;

        if (old == PresenceMode.Alone && mode != PresenceMode.Alone) ComeBack();
        switch (mode)
        {
            case PresenceMode.Focus:
                if (CanReact) GoToRestSpot(then: () => BeginSit(sleepAfter: false));
                break;
            case PresenceMode.Quiet:
                if (Machine.State is BehaviorState.Walking) StopWalk("quiet");
                if (CanReact && Machine.State is BehaviorState.Idle or BehaviorState.Emote) BeginSit(sleepAfter: false);
                break;
            case PresenceMode.Play:
                if (CanReact)
                    EnsureStanding(() => { if (!Settings.ReducedMotion) JumpTo(Feet, 60); else PlayEmote(AnimClip.Success); });
                ScheduleDecision(1.5);
                break;
            case PresenceMode.Company:
                if (CanReact) EnsureStanding(() => PlayEmote(AnimClip.Wave));
                ScheduleDecision(1.0);
                break;
            case PresenceMode.Normal:
                ScheduleDecision(2);
                break;
        }
    }

    public void Execute(PetCommand command)
    {
        var m = Metrics;
        switch (command)
        {
            case PetCommand.ComeHere:
            {
                if (Mode == PresenceMode.Alone) SetMode(_modeBeforeAlone == PresenceMode.Alone ? PresenceMode.Normal : _modeBeforeAlone);
                if (!CanReact) return;
                var cm = World.MonitorAt(_cursor) ?? World.NearestMonitor(_cursor);
                var side = Feet.X < _cursor.X ? -1 : 1;
                var target = new Vec2(_cursor.X + side * Dip(70), cm.WorkArea.Bottom);
                var far = Vec2.Distance(target, Feet) > Dip(600);
                EnsureStanding(() => TravelTo(target, run: far, onArrive: () =>
                {
                    var dir = Math.Sign(_cursor.X - Feet.X);
                    if (dir != 0 && dir != Facing) TurnThen(() => PlayEmote(AnimClip.Wave));
                    else PlayEmote(AnimClip.Wave);
                }));
                break;
            }
            case PetCommand.StayHere:
                Territory.SetAnchor(Feet, Dip(220));
                if (Machine.State == BehaviorState.Walking) StopWalk("stay here");
                PlayEmote(AnimClip.Success);
                break;
            case PetCommand.YoureFree:
                Territory.ClearAnchor();
                PlayEmote(AnimClip.Wave);
                ScheduleDecision(1.5);
                break;
            case PetCommand.GoHome:
            {
                if (!CanReact) return;
                var home = Territory.HomeFeet() ?? DefaultHome();
                EnsureStanding(() =>
                {
                    if (!TravelTo(home, run: false, onArrive: () => BeginSit(false)))
                        TravelTo(home, run: false, onArrive: () => BeginSit(false), ignoreTerritory: true);
                });
                break;
            }
            case PetCommand.BeQuiet:
                SetMode(PresenceMode.Quiet);
                break;
            case PetCommand.LetsPlay:
                SetMode(PresenceMode.Play);
                break;
            case PetCommand.LeaveMeAlone:
                SetMode(PresenceMode.Alone);
                break;
            case PetCommand.ComeBack:
                SetMode(_modeBeforeAlone == PresenceMode.Alone ? PresenceMode.Normal : _modeBeforeAlone);
                if (HiddenReasons.HasFlag(HiddenReason.Alone)) ComeBack();
                break;
            case PetCommand.ShowBackpack:
                // The host opens the panel and calls BackpackOpened(true).
                break;
            case PetCommand.Normal:
                SetMode(PresenceMode.Normal);
                break;
            case PetCommand.Focus:
                SetMode(PresenceMode.Focus);
                break;
            case PetCommand.Company:
                SetMode(PresenceMode.Company);
                break;
        }
    }

    /// <summary>Walks to the floor below a point the user chose (desktop menu "Come here"); optionally stays there.</summary>
    public void ComeTo(Vec2 point, bool stay)
    {
        if (Mode == PresenceMode.Alone) SetMode(_modeBeforeAlone == PresenceMode.Alone ? PresenceMode.Normal : _modeBeforeAlone);
        var mon = World.MonitorAt(point) ?? World.NearestMonitor(point);
        var target = new Vec2(point.X, mon.WorkArea.Bottom);
        if (stay) Territory.ClearAnchor();
        if (!CanReact) return;
        var far = Vec2.Distance(target, Feet) > Dip(600);
        EnsureStanding(() =>
        {
            if (!TravelTo(target, run: far, onArrive: () =>
                {
                    if (stay) Territory.SetAnchor(Feet, Dip(220));
                    PlayEmote(stay ? AnimClip.ThumbsUp : AnimClip.Wave);
                }))
                TravelTo(target, run: far, onArrive: () => PlayEmote(AnimClip.Confused), ignoreTerritory: true);
        });
    }

    /// <summary>Makes the floor below a point Hoodie's Home (desktop menu "Set Home here") and walks there.</summary>
    public void SetHomeAt(Vec2 point)
    {
        var mon = World.MonitorAt(point) ?? World.NearestMonitor(point);
        var feet = new Vec2(Math.Clamp(point.X, mon.WorkArea.Left + Metrics.HalfWidthPx, mon.WorkArea.Right - Metrics.HalfWidthPx), mon.WorkArea.Bottom);
        Territory.SetHome(mon, feet);
        if (!CanReact) return;
        EnsureStanding(() => TravelTo(feet, run: false, onArrive: () => PlayEmote(AnimClip.Proud), ignoreTerritory: true));
    }

    public void SetHomeHere()
    {
        var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        Territory.SetHome(mon, Feet);
        PlayEmote(AnimClip.Success);
    }
}
