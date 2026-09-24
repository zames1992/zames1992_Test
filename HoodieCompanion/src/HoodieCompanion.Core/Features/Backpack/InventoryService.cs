using HoodieCompanion.Storage;

namespace HoodieCompanion.Features.Backpack;

public sealed record AddResult(InventoryItem Item, bool AlreadyPresent);

/// <summary>Hoodie's Backpack: a small, persistent list of references (files, folders, apps, links).</summary>
public sealed class InventoryService
{
    public const string FileName = "inventory.json";
    private readonly AppStorage _storage;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _dirExists;
    private InventoryDocument _doc;

    public InventoryService(AppStorage storage, Func<string, bool>? fileExists = null, Func<string, bool>? dirExists = null)
    {
        _storage = storage;
        _fileExists = fileExists ?? File.Exists;
        _dirExists = dirExists ?? Directory.Exists;
        _doc = storage.Load(FileName, () => new InventoryDocument());
        _doc.Items ??= new List<InventoryItem>();
        _doc.Items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Target));
    }

    public event Action? Changed;

    public IReadOnlyList<InventoryItem> Items => _doc.Items;

    /// <summary>Pinned first, then by manual order, newest first.</summary>
    public IEnumerable<InventoryItem> Ordered() =>
        _doc.Items.OrderByDescending(i => i.IsPinned).ThenBy(i => i.SortOrder).ThenByDescending(i => i.AddedAt);

    public IEnumerable<InventoryItem> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Ordered();
        var q = query.Trim();
        return Ordered().Where(i => i.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                    i.Target.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    public InventoryItem? Find(string id) => _doc.Items.FirstOrDefault(i => i.Id == id);

    public static bool LooksLikeUrl(string s) =>
        Uri.TryCreate(s.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    public InventoryItemType Classify(string target)
    {
        if (LooksLikeUrl(target)) return InventoryItemType.Url;
        if (_dirExists(target)) return InventoryItemType.Folder;
        var ext = Path.GetExtension(target).ToLowerInvariant();
        return ext switch
        {
            ".exe" or ".com" or ".bat" or ".cmd" or ".msc" or ".appref-ms" => InventoryItemType.Application,
            ".lnk" or ".url" => InventoryItemType.Shortcut,
            _ => InventoryItemType.File,
        };
    }

    public static string DefaultName(string target, InventoryItemType type)
    {
        if (type == InventoryItemType.Url)
        {
            return Uri.TryCreate(target, UriKind.Absolute, out var u) ? u.Host + (u.AbsolutePath.Length > 1 ? u.AbsolutePath : "") : target;
        }
        var trimmed = target.TrimEnd('\\', '/');
        var name = type is InventoryItemType.Shortcut or InventoryItemType.Application
            ? Path.GetFileNameWithoutExtension(trimmed)
            : Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    public AddResult Add(string target, string? displayName = null)
    {
        target = target.Trim().Trim('"');
        var existing = _doc.Items.FirstOrDefault(i => string.Equals(i.Target, target, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return new AddResult(existing, true);
        var type = Classify(target);
        var item = new InventoryItem
        {
            Type = type,
            Target = target,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? DefaultName(target, type) : displayName.Trim(),
            AddedAt = DateTime.Now,
            SortOrder = _doc.Items.Count == 0 ? 0 : _doc.Items.Min(i => i.SortOrder) - 1,
        };
        _doc.Items.Add(item);
        Commit();
        return new AddResult(item, false);
    }

    /// <summary>Removes Hoodie's reference only. The original file is never touched.</summary>
    public bool Remove(string id)
    {
        var removed = _doc.Items.RemoveAll(i => i.Id == id) > 0;
        if (removed) Commit();
        return removed;
    }

    public void Rename(string id, string name)
    {
        var item = Find(id);
        if (item is null || string.IsNullOrWhiteSpace(name)) return;
        item.DisplayName = name.Trim();
        Commit();
    }

    public void TogglePin(string id)
    {
        var item = Find(id);
        if (item is null) return;
        item.IsPinned = !item.IsPinned;
        Commit();
    }

    public void Relocate(string id, string newTarget)
    {
        var item = Find(id);
        if (item is null) return;
        item.Target = newTarget;
        item.Type = Classify(newTarget);
        item.IconCache = null;
        Commit();
    }

    public void SetIconCache(string id, string? path)
    {
        var item = Find(id);
        if (item is null) return;
        item.IconCache = path;
        Commit(notify: false);
    }

    public void MarkOpened(string id)
    {
        var item = Find(id);
        if (item is null) return;
        item.LastOpenedAt = DateTime.Now;
        Commit(notify: false);
    }

    public bool Exists(InventoryItem item) => item.Type switch
    {
        InventoryItemType.Url => true,
        InventoryItemType.Folder => _dirExists(item.Target),
        _ => _fileExists(item.Target) || _dirExists(item.Target),
    };

    private void Commit(bool notify = true)
    {
        _storage.Save(FileName, _doc);
        if (notify) Changed?.Invoke();
    }
}
