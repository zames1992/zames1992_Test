using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Geometry;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;
using Xunit;

namespace HoodieCompanion.Tests;

/// <summary>v1.4: Hoodie answers a click in every state it can be seen in.</summary>
public class ClickTests
{
    private sealed class Sim
    {
        public readonly PetController Pet;
        public readonly AppSettings Settings = new() { AutonomousBehavior = false };
        public Vec2 Cursor = new(1000, 900);
        public RenderState Last;

        public Sim(int seed = 3)
        {
            var world = TestWorlds.Single();
            var territory = new TerritoryService(new TerritoryData(), world);
            Pet = new PetController(world, territory, Settings, new Random(seed));
            Pet.Place(new Vec2(900, 1040), appear: false);
        }

        public void Run(double seconds, Action<RenderState>? each = null)
        {
            var steps = Math.Max(1, (int)(seconds * 60));
            for (var i = 0; i < steps; i++)
            {
                Last = Pet.Update(new PetInput { Dt = 1 / 60.0, Cursor = Cursor });
                each?.Invoke(Last);
            }
        }

        /// <summary>Clicks and reports whether anything visibly answered within the next half second.</summary>
        public (ClickResult Result, bool Visible) Click(ClickTarget target = ClickTarget.Body)
        {
            var stateBefore = Pet.State;
            var clipBefore = Pet.Animation.Current;
            var result = Pet.Clicked(target);
            var visible = Pet.Animation.ActiveGesture != Gesture.None || Pet.State != stateBefore || Pet.Animation.Current != clipBefore;
            Run(0.5, _ => visible |= Pet.Animation.ActiveGesture != Gesture.None || Pet.State != stateBefore || Pet.Animation.Current != clipBefore);
            return (result, visible);
        }
    }

    private static void AssertAnswers(Sim sim, ClickResponse expected, bool menu = true, ClickTarget target = ClickTarget.Body)
    {
        var state = sim.Pet.State;
        var (result, visible) = sim.Click(target);
        Assert.Equal(expected, result.Response);
        Assert.True(visible, $"no visible answer to a click in {state}");
        Assert.Equal(menu, result.OpenMenu);
    }

    [Fact]
    public void Idle_Boops()
    {
        var sim = new Sim();
        sim.Run(1);
        Assert.Equal(BehaviorState.Idle, sim.Pet.State);
        AssertAnswers(sim, ClickResponse.Boop);
    }

    [Fact]
    public void Walking_StopsAndBoops()
    {
        var sim = new Sim();
        sim.Run(0.5);
        sim.Pet.TravelTo(new Vec2(200, 1040), run: false, onArrive: null);
        sim.Run(0.5);
        Assert.Equal(BehaviorState.Walking, sim.Pet.State);
        AssertAnswers(sim, ClickResponse.Boop);
        Assert.NotEqual(BehaviorState.Walking, sim.Pet.State);
    }

    [Fact]
    public void Sitting_WavesWithoutGettingUp()
    {
        var sim = new Sim();
        sim.Run(0.5);
        sim.Pet.SetMode(PresenceMode.Quiet);
        sim.Run(3);
        Assert.Equal(BehaviorState.Sitting, sim.Pet.State);
        AssertAnswers(sim, ClickResponse.SeatedWave);
        Assert.Equal(BehaviorState.Sitting, sim.Pet.State);
    }

    [Fact]
    public void Sleeping_WakesUp()
    {
        var sim = new Sim();
        sim.Run(0.5);
        sim.Pet.WorldSleep(true);
        sim.Run(3);
        Assert.Equal(BehaviorState.Sleeping, sim.Pet.State);
        AssertAnswers(sim, ClickResponse.Wake);
    }

    [Fact]
    public void Writing_LooksUp_AndTheNotebookItselfIsClickable()
    {
        var sim = new Sim();
        sim.Run(0.5);
        sim.Pet.SetPanelActivity(PanelActivity.Notes);
        sim.Run(3);
        Assert.Equal(BehaviorState.Activity, sim.Pet.State);
        AssertAnswers(sim, ClickResponse.Glance);
        sim.Run(1.5);
        AssertAnswers(sim, ClickResponse.PropUse, target: ClickTarget.Prop);
        Assert.Equal(BehaviorState.Activity, sim.Pet.State);
    }

    [Fact]
    public void Laptop_LooksUpFromIt()
    {
        var sim = new Sim();
        sim.Run(0.5);
        sim.Pet.SetPanelActivity(PanelActivity.Laptop);
        sim.Run(3);
        Assert.Equal(BehaviorState.Activity, sim.Pet.State);
        AssertAnswers(sim, ClickResponse.Glance);
    }

    [Fact]
    public void Ball_IsKicked_WithoutOpeningTheMenu()
    {
        var sim = new Sim();
        sim.Pet.Memory.GiveItem("ball");
        sim.Run(0.5);
        Assert.True(sim.Pet.DebugIntent(Activity.PlayBall));
        sim.Run(2);
        Assert.Equal(WorldItem.Ball, sim.Pet.HeldItem);
        AssertAnswers(sim, ClickResponse.KickBall, menu: false, target: ClickTarget.Ball);
    }

