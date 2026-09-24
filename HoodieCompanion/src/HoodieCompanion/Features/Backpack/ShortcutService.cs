using System;
using System.Diagnostics;
using System.IO;
using HoodieCompanion.Platform;

namespace HoodieCompanion.Features.Backpack;

/// <summary>Opens Backpack items through the Windows shell. Never modifies the targets.</summary>
public static class ShortcutService
{
    /// <summary>
    /// Opens the item on a background thread (starting Explorer or an app can take a moment; Hoodie's
    /// animation and the panel never wait for it). <paramref name="failed"/> runs on the calling thread's dispatcher.
    /// </summary>
    public static void OpenAsync(InventoryItem item, Action<string> failed)
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        System.Threading.Tasks.Task.Run(() =>
        {
            if (!Open(item, out var error)) dispatcher.BeginInvoke(() => failed(error ?? ""));
        });
    }

    public static bool Open(InventoryItem item, out string? error)
    {
        error = null;
        try
        {
            ProcessStartInfo psi;
            if (item.Type == InventoryItemType.ShellItem)
            {
                // "::{GUID}" objects (Recycle Bin...) and "shell:AppsFolder\\AUMID" apps open through Explorer.
                var target = item.Target.StartsWith("::", StringComparison.Ordinal) ? "shell:" + item.Target : item.Target;
                psi = new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true };
            }
            else
            {
                // Folders are opened by the shell directly (reuses the running Explorer: much faster than a new explorer.exe).
                psi = new ProcessStartInfo(item.Target) { UseShellExecute = true, Verb = item.Type == InventoryItemType.Folder ? "open" : "" };
            }
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
            if (item.Type is InventoryItemType.Url or InventoryItemType.ShellItem) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Target}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("show in folder failed", ex);
        }
    }
}
