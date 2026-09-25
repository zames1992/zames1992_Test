using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Memory;
using HoodieCompanion.Companion.Perception;
using HoodieCompanion.Geometry;
using HoodieCompanion.Settings;

namespace HoodieCompanion.Companion.Behavior;

/// <summary>
/// Perception → Mind → Intent. What the PC and the user do becomes percepts; percepts nudge the Mind, trigger
/// small contextual reactions and create reasons for the next intent. Also: being considerate (keeping out of
/// the way while the user types), the expressiveness budget (rare big moments, frequent small ones), and memory.
/// </summary>
public sealed partial class PetController
{
    // Reasons waiting for the intent layer.
    private Vec2? _curiousAbout;
    private string? _curiousApp;
    private double _curiousUntil;
    private bool _breakDue;
    private double _breakAskedAt = -1e9;
    private bool _scaredFall;
    private bool _ridingDrag;
    private double _lastExpressive = -1e9;
    private double _nextOutOfWay;
    private double _restStartedAt = -1;
    private string _lastGreetDay = "";
    private double _memoryTick;

    /// <summary>Clips that are small enough to never count against the expressiveness budget.</summary>
    private static readonly HashSet<AnimClip> MicroClips = new()
    {
        AnimClip.HeadTilt, AnimClip.NoticeMovement, AnimClip.LookUp, AnimClip.LookLeft, AnimClip.LookRight, AnimClip.LookDown,
        AnimClip.Curious, AnimClip.Listen, AnimClip.SearchCursor, AnimClip.Suspicious,
    };

    private static Personality ShapePersonality(CompanionMemory memory)
    {
        var p = Personality.FromSeed(memory.Doc.PersonalitySeed);
        // Shaped by life together (bounded, slow): attachment grows with days and interactions, caution with scares,
        // confidence with climbs and rides.
        p.Attachment = Math.Clamp(p.Attachment + Math.Min(0.4, memory.DaysTogether * 0.02 + memory.HoursTogether * 0.004
                                                                + memory.Interactions("click") * 0.002 + memory.Interactions("item") * 0.01), 0, 1);
        p.Caution = Math.Clamp(p.Caution + Math.Min(0.2, memory.Count("scared:window-closed") * 0.03), 0, 1);
        p.Confidence = Math.Clamp(p.Confidence + Math.Min(0.25, memory.Count("climbed") * 0.01 + memory.Count("rode-window") * 0.02), 0, 1);
        return p;
    }

    private void UpdatePerception(in PetInput input, double dt)
    {
        var env = input.Env;
        env.UserIdleSeconds = input.UserIdleSeconds;
        if (input.LocalHour is int h) env.Hour = h;
        Perception.Update(dt, env);
        while (Perception.TryDequeue(out var p)) OnPercept(p);

        // Memory: time together, favourite apps, favourite places.
        var active = input.UserIdleSeconds < 120;
        Memory.TickTogether(dt, DateTime.Now, active);
        if (active && Perception.ForegroundProcess is { } fg) Memory.AppForeground(fg, dt);
        _memoryTick += dt;
        if (_memoryTick >= 5)
        {
            _memoryTick = 0;
            Memory.Tick(5);
            CheckProgression();
        }
        TrackRestPlace();
        KeepOutOfTheWay();
    }

