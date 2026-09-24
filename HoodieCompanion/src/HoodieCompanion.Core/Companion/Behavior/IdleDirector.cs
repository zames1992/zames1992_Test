using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Settings;

namespace HoodieCompanion.Companion.Behavior;

/// <summary>Posture the idle director is choosing for.</summary>
public enum IdlePosture
{
    Standing,
    Sitting,
    Lying,
}

/// <summary>
/// Keeps an idle Hoodie alive without looking like a loop. Four rarity tiers, each on its own clock:
/// <list type="bullet">
/// <item>Blink: every few seconds (the animation controller's overlay).</item>
/// <item>Frequent (every ~6-14 s): look around, tilt the head, shift weight, breathing variant.</item>
/// <item>Occasional (every ~25-60 s): scratch, stretch, inspect itself, listen, sigh, check the time.</item>
/// <item>Rare (every ~3-6 min): spin, dance, count fingers, tiny playful moments.</item>
/// </list>
/// Contextual clips never come from here: they are only played by <see cref="ReactionSystem"/> on events.
/// Choices are weighted by the <see cref="Mind"/> and never repeat back to back.
/// </summary>
public sealed class IdleDirector
{
    private readonly Random _rng;
    private double _nextFrequent, _nextOccasional, _nextRare, _nextBaseSwap;
    private AnimClip _last = AnimClip.IdleBreathing;

    public IdleDirector(Random rng)
    {
        _rng = rng;
        Reset(0);
    }

    /// <summary>The standing breathing variant currently used as the base loop.</summary>
    public AnimClip BaseLoop { get; private set; } = AnimClip.IdleBreathing;

    public void Reset(double now)
    {
        _nextFrequent = now + 5 + _rng.NextDouble() * 6;
        _nextOccasional = now + 22 + _rng.NextDouble() * 25;
        _nextRare = now + 150 + _rng.NextDouble() * 150;
        _nextBaseSwap = now + 20 + _rng.NextDouble() * 20;
    }

    /// <summary>
    /// Returns a micro-action to play now, or null. <paramref name="calm"/> (Focus/Quiet) stretches every
    /// interval and removes big movements.
    /// </summary>
    public AnimClip? Tick(double now, IdlePosture posture, Mind mind, PresenceMode mode, bool reducedMotion, bool cursorNear)
    {
        var calm = mode is PresenceMode.Focus or PresenceMode.Quiet;
        if (now >= _nextBaseSwap)
        {
            _nextBaseSwap = now + 20 + _rng.NextDouble() * 25;
            BaseLoop = BaseLoop == AnimClip.IdleBreathing ? AnimClip.IdleBreathing2 : AnimClip.IdleBreathing;
        }

        var stretch = calm ? 2.5 : mode == PresenceMode.Play ? 0.6 : 1.0;
        if (now >= _nextRare)
        {
            _nextRare = now + (180 + _rng.NextDouble() * 180) * stretch;
            _nextOccasional = Math.Max(_nextOccasional, now + 10);
            _nextFrequent = Math.Max(_nextFrequent, now + 4);
            if (!calm && Pick(Rare(posture, mind, mode, reducedMotion, cursorNear)) is AnimClip r) return r;
        }
        if (now >= _nextOccasional)
        {
            _nextOccasional = now + (25 + _rng.NextDouble() * 35) * stretch;
            _nextFrequent = Math.Max(_nextFrequent, now + 4);
            if (Pick(Occasional(posture, mind, calm)) is AnimClip o) return o;
        }
        if (now >= _nextFrequent)
        {
            _nextFrequent = now + (6 + _rng.NextDouble() * 8) * stretch;
            if (Pick(Frequent(posture, mind)) is AnimClip f) return f;
        }
        return null;
    }

    private static IEnumerable<(AnimClip, double)> Frequent(IdlePosture p, Mind m)
    {
        if (p == IdlePosture.Lying) yield break;
        yield return (AnimClip.LookLeft, 1);
        yield return (AnimClip.LookRight, 1);
        yield return (AnimClip.LookUp, 0.5 + m.Curiosity);
        yield return (AnimClip.LookDown, 0.4 + m.Boredom);
        yield return (AnimClip.HeadTilt, 0.6 + m.Curiosity * 0.6);
        if (p == IdlePosture.Standing) yield return (AnimClip.WeightShift, 1.2);
    }

    private static IEnumerable<(AnimClip, double)> Occasional(IdlePosture p, Mind m, bool calm)
    {
        switch (p)
        {
            case IdlePosture.Lying:
                yield return (AnimClip.LookUp, 1);
                yield break;
            case IdlePosture.Sitting:
                yield return (AnimClip.ChinRest, 0.6 + m.Boredom * 1.5);
                yield return (AnimClip.PickSurface, 0.4 + m.Boredom);
                yield return (AnimClip.CountFingers, 0.3 + m.Boredom * 0.6);
                yield return (AnimClip.Yawn, m.Sleepiness * 2);
                yield break;
        }
        yield return (AnimClip.Scratch, 1);
        yield return (AnimClip.StretchBody, 0.4 + (1 - m.Energy) * 0.8);
        yield return (AnimClip.InspectSelf, 0.8);
        yield return (AnimClip.Listen, 0.4 + m.Curiosity);
        yield return (AnimClip.Sigh, m.Boredom * 1.6);
        yield return (AnimClip.StareVoid, m.Boredom * 1.2);
        yield return (AnimClip.Yawn, m.Sleepiness * 2);
        yield return (AnimClip.Stretch, 0.3 + (1 - m.Energy) * 0.5);
        if (!calm) yield return (AnimClip.WatchWindow, 0.4 + m.Curiosity * 0.5);
    }

    private static IEnumerable<(AnimClip, double)> Rare(IdlePosture p, Mind m, PresenceMode mode, bool reducedMotion, bool cursorNear)
    {
        if (p != IdlePosture.Standing) yield break;
        if (!reducedMotion)
        {
            yield return (AnimClip.Spin, 0.4 + m.Mood);
            yield return (AnimClip.Dance, 0.2 + m.Mood * 0.6 + (mode == PresenceMode.Play ? 1 : 0));
            if (cursorNear) yield return (AnimClip.CatchCursor, 0.8 + (mode == PresenceMode.Play ? 1.5 : 0));
            yield return (AnimClip.Balance, 0.3);
        }
        yield return (AnimClip.CountFingers, 0.3 + m.Boredom);
        yield return (AnimClip.Proud, 0.2 + m.Mood * 0.3);
        yield return (AnimClip.PeekIn, 0.4 + m.Curiosity * 0.5);
    }

    private AnimClip? Pick(IEnumerable<(AnimClip Clip, double Weight)> options)
    {
        var list = options.Where(o => o.Weight > 0 && o.Clip != _last).ToList();
        if (list.Count == 0) return null;
        var total = list.Sum(o => o.Weight);
        var r = _rng.NextDouble() * total;
        foreach (var (clip, w) in list)
        {
            r -= w;
            if (r <= 0)
            {
                _last = clip;
                return clip;
            }
        }
        _last = list[^1].Clip;
        return _last;
    }
}
