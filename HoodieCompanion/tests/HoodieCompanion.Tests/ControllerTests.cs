using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Companion.Physics;
using HoodieCompanion.Geometry;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;
using Xunit;

namespace HoodieCompanion.Tests;

public class ControllerTests
{
    private sealed class Sim
    {
        public readonly PetController Pet;
        public readonly TerritoryService Territory;
        public readonly AppSettings Settings = new();
        public Vec2 Cursor = new(200, 200);
        public bool Button;
        public string? Fullscreen;
        public RenderState Last;

        public Sim(WorldGeometry world, int seed = 1, TerritoryData? data = null)
        {
            Territory = new TerritoryService(data ?? new TerritoryData(), world);
            Pet = new PetController(world, Territory, Settings, new Random(seed));
            Pet.Place(Pet.DefaultHome(), appear: false);
        }

        public void Run(double seconds, Action<RenderState>? each = null)
        {
            var steps = (int)(seconds * 60);
            for (var i = 0; i < steps; i++)
            {
                Last = Pet.Update(new PetInput { Dt = 1 / 60.0, Cursor = Cursor, LeftButtonDown = Button, FullscreenMonitorId = Fullscreen });
                foreach (var v in Last.Pose.ToArray()) Assert.True(double.IsFinite(v), "pose value not finite");
                Assert.True(double.IsFinite(Pet.Feet.X) && double.IsFinite(Pet.Feet.Y));
                each?.Invoke(Last);
            }
        }
    }

    [Fact]
    public void LongAutonomousLife_StaysInsideWorld_AndNeverFreezes()
    {
        var world = TestWorlds.SideBySide();
        var sim = new Sim(world, seed: 7);
        var states = new HashSet<BehaviorState>();
        var rnd = new Random(3);
        for (var minute = 0; minute < 20; minute++)
        {
            sim.Cursor = new Vec2(rnd.Next(0, 4400), rnd.Next(0, 1400));
            sim.Run(60, r =>
            {
                states.Add(r.State);
                if (r.State is BehaviorState.Idle or BehaviorState.Walking or BehaviorState.Sitting or BehaviorState.Sleeping)
                {
                    var mon = world.MonitorAt(sim.Pet.Feet);
                    Assert.NotNull(mon);
                    Assert.InRange(sim.Pet.Feet.Y, mon!.WorkArea.Bottom - 0.6, mon.WorkArea.Bottom + 0.6);
                }
            });
        }
        Assert.Contains(BehaviorState.Walking, states);
        Assert.True(states.Count >= 4, string.Join(",", states));
    }

    [Fact]
    public void NoGoMonitor_IsNeverEnteredAutonomously()
    {
        var world = TestWorlds.SideBySide();
        var data = new TerritoryData();
        data.MonitorRules["B"] = RegionType.NoGo;
        var sim = new Sim(world, seed: 11, data);
        sim.Settings.CurrentPresenceMode = PresenceMode.Play;
        sim.Pet.SetMode(PresenceMode.Play);
        for (var i = 0; i < 15; i++)
        {
            sim.Cursor = new Vec2(i % 2 == 0 ? 3000 : 1800, 1300);
            sim.Run(40, _ => Assert.True(sim.Pet.Feet.X < 1920 || sim.Pet.State is BehaviorState.Vanishing, $"entered NO_GO at {sim.Pet.Feet} state {sim.Pet.State} log: {string.Join(" | ", sim.Pet.Machine.RecentTransitions.TakeLast(8))}"));
        }
    }

    [Fact]
    public void NoGoRegion_BlocksWalking_AndThrownInsideWalksOut()
    {
        var world = TestWorlds.Single();
        var data = new TerritoryData();
        var sim = new Sim(world, seed: 5, data);
        var mon = world.Monitors[0];
        data.Regions.Add(sim.Territory.MakeRegion(RegionType.NoGo, mon, RectD.FromEdges(800, 0, 1100, 1040)));
        // Drop Hoodie right into the forbidden area (a user action is allowed to do that).
        sim.Pet.Place(new Vec2(950, 1040), appear: false);
        sim.Run(20);
        Assert.True(sim.Pet.Feet.X < 800 || sim.Pet.Feet.X > 1100, $"still inside NO_GO at {sim.Pet.Feet}");
        // And it never wanders back in.
        sim.Run(300, _ => Assert.False(sim.Pet.Feet.X > 820 && sim.Pet.Feet.X < 1080, $"re-entered at {sim.Pet.Feet}"));
    }