    private void OnPercept(in Percept p)
    {
        Log?.Invoke($"percept {p.Kind} {p.Subject}");
        var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        var near = p.Where is Vec2 w && Math.Abs(w.X - Feet.X) < Dip(600) && mon.Bounds.Contains(w);
        switch (p.Kind)
        {
            case PerceptKind.WindowOpened:
                if (near && !Perception.UserBusy && CanReact)
                {
                    // Turns to look at the new thing. Gets used to it over time.
                    var used = Memory.Habituate("window-opened");
                    if (_rng.NextDouble() > used * 0.8) LookAtPoint(p.Where!.Value, 1.6);
                    if (_rng.NextDouble() < Mind.Traits.Curiosity * (1 - used) + 0.08) NoteCuriosity(p.Where!.Value, p.Subject);
                }
                break;
            case PerceptKind.AppFirstSeen:
                if (p.Subject is { } app)
                {
                    Memory.Remember("first-new-app");
                    var cat = AppCategories.Of(app, false);
                    if (cat != AppCategory.Unknown) Memory.Remember("first-app:" + cat, app);
                    if (p.Where is Vec2 where) NoteCuriosity(where, app);
                    Mind.Curiosity2Boost(0.15);
                }
                break;
            case PerceptKind.WindowDragStarted:
                if (_onSurface is { } sid && FindSurface(sid) is { } surf && p.Where is Vec2 c && Math.Abs(c.X - (surf.Left + surf.Right) / 2) < surf.Width)
                {
                    // Someone is moving the window Hoodie stands on: hold on!
                    _ridingDrag = true;
                    if (Machine.State is BehaviorState.Idle or BehaviorState.Sitting or BehaviorState.Emote)
                        Animation.Play(AnimClip.Balance, force: true, restart: true);
                }
                break;
            case PerceptKind.WindowDragEnded:
                if (_ridingDrag)
                {
                    _ridingDrag = false;
                    Memory.Habituate("rode-window");
                    if (Memory.Remember("rode-window")) React(PetEvent.TaskSucceeded);
                    else if (_rng.NextDouble() < 0.5) PlayEmote(Mind.Traits.Confidence > 0.55 ? AnimClip.Proud : AnimClip.Sigh);
                }
                break;
            case PerceptKind.TypingStarted:
                _nextOutOfWay = Math.Min(_nextOutOfWay, _time + 1.5);
                break;
            case PerceptKind.LongWorkSession:
                _breakDue = true;
                break;
            case PerceptKind.UserReturned:
            {
                // First activity of a new day: a "good morning" greeting (once a day).
                var day = DateTime.Now.ToString("yyyy-MM-dd");
                if (day != _lastGreetDay && Memory.DaysTogether > 1 && Mind.Afk == AfkPhase.Present)
                {
                    _lastGreetDay = day;
                    React(PetEvent.UserReturned);
                }
                break;
            }
            case PerceptKind.PcHot:
                Memory.Habituate("pc-hot");
                ScheduleDecision(1.5);
                break;
            case PerceptKind.PcCooled:
                if (_activity is { Name: "fan" or "carry" }) StopActivity();
                break;
            case PerceptKind.DownloadRunning:
                React(PetEvent.DownloadActive);
                break;
            case PerceptKind.GameStarted:
                Memory.Remember("first-game", p.Subject);
                break;
            case PerceptKind.NightFell:
                React(PetEvent.NightTime);
                break;
        }
    }

    /// <summary>Remember something interesting to go and look at (the intent layer decides whether to).</summary>
    private void NoteCuriosity(Vec2 where, string? app)
    {
        _curiousAbout = where;
        _curiousApp = app;
        _curiousUntil = _time + 45;
        if (Machine.State == BehaviorState.Idle) ScheduleDecision(Math.Min(_nextDecisionAt - _time, 2 + _rng.NextDouble() * 3));
    }

    private void LookAtPoint(Vec2 world, double seconds)
    {
        var head = HeadWorld;
        var dir = (world - head).Normalized();
        _lookAt = (-Facing * dir.X, dir.Y);
        _lookAtUntil = _time + seconds;
        if (Math.Sign(world.X - Feet.X) == -Facing && Machine.State == BehaviorState.Idle && Math.Abs(world.X - Feet.X) > Dip(80))
            TurnThen(() => Go(BehaviorState.Idle, "looked round", force: true));
    }

    private (double X, double Y) _lookAt;
    private double _lookAtUntil;

    // ------------------------------------------------------------------ being considerate

