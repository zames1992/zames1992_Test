using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Interaction;

public enum CursorEvent
{
    None,
    Curious,
    Surprised,
    Obstructing,
}

/// <summary>
/// Treats the cursor as the user's hand. Produces a look target and occasional, cooled-down reactions.
/// Also notices when Hoodie is in the way (pointer resting on it, or repeated clicks right next to it).
/// </summary>
public sealed class CursorInteractionService
{
    private Vec2 _last;
    private bool _hasLast;
    private double _hoverNear;
    private double _restOnPet;
    private bool _prevButton;
    private readonly Queue<double> _nearClicks = new();

    public Vec2 Velocity { get; private set; }   // px/s, smoothed
    public double Speed => Velocity.Length;
    public double ShyUntil { get; private set; } = double.NegativeInfinity;

    public bool IsShy(double now) => now < ShyUntil;

    public void MakeShy(double now, double seconds) => ShyUntil = Math.Max(ShyUntil, now + seconds);

    /// <summary>
    /// Updates cursor tracking. <paramref name="petBox"/> is Hoodie's world bounding box,
    /// <paramref name="head"/> its head position; distances are measured in DIP via <paramref name="dipScale"/>.
    /// </summary>
    public CursorEvent Update(double now, double dt, Vec2 cursor, bool buttonDown, RectD petBox, Vec2 head, double dipScale)
    {
        if (dt <= 0) return CursorEvent.None;
        if (_hasLast)
        {
            var inst = (cursor - _last) / dt;
            Velocity = MathUtil.Approach(Velocity, inst, 18, dt);
        }
        _last = cursor;
        _hasLast = true;

        var ev = CursorEvent.None;
        var dist = Vec2.Distance(cursor, head) / dipScale;
        var speedDip = Speed / dipScale;
        var overPet = petBox.Contains(cursor);

        // Surprise: fast approach towards the head.
        var toHead = head - cursor;
        var approaching = toHead.X * Velocity.X + toHead.Y * Velocity.Y > 0;
        if (dist < 170 && speedDip > 2300 && approaching) ev = CursorEvent.Surprised;

        // Curiosity: the hand lingers nearby.
        if (dist < 120 && speedDip < 250 && !overPet) _hoverNear += dt; else _hoverNear = Math.Max(0, _hoverNear - dt * 2);
        if (_hoverNear > 1.6 && ev == CursorEvent.None)
        {
            ev = CursorEvent.Curious;
            _hoverNear = 0;
        }

        // Obstruction: pointer resting on Hoodie without clicking...
        if (overPet && !buttonDown && speedDip < 40) _restOnPet += dt; else _restOnPet = 0;
        if (_restOnPet > 1.8)
        {
            _restOnPet = 0;
            ev = CursorEvent.Obstructing;
        }

        // ...or repeated clicks right next to it (the user is trying to reach something behind it).
        if (buttonDown && !_prevButton && !overPet && petBox.Inflate(50 * dipScale, 30 * dipScale).Contains(cursor))
        {
            _nearClicks.Enqueue(now);
        }
        while (_nearClicks.Count > 0 && now - _nearClicks.Peek() > 8) _nearClicks.Dequeue();
        if (_nearClicks.Count >= 2)
        {
            _nearClicks.Clear();
            ev = CursorEvent.Obstructing;
        }
        _prevButton = buttonDown;
        return ev;
    }

    /// <summary>Look weight 0..1 by distance (DIP).</summary>
    public static double LookWeight(double distDip, double radius = 320) => Math.Clamp(1 - distDip / radius, 0, 0.9);
}
