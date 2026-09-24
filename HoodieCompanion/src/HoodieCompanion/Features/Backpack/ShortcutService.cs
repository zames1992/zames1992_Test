using System;
using System.Diagnostics;
using System.IO;
using HoodieCompanion.Platform;

namespace HoodieCompanion.Features.Backpack;

/// <summary>Opens Backpack items through the Windows shell. Never modifies the targets.</summary>
public static class ShortcutService
{
    public static bool Open(InventoryItem item, out string? error)
    {
        error = null;
        try
        {
            var psi = item.Type == InventoryItemType.Folder
                ? new ProcessStartInfo("explorer.exe", $"\"{item.Target}\"") { UseShellExecute = true }
                : new ProcessStartInfo(item.Target) { UseShellExecute = true };
            if (item.Type is InventoryItemType.Application)
            {
                var dir = Path.GetDirectoryName(item.Target);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
            }
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error("open failed: " + item.Target, ex);
            return false;
        }
    }

    public static void ShowInFolder(InventoryItem item)
    {
        try
        {
            if (item.Type == InventoryItemType.Url) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Target}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("show in folder failed", ex);
        }
    }
}