    /// <summary>
    /// While the user types, Hoodie does not stand in the middle of their work: if it is close to the pointer or
    /// on top of the (non-maximised) active window, it quietly walks aside. Never more than every ~20 s.
    /// </summary>
    private void KeepOutOfTheWay()
    {
        if (!Perception.Typing || _time < _nextOutOfWay || Machine.State is not (BehaviorState.Idle or BehaviorState.Sitting)) return;
        if (!Settings.AutonomousBehavior || Territory.Anchor is not null) return;
        _nextOutOfWay = _time + 20;
        var m = Metrics;
        var body = Transform.Bounds(RigTransform.BodyLocal);
        var nearPointer = Vec2.Distance(_cursor, body.Center) < Dip(240);
        var fg = Perception.ForegroundBounds;
        var mon = World.MonitorAt(Feet) ?? World.NearestMonitor(Feet);
        var overWindow = fg is RectD r && r.Intersects(body) && r.Width < mon.WorkArea.Width * 0.95;
        if (!nearPointer && !overWindow) return;
        double? target = null;
        if (overWindow && fg is RectD w)
        {
            // The nearest floor spot just outside the active window.
            foreach (var x in new[] { w.Left - m.HalfWidthPx * 1.4, w.Right + m.HalfWidthPx * 1.4 }.OrderBy(x => Math.Abs(x - Feet.X)))
            {
                if (x < mon.WorkArea.Left + m.HalfWidthPx || x > mon.WorkArea.Right - m.HalfWidthPx) continue;
                if (!Territory.CanStop(new Vec2(x, Feet.Y), m.HeightPx)) continue;
                target = x;
                break;
            }
        }
        if (target is null && nearPointer)
        {
            StepAside();
            Mind.CurrentReason = "making room while you type";
            return;
        }
        if (target is double tx)
        {
            Mind.CurrentIntent = "KeepOutOfWay";
            Mind.CurrentReason = "not standing on your work";
            _sequence.Clear();
            StartWalk(tx, run: false, onArrive: () => { if (_rng.NextDouble() < 0.6) BeginSit(false); });
        }
    }

    // ------------------------------------------------------------------ memory of places

    private void TrackRestPlace()
    {
        var resting = Machine.State is BehaviorState.Sitting or BehaviorState.Sleeping || _activity is { Sitting: true };
        if (resting && _restStartedAt < 0) _restStartedAt = _time;
        if (!resting && _restStartedAt >= 0)
        {
            var seconds = _time - _restStartedAt;
            _restStartedAt = -1;
            if (seconds > 20 && World.MonitorAt(Feet) is { } mon && _onSurface is null)
                Memory.RestedAt(mon.Id, (Feet.X - mon.WorkArea.Left) / Math.Max(1, mon.WorkArea.Width), seconds);
        }
    }

    private Vec2? FavoritePlaceFeet()
    {
        var m = Metrics;
        var fav = Memory.FavoritePlace(p =>
        {
            var mon = World.FindById(p.MonitorId);
            if (mon is null) return false;
            var x = mon.WorkArea.Left + p.RelX * mon.WorkArea.Width;
            return Territory.CanStop(new Vec2(x, mon.WorkArea.Bottom), m.HeightPx);
        });
        if (fav is null) return null;
        var mm = World.FindById(fav.MonitorId)!;
        return new Vec2(mm.WorkArea.Left + fav.RelX * mm.WorkArea.Width, mm.WorkArea.Bottom);
    }

    // ------------------------------------------------------------------ expressiveness budget

    /// <summary>
    /// Rare big moments are worth more than constant animation: a non-micro contextual reaction is allowed
    /// only every ~1.5 minutes (4 minutes while the user is busy).
    /// </summary>
    private bool ExpressiveAllowed(IReadOnlyList<AnimClip> clips, ReactionPriority priority)
    {
        if (priority < ReactionPriority.Contextual) return true;
        if (clips.Count == 1 && MicroClips.Contains(clips[0])) return true;
        var gap = Perception.UserBusy ? 240 : 90;
        if (_time - _lastExpressive < gap) return false;
        _lastExpressive = _time;
        return true;
    }

