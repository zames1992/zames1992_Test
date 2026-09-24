using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Physics;
using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Behavior;

public enum SurfaceKind
{
    /// <summary>The visible part of a window's top edge (title bar).</summary>
    Window,
    /// <summary>The top of a desktop icon.</summary>
    Icon,
}

/// <summary>A walkable ledge above the floor, in virtual-desktop physical pixels. Id is stable while it exists.</summary>
public readonly record struct Surface(string Id, SurfaceKind Kind, double Left, double Right, double Y)
{
    public double Width => Right - Left;
    public bool Contains(double x, double margin = 0) => x >= Left - margin && x <= Right + margin;
}

/// <summary>
/// Extra platforms: window tops and desktop icons. Hoodie can jump or climb onto them, sit on their edge,
/// ride along when a window moves, hop down politely when the pointer comes close to a title bar, and falls
/// when the platform disappears. Also: climbing up the side of the screen and sliding back down.
/// </summary>
public sealed partial class PetController
{
    private IReadOnlyList<Surface> _surfaces = Array.Empty<Surface>();
    private string? _onSurface;
    private double _surfaceLeft;
    private string? _ignoreSurface;
    private double _ignoreSurfaceUntil;

    /// <summary>Id of the platform Hoodie stands on (null = a monitor floor).</summary>
    public string? StandingOn => _onSurface;

    public IReadOnlyList<Surface> Surfaces => _surfaces;

    /// <summary>The host reports window tops / icons a few times per second.</summary>
    public void SetSurfaces(IReadOnlyList<Surface> surfaces) => _surfaces = surfaces;

    private Surface? FindSurface(string id)
    {
        foreach (var s in _surfaces)
            if (s.Id == id) return s;
        return null;
    }

    /// <summary>Every frame while standing on a platform: ride along, fall when it vanishes, make room for the pointer.</summary>
    private void UpdateSurface()
    {
        if (_onSurface is not { } id) return;
        if (Machine.State is BehaviorState.Grabbed or BehaviorState.Airborne or BehaviorState.Jumping or BehaviorState.Hidden
            or BehaviorState.Leaving or BehaviorState.Vanishing or BehaviorState.Appearing or BehaviorState.Returning)
        {
            _onSurface = null;
            return;
        }
        if (Machine.State == BehaviorState.Climbing && _climb is not null) return;
        var m = Metrics;
        if (FindSurface(id) is not { } s)
        {
            FallOffSurface("platform gone");
            return;
        }
        // A window that moved sideways carries Hoodie with it (only when its width did not change).
        var dx = s.Left - _surfaceLeft;
        _surfaceLeft = s.Left;
        if (Math.Abs(dx) > 0.01 && Math.Abs(dx) < Dip(400)) Feet = new Vec2(Feet.X + dx, Feet.Y);
        if (Math.Abs(s.Y - Feet.Y) > 0.01)
        {
            if (Math.Abs(s.Y - Feet.Y) > Dip(300)) { FallOffSurface("platform jumped"); return; }
            Feet = new Vec2(Feet.X, s.Y);
        }
        if (!s.Contains(Feet.X, m.HalfWidthPx * 0.2))
        {
            FallOffSurface("off the edge");
            return;
        }
        // On a title bar, the user probably wants to click there: hop down when the pointer comes close.
        if (s.Kind == SurfaceKind.Window && Vec2.Distance(_cursor, HeadWorld) < Dip(150) && CanReact
            && Machine.State is BehaviorState.Idle or BehaviorState.Sitting or BehaviorState.Walking or BehaviorState.Emote or BehaviorState.Activity)
        {
            HopDown("pointer wants the title bar");
        }
    }

    private void StandOnSurface(Surface s)
    {
        _onSurface = s.Id;
        _surfaceLeft = s.Left;
        Feet = new Vec2(Math.Clamp(Feet.X, s.Left + 2, s.Right - 2), s.Y);
        Log?.Invoke($"standing on {s.Kind} {s.Id}");
    }

    private void FallOffSurface(string reason)
    {
        var m = Metrics;
        _ignoreSurface = _onSurface;
        _ignoreSurfaceUntil = _time + 0.6;
        _onSurface = null;
        _sequence.Clear();
        DropActivity();
        Physics.Launch(Feet - new Vec2(0, m.FeetOffsetPx), new Vec2(0, -Dip(60)));
        _thrownByUser = false;
        _jumpToMonitor = true;
        EnterAirborne(reason);
    }