    [Fact]
    public void KickBall_MovesTheBall()
    {
        var plain = Pose.Neutral;
        var kicked = Pose.Neutral;
        AnimationController.ApplyGesture(ref kicked, Gesture.KickBall, 0.35, reducedMotion: false);
        Assert.True(kicked.BallDx < plain.BallDx - 50, $"ball barely moved: {kicked.BallDx}");
    }

    [Fact]
    public void Climbing_HoldsOnAndWaves()
    {
        var sim = new Sim();
        sim.Pet.Place(new Vec2(300, 1040), appear: false);
        sim.Run(0.3);
        Assert.True(sim.Pet.DebugClimbWall());
        var climbing = false;
        for (var i = 0; i < 600 && !climbing; i++)
        {
            sim.Run(1 / 60.0);
            climbing = sim.Pet.State == BehaviorState.Climbing;
        }
        Assert.True(climbing);
        AssertAnswers(sim, ClickResponse.HoldWave);
    }

    [Fact]
    public void Landing_SaysImOkay()
    {
        var sim = new Sim();
        sim.Run(0.5);
        var head = sim.Pet.HeadWorld;
        sim.Cursor = head;
        Assert.True(sim.Pet.BeginGrab(head));
        for (var i = 0; i < 20; i++) { sim.Cursor += new Vec2(0, -25); sim.Run(1 / 60.0); }
        sim.Pet.EndGrab(sim.Cursor);
        var landing = false;
        for (var i = 0; i < 600 && !landing; i++)
        {
            sim.Run(1 / 60.0);
            landing = sim.Pet.State is BehaviorState.Landing or BehaviorState.Recovering;
        }
        Assert.True(landing);
        AssertAnswers(sim, ClickResponse.ImOkay);
    }

    [Fact]
    public void Alert_IsAcknowledged_AndNoMenuOpens()
    {
        var sim = new Sim();
        sim.Run(0.5);
        sim.Pet.StartAlert(AlertKind.Timer);
        sim.Run(1);
        Assert.Equal(BehaviorState.Alert, sim.Pet.State);
        AssertAnswers(sim, ClickResponse.AcknowledgeAlert, menu: false);
    }

    [Fact]
    public void Leaving_LooksBackAndWaves_ButKeepsLeaving()
    {
        var sim = new Sim();
        sim.Run(0.5);
        sim.Pet.Execute(PetCommand.LeaveMeAlone);
        var leaving = false;
        for (var i = 0; i < 300 && !leaving; i++)
        {
            sim.Run(1 / 60.0);
            leaving = sim.Pet.State == BehaviorState.Leaving;
        }
        Assert.True(leaving);
        AssertAnswers(sim, ClickResponse.LookBackWave);
        sim.Run(10);
        Assert.Equal(BehaviorState.Hidden, sim.Pet.State);
    }

    [Fact]
    public void InTheAir_AClickNeverOpensTheMenu()
    {
        var sim = new Sim();
        sim.Run(0.5);
        var head = sim.Pet.HeadWorld;
        Assert.True(sim.Pet.BeginGrab(head));
        sim.Run(0.1);
        sim.Pet.EndGrab(head + new Vec2(0, -40));
        Assert.Equal(BehaviorState.Airborne, sim.Pet.State);
        var r = sim.Pet.Clicked();
        Assert.False(r.OpenMenu);
    }

    [Fact]
    public void EveryVisibleState_HasAClickAnswer()
    {
        var sim = new Sim();
        var silent = new[] { BehaviorState.Hidden, BehaviorState.Vanishing, BehaviorState.Grabbed, BehaviorState.Airborne, BehaviorState.Jumping };
        foreach (var s in Enum.GetValues<BehaviorState>().Except(silent))
            Assert.True(sim.Pet.ResponseFor(s, ClickTarget.Body) != ClickResponse.None, $"no click answer for {s}");
    }

    [Fact]
    public void Press_SquashesOnTheVeryNextFrame()
    {
        var sim = new Sim();
        sim.Run(1);
        var before = sim.Last.Pose.BodySy;
        sim.Pet.Pressed();
        sim.Run(1 / 60.0);
        Assert.True(sim.Last.Pose.BodySy < before - 0.02, $"no squash: {before} -> {sim.Last.Pose.BodySy}");
        sim.Run(0.5);
        Assert.False(sim.Pet.Animation.PressActive);
    }

    [Fact]
    public void TwentyClicksInARow_GetTwentyAnswers()
    {
        var sim = new Sim(seed: 11);
        sim.Settings.AutonomousBehavior = true;
        var answered = 0;
        for (var i = 0; i < 20; i++)
        {
            sim.Run(2.3);
            if (!sim.Pet.Machine.IsPhysical && sim.Pet.State is not (BehaviorState.Hidden or BehaviorState.Vanishing))
            {
                var (result, visible) = sim.Click();
                if (result.Response != ClickResponse.None && visible) answered++;
                else Assert.Fail($"click {i} in {sim.Pet.State} got no answer");
            }
            else answered++;
        }
        Assert.Equal(20, answered);
    }
}