    // ------------------------------------------------------------------ intent context

    private IntentContext BuildIntentContext(in DecisionContext basics) => new(
        basics,
        Mind.Traits,
        Mind,
        Perception.UserBusy,
        Perception.Typing,
        Perception.ForegroundCategory,
        Perception.Gaming,
        Perception.PcHot,
        Perception.Night || Mind.IsNight,
        Perception.WorkSessionMinutes,
        _curiousAbout is not null && _time < _curiousUntil,
        _breakDue && !Perception.Typing && _userIdle is > 1.5 and < 90 && _time - _breakAskedAt > 3600,
        FavoritePlaceFeet() is not null,
        id => Memory.IsUnlocked(id),
        id => Memory.HasItem(id));

    /// <summary>Carries out the intent-level activities (the classic ones are handled in Decide).</summary>
    private bool DoIntent(Activity act)
    {
        var m = Metrics;
        switch (act)
        {
            case Activity.Nothing:
                // Just be. Sometimes sit down for it.
                if (_rng.NextDouble() < 0.35 + (1 - Mind.Energy) * 0.3) BeginSit(false);
                ScheduleDecision(IntentsDelay() * 1.5);
                return true;
            case Activity.InvestigateWindow:
            {
                if (_curiousAbout is not Vec2 where) return false;
                var app = _curiousApp;
                _curiousAbout = null;
                var mon = World.MonitorAt(where) ?? World.NearestMonitor(where);
                var x = Math.Clamp(where.X, mon.WorkArea.Left + m.HalfWidthPx, mon.WorkArea.Right - m.HalfWidthPx);
                Memory.Habituate("investigate");
                return TravelTo(new Vec2(x, mon.WorkArea.Bottom), run: false, onArrive: () =>
                {
                    Memory.Remember("investigated-window", app);
                    // Look up at it; a confident, curious Hoodie might climb onto it.
                    RunSequence(BehaviorState.ReceivingItem, new() { (AnimClip.LookUp, null), (AnimClip.HeadTilt, null) }, () =>
                    {
                        if (Mind.Traits.Curiosity + Mind.Traits.Confidence > 1.1 && _rng.NextDouble() < 0.5) VisitSurface();
                    });
                });
            }
            case Activity.CoolDown:
                if (Memory.HasItem("fan"))
                {
                    if (Memory.Remember("first-fan")) Memory.Interaction("item");
                    StartActivity("fan", sitting: true,
                        enter: new() { (AnimClip.OpenBackpack, 0.5), (AnimClip.ShowItem, null) }, loop: AnimClip.FanSelf,
                        exit: new() { (AnimClip.CloseBackpack, null) }, loopSeconds: 25 + _rng.NextDouble() * 25, item: WorldItem.Fan);
                }
                else
                {
                    StartActivity("carry", sitting: false, enter: new(), loop: AnimClip.CarryLoad, exit: new() { (AnimClip.Sigh, null) },
                        loopSeconds: 6 + _rng.NextDouble() * 5);
                }
                return true;
            case Activity.SuggestBreak:
            {
                _breakDue = false;
                _breakAskedAt = _time;
                Memory.Remember("first-break-hint");
                var cm = World.MonitorAt(_cursor) ?? World.NearestMonitor(_cursor);
                var side = Feet.X < _cursor.X ? -1 : 1;
                var x = Math.Clamp(_cursor.X + side * Dip(220), cm.WorkArea.Left + m.HalfWidthPx, cm.WorkArea.Right - m.HalfWidthPx);
                var mug = Memory.HasItem("mug");
                return TravelTo(new Vec2(x, cm.WorkArea.Bottom), run: false, onArrive: () =>
                {
                    var dir = Math.Sign(_cursor.X - Feet.X);
                    void Ask()
                    {
                        if (mug)
                            StartActivity("break", sitting: false, enter: new() { (AnimClip.Stretch, null), (AnimClip.ShowItem, null) },
                                loop: AnimClip.SipMug, exit: new() { (AnimClip.Wave, null) }, loopSeconds: 8 + _rng.NextDouble() * 6, item: WorldItem.Mug);
                        else
                            RunSequence(BehaviorState.ReceivingItem, new() { (AnimClip.Stretch, null), (AnimClip.CheckTime, null), (AnimClip.Point, null) }, null);
                    }
                    if (dir != 0 && dir != Facing) TurnThen(Ask); else Ask();
                });
            }
            case Activity.WorkAlongside:
                StartActivity("laptop", sitting: true, enter: new() { (AnimClip.LaptopOpen, null) }, loop: AnimClip.LaptopType,
                    exit: new() { (AnimClip.LaptopClose, null) }, loopSeconds: 40 + _rng.NextDouble() * 60);
                return true;
            case Activity.FavoritePlace:
                if (FavoritePlaceFeet() is not Vec2 fav) return false;
                if (Vec2.Distance(fav, Feet) < Dip(60)) { BeginSit(false); return true; }
                return TravelTo(fav, run: false, onArrive: () =>
                {
                    Memory.Remember("favourite-spot");
                    BeginSit(false);
                });
            case Activity.PlayBall:
                if (!Memory.HasItem("ball")) return false;
                StartActivity("ball", sitting: false, enter: new() { (AnimClip.ShowItem, 0.7) }, loop: AnimClip.PlayBall,
                    exit: new(), loopSeconds: 8 + _rng.NextDouble() * 10, item: WorldItem.Ball);
                Memory.Remember("first-ball-game");
                return true;
            case Activity.WatchUser:
            {
                var dir = Math.Sign(_cursor.X - Feet.X);
                if (dir != 0 && dir != Facing) TurnThen(() => BeginSit(false));
                else BeginSit(false);
                _lookScriptUntil = 0;
                return true;
            }
        }
        return false;
    }

