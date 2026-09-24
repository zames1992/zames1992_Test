using HoodieCompanion.Companion.Physics;
using HoodieCompanion.Geometry;
using Xunit;

namespace HoodieCompanion.Tests;

public class GeometryAndPhysicsTests
{
    [Fact]
    public void FloorIsWorkingAreaBottom_NotBehindTaskbar()
    {
        var w = TestWorlds.SideBySide();
        Assert.Equal(1040, w.FloorBelow(500, 100));
        Assert.Equal(1440, w.FloorBelow(3000, 100));
        Assert.Null(w.FloorBelow(-10, 0));
    }

    [Fact]
    public void StackedMonitors_FloorBelowFindsUpperMonitorFirst()
    {
        var w = TestWorlds.Stacked();
        // A point inside the upper monitor lands on the upper monitor floor (y = 0).
        Assert.Equal(0, w.FloorBelow(1000, -500));
        // Below that, the lower monitor floor.
        Assert.Equal(1040, w.FloorBelow(1000, 10));
        Assert.Equal(w.Monitors[1], w.MonitorAbove(w.Monitors[0]));
    }

    [Fact]
    public void NegativeCoordinates_AreFirstClass()
    {
        var w = TestWorlds.NegativeLeft();
        Assert.Equal("B", w.MonitorAt(new Vec2(-100, 900))!.Id);
        Assert.Equal(1224, w.FloorBelow(-100, 300));
        Assert.Equal(w.Monitors[1], w.MonitorBeside(w.Monitors[0], -1));
    }

    [Fact]
    public void ThrownBody_ContinuesAcrossMonitorEdge_AndLandsOnOtherMonitor()
    {
        var w = TestWorlds.SideBySide();
        var phys = new PetPhysics();
        var m = BodyMetrics.For(150, 1.0);
        phys.Launch(new Vec2(1500, 600), new Vec2(3200, -900));
        LandingInfo? landing = null;
        for (var i = 0; i < 600 && landing is null; i++)
        {
            landing = phys.Step(1 / 60.0, w, BodyMetrics.For(150, w.NearestMonitor(phys.Center).Scale));
            Assert.True(double.IsFinite(phys.Center.X) && double.IsFinite(phys.Center.Y));
        }
        Assert.NotNull(landing);
        var feetY = phys.Center.Y + BodyMetrics.For(150, 1.5).FeetOffsetPx;
        Assert.True(phys.Center.X > 1920, $"landed at {phys.Center}");
        Assert.InRange(feetY, 1439, 1441);
    }

    [Fact]
    public void ThrownBody_BouncesOffOuterWall()
    {
        var w = TestWorlds.Single();
        var phys = new PetPhysics();
        var m = BodyMetrics.For(150, 1.0);
        phys.Launch(new Vec2(1700, 500), new Vec2(4000, 0));
        for (var i = 0; i < 600; i++)
        {
            if (phys.Step(1 / 60.0, w, m) is not null) break;
        }
        Assert.True(phys.WallHits > 0);
        Assert.InRange(phys.Center.X, 0, 1920);
    }

    [Fact]
    public void FastFall_NeverTunnelsThroughFloor()
    {
        var w = TestWorlds.Single();
        var phys = new PetPhysics();
        var m = BodyMetrics.For(150, 1.0);
        phys.Launch(new Vec2(900, 100), new Vec2(0, 5000));
        LandingInfo? l = null;
        for (var i = 0; i < 100 && l is null; i++) l = phys.Step(0.05, w, m);
        Assert.NotNull(l);
        Assert.InRange(phys.Center.Y + m.FeetOffsetPx, 1039.5, 1040.5);
    }

    [Fact]
    public void JumpVelocity_ReachesUpperMonitor()
    {
        var w = TestWorlds.Stacked();
        var m = BodyMetrics.For(150, 1.0);
        var from = new Vec2(1000, 1040 - m.FeetOffsetPx);
        var to = new Vec2(1100, 0 - m.FeetOffsetPx);
        var phys = new PetPhysics();
        phys.Launch(from, PetPhysics.JumpVelocity(from, to, m, 80));
        LandingInfo? l = null;
        for (var i = 0; i < 600 && l is null; i++) l = phys.Step(1 / 60.0, w, m);
        Assert.NotNull(l);
        Assert.InRange(phys.Center.Y + m.FeetOffsetPx, -0.5, 0.5);
    }

    [Fact]
    public void ThrowEstimator_WeightsRecentMotion_AndIgnoresStoppedPointer()
    {
        var t = new ThrowController();
        for (var i = 0; i <= 10; i++) t.AddSample(i * 0.01, new Vec2(i * 20, 0)); // 2000 px/s
        var v = t.Estimate(0.1);
        Assert.InRange(v.X, 1800, 2200);

        var s = new ThrowController();
        for (var i = 0; i <= 10; i++) s.AddSample(i * 0.01, new Vec2(i * 20, 0));
        for (var i = 11; i <= 30; i++) s.AddSample(i * 0.01, new Vec2(200, 0)); // held still for 200 ms
        Assert.True(s.Estimate(0.30).Length < 50);
    }

    [Fact]
    public void GrabPendulum_SwingsBehindMotion_AndSettles()
    {
        var g = new GrabController();
        var m = BodyMetrics.For(150, 1.0);
        g.Begin(new Vec2(500, 500), new Vec2(272, 120), new Vec2(500, 500));
        // Yank to the right: body should lag (positive angle = body swings left of pivot).
        for (var i = 0; i < 10; i++) g.Update(1 / 60.0, new Vec2(500 + i * 30, 500), m, false);
        Assert.True(g.Angle > 5, $"angle {g.Angle}");
        for (var i = 0; i < 600; i++) g.Update(1 / 60.0, new Vec2(800, 500), m, false);
        Assert.InRange(g.Angle, -1, 1);
        Assert.InRange(g.Pivot.X, 799, 801);
    }
}