    /// <summary>Jumps down from the platform to the floor below.</summary>
    private void HopDown(string reason)
    {
        if (_onSurface is null) return;
        var m = Metrics;
        _ignoreSurface = _onSurface;
        _ignoreSurfaceUntil = _time + 0.6;
        _onSurface = null;
        DropActivity();
        _walkTargetX = null;
        Physics.Launch(Feet - new Vec2(0, m.FeetOffsetPx), new Vec2(Facing * Dip(160), -Dip(420)));
        _thrownByUser = false;
        _jumpToMonitor = true;
        EnterAirborne("hop down: " + reason);
        Animation.Play(AnimClip.Airborne, force: true);
    }

    /// <summary>While airborne: land on a platform crossed from above (not the one just left).</summary>
    private bool TryLandOnSurface(Vec2 prevCenter, Vec2 velocityBefore, BodyMetrics m)
    {
        if (_surfaces.Count == 0) return false;
        var prevFeet = prevCenter.Y + m.FeetOffsetPx;
        var feet = Physics.Center.Y + m.FeetOffsetPx;
        if (feet <= prevFeet) return false;
        var x = Physics.Center.X;
        foreach (var s in _surfaces)
        {
            if (s.Id == _ignoreSurface && _time < _ignoreSurfaceUntil) continue;
            if (prevFeet > s.Y + 0.5 || feet < s.Y) continue;
            if (!s.Contains(x, -m.HalfWidthPx * 0.15)) continue;
            if (s.Width < m.HalfWidthPx * 0.6) continue;
            Physics.Center = new Vec2(x, s.Y - m.FeetOffsetPx);
            var impact = Math.Max(0, velocityBefore.Y) / m.MonitorScale;
            var sideways = velocityBefore.X / m.MonitorScale;
            Physics.Velocity = Vec2.Zero;
            Land(new LandingInfo(impact, sideways * 0.3), m);
            StandOnSurface(s);
            return true;
        }
        return false;
    }

    /// <summary>A platform on this monitor Hoodie could visit: wide enough, with head room, not too high.</summary>
    private Surface? PickSurfaceToVisit()
    {
        if (_surfaces.Count == 0 || _onSurface is not null) return null;
        var m = Metrics;
        var mon = World.MonitorAt(Feet);
        if (mon is null) return null;
        var options = _surfaces.Where(s =>
                s.Width >= m.HalfWidthPx * 1.2 &&
                s.Y < Feet.Y - Dip(50) &&
                s.Y > mon.WorkArea.Top + m.HeightPx + Dip(10) &&
                mon.WorkArea.ContainsX(s.Left + s.Width / 2) &&
                Feet.Y - s.Y < Dip(700) &&
                Territory.CanStop(new Vec2((s.Left + s.Right) / 2, s.Y), m.HeightPx))
            .ToList();
        if (options.Count == 0) return null;
        return options[_rng.Next(options.Count)];
    }

    /// <summary>Walks under the platform and jumps up (low) or props a ladder and climbs (high).</summary>
    private bool VisitSurface()
    {
        if (PickSurfaceToVisit() is not { } s) return false;
        var m = Metrics;
        var inset = Math.Min(m.HalfWidthPx * 0.8, s.Width / 3);
        var landX = Math.Clamp(Feet.X, s.Left + inset, s.Right - inset);
        var rise = Feet.Y - s.Y;
        var id = s.Id;
        Log?.Invoke($"visit {s.Kind} {id} rise {rise:0}");
        if (rise <= Dip(230) && !Settings.ReducedMotion)
        {
            // Stand just beside the landing spot, then jump up onto it.
            var standX = landX - Facing * Dip(40);
            StartWalk(standX, run: false, onArrive: () =>
            {
                if (FindSurface(id) is { } now) JumpTo(new Vec2(Math.Clamp(landX, now.Left + inset, now.Right - inset), now.Y), extraApexDip: 45);
            });
            return true;
        }
        StartWalk(landX, run: false, onArrive: () =>
        {
            if (FindSurface(id) is not { } now || !now.Contains(Feet.X)) return;
            _climbOntoSurface = id;
            ClimbUp(now.Y, Feet.X);
        });
        return true;
    }

    private string? _climbOntoSurface;

    /// <summary>Test / QA hooks: start the corresponding autonomous activity now.</summary>
    public bool DebugVisitSurface() => VisitSurface();

