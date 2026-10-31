namespace HoodieCompanion.Geometry;

public static class MathUtil
{
    public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static double SmoothStep(double t)
    {
        t = Clamp01(t);
        return t * t * (3 - 2 * t);
    }

    public static double EaseOutBack(double t)
    {
        t = Clamp01(t);
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    public static double EaseInOut(double t) => SmoothStep(t);

    /// <summary>Frame-rate independent exponential approach: returns the new value.</summary>
    public static double Approach(double current, double target, double sharpness, double dt) =>
        target + (current - target) * Math.Exp(-sharpness * dt);

    public static Vec2 Approach(Vec2 current, Vec2 target, double sharpness, double dt) =>
        new(Approach(current.X, target.X, sharpness, dt), Approach(current.Y, target.Y, sharpness, dt));

    /// <summary>Triangle-ish 0..1..0 pulse over [0,1].</summary>
    public static double Bump(double t) => Math.Sin(Clamp01(t) * Math.PI);

    public static double MoveTowards(double current, double target, double maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta) return target;
        return current + Math.Sign(target - current) * maxDelta;
    }
}
