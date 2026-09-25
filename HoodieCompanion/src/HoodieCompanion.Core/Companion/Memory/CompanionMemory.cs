using HoodieCompanion.Companion.Perception;
using HoodieCompanion.Storage;

namespace HoodieCompanion.Companion.Memory;

/// <summary>A place Hoodie liked to rest (per monitor, bucketed along the floor or a window top).</summary>
public sealed class PlaceMemory
{
    public string MonitorId { get; set; } = "";
    /// <summary>0..1 across the monitor's working area.</summary>
    public double RelX { get; set; }
    public double Seconds { get; set; }
}

public sealed class AppMemory
{
    public AppCategory Category { get; set; }
    public DateTime FirstSeen { get; set; } = DateTime.Now;
    public double ForegroundMinutes { get; set; }
    public int Reactions { get; set; }
}

/// <summary>A memorable moment ("the first time it..."), shown in the Memories page.</summary>
public sealed class MomentMemory
{
    public string Key { get; set; } = "";
    public DateTime At { get; set; } = DateTime.Now;
    /// <summary>Optional detail (e.g. a process name). Never user content.</summary>
    public string? Detail { get; set; }
}

public sealed class MemoryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime FirstMet { get; set; } = DateTime.Now;
    public List<string> DaysTogether { get; set; } = new();
    public double MinutesTogether { get; set; }
    public List<PlaceMemory> Places { get; set; } = new();
    public Dictionary<string, AppMemory> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> EventCounts { get; set; } = new();
    public double[] ActiveMinutesByHour { get; set; } = new double[24];
    public Dictionary<string, int> Interactions { get; set; } = new();
    public List<MomentMemory> Moments { get; set; } = new();
    public List<string> Unlocked { get; set; } = new();
    /// <summary>Hoodie's own things (world items, not the user's shortcuts).</summary>
    public List<string> Items { get; set; } = new();
    /// <summary>Items Hoodie has misplaced for a while (it finds them again later).</summary>
    public List<string> LostItems { get; set; } = new();
    public string HoodieColor { get; set; } = "charcoal";
    /// <summary>Stable personality seed, fixed on first run (so every Hoodie is a little different).</summary>
    public int PersonalitySeed { get; set; } = Random.Shared.Next();
}

/// <summary>
/// What Hoodie remembers about living on this PC. Only safe, local facts: places, process names and
/// categories, counts, hours of activity, moments. Never typed text, window titles, documents, messages
/// or anything the user produces. Everything is bounded in size.
/// </summary>
public sealed class CompanionMemory
{
    public const string FileName = "memory.json";
    private const int MaxApps = 200, MaxPlaces = 60, MaxEvents = 200, MaxMoments = 300;

    private readonly AppStorage? _storage;
    private double _dirtyFor;
    private bool _dirty;

    public CompanionMemory(AppStorage? storage)
    {
        _storage = storage;
        Doc = storage?.Load(FileName, () => new MemoryDocument()) ?? new MemoryDocument();
        Doc.Apps ??= new(StringComparer.OrdinalIgnoreCase);
        if (Doc.Apps.Comparer != StringComparer.OrdinalIgnoreCase) Doc.Apps = new(Doc.Apps, StringComparer.OrdinalIgnoreCase);
        Doc.ActiveMinutesByHour = Doc.ActiveMinutesByHour is { Length: 24 } a ? a : new double[24];
    }

    public MemoryDocument Doc { get; }

    public event Action<MomentMemory>? MomentAdded;

    public int DaysTogether => Doc.DaysTogether.Count;
    public double HoursTogether => Doc.MinutesTogether / 60;

    // ------------------------------------------------------------------ time

    /// <summary>Called while the app runs and the user is present.</summary>
    public void TickTogether(double dt, DateTime now, bool userActive)
    {
        if (!userActive) return;
        Doc.MinutesTogether += dt / 60;
        Doc.ActiveMinutesByHour[now.Hour] += dt / 60;
        var day = now.ToString("yyyy-MM-dd");
        if (Doc.DaysTogether.Count == 0 || Doc.DaysTogether[^1] != day)
        {
            if (!Doc.DaysTogether.Contains(day)) Doc.DaysTogether.Add(day);
            if (Doc.DaysTogether.Count > 3650) Doc.DaysTogether.RemoveAt(0);
            MarkDirty();
        }
    }

    /// <summary>Hours of the day the user is usually around (share of all activity, 0..1).</summary>
    public double UsualActivity(int hour)
    {
        var total = Doc.ActiveMinutesByHour.Sum();
        return total < 60 ? 0.5 : Doc.ActiveMinutesByHour[hour] / total * 24 / 2;
    }

    // ------------------------------------------------------------------ apps

    /// <summary>Records an app; returns true the first time it is ever seen.</summary>
    public bool SeeApp(string process, AppCategory category)
    {
        if (Doc.Apps.TryGetValue(process, out var a))
        {
            if (a.Category == AppCategory.Unknown && category != AppCategory.Unknown) a.Category = category;
            return false;
        }
        if (Doc.Apps.Count >= MaxApps)
        {
            var least = Doc.Apps.OrderBy(x => x.Value.ForegroundMinutes).First().Key;
            Doc.Apps.Remove(least);
        }
        Doc.Apps[process] = new AppMemory { Category = category };
        MarkDirty();
        return true;
    }

    public void AppForeground(string process, double dt)
    {
        if (Doc.Apps.TryGetValue(process, out var a)) a.ForegroundMinutes += dt / 60;
    }

    /// <summary>How familiar an app is (0 new .. 1 used for hours).</summary>
    public double Familiarity(string? process) =>
        process is not null && Doc.Apps.TryGetValue(process, out var a) ? Math.Clamp(a.ForegroundMinutes / 120, 0, 1) : 0;