    [Fact]
    public void GrabDragThrow_AcrossMonitors_LandsAndRecovers()
    {
        var world = TestWorlds.SideBySide();
        var sim = new Sim(world, seed: 2);
        sim.Pet.Place(new Vec2(1500, 1040), appear: false);
        sim.Run(1);
        var head = sim.Pet.HeadWorld;
        sim.Cursor = head;
        Assert.True(sim.Pet.BeginGrab(head));
        sim.Run(0.3);
        Assert.Equal(BehaviorState.Grabbed, sim.Pet.State);
        // Swing fast to the right, towards monitor B.
        for (var i = 0; i < 12; i++)
        {
            sim.Cursor += new Vec2(45, -12);
            sim.Run(1 / 60.0);
        }
        sim.Pet.EndGrab(sim.Cursor);
        Assert.Equal(BehaviorState.Airborne, sim.Pet.State);
        var sawLanding = false;
        sim.Run(8, r => sawLanding |= r.State is BehaviorState.Landing or BehaviorState.Recovering);
        Assert.True(sawLanding);
        Assert.True(sim.Pet.Feet.X > 1920, $"expected to land on monitor B, feet {sim.Pet.Feet}");
        Assert.Equal(1440, sim.Pet.Feet.Y, 1);
    }

    [Fact]
    public void GrabbedPose_RotatesAroundHood_AndReleasesWithoutJump()
    {
        var world = TestWorlds.Single();
        var sim = new Sim(world, seed: 4);
        sim.Pet.Place(new Vec2(900, 1040), appear: false);
        sim.Run(0.5);
        var hood = sim.Pet.Transform.LocalToWorld(new Vec2(272, 130));
        sim.Cursor = hood;
        sim.Pet.BeginGrab(hood);
        for (var i = 0; i < 30; i++) { sim.Cursor += new Vec2(8, -6); sim.Run(1 / 60.0); }
        var before = sim.Pet.Transform.LocalToWorld(BodyMetrics.CenterLocal);
        sim.Pet.EndGrab(sim.Cursor);
        var after = sim.Pet.Transform.LocalToWorld(BodyMetrics.CenterLocal);
        Assert.True(Vec2.Distance(before, after) < 1.0, $"visual jump on release {before} -> {after}");
    }

    [Fact]
    public void LeaveMeAlone_ExitsAndStaysHidden_ThenComesBack()
    {
        var world = TestWorlds.Single();
        var sim = new Sim(world, seed: 9);
        sim.Pet.Place(new Vec2(960, 1040), appear: false);
        sim.Run(0.5);
        sim.Pet.Execute(PetCommand.LeaveMeAlone);
        sim.Run(8);
        Assert.Equal(BehaviorState.Hidden, sim.Pet.State);
        Assert.False(sim.Last.Visible);
        sim.Run(60);
        Assert.Equal(BehaviorState.Hidden, sim.Pet.State);
        sim.Pet.Execute(PetCommand.ComeBack);
        sim.Run(15);
        Assert.True(sim.Last.Visible);
        Assert.NotEqual(BehaviorState.Hidden, sim.Pet.State);
        Assert.NotNull(world.MonitorAt(sim.Pet.Feet));
    }

    [Fact]
    public void GoHome_ReturnsToHome_OnAnotherMonitor()
    {
        var world = TestWorlds.SideBySide();
        var sim = new Sim(world, seed: 12);
        sim.Pet.Place(new Vec2(3500, 1440), appear: false);
        sim.Run(0.5);
        sim.Territory.SetHome(world.Monitors[0], new Vec2(400, 1040));
        sim.Pet.Execute(PetCommand.GoHome);
        var log = new List<string>();
        sim.Pet.Log += m => log.Add(m);
        sim.Run(90);
        Assert.True(Math.Abs(sim.Pet.Feet.X - 400) < 40, string.Join(" | ", log.TakeLast(25)));
        Assert.Equal(1040, sim.Pet.Feet.Y, 1);
    }

