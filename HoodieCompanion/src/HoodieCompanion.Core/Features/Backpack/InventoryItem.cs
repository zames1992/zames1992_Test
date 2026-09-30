namespace HoodieCompanion.Features.Backpack;

public enum InventoryItemType
{
    Application,
    File,
    Folder,
    Shortcut,
    Url,
    /// <summary>A virtual shell object: Recycle Bin, This PC, Control Panel, Store apps (Calculator...).</summary>
    ShellItem,
}

/// <summary>
/// A reference to something the user entrusted to Hoodie. Inventory never copies, moves or deletes
/// the original: removing an item only removes Hoodie's reference.
/// </summary>
public sealed class InventoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public InventoryItemType Type { get; set; }
    public string DisplayName { get; set; } = "";
    public string Target { get; set; } = "";
    public string? IconCache { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public bool IsPinned { get; set; }
    public int SortOrder { get; set; }
    public DateTime? LastOpenedAt { get; set; }
}

public sealed class InventoryDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<InventoryItem> Items { get; set; } = new();
}
