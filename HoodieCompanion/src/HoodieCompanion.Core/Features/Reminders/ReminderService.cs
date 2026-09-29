using HoodieCompanion.Storage;

namespace HoodieCompanion.Features.Reminders;

/// <summary>A promise Hoodie agreed to keep.</summary>
public sealed class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime DueAt { get; set; }
    public bool Completed { get; set; }
    public bool Fired { get; set; }
}

public sealed class RemindersDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<Reminder> Reminders { get; set; } = new();
}

public sealed class ReminderService
{
    public const string FileName = "reminders.json";
    private readonly AppStorage _storage;
    private readonly RemindersDocument _doc;

    public ReminderService(AppStorage storage)
    {
        _storage = storage;
        _doc = storage.Load(FileName, () => new RemindersDocument());
        _doc.Reminders ??= new List<Reminder>();
        // Anything that fired but was never acknowledged should be announced again after a restart.
        foreach (var r in _doc.Reminders.Where(r => !r.Completed)) r.Fired = false;
    }

    public event Action? Changed;
    public event Action<Reminder>? Due;

    public IEnumerable<Reminder> Pending() => _doc.Reminders.Where(r => !r.Completed).OrderBy(r => r.DueAt);

    public Reminder Add(string text, DateTime dueAt)
    {
        var r = new Reminder { Text = string.IsNullOrWhiteSpace(text) ? "Reminder" : text.Trim(), DueAt = dueAt };
        _doc.Reminders.Add(r);
        Commit();
        return r;
    }

    public Reminder AddIn(string text, TimeSpan delay, DateTime now) => Add(text, now + delay);

    /// <summary>Parses "HH:mm" as the next occurrence of that local time.</summary>
    public static DateTime? NextOccurrence(string hhmm, DateTime now)
    {
        if (!TimeSpan.TryParse(hhmm.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var tod) || tod < TimeSpan.Zero || tod >= TimeSpan.FromDays(1))
            return null;
        var candidate = now.Date + tod;
        return candidate <= now ? candidate.AddDays(1) : candidate;
    }

    /// <summary>Call about once per second. Raises <see cref="Due"/> for each newly due reminder.</summary>
    public void Tick(DateTime now)
    {
        List<Reminder>? due = null;
        foreach (var r in _doc.Reminders)
        {
            if (r.Completed || r.Fired || r.DueAt > now) continue;
            r.Fired = true;
            (due ??= new()).Add(r);
        }
        if (due is null) return;
        Commit();
        foreach (var r in due) Due?.Invoke(r);
    }

    public void Complete(string id)
    {
        var r = _doc.Reminders.FirstOrDefault(x => x.Id == id);
        if (r is null) return;
        r.Completed = true;
        // Keep the file small: drop old completed reminders.
        _doc.Reminders.RemoveAll(x => x.Completed && x.DueAt < DateTime.Now.AddDays(-7));
        Commit();
    }

    public void Snooze(string id, TimeSpan by, DateTime now)
    {
        var r = _doc.Reminders.FirstOrDefault(x => x.Id == id);
        if (r is null) return;
        r.DueAt = now + by;
        r.Fired = false;
        Commit();
    }

    public void Delete(string id)
    {
        if (_doc.Reminders.RemoveAll(x => x.Id == id) > 0) Commit();
    }

    private void Commit()
    {
        _storage.Save(FileName, _doc);
        Changed?.Invoke();
    }
}
