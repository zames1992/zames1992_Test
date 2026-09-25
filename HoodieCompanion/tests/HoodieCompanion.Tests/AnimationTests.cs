using System.Text.Json;
using HoodieCompanion.Companion.Animation;
using Xunit;

namespace HoodieCompanion.Tests;

public class AnimationTests
{
    [Fact]
    public void EveryClip_HasCatalogEntry()
    {
        foreach (AnimClip c in Enum.GetValues<AnimClip>()) Assert.NotNull(AnimationCatalog.Get(c));
        Assert.Equal(Enum.GetValues<AnimClip>().Length, AnimationCatalog.All.Count);
    }

    /// <summary>
    /// Rig validation: every clip at every moment keeps a complete, recognisable body:
    /// no collapsed scale (disappearing parts), no limb detached far from its joint, body opaque.
    /// </summary>
    [Fact]
    public void EveryClip_KeepsBodyIntact()
    {
        var ctx = new AnimContext();
        foreach (AnimClip c in Enum.GetValues<AnimClip>())
        {
            var info = AnimationCatalog.Get(c);
            var dur = info.Loop ? 4 : info.Duration;
            for (var t = 0.0; t <= dur; t += 1 / 60.0)
            {
                ctx.Time = t;
                ctx.WalkPhase = t * 1.1;
                ctx.AirVy = Math.Sin(t);
                ctx.SwingAngle = 30 * Math.Sin(t * 3);
                ctx.SwingSpeed = 200 * Math.Cos(t * 3);
                var p = ProceduralAnimator.Evaluate(c, t, ctx).Pose;
                foreach (var v in p.ToArray()) Assert.True(double.IsFinite(v), $"{c} not finite");
                Assert.InRange(p.BodySx, 0.35, 1.3);
                Assert.InRange(p.BodySy, 0.7, 1.3);
                Assert.InRange(p.LegLSy, 0.8, 1.1);
                Assert.InRange(p.LegRSy, 0.8, 1.1);
                Assert.InRange(Math.Abs(p.LegLDy), 0, 20);
                Assert.InRange(Math.Abs(p.ArmLDy), 0, 40);
                Assert.InRange(Math.Abs(p.HeadDx) + Math.Abs(p.HeadDy), 0, 20);
                Assert.InRange(Math.Abs(p.HeadRot), 0, 20);
                Assert.Equal(1, p.Opacity);
                Assert.Equal(1, p.Scale);
            }
        }
    }

    [Fact]
    public void WalkStride_MatchesLegSwing_SoFeetDoNotSlide()
    {
        // Over half a cycle the stance foot sweeps from +A to -A: 2 * L * sin(A) per step, two steps per cycle.
        var expected = 2 * 2 * ProceduralAnimator.LegLength * Math.Sin(ProceduralAnimator.WalkAmplitude * Math.PI / 180);
        Assert.Equal(expected, ProceduralAnimator.WalkStride, 6);
    }

    [Fact]
    public void Controller_RespectsPriority_AndCrossFades()
    {
        var a = new AnimationController(new Random(1));
        Assert.True(a.Play(AnimClip.LandHard));
        a.Update(0.1, new AnimContext());
        Assert.False(a.Play(AnimClip.IdleBreathing));  // non-interruptible, higher priority
        Assert.True(a.Play(AnimClip.Grabbed, force: true));
        var first = a.Update(0.016, new AnimContext());
        var later = a.Update(0.5, new AnimContext());
        Assert.NotEqual(first.ArmLRot, later.ArmLRot);
    }

    [Fact]
    public void RigJson_IsComplete()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rig.json")));
        var groups = doc.RootElement.GetProperty("groups");
        var names = groups.EnumerateObject().Select(g => g.Name).ToHashSet();
        foreach (var required in new[] { "body", "legL", "legR", "torso", "armL", "armR", "head", "face", "eyes", "strings", "shadow", "item" })
            Assert.Contains(required, names);
        foreach (var g in groups.EnumerateObject())
        {
            var parent = g.Value.GetProperty("parent");
            if (parent.ValueKind == JsonValueKind.String) Assert.Contains(parent.GetString()!, names);
        }
        var parts = doc.RootElement.GetProperty("parts").EnumerateArray().ToList();
        Assert.True(parts.Count >= 25);
        foreach (var p in parts) Assert.Contains(p.GetProperty("group").GetString()!, names);
        // Two legs, two shoes, two arms, two eyes: correct limb count.
        Assert.Equal(2, parts.Count(p => p.GetProperty("name").GetString()!.EndsWith(".shoe")));
        Assert.Equal(2, parts.Count(p => p.GetProperty("name").GetString()!.EndsWith(".sleeve")));
        Assert.Equal(2, parts.Count(p => p.GetProperty("name").GetString()!.StartsWith("eyes.")));
    }

    [Fact]
    public void ExportCatalogMarkdown()
    {
        var md = AnimationCatalog.ToMarkdown();
        Assert.Contains("| Walk |", md);
        var target = Environment.GetEnvironmentVariable("HOODIE_EXPORT_CATALOG");
        if (!string.IsNullOrEmpty(target)) File.WriteAllText(target, md);
    }
}
