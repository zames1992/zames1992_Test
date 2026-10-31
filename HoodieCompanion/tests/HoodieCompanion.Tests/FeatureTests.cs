using HoodieCompanion.Features.Backpack;
using HoodieCompanion.Features.Notes;
using HoodieCompanion.Features.Reminders;
using HoodieCompanion.Features.SystemMonitor;
using HoodieCompanion.Features.Timers;
using HoodieCompanion.Geometry;
using HoodieCompanion.Presence;
using HoodieCompanion.Storage;
using Xunit;

namespace HoodieCompanion.Tests;

public sealed class FeatureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hoodie-tests-" + Guid.NewGuid().ToString("N"));

    public FeatureTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Inventory_PersistsAcrossRestart_AndRemoveKeepsOriginalFile()
    {
        var file = Path.Combine(_dir, "test.txt");
        File.WriteAllText(file, "hello");
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "Stuff")).FullName;
        var storage = new AppStorage(Path.Combine(_dir, "data"));
        var inv = new InventoryService(storage);
        var a = inv.Add(file);
        var b = inv.Add(folder);
        var c = inv.Add("https://example.com/docs");
        Assert.False(a.AlreadyPresent);
        Assert.True(inv.Add(file).AlreadyPresent);
        Assert.Equal(InventoryItemType.File, a.Item.Type);
        Assert.Equal(InventoryItemType.Folder, b.Item.Type);
        Assert.Equal(InventoryItemType.Url, c.Item.Type);
        inv.TogglePin(b.Item.Id);
        inv.Rename(a.Item.Id, "My note");

        // "Restart"
        var inv2 = new InventoryService(new AppStorage(Path.Combine(_dir, "data")));
        Assert.Equal(3, inv2.Items.Count);
        Assert.Equal(b.Item.Id, inv2.Ordered().First().Id);
        Assert.Equal("My note", inv2.Find(a.Item.Id)!.DisplayName);
        Assert.Single(inv2.Search("note"));

        inv2.Remove(a.Item.Id);
        Assert.True(File.Exists(file));
        Assert.Equal("hello", File.ReadAllText(file));
        Assert.Equal(2, new InventoryService(new AppStorage(Path.Combine(_dir, "data"))).Items.Count);
    }

    [Fact]
    public void Inventory_DetectsMissingTargets()
    {
        var storage = new AppStorage(Path.Combine(_dir, "data"));
        var inv = new InventoryService(storage);
        var file = Path.Combine(_dir, "gone.txt");
        File.WriteAllText(file, "x");
        var item = inv.Add(file).Item;
        Assert.True(inv.Exists(item));
        File.Delete(file);
        Assert.False(inv.Exists(item));
        var moved = Path.Combine(_dir, "moved.txt");
        File.WriteAllText(moved, "x");
        inv.Relocate(item.Id, moved);
        Assert.True(inv.Exists(inv.Find(item.Id)!));
    }

    [Fact]
    public void Storage_RecoversFromCorruptFile_UsingBackup()
    {
        var storage = new AppStorage(_dir);
        storage.Save("x.json", new NotesDocument { Notes = { new Note { Text = "first" } } });
        storage.Save("x.json", new NotesDocument { Notes = { new Note { Text = "second" } } });
        File.WriteAllText(storage.PathFor("x.json"), "{ this is not json");
        var loaded = storage.Load("x.json", () => new NotesDocument());
        Assert.Equal("first", loaded.Notes.Single().Text);
        Assert.NotEmpty(Directory.GetFiles(_dir, "x.json.corrupt-*"));
    }

    [Fact]
    public void Reminders_FireOnce_SnoozeAndComplete()
    {
        var svc = new ReminderService(new AppStorage(_dir));
        var now = new DateTime(2026, 1, 1, 10, 0, 0);
        var r = svc.AddIn("stretch", TimeSpan.FromMinutes(5), now);
        var fired = new List<string>();
        svc.Due += x => fired.Add(x.Id);
        svc.Tick(now.AddMinutes(4));
        Assert.Empty(fired);
        svc.Tick(now.AddMinutes(5));
        svc.Tick(now.AddMinutes(6));
        Assert.Single(fired);
        svc.Snooze(r.Id, TimeSpan.FromMinutes(10), now.AddMinutes(6));
        svc.Tick(now.AddMinutes(16));
        Assert.Equal(2, fired.Count);
        svc.Complete(r.Id);
        Assert.Empty(svc.Pending());
        Assert.Equal(new DateTime(2026, 1, 2, 9, 30, 0), ReminderService.NextOccurrence("09:30", now));
        Assert.Equal(new DateTime(2026, 1, 1, 18, 0, 0), ReminderService.NextOccurrence("18:00", now));
    }

    [Fact]
    public void Timers_FinishAndPersist()
    {
        var now = new DateTime(2026, 1, 1, 10, 0, 0);
        var svc = new TimerService(new AppStorage(_dir));
        svc.Start(TimeSpan.FromMinutes(25), now);
        var again = new TimerService(new AppStorage(_dir));
        Assert.Single(again.Active);
        CountdownTimer? done = null;
        again.Finished += t => done = t;
        again.Tick(now.AddMinutes(25));
        Assert.NotNull(done);
        Assert.Equal("25:00", TimerService.FormatRemaining(TimeSpan.FromMinutes(25)));
    }

    [Fact]
    public void Notes_AddToggleDelete()
    {
        var svc = new NoteService(new AppStorage(_dir));
        var n = svc.Add("buy milk")!;
        Assert.Null(svc.Add("   "));
        svc.Toggle(n.Id);
        Assert.Equal(0, svc.OpenCount);
        svc.Delete(n.Id);
        Assert.Empty(new NoteService(new AppStorage(_dir)).Ordered());
    }

    [Fact]
    public void Territory_RegionOverridesMonitorRule_MostRestrictiveWins()
    {
        var world = TestWorlds.SideBySide();
        var data = new TerritoryData();
        var t = new TerritoryService(data, world);
        var a = world.Monitors[0];
        t.SetMonitorRule("A", RegionType.NoGo);
        // Bottom strip may be passed through, a small bit of it is fully allowed.
        data.Regions.Add(t.MakeRegion(RegionType.PassThrough, a, RectD.FromEdges(0, 920, 1920, 1040)));
        data.Regions.Add(t.MakeRegion(RegionType.Free, a, RectD.FromEdges(100, 920, 300, 1040)));
        Assert.Equal(RegionType.NoGo, t.TypeAt(new Vec2(900, 500)));
        Assert.Equal(RegionType.PassThrough, t.TypeAt(new Vec2(900, 1000)));
        Assert.Equal(RegionType.PassThrough, t.TypeAt(new Vec2(200, 1000)));
        Assert.True(t.CanTraverse(new Vec2(900, 1040), 150));
        Assert.False(t.CanStop(new Vec2(900, 1040), 150));
        Assert.Equal(RegionType.Free, t.TypeAt(new Vec2(3000, 1000)));
    }

    [Fact]
    public void Environment_ReactsOnlyToSustainedLoad_WithCooldown()
    {
        var env = new EnvironmentInterpreter();
        var busy = new SystemStatus(95, 1, 2, null, null, null, null, TimeSpan.Zero, DateTime.Now, 0, 0);
        var moods = new List<EnvironmentMood>();
        for (var i = 0; i < 60; i++) moods.Add(env.Feed(busy, 1));
        Assert.Equal(1, moods.Count(m => m == EnvironmentMood.Busy));
        Assert.Equal(EnvironmentMood.Calm, moods[5]);
    }
}