    public bool DebugClimbWall() => ClimbWall();

    /// <summary>Called when a ladder climb finishes: if it was onto a platform, stand on it.</summary>
    private void ArrivedAtLadderTop()
    {
        if (_climbOntoSurface is not { } id) return;
        _climbOntoSurface = null;
        if (FindSurface(id) is { } s && s.Contains(Feet.X, Metrics.HalfWidthPx * 0.2)) StandOnSurface(s);
        else FallOffSurface("platform moved away");
    }

    // ------------------------------------------------------------------ climbing the side of the screen

    private sealed class WallPlan
    {
        public required int Dir;         // -1 left edge, +1 right edge
        public required double FloorY;
        public required double TopY;
        public int Phase;                // 0 jump on, 1 up, 2 look around, 3 slide down
        public double HoldUntil;
    }

    private WallPlan? _wall;

    /// <summary>Is there a screen edge without a neighbouring monitor near enough to climb?</summary>
    private int? ClimbableWallSide()
    {
        var mon = World.MonitorAt(Feet);
        if (mon is null || _onSurface is not null) return null;
        var left = World.MonitorBeside(mon, -1) is null;
        var right = World.MonitorBeside(mon, 1) is null;
        if (!left && !right) return null;
        if (left && right) return Feet.X - mon.WorkArea.Left < mon.WorkArea.Right - Feet.X ? -1 : 1;
        return left ? -1 : 1;
    }

    /// <summary>Walks to the edge of the screen, climbs up its side, looks around, slides back down.</summary>
    private bool ClimbWall()
    {
        if (Settings.ReducedMotion || ClimbableWallSide() is not int dir) return false;
        var m = Metrics;
        var mon = World.MonitorAt(Feet)!;
        var edgeX = dir < 0 ? mon.WorkArea.Left + m.HalfWidthPx * 1.05 : mon.WorkArea.Right - m.HalfWidthPx * 1.05;
        if (!Territory.CanStop(new Vec2(edgeX, Feet.Y), m.HeightPx)) return false;
        var height = mon.WorkArea.Height * (0.25 + _rng.NextDouble() * 0.3);
        var topY = Math.Max(mon.WorkArea.Top + m.HeightPx * 1.1, mon.WorkArea.Bottom - height);
        StartWalk(edgeX, run: false, onArrive: () =>
        {
            void Start()
            {
                _wall = new WallPlan { Dir = dir, FloorY = Feet.Y, TopY = topY };
                Go(BehaviorState.Climbing, "climb the screen side", force: true);
                Animation.Play(AnimClip.Jump, force: true, restart: true);
            }
            if (Facing != dir) TurnThen(Start);
            else Start();
        });
        return true;
    }

    private void UpdateWall(double dt)
    {
        var w = _wall!;
        var speed = Dip(ClimbSpeedDip) * 0.8;
        switch (w.Phase)
        {
            case 0:
                if (!Animation.IsFinished) return;
                w.Phase = 1;
                Animation.Play(AnimClip.ClimbLadder, force: true);
                return;
            case 1:
            {
                var step = Math.Min(Feet.Y - w.TopY, speed * dt);
                Feet = new Vec2(Feet.X, Feet.Y - step);
                _walkPhase += step / Dip(64);
                if (Feet.Y - w.TopY > 0.5) return;
                w.Phase = 2;
                w.HoldUntil = _time + 1.6 + _rng.NextDouble() * 1.6;
                Animation.Play(AnimClip.HangEdge, force: true, restart: true);
                return;
            }
            case 2:
                if (_time < w.HoldUntil) return;
                w.Phase = 3;
                Animation.Play(AnimClip.ClimbRope, force: true);
                return;
            case 3:
            {
                var step = Math.Min(w.FloorY - Feet.Y, speed * 1.4 * dt);
                Feet = new Vec2(Feet.X, Feet.Y + step);
                _walkPhase += step / Dip(80);
                if (w.FloorY - Feet.Y > 0.5) return;
                Feet = new Vec2(Feet.X, w.FloorY);
                _wall = null;
                Go(BehaviorState.Landing, "slid down the side", force: true);
                _landingImpact = 0;
                _slideVelocity = 0;
                Mind.OnPlayed();
                Animation.Play(AnimClip.LandSoft, force: true, restart: true);
                return;
            }
        }
    }
}