    [Fact]
    public void StackedMonitors_TravelUpUsesLadder_AndDownUsesRope()
    {
        var world = TestWorlds.Stacked();
        var sim = new Sim(world, seed: 21);
        sim.Pet.Place(new Vec2(1500, 1040), appear: false);
        sim.Run(0.5);
        var ladder = false;
        sim.Pet.TravelTo(new Vec2(1800, -1), run: false, onArrive: null);
        sim.Run(40, r => ladder |= r.State == BehaviorState.Climbing && r.Prop is { Kind: WorldPropKind.Ladder });
        Assert.True(ladder, "expected a ladder climb");
        Assert.Equal(0, sim.Pet.Feet.Y, 1);
        Assert.Equal("B", world.MonitorAt(sim.Pet.Feet)!.Id);

        var rope = false;
        sim.Pet.TravelTo(new Vec2(900, 1040), run: false, onArrive: null);
        sim.Run(40, r => rope |= r.State == BehaviorState.Climbing && r.Prop is { Kind: WorldPropKind.Rope });
        Assert.True(rope, "expected a rope descent");
        Assert.Equal(1040, sim.Pet.Feet.Y, 1);
    }

    [Fact]
    public void HigherSideMonitor_IsReachedWithLadder_AndBackWithRope()
    {
        var world = TestWorlds.OffsetHigher();
        var sim = new Sim(world, seed: 23);
        sim.Pet.Place(new Vec2(1500, 1040), appear: false);
        sim.Run(0.5);
        var ladder = false;
        var poof = false;
        sim.Pet.TravelTo(new Vec2(2600, 780), run: false, onArrive: null);
        sim.Run(40, r => { ladder |= r.Prop is { Kind: WorldPropKind.Ladder }; poof |= r.State == BehaviorState.Vanishing; });
        Assert.True(ladder, "expected a ladder");
        Assert.False(poof, "should not teleport");
        Assert.Equal("B", world.MonitorAt(sim.Pet.Feet)!.Id);
        Assert.Equal(780, sim.Pet.Feet.Y, 1);

        var rope = false;
        sim.Pet.TravelTo(new Vec2(800, 1040), run: false, onArrive: null);
        sim.Run(40, r => rope |= r.Prop is { Kind: WorldPropKind.Rope });
        Assert.True(rope, "expected a rope");
        Assert.Equal("A", world.MonitorAt(sim.Pet.Feet)!.Id);
    }

    [Fact]
    public void PanelActivities_UseAccessories_AndPutThemAway()
    {
        var world = TestWorlds.Single();
        var sim = new Sim(world, seed: 22);
        sim.Settings.AutonomousBehavior = false;
        sim.Run(1);
        sim.Pet.SetPanelActivity(PanelActivity.Laptop);
        sim.Run(3);
        Assert.Equal(BehaviorState.Activity, sim.Pet.State);
        Assert.Equal(AnimClip.LaptopType, sim.Last.Clip);
        Assert.True(sim.Last.Pose.PropLaptop > 0.9);
        sim.Pet.SetPanelActivity(PanelActivity.Backpack);
        sim.Run(4);
        Assert.Equal(AnimClip.SearchBackpack, sim.Last.Clip);
        Assert.True(sim.Last.Pose.PropBackpack > 0.9 && sim.Last.Pose.PropLaptop < 0.01);
        sim.Pet.ItemPresented();
        sim.Run(0.3);
        Assert.Equal(AnimClip.PresentItem, sim.Last.Clip);
        sim.Run(2);
        Assert.Equal(AnimClip.SearchBackpack, sim.Last.Clip);
        sim.Pet.SetPanelActivity(PanelActivity.None);
        sim.Run(2);
        Assert.NotEqual(BehaviorState.Activity, sim.Pet.State);
        Assert.True(sim.Last.Pose.PropBackpack < 0.01);
    }

