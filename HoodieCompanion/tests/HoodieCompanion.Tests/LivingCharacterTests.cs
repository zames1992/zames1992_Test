using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Companion.Memory;
using HoodieCompanion.Companion.Perception;
using HoodieCompanion.Geometry;
using HoodieCompanion.Presence;
using HoodieCompanion.Settings;
using HoodieCompanion.Storage;
using Xunit;

namespace HoodieCompanion.Tests;

/// <summary>v1.3: Perception → Mind → Intent → Action, memory, items, progression, being considerate, long runs.</summary>
public sealed class LivingCharacterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hoodie-living-" + Guid.NewGuid().ToString("N"));

    public LivingCharacterTests() => Directory.CreateDirectory(_dir);

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
        public EnvironmentSample Env;
        public RenderState Last;
        public readonly List<string> Log = new();

        public Sim(WorldGeometry world, int seed = 1, CompanionMemory? memory = null)
        {
            Pet = new PetController(world, new TerritoryService(new TerritoryData(), world), Settings, new Random(seed));
            if (memory is not null)
            {
                memory.Doc.PersonalitySeed = seed; // deterministic character per test
                Pet.Memory = memory;
            }
            Pet.Log += m => { if (Log.Count < 20000) Log.Add(m); };
            Pet.Place(Pet.DefaultHome(), appear: false);
        }

        public void Run(double seconds, Action<RenderState>? each = null, bool typing = false)
        {
            var steps = (int)Math.Round(seconds * 60);
            for (var i = 0; i < steps; i++)
            {
                var env = Env;
                env.KeyboardInput = typing && i % 6 == 0;
                if (typing) Idle = 0;
                Last = Pet.Update(new PetInput { Dt = 1 / 60.0, Cursor = Cursor, UserIdleSeconds = Idle, Env = env, LocalHour = 14 });
                foreach (var v in Last.Pose.ToArray()) Assert.True(double.IsFinite(v), "pose value not finite");
                each?.Invoke(Last);
            }
        }
    }

    // ------------------------------------------------------------------ perception

    [Fact]
    public void Perception_TypingSessionsLoadAndNewApps()
    {
        var p = new PerceptionSystem();
        var seen = new HashSet<string>();
        p.IsNewApp = a => seen.Add(a);
        var got = new List<PerceptKind>();
        void Step(double s, EnvironmentSample e)
        {
            for (var t = 0.0; t < s; t += 0.1)
            {
                p.Update(0.1, e);
                while (p.TryDequeue(out var x)) got.Add(x.Kind);
            }
        }
        Step(2, new EnvironmentSample { UserIdleSeconds = 0.1, KeyboardInput = true, ForegroundProcess = "code" });
        Assert.True(p.Typing);
        Assert.True(p.UserBusy);
        Assert.Contains(PerceptKind.TypingStarted, got);
        Assert.Contains(PerceptKind.AppFirstSeen, got);
        Assert.Equal(AppCategory.Code, p.ForegroundCategory);
        Step(8, new EnvironmentSample { UserIdleSeconds = 5, ForegroundProcess = "code" });
        Assert.False(p.Typing);
        Assert.Contains(PerceptKind.TypingStopped, got);
        // Sustained load becomes "the PC is hot"; a short spike does not.
        Step(5, new EnvironmentSample { UserIdleSeconds = 1, Cpu = 95, ForegroundProcess = "code" });
        Assert.False(p.PcHot);
        Step(20, new EnvironmentSample { UserIdleSeconds = 1, Gpu = 97, ForegroundProcess = "code" });
        Assert.True(p.PcHot);
        Assert.Contains(PerceptKind.PcHot, got);
        // Switching back to a known app is not "new".
        got.Clear();
        Step(1, new EnvironmentSample { UserIdleSeconds = 1, ForegroundProcess = "chrome" });
        Step(1, new EnvironmentSample { UserIdleSeconds = 1, ForegroundProcess = "code" });
        Assert.Equal(1, got.Count(k => k == PerceptKind.AppFirstSeen));
        // A long work session is noticed once.
        Step(56 * 60, new EnvironmentSample { UserIdleSeconds = 10, ForegroundProcess = "code" });
        Assert.Contains(PerceptKind.LongWorkSession, got);
    }

    // ------------------------------------------------------------------ intents

    [Fact]
    public void Intents_HaveReasons_DoNothingIsReal_AndBusyUsersGetQuiet()
    {
        var drives = new CharacterDrives();
        var mind = new Mind(drives) { Traits = Personality.FromSeed(3) };
        var intents = new IntentSystem(new BehaviorController(new Random(1)), new Random(2));
        IntentContext Ctx(bool busy, bool hot = false, bool typing = false) => new(
            new DecisionContext(PresenceMode.Normal, drives, true, true, true, 5, true, false, false),
            mind.Traits, mind, busy, typing, AppCategory.Code, false, hot, false, 30, false, false, false, _ => true, id => id == "fan");

        var calm = intents.Evaluate(Ctx(false));
        Assert.All(calm, o => Assert.False(string.IsNullOrWhiteSpace(o.Reason)));
        Assert.Contains(calm, o => o.Activity == Activity.Nothing);

        var busy = intents.Evaluate(Ctx(true, typing: true));
        double Share(List<IntentOption> os, params Activity[] a) => os.Where(o => a.Contains(o.Activity)).Sum(o => o.Score) / os.Sum(o => o.Score);
        var quiet = new[] { Activity.Nothing, Activity.WorkAlongside, Activity.Sit, Activity.ReadBook, Activity.LieAround, Activity.SitEdge };
        Assert.True(Share(busy, quiet) > Share(calm, quiet) + 0.2, $"busy {Share(busy, quiet):0.00} vs calm {Share(calm, quiet):0.00}");
        Assert.Contains(busy, o => o.Activity == Activity.WorkAlongside);

        var hot = intents.Evaluate(Ctx(false, hot: true));
        Assert.Equal(Activity.CoolDown, hot[0].Activity);
        Assert.Contains("fan", hot[0].Reason);
    }

    [Fact]
    public void Personality_IsStablePerInstall_AndDiffersBetweenInstalls()
    {
        var a1 = Personality.FromSeed(11);
        var a2 = Personality.FromSeed(11);
        var b = Personality.FromSeed(12);
        Assert.Equal(a1.ToString(), a2.ToString());
        Assert.NotEqual(a1.ToString(), b.ToString());
    }

    // ------------------------------------------------------------------ memory

    [Fact]
    public void Memory_IsBounded_Habituates_Persists_AndStoresNoContent()
    {
        var storage = new AppStorage(Path.Combine(_dir, "mem"));
        var m = new CompanionMemory(storage);
        Assert.True(m.SeeApp("code", AppCategory.Code));
        Assert.False(m.SeeApp("code", AppCategory.Code));
        var h0 = m.Habituate("window-opened");
        for (var i = 0; i < 30; i++) m.Habituate("window-opened");
        Assert.Equal(0, h0);
        Assert.True(m.Habituate("window-opened") > 0.9);
        for (var i = 0; i < 1000; i++) m.SeeApp("app" + i, AppCategory.Unknown);
        for (var i = 0; i < 1000; i++) m.Habituate("event" + i);
        for (var i = 0; i < 1000; i++) m.Remember("moment" + i);
        for (var i = 0; i < 500; i++) m.RestedAt("A", i / 500.0, 10);
        Assert.True(m.Doc.Apps.Count <= 200);
        Assert.True(m.Doc.EventCounts.Count <= 200);
        Assert.True(m.Doc.Moments.Count <= 300);
        Assert.True(m.Doc.Places.Count <= 60);
        m.RestedAt("A", 0.5, 900);
        Assert.Equal(0.5, m.FavoritePlace()!.RelX, 2);
        m.Flush(force: true);
        var again = new CompanionMemory(storage);
        Assert.Equal(m.Doc.PersonalitySeed, again.Doc.PersonalitySeed);
        Assert.Equal(0.5, again.FavoritePlace()!.RelX, 2);
        // Only process names, counts, places and moment keys: no free text fields exist in the document.
        var json = File.ReadAllText(storage.PathFor(CompanionMemory.FileName));
        Assert.DoesNotContain("Title", json);
        Assert.DoesNotContain("Text", json);
    }

    [Fact]
    public void Progression_UnlocksPassively_ByTimeTogether_NotStreaks()
    {
        var m = new CompanionMemory(null);
        Assert.Empty(Progression.Due(m));
        m.TickTogether(3600, new DateTime(2026, 1, 1, 10, 0, 0), true);
        Assert.Contains(Progression.Due(m), u => u.Id == "mug");
        Assert.DoesNotContain(Progression.Due(m), u => u.Id == "fan"); // needs a second day
        // Days do not need to be consecutive: coming back a week later counts.
        m.TickTogether(3 * 3600, new DateTime(2026, 1, 9, 10, 0, 0), true);
        Assert.Contains(Progression.Due(m), u => u.Id == "fan");
    }

    // ------------------------------------------------------------------ behaviour in context

    [Fact]
    public void HotPc_WithAFan_HoodieFetchesItAndFansItself()
    {
        var mem = new CompanionMemory(null);
        mem.GiveItem("fan");
        var sim = new Sim(TestWorlds.Single(), seed: 5, memory: mem);
        sim.Env.Cpu = 97;
        sim.Env.ForegroundProcess = "blender";
        var sawFan = false;
        sim.Run(60, r => sawFan |= r.Pose.PropFan > 0.5 && r.Clip == AnimClip.FanSelf);
        Assert.True(sawFan, string.Join(" | ", sim.Log.Where(l => l.StartsWith("intent") || l.StartsWith("percept")).TakeLast(12)));
        Assert.True(mem.HasMoment("first-fan"));
    }

    [Fact]
    public void WhileTheUserTypes_HoodieGetsOutOfTheWay_AndStaysQuiet()
    {
        var sim = new Sim(TestWorlds.Single(), seed: 6);
        sim.Pet.Place(new Vec2(900, 1040), appear: false);
        sim.Run(1);
        // The active window covers Hoodie's spot; the pointer (caret area) is right there.
        sim.Env.ForegroundProcess = "winword";
        sim.Env.ForegroundBounds = new RectD(700, 300, 600, 780);
        sim.Cursor = new Vec2(920, 950);
        var expressive = 0;
        sim.Run(40, r =>
        {
            if (r.Clip is AnimClip.Dance or AnimClip.JumpForJoy or AnimClip.Spin or AnimClip.Excited or AnimClip.Run) expressive++;
        }, typing: true);
        Assert.True(sim.Pet.Feet.X < 700 - 10 || sim.Pet.Feet.X > 1300 + 10, $"still over the active window at {sim.Pet.Feet}");
        Assert.Equal(0, expressive);
    }

    [Fact]
    public void WindowClosedUnderHoodie_ItFalls_IsScared_AndRemembers()
    {
        var mem = new CompanionMemory(null);
        var sim = new Sim(TestWorlds.Single(), seed: 7, memory: mem);
        sim.Pet.Place(new Vec2(900, 1040), appear: false);
        sim.Pet.SetSurfaces(new[] { new Surface("w1", SurfaceKind.Window, 700, 1200, 900) });
        sim.Run(0.3);
        Assert.True(sim.Pet.DebugVisitSurface());
        sim.Run(8);
        Assert.Equal("w1", sim.Pet.StandingOn);
        sim.Pet.SetSurfaces(Array.Empty<Surface>());
        var scared = false;
        sim.Run(5, r => scared |= r.Clip is AnimClip.Scared or AnimClip.Annoyed or AnimClip.Sigh);
        Assert.True(scared);
        Assert.True(mem.HasMoment("window-closed-under-me"));
        Assert.True(mem.HasMoment("first-window-top"));
    }

    [Fact]
    public void NewWindowNearby_HoodieLooksAndMayGoInvestigate()
    {
        var mem = new CompanionMemory(null);
        var sim = new Sim(TestWorlds.Single(), seed: 8, memory: mem);
        sim.Pet.Mind.Traits.Curiosity = 0.9;
        sim.Pet.Place(new Vec2(1500, 1040), appear: false);
        sim.Run(1);
        sim.Pet.Perception.OnWindowEvent(new WindowEvent(WindowEventKind.Opened, "photoshop", new RectD(300, 200, 500, 400)));
        sim.Run(50);
        Assert.Contains(sim.Log, l => l.StartsWith("percept WindowOpened") || l.StartsWith("percept AppFirstSeen"));
        Assert.True(mem.HasMoment("first-new-app"));
        Assert.Contains(sim.Log, l => l.Contains("intent InvestigateWindow"));
        Assert.True(sim.Pet.Feet.X < 1200, $"did not go to look: {sim.Pet.Feet} " + string.Join(" | ", sim.Log.TakeLast(20)));
    }

    [Fact]
    public void AfterALongSession_HoodieSuggestsABreak_WhenYouPause()
    {
        var mem = new CompanionMemory(null);
        mem.GiveItem("mug");
        var sim = new Sim(TestWorlds.Single(), seed: 9, memory: mem);
        sim.Pet.Perception.OnWindowEvent(new WindowEvent(WindowEventKind.Foreground, "code", new RectD(0, 0, 1920, 1040)));
        // 56 minutes of steady work, then the user leans back (a natural pause).
        for (var i = 0; i < 56; i++) sim.Run(60, typing: i % 2 == 0);
        sim.Idle = 5;
        sim.Cursor = new Vec2(400, 700);
        var sipped = false;
        sim.Run(40, r => { sim.Idle += 1 / 60.0; sipped |= r.Clip == AnimClip.SipMug; });
        Assert.True(mem.HasMoment("first-break-hint"), string.Join(" | ", sim.Log.Where(l => !l.Contains("->")).TakeLast(14)));
        Assert.True(sipped);
    }

    [Fact]
    public void UnlockedItem_IsFoundAndShownAtACalmMoment()
    {
        var mem = new CompanionMemory(null);
        mem.TickTogether(3600, DateTime.Now, true);
        var sim = new Sim(TestWorlds.Single(), seed: 10, memory: mem);
        sim.Idle = 5;
        var shown = false;
        sim.Run(40, r => shown |= r.Clip == AnimClip.ShowItem && r.Pose.PropMug > 0.3);
        Assert.True(mem.HasItem("mug"));
        Assert.True(mem.HasMoment("unlock:mug"));
        Assert.True(shown, string.Join(" | ", sim.Log.TakeLast(20)));
    }

    [Fact]
    public void Spin_AndDance_OnlyAfterTheyAreUnlocked()
    {
        var sim = new Sim(TestWorlds.Single(), seed: 13);
        var clips = new HashSet<AnimClip>();
        sim.Run(900, r => clips.Add(r.Clip));
        Assert.DoesNotContain(AnimClip.Spin, clips);
        Assert.DoesNotContain(AnimClip.Dance, clips);
    }

    // ------------------------------------------------------------------ long runs

    [Fact]
    public void TwelveHours_OfLife_StayStableAndBounded()
    {
        var world = TestWorlds.SideBySide();
        var mem = new CompanionMemory(new AppStorage(Path.Combine(_dir, "soak")));
        var sim = new Sim(world, seed: 21, memory: mem);
        var rnd = new Random(4);
        var apps = new[] { "code", "chrome", "winword", "telegram", "spotify", "game_x" };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var gc0 = GC.GetTotalMemory(true);
        for (var minute = 0; minute < 12 * 60; minute++)
        {
            var hour = minute / 60;
            sim.Env.ForegroundProcess = apps[rnd.Next(apps.Length)];
            sim.Env.ForegroundFullscreen = sim.Env.ForegroundProcess == "game_x" && rnd.NextDouble() < 0.5;
            sim.Env.Cpu = rnd.NextDouble() < 0.1 ? 95 : 20;
            sim.Cursor = new Vec2(rnd.Next(0, 4400), rnd.Next(0, 1400));
            sim.Idle = hour % 4 == 3 ? sim.Idle + 60 : rnd.NextDouble() * 30;
            if (rnd.NextDouble() < 0.2) sim.Pet.Perception.OnWindowEvent(new WindowEvent(WindowEventKind.Opened, apps[rnd.Next(apps.Length)], new RectD(rnd.Next(0, 3000), 100, 600, 500)));
            if (rnd.NextDouble() < 0.05)
                sim.Pet.SetSurfaces(new[] { new Surface("w" + rnd.Next(5), SurfaceKind.Window, rnd.Next(0, 3000), rnd.Next(3000, 4400), rnd.Next(300, 900)) });
            // Simulate one representative second per minute at full rate, the rest in 1 s steps (like a calm frame rate).
            sim.Run(1, typing: rnd.NextDouble() < 0.4);
            for (var s = 0; s < 59; s++)
                sim.Last = sim.Pet.Update(new PetInput { Dt = 1, Cursor = sim.Cursor, UserIdleSeconds = sim.Idle, Env = sim.Env, LocalHour = (9 + hour) % 24 });
            Assert.True(world.Extent.Inflate(600, 600).Contains(sim.Pet.Feet), $"left the world at minute {minute}: {sim.Pet.Feet}");
        }
        mem.Flush(force: true);
        var gc1 = GC.GetTotalMemory(true);
        Assert.True(sim.Pet.Perception.Pending <= 64);
        Assert.True(mem.Doc.Apps.Count <= 200 && mem.Doc.Moments.Count <= 300 && mem.Doc.EventCounts.Count <= 200);
        Assert.InRange(mem.HoursTogether, 5, 12.1);
        Assert.True(gc1 - gc0 < 20_000_000, $"managed memory grew by {(gc1 - gc0) / 1e6:0.0} MB");
        Assert.True(sw.Elapsed.TotalSeconds < 60, $"12 h simulation took {sw.Elapsed.TotalSeconds:0} s");
    }
}
