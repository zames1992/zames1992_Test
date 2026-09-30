using HoodieCompanion.Storage;

namespace HoodieCompanion.Features.Notes;

/// <summary>Something the user asked Hoodie to remember.</summary>
public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsCompleted { get; set; }
    /// <summary>Card colour (see <see cref="NoteColors"/>).</summary>
    public string Color { get; set; } = NoteColors.Default;
    /// <summary>Optional short label, e.g. "work", "ideas".</summary>
    public string? Label { get; set; }
    /// <summary>Pinned notes float on the desktop above everything until unpinned.</summary>
    public bool IsPinned { get; set; }
    public NotePlacement? Placement { get; set; }

    public string Title
    {
        get
        {
            var first = Text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
            return first.Length > 60 ? first[..60] + "…" : first;
        }
    }
}

/// <summary>Where a pinned note sits on the desktop (WPF device-independent units).</summary>
public sealed class NotePlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 220;
}

public static class NoteColors
{
    public const string Default = "yellow";
    public static readonly string[] All = { "yellow", "green", "pink", "purple", "blue", "gray", "charcoal" };

    /// <summary>(background, header, text) as #RRGGBB, modelled on Windows Sticky Notes.</summary>
    public static (string Back, string Header, string Text) Palette(string? color) => color switch
    {
        "green" => ("#E4F9E0", "#C6EFBE", "#1F2A1D"),
        "pink" => ("#FFE4F1", "#FFC8E2", "#2E1C25"),
        "purple" => ("#F2E6FF", "#E0CBFF", "#261C33"),
        "blue" => ("#E2F1FF", "#C3E1FF", "#1A2531"),
        "gray" => ("#F3F2F1", "#E1DFDD", "#252423"),
        "charcoal" => ("#4A4A4A", "#3A3A3A", "#F5F5F5"),
        _ => ("#FFF7D1", "#FFEAA3", "#2D2A1E"),
    };
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

    public IEnumerable<Note> Ordered() => _doc.Notes.OrderBy(n => n.IsCompleted).ThenByDescending(n => n.IsPinned).ThenByDescending(n => n.UpdatedAt);

    public IReadOnlyList<Note> All => _doc.Notes;

    public Note? Find(string id) => _doc.Notes.FirstOrDefault(n => n.Id == id);

    public IEnumerable<Note> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Ordered();
        var q = query.Trim();
        return Ordered().Where(n => n.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                                    (n.Label?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false));
    }

    public Note? Add(string text, string? color = null, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var n = new Note { Text = text.Trim(), Color = color ?? NoteColors.Default, Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim() };
        _doc.Notes.Add(n);
        Commit();
        return n;
    }

    /// <summary>Saves new text. Empty text deletes the note.</summary>
    public void Update(string id, string text, bool notify = true)
    {
        var n = Find(id);
        if (n is null) return;
        if (string.IsNullOrWhiteSpace(text))
        {
            Delete(id);
            return;
        }
        if (n.Text == text.Trim()) return;
        n.Text = text.Trim();
        n.UpdatedAt = DateTime.Now;
        Commit(notify);
    }

    public void SetColor(string id, string color)
    {
        var n = Find(id);
        if (n is null || !NoteColors.All.Contains(color)) return;
        n.Color = color;
        Commit();
    }

    public void SetLabel(string id, string? label)
    {
        var n = Find(id);
        if (n is null) return;
        n.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        Commit();
    }

    public void SetPinned(string id, bool pinned)
    {
        var n = Find(id);
        if (n is null || n.IsPinned == pinned) return;
        n.IsPinned = pinned;
        Commit();
    }

    public void SetPlacement(string id, NotePlacement placement)
    {
        var n = Find(id);
        if (n is null) return;
        n.Placement = placement;
        Commit(notify: false);
    }

    public IEnumerable<string> Labels() => _doc.Notes.Select(n => n.Label).Where(l => l is not null).Distinct(StringComparer.CurrentCultureIgnoreCase)!;

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

    private void Commit(bool notify = true)
    {
        _storage.Save(FileName, _doc);
        if (notify) Changed?.Invoke();
    }
}