    [Fact]
    public void Focus_GoesToHome_AndSitsQuietly()
    {
        var world = TestWorlds.Single();
        var sim = new Sim(world, seed: 13);
        sim.Pet.Place(new Vec2(1500, 1040), appear: false);
        sim.Territory.SetHome(world.Monitors[0], new Vec2(200, 1040));
        sim.Pet.SetMode(PresenceMode.Focus);
        sim.Run(60);
        Assert.InRange(sim.Pet.Feet.X, 60, 340);
        Assert.Contains(sim.Pet.State, new[] { BehaviorState.Sitting, BehaviorState.Sleeping });
    }

    [Fact]
    public void StayHere_KeepsHoodieInsideAnchorRadius()
    {
        var world = TestWorlds.Single();
        var sim = new Sim(world, seed: 14);
        sim.Pet.Place(new Vec2(960, 1040), appear: false);
        sim.Run(0.5);
        sim.Pet.Execute(PetCommand.StayHere);
        sim.Run(300, _ => Assert.InRange(sim.Pet.Feet.X, 960 - 225, 960 + 225));
        sim.Pet.Execute(PetCommand.YoureFree);
        Assert.Null(sim.Territory.Anchor);
    }

    [Fact]
    public void FullscreenOnOnlyMonitor_HidesAndReturns()
    {
        var world = TestWorlds.Single();
        var sim = new Sim(world, seed: 15);
        sim.Fullscreen = "A";
        sim.Run(2);
        Assert.Equal(BehaviorState.Hidden, sim.Pet.State);
        sim.Fullscreen = null;
        sim.Run(2);
        Assert.True(sim.Last.Visible);
    }

    [Fact]
    public void FullscreenWithSecondMonitor_MovesThere()
    {
        var world = TestWorlds.SideBySide();
        var sim = new Sim(world, seed: 16);
        sim.Pet.Place(new Vec2(500, 1040), appear: false);
        sim.Fullscreen = "A";
        sim.Run(3);
        Assert.Equal("B", world.MonitorAt(sim.Pet.Feet)!.Id);
        Assert.True(sim.Last.Visible);
    }

    [Fact]
    public void ReceivingItem_PlaysCatchInspectStore_WithinTwoSeconds()
    {
        var world = TestWorlds.Single();
        var sim = new Sim(world, seed: 17);
        sim.Run(1);
        sim.Pet.DragEntered();
        sim.Run(0.2);
        sim.Pet.ItemReceived(alreadyHad: false);
        var clips = new List<AnimClip>();
        var elapsed = 0.0;
        sim.Run(2.5, r => { if (clips.Count == 0 || clips[^1] != r.Clip) clips.Add(r.Clip); if (r.State == BehaviorState.ReceivingItem) elapsed += 1 / 60.0; });
        Assert.Contains(AnimClip.CatchItem, clips);
        Assert.Contains(AnimClip.InspectItem, clips);
        Assert.Contains(AnimClip.PutInBackpack, clips);
        Assert.InRange(elapsed, 0.8, 1.8);
    }

    [Fact]
    public void RepeatedClicksNextToHoodie_MakeItStepAside()
    {
        var world = TestWorlds.Single();
        var sim = new Sim(world, seed: 18);
        sim.Settings.AutonomousBehavior = false;
        sim.Pet.Place(new Vec2(960, 1040), appear: false);
        sim.Run(1);
        var box = sim.Pet.Transform.Bounds(RigTransform.BodyLocal);
        sim.Cursor = new Vec2(box.Right + 20, box.Center.Y);
        var start = sim.Pet.Feet.X;
        for (var i = 0; i < 2; i++)
        {
            sim.Button = true; sim.Run(0.1);
            sim.Button = false; sim.Run(0.3);
        }
        sim.Run(5);
        Assert.True(Math.Abs(sim.Pet.Feet.X - start) > 40, $"did not move aside ({start} -> {sim.Pet.Feet.X})");
    }

    [Fact]
    public void RigTransform_RoundTrips_WithMirrorAndTilt()
    {
        var t = new RigTransform(new Vec2(1000, 800), new Vec2(272, 130), 33, 1, 0.3);
        var p = new Vec2(180, 600);
        var back = t.WorldToLocal(t.LocalToWorld(p));
        Assert.InRange(Vec2.Distance(p, back), 0, 1e-6);
    }
}