    private double IntentsDelay() => Intents.NextDecisionDelay(EffectiveMode, Perception.UserBusy);

    // ------------------------------------------------------------------ progression

    private readonly Queue<Unlock> _announce = new();

    /// <summary>New things become available over time; Hoodie "finds" them at a calm moment and shows you.</summary>
    private void CheckProgression()
    {
        foreach (var u in Progression.Due(Memory).ToList())
        {
            Memory.Unlock(u.Id);
            if (u.Kind == UnlockKind.Item) Memory.GiveItem(u.Id);
            Memory.Remember("unlock:" + u.Id);
            if (u.Kind == UnlockKind.Item) _announce.Enqueue(u);
            Log?.Invoke($"unlocked {u.Id}");
        }
        // Show a new item when nothing else is going on and the user is around but not busy.
        if (_announce.Count > 0 && Machine.State is BehaviorState.Idle or BehaviorState.Sitting && !Perception.UserBusy && _userIdle < 60 && CanReact)
        {
            var u = _announce.Dequeue();
            var item = u.Id switch { "fan" => WorldItem.Fan, "mug" => WorldItem.Mug, "ball" => WorldItem.Ball, "blanket" => WorldItem.Blanket, _ => WorldItem.None };
            StartActivity("found", sitting: false, enter: new() { (AnimClip.OpenBackpack, 0.45), (AnimClip.SearchBackpack, 1.2) },
                loop: AnimClip.ShowItem, exit: new() { (AnimClip.Happy, null), (AnimClip.CloseBackpack, null) }, loopSeconds: 1.8, item: item);
        }
    }

    // ------------------------------------------------------------------ scares

    /// <summary>After falling because the window under it vanished: a scare, remembered.</summary>
    private void AfterScaredLanding()
    {
        _scaredFall = false;
        var used = Memory.Habituate("scared:window-closed");
        Memory.Remember("window-closed-under-me");
        var clip = used < 0.5 ? AnimClip.Scared : _rng.NextDouble() < 0.5 ? AnimClip.Annoyed : AnimClip.Sigh;
        _pendingAfterLanding = clip;
    }

    private AnimClip? _pendingAfterLanding;
}
