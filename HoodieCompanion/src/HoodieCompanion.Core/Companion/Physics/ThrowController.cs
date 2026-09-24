using HoodieCompanion.Geometry;

namespace HoodieCompanion.Companion.Physics;

/// <summary>
/// Estimates release velocity from recent cursor movement: a recency-weighted average of
/// segment velocities over a short rolling window.
/// </summary>
public sealed class ThrowController
{
    private readonly (double T, Vec2 P)[] _samples = new (double, Vec2)[32];
    private int _count;
    private int _head;

    public double Window { get; init; } = 0.12;
    public double TuningFactor { get; init; } = 1.0;

    public void Reset()
    {
        _count = 0;
        _head = 0;
    }

    public void AddSample(double time, Vec2 position)
    {
        _samples[_head] = (time, position);
        _head = (_head + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
    }

    /// <summary>Velocity in pixels per second at <paramref name="now"/>.</summary>
    public Vec2 Estimate(double now)
    {
        if (_count < 2) return Vec2.Zero;
        var sum = Vec2.Zero;
        double wsum = 0;
        double lastMove = double.NegativeInfinity;
        for (var i = 1; i < _count; i++)
        {
            var a = _samples[(_head - i - 1 + _samples.Length * 2) % _samples.Length];
            var b = _samples[(_head - i + _samples.Length * 2) % _samples.Length];
            if (now - b.T > Window) break;
            var dt = b.T - a.T;
            if (dt <= 1e-4) continue;
            var v = (b.P - a.P) / dt;
            if ((b.P - a.P).LengthSquared > 0.25) lastMove = Math.Max(lastMove, b.T);
            var w = Math.Exp(-(now - b.T) / 0.045) * dt;
            sum += v * w;
            wsum += w;
        }
        if (wsum <= 0) return Vec2.Zero;
        var est = sum / wsum * TuningFactor;
        // If the pointer stopped before release, the throw is weak.
        if (double.IsFinite(lastMove)) est *= Math.Exp(-Math.Max(0, now - lastMove - 0.02) / 0.05);
        else est = Vec2.Zero;
        return est;
    }
}
