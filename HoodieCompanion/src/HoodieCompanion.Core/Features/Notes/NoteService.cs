using HoodieCompanion.Storage;

namespace HoodieCompanion.Features.Notes;

/// <summary>Something the user asked Hoodie to remember.</summary>
public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsCompleted { get; set; }
}

public sealed class NotesDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<Note> Notes { get; set; } = new();
}

public sealed class NoteService
{
    public const string FileName = "notes.json";
    private readonly AppStorage _storage;
    private readonly NotesDocument _doc;

    public NoteService(AppStorage storage)
    {
        _storage = storage;
        _doc = storage.Load(FileName, () => new NotesDocument());
        _doc.Notes ??= new List<Note>();
    }

    public event Action? Changed;

    public IEnumerable<Note> Ordered() => _doc.Notes.OrderBy(n => n.IsCompleted).ThenByDescending(n => n.CreatedAt);

    public Note? Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var n = new Note { Text = text.Trim() };
        _doc.Notes.Add(n);
        Commit();
        return n;
    }

    public void Toggle(string id)
    {
        var n = _doc.Notes.FirstOrDefault(x => x.Id == id);
        if (n is null) return;
        n.IsCompleted = !n.IsCompleted;
        Commit();
    }

    public void Delete(string id)
    {
        if (_doc.Notes.RemoveAll(x => x.Id == id) > 0) Commit();
    }

    public int OpenCount => _doc.Notes.Count(n => !n.IsCompleted);

    private void Commit()
    {
        _storage.Save(FileName, _doc);
        Changed?.Invoke();
    }
}
