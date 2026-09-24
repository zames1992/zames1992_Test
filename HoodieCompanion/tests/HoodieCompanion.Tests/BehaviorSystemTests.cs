using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Companion.Physics;
using HoodieCompanion.Features.Backpack;
using HoodieCompanion.Features.Notes;
using HoodieCompanion.Geometry;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;
using HoodieCompanion.Storage;
using Xunit;

namespace HoodieCompanion.Tests;

/// <summary>v1.2: mind, reactions, idle director, grab regions, landing, ledge, lying sleep, AFK timeline.</summary>
public sealed class BehaviorSystemTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hoodie-tests-" + Guid.NewGuid().ToString("N"));

    public BehaviorSystemTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class Sim
    {
        public readonly PetController Pet;
        public readonly AppSettings Settings = new();
        public Vec2 Cursor = new(200, 200);
        public double Idle;
        public RenderState Last;

        public Sim(WorldGeometry world, int seed = 1)
        {
            Pet = new PetController(world, new TerritoryService(new TerritoryData(), world), Settings, new Random(seed));
            Pet.Place(Pet.DefaultHome(), appear: false);
        }

        public void Run(double seconds, Action<RenderState>? each = null, double idleRate = 0)
        {
            var steps = (int)Math.Round(seconds * 60);
            for (var i = 0; i < steps; i++)
            {
                Idle += idleRate / 60.0;
                Last = Pet.Update(new PetInput { Dt = 1 / 60.0, Cursor = Cursor, UserIdleSeconds = Idle });
                foreach (var v in Last.Pose.ToArray()) Assert.True(double.IsFinite(v), "pose value not finite");
                each?.Invoke(Last);
            }
        }
    }

    // ------------------------------------------------------------------ reactions

    [Fact]
    public void Reactions_RespectCooldowns_CalmModes_AndPriorities()
    {
        var mind = new Mind(new CharacterDrives());
        var rs = new ReactionSystem(new Random(1));
        Assert.NotNull(rs.Resolve(PetEvent.CursorRushed, mind, PresenceMode.Normal, 0));
        Assert.Null(rs.Resolve(PetEvent.CursorRushed, mind, PresenceMode.Normal, 1));      // cooldown
        Assert.NotNull(rs.Resolve(PetEvent.CursorRushed, mind, PresenceMode.Normal, 100));
        Assert.Null(rs.Resolve(PetEvent.CursorApproached, mind, PresenceMode.Focus, 0));   // calm mode
        Assert.NotNull(rs.Resolve(PetEvent.ReminderDue, mind, PresenceMode.Focus, 0));     // alerts always
        // Interruption rule: physical beats everything, contextual never interrupts an alert.
        Assert.True(ReactionSystem.MayInterrupt(ReactionPriority.Physical, ReactionPriority.Alert));
        Assert.False(ReactionSystem.MayInterrupt(ReactionPriority.Contextual, ReactionPriority.Alert));
        Assert.False(ReactionSystem.MayInterrupt(ReactionPriority.Contextual, ReactionPriority.Contextual));
        // Every event has a rule and every rule has at least one option.
        foreach (PetEvent e in Enum.GetValues<PetEvent>())
            Assert.NotEmpty(ReactionSystem.RuleFor(e).Options);
    }

    [Fact]
    public void Mind_AfkTimeline_Thresholds()
    {
        Assert.Equal(AfkPhase.Present, Mind.PhaseFor(4 * 60));
        Assert.Equal(AfkPhase.Relaxed, Mind.PhaseFor(5 * 60));
        Assert.Equal(AfkPhase.Bored, Mind.PhaseFor(15 * 60));
        Assert.Equal(AfkPhase.Exploring, Mind.PhaseFor(30 * 60));
        Assert.Equal(AfkPhase.Sleepy, Mind.PhaseFor(60 * 60));
        Assert.Equal(AfkPhase.Asleep, Mind.PhaseFor(90 * 60));
    }

    [Fact]
    public void IdleDirector_VariesClips_ByTier_WithoutRepeats()
    {
        var d = new IdleDirector(new Random(5));
        var mind = new Mind(new CharacterDrives());
        var seen = new List<AnimClip>();
        for (var t = 0.0; t < 900; t += 0.5)
            if (d.Tick(t, IdlePosture.Standing, mind, PresenceMode.Normal, false, cursorNear: true) is AnimClip c) seen.Add(c);
        Assert.True(seen.Count > 40, $"only {seen.Count} idle moments in 15 min");
        Assert.True(seen.Distinct().Count() >= 8, string.Join(",", seen.Distinct()));
        for (var i = 1; i < seen.Count; i++) Assert.NotEqual(seen[i - 1], seen[i]);
        // Contextual clips never come from the idle director.
        Assert.DoesNotContain(AnimClip.ReminderAlert, seen);
        Assert.DoesNotContain(AnimClip.CatchPackage, seen);
        // Lying: nothing that needs standing.
        for (var t = 1000.0; t < 1300; t += 0.5)
        {
            var c = d.Tick(t, IdlePosture.Lying, mind, PresenceMode.Normal, false, false);
            Assert.True(c is null or AnimClip.LookUp, c.ToString());
        }
    }

    [Fact]
    public void EveryLibraryClip_HasCatalogEntry_StateAndRarity()
    {
        var clips = Enum.GetValues<AnimClip>();
        Assert.True(clips.Length >= 110, $"{clips.Length} clips");
        foreach (var c in clips)
        {
            var info = AnimationCatalog.Get(c);
            Assert.False(string.IsNullOrWhiteSpace(info.Category), c.ToString());
            Assert.Contains(info.Rarity, new[] { "Frequent", "Occasional", "Rare", "Contextual" });
        }
    }

    // ------------------------------------------------------------------ grab anywhere

    [Fact]
    public void GrabRegions_AndRestAngles()
    {
        Assert.Equal(PetController.GrabRegion.Hood, PetController.RegionAt(new Vec2(272, 150)));
        Assert.Equal(PetController.GrabRegion.HandLeft, PetController.RegionAt(new Vec2(140, 590)));
        Assert.Equal(PetController.GrabRegion.HandRight, PetController.RegionAt(new Vec2(412, 600)));
        Assert.Equal(PetController.GrabRegion.Foot, PetController.RegionAt(new Vec2(200, 800)));
        Assert.Equal(PetController.GrabRegion.Torso, PetController.RegionAt(new Vec2(272, 500)));
        // Held by a foot Hoodie hangs upside down; by a hand, tilted towards the free side.
        var foot = GrabController.RestAngleFor(new Vec2(186, 800), -1);
        Assert.InRange(Math.Abs(foot), 160, 180);
        var hand = GrabController.RestAngleFor(ProceduralAnimator.HangAnchor(AnimClip.HangHandL), -1);
        Assert.InRange(Math.Abs(hand), 5, 40);
        Assert.Equal(-hand, GrabController.RestAngleFor(ProceduralAnimator.HangAnchor(AnimClip.HangHandL), 1), 6);
    }

    [Theory]
    [InlineData(140, 596, AnimClip.HangHandL)]
    [InlineData(412, 610, AnimClip.HangHandR)]
    [InlineData(200, 790, AnimClip.HangFoot)]
    [InlineData(272, 520, AnimClip.HangTorso)]
    public void GrabbingABodyPart_HangsFromIt_AndSettlesBelowTheCursor(double lx, double ly, AnimClip expected)
    {
        var world = TestWorlds.Single();
        var sim = new Sim(world, seed: 3);
        sim.Pet.Place(new Vec2(900, 1040), appear: false);
        sim.Run(0.5);
        var at = sim.Pet.Transform.LocalToWorld(new Vec2(lx, ly));
        sim.Cursor = at;
        Assert.True(sim.Pet.BeginGrab(at));
        sim.Cursor = at - new Vec2(0, 300);
        sim.Run(4);
        Assert.Equal(BehaviorState.Grabbed, sim.Pet.State);
        Assert.Contains(sim.Last.Clip, new[] { expected, AnimClip.Struggle, AnimClip.RelaxedCarry });
        // The body's centre hangs (almost) straight below the held point.
        var t = sim.Pet.Transform;
        var com = t.LocalToWorld(BodyMetrics.CenterLocal);
        var pivot = t.AnchorWorld;
        Assert.True(com.Y > pivot.Y - 1, $"centre {com} should hang below {pivot}");
        Assert.True(Math.Abs(com.X - pivot.X) < Math.Abs(com.Y - pivot.Y) * 0.35 + 3, $"not hanging straight: {com} vs {pivot}");
        sim.Pet.EndGrab(sim.Cursor);
        sim.Run(6);
        Assert.False(sim.Pet.Machine.IsPhysical && sim.Pet.State != BehaviorState.Climbing, sim.Pet.State.ToString());
        Assert.Equal(1040, sim.Pet.Feet.Y, 1);
    }

    [Fact]
    public void Landing_KeepsTheFeetWhereTheyWereDrawn()
    {
        var world = TestWorlds.Single();
        var sim = new Sim(world, seed: 9);
        sim.Pet.Place(new Vec2(900, 1040), appear: false);
        sim.Run(0.5);
        var hood = sim.Pet.Transform.LocalToWorld(new Vec2(230, 140));
        sim.Cursor = hood;
        sim.Pet.BeginGrab(hood);
        for (var i = 0; i < 20; i++) { sim.Cursor += new Vec2(14, -10); sim.Run(1 / 60.0); }
        sim.Pet.EndGrab(sim.Cursor);
        Vec2? prevFeet = null;
        var maxJump = 0.0;
        sim.Run(3, r =>
        {
            var feet = r.Transform.LocalToWorld(BodyMetrics.RootLocal);
            if (prevFeet is Vec2 p && r.State is BehaviorState.Landing) maxJump = Math.Max(maxJump, Math.Abs(feet.X - p.X));
            prevFeet = feet;
        });
        Assert.True(maxJump < 25, $"feet jumped sideways by {maxJump:0.0} px on landing");
    }

    [Fact]
    public void DroppedBelowTheTaskbarEdge_GrabsTheEdge_AndClimbsUp()
    {
        var world = TestWorlds.Single(); // work area bottom 1040, screen bottom 1080
        var sim = new Sim(world, seed: 5);
        sim.Pet.Place(new Vec2(900, 1040), appear: false);
        sim.Run(0.5);
        var hood = sim.Pet.HeadWorld;
        sim.Cursor = hood;
        sim.Pet.BeginGrab(hood);
        // Drag Hoodie down onto the taskbar and let go.
        for (var i = 0; i < 40; i++) { sim.Cursor += new Vec2(0, 6); sim.Run(1 / 60.0); }
        sim.Run(0.4);
        sim.Pet.EndGrab(sim.Cursor);
        var sawHang = false;
        var sawRespawn = false;
        sim.Run(6, r =>
        {
            sawHang |= r.Clip == AnimClip.HangEdge;
            sawRespawn |= r.State == BehaviorState.Appearing;
        });
        Assert.True(sawHang, "should hang from the edge");
        Assert.False(sawRespawn, "must not vanish and respawn");
        Assert.Equal(1040, sim.Pet.Feet.Y, 1);
        Assert.InRange(sim.Pet.Feet.X, 700, 1100);
    }

    // ------------------------------------------------------------------ sleep & AFK

    [Fact]
    public void Sleep_IsLyingDown_AndWakesUpSitting()
    {
        var sim = new Sim(TestWorlds.Single(), seed: 2);
        sim.Pet.Place(new Vec2(900, 1040), appear: false);
        sim.Run(0.5);
        sim.Pet.WorldSleep(true);
        sim.Run(3);
        Assert.Equal(BehaviorState.Sleeping, sim.Pet.State);
        Assert.Contains(sim.Last.Clip, new[] { AnimClip.SleepLying, AnimClip.DreamTwitch });
        Assert.InRange(Math.Abs(sim.Last.Pose.RootRot), 60, 100);
        sim.Pet.WorldSleep(false);
        sim.Run(2.5);
        Assert.NotEqual(BehaviorState.Sleeping, sim.Pet.State);
        Assert.True(Math.Abs(sim.Last.Pose.RootRot) < 20, $"still lying: {sim.Last.Pose.RootRot}");
    }

    [Fact]
    public void AfkTimeline_EndsAsleep_AndGreetsTheUserOnReturn()
    {
        var sim = new Sim(TestWorlds.Single(), seed: 4);
        sim.Pet.Mind.AfkScale = 1 / 60.0; // one "minute" per second
        var phases = new HashSet<AfkPhase>();
        sim.Run(110, _ => phases.Add(sim.Pet.Mind.Afk), idleRate: 1);
        Assert.Contains(AfkPhase.Bored, phases);
        Assert.Contains(AfkPhase.Exploring, phases);
        Assert.Equal(AfkPhase.Asleep, sim.Pet.Mind.Afk);
        sim.Run(30, idleRate: 1);
        Assert.Equal(BehaviorState.Sleeping, sim.Pet.State);

        // The user comes back.
        sim.Idle = 0;
        var clips = new List<AnimClip>();
        sim.Run(8, r => { if (clips.Count == 0 || clips[^1] != r.Clip) clips.Add(r.Clip); });
        Assert.Contains(AnimClip.WakeFromLying, clips);
        Assert.Contains(AnimClip.NoticeMovement, clips);
        Assert.True(clips.Contains(AnimClip.Wave) || clips.Contains(AnimClip.Happy) || clips.Contains(AnimClip.Excited), string.Join(",", clips));
    }

    [Fact]
    public void IdleHoodie_ShowsVariedMicroActions()
    {
        var sim = new Sim(TestWorlds.Single(), seed: 8);
        sim.Settings.AutonomousBehavior = false; // only the idle director moves it
        var clips = new HashSet<AnimClip>();
        sim.Run(240, r => clips.Add(r.Clip));
        var micro = clips.Where(c => AnimationCatalog.Get(c).Rarity is "Frequent" or "Occasional" or "Rare").ToList();
        Assert.True(micro.Count >= 5, string.Join(",", clips));
    }

    // ------------------------------------------------------------------ backpack & notes

    [Fact]
    public void Inventory_ShellItems_AndManualOrder()
    {
        var inv = new InventoryService(new AppStorage(Path.Combine(_dir, "inv")), _ => true, _ => false);
        var bin = inv.Add("::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin").Item;
        Assert.Equal(InventoryItemType.ShellItem, bin.Type);
        Assert.True(inv.Exists(bin));
        var calc = inv.Add("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App").Item;
        Assert.Equal(InventoryItemType.ShellItem, calc.Type);
        Assert.Equal("WindowsCalculator", calc.DisplayName);
        var c = inv.Add(Path.Combine(_dir, "c.txt")).Item;
        var order = inv.Ordered().Select(i => i.Id).ToList();
        Assert.Equal(new[] { c.Id, calc.Id, bin.Id }, order);
        inv.Move(bin.Id, 0);
        Assert.Equal(new[] { bin.Id, c.Id, calc.Id }, inv.Ordered().Select(i => i.Id).ToArray());
        // The order persists.
        var again = new InventoryService(new AppStorage(Path.Combine(_dir, "inv")), _ => true, _ => false);
        Assert.Equal(new[] { bin.Id, c.Id, calc.Id }, again.Ordered().Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Notes_ColoursLabelsPinsAndSearch()
    {
        var svc = new NoteService(new AppStorage(Path.Combine(_dir, "notes")));
        var a = svc.Add("Buy milk\nand bread", "green", "home")!;
        var b = svc.Add("Call Anna")!;
        svc.SetPinned(b.Id, true);
        svc.SetPlacement(b.Id, new NotePlacement { Left = 10, Top = 20, Width = 200, Height = 180 });
        Assert.Equal("Buy milk", a.Title);
        Assert.Single(svc.Search("home"));
        Assert.Equal(b.Id, svc.Ordered().First().Id); // pinned first
        svc.Update(a.Id, "Buy oat milk");
        svc.SetColor(a.Id, "not-a-colour");
        var again = new NoteService(new AppStorage(Path.Combine(_dir, "notes")));
        var a2 = again.Find(a.Id)!;
        Assert.Equal("Buy oat milk", a2.Text);
        Assert.Equal("green", a2.Color);
        Assert.Equal(20, again.Find(b.Id)!.Placement!.Top);
        again.Update(a.Id, "   ");
        Assert.Null(again.Find(a.Id));
    }
}