    public string? FavoriteApp() => Doc.Apps.OrderByDescending(a => a.Value.ForegroundMinutes).FirstOrDefault().Key;

    // ------------------------------------------------------------------ habituation

    /// <summary>Counts an event and returns how "used to it" Hoodie is (0 = first time .. 1 = seen it many times).</summary>
    public double Habituate(string key)
    {
        Doc.EventCounts.TryGetValue(key, out var n);
        if (!Doc.EventCounts.ContainsKey(key) && Doc.EventCounts.Count >= MaxEvents)
            Doc.EventCounts.Remove(Doc.EventCounts.OrderBy(e => e.Value).First().Key);
        Doc.EventCounts[key] = n + 1;
        MarkDirty();
        return 1 - Math.Exp(-n / 6.0);
    }

    public int Count(string key) => Doc.EventCounts.TryGetValue(key, out var n) ? n : 0;

    public void Interaction(string kind)
    {
        Doc.Interactions.TryGetValue(kind, out var n);
        Doc.Interactions[kind] = n + 1;
        MarkDirty();
    }

    public int Interactions(string kind) => Doc.Interactions.TryGetValue(kind, out var n) ? n : 0;

    // ------------------------------------------------------------------ places

    public void RestedAt(string monitorId, double relX, double seconds)
    {
        relX = Math.Round(Math.Clamp(relX, 0, 1) * 20) / 20;
        var p = Doc.Places.FirstOrDefault(x => x.MonitorId == monitorId && Math.Abs(x.RelX - relX) < 0.01);
        if (p is null)
        {
            if (Doc.Places.Count >= MaxPlaces) Doc.Places.Remove(Doc.Places.OrderBy(x => x.Seconds).First());
            p = new PlaceMemory { MonitorId = monitorId, RelX = relX };
            Doc.Places.Add(p);
        }
        p.Seconds += seconds;
        MarkDirty();
    }

    /// <summary>The spot Hoodie has spent the most time resting at (if it rested there for a while).</summary>
    public PlaceMemory? FavoritePlace(Func<PlaceMemory, bool>? allowed = null) =>
        Doc.Places.Where(p => p.Seconds > 300 && (allowed?.Invoke(p) ?? true)).OrderByDescending(p => p.Seconds).FirstOrDefault();

    // ------------------------------------------------------------------ moments, unlocks, items

    public bool HasMoment(string key) => Doc.Moments.Any(m => m.Key == key);

    /// <summary>Remembers a moment once (first time only). Returns true if it is new.</summary>
    public bool Remember(string key, string? detail = null)
    {
        if (HasMoment(key)) return false;
        var m = new MomentMemory { Key = key, Detail = detail, At = DateTime.Now };
        Doc.Moments.Add(m);
        if (Doc.Moments.Count > MaxMoments) Doc.Moments.RemoveAt(0);
        MarkDirty();
        MomentAdded?.Invoke(m);
        return true;
    }

    public bool IsUnlocked(string id) => Doc.Unlocked.Contains(id);

    public bool Unlock(string id)
    {
        if (Doc.Unlocked.Contains(id)) return false;
        Doc.Unlocked.Add(id);
        MarkDirty();
        return true;
    }

    public bool HasItem(string id) => Doc.Items.Contains(id) && !Doc.LostItems.Contains(id);

    public void GiveItem(string id)
    {
        if (!Doc.Items.Contains(id)) Doc.Items.Add(id);
        Doc.LostItems.Remove(id);
        MarkDirty();
    }

    public void LoseItem(string id)
    {
        if (Doc.Items.Contains(id) && !Doc.LostItems.Contains(id)) Doc.LostItems.Add(id);
        MarkDirty();
    }

    public void FindItem(string id)
    {
        Doc.LostItems.Remove(id);
        MarkDirty();
    }

    // ------------------------------------------------------------------ persistence

    public void MarkDirty() => _dirty = true;

    /// <summary>
    /// Forgets everything Hoodie learned (apps, places, moments, counts, time together, found things).
    /// Its personality and chosen colour stay, so it is still the same Hoodie.
    /// </summary>
    public void Forget()
    {
        var seed = Doc.PersonalitySeed;
        var color = Doc.HoodieColor;
        Doc.FirstMet = DateTime.Now;
        Doc.DaysTogether.Clear();
        Doc.MinutesTogether = 0;
        Doc.Places.Clear();
        Doc.Apps.Clear();
        Doc.EventCounts.Clear();
        Doc.ActiveMinutesByHour = new double[24];
        Doc.Interactions.Clear();
        Doc.Moments.Clear();
        Doc.Unlocked.Clear();
        Doc.Items.Clear();
        Doc.LostItems.Clear();
        Doc.PersonalitySeed = seed;
        Doc.HoodieColor = color == "charcoal" ? color : "charcoal";
        _dirty = true;
        Flush(force: true);
    }

    private double _savedMinutes = -1;

    /// <summary>Saves at most once a minute when something changed (or immediately when forced).</summary>
    public void Flush(bool force = false)
    {
        if (_storage is null) return;
        if (!force && _dirtyFor < 60) return;
        if (!_dirty && Math.Abs(_savedMinutes - Doc.MinutesTogether) < 0.01) { _dirtyFor = 0; return; }
        try
        {
            _storage.Save(FileName, Doc);
        }
        catch
        {
            // Memory is nice to have; never crash for it.
        }
        _savedMinutes = Doc.MinutesTogether;
        _dirty = false;
        _dirtyFor = 0;
    }

    public void Tick(double dt)
    {
        _dirtyFor += dt;
        Flush();
    }
}
