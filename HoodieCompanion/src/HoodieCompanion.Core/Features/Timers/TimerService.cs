using HoodieCompanion.Storage;

namespace HoodieCompanion.Features.Timers;

/// <summary>A clock Hoodie watches for the user.</summary>
public sealed class CountdownTimer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime DueAt { get; set; }
    public bool Finished { get; set; }

    public TimeSpan Remaining(DateTime now) => DueAt > now ? DueAt - now : TimeSpan.Zero;
    public TimeSpan Duration => DueAt - StartedAt;
}

public sealed class TimersDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<CountdownTimer> Timers { get; set; } = new();
}

public sealed class TimerService
{
    public const string FileName = "timers.json";
    public static readonly int[] PresetMinutes = { 5, 15, 25, 45 };
    private readonly AppStorage _storage;
    private readonly TimersDocument _doc;

    public TimerService(AppStorage storage)
    {
        _storage = storage;
        _doc = storage.Load(FileName, () => new TimersDocument());
        _doc.Timers ??= new List<CountdownTimer>();
        _doc.Timers.RemoveAll(t => t.Finished);
    }

    public event Action? Changed;
    public event Action<CountdownTimer>? Finished;

    public IEnumerable<CountdownTimer> Active => _doc.Timers.Where(t => !t.Finished).OrderBy(t => t.DueAt);

    public CountdownTimer Start(TimeSpan duration, DateTime now, string? label = null)
    {
        if (duration <= TimeSpan.Zero) duration = TimeSpan.FromMinutes(1);
        var t = new CountdownTimer
        {
            StartedAt = now,
            DueAt = now + duration,
            Label = string.IsNullOrWhiteSpace(label) ? FormatDuration(duration) + " timer" : label.Trim(),
        };
        _doc.Timers.Add(t);
        Commit();
        return t;
    }

    public void Cancel(string id)
    {
        if (_doc.Timers.RemoveAll(t => t.Id == id) > 0) Commit();
    }

    public void Acknowledge(string id) => Cancel(id);

    public void Tick(DateTime now)
    {
        List<CountdownTimer>? done = null;
        foreach (var t in _doc.Timers)
        {
            if (t.Finished || t.DueAt > now) continue;
            t.Finished = true;
            (done ??= new()).Add(t);
        }
        if (done is null) return;
        Commit();
        foreach (var t in done) Finished?.Invoke(t);
    }

    public static string FormatDuration(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes:00} min" : d.TotalMinutes >= 1 ? $"{(int)Math.Round(d.TotalMinutes)} min" : $"{d.Seconds} s";

    public static string FormatRemaining(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{d.Minutes:00}:{d.Seconds:00}";

    private void Commit()
    {
        _storage.Save(FileName, _doc);
        Changed?.Invoke();
    }
}
