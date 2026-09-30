using System;
using Microsoft.Win32;

namespace HoodieCompanion.Platform;

/// <summary>
/// A "Hoodie" submenu in the desktop's right-click menu (per user, no admin rights):
/// Come here · Stay here · Set Home here · Open panel · Hide / show. Each entry runs
/// "HoodieCompanion.exe --cmd name", which forwards the command (with the click position) to the running
/// instance. On Windows 11 it appears under "Show more options" (Shift+F10), like every classic menu entry.
/// </summary>
public static class DesktopMenuService
{
    private const string Root = @"Software\Classes\DesktopBackground\Shell\HoodieCompanion";

    public static readonly (string Id, string Label)[] Commands =
    {
        ("comehere", "Come here"),
        ("stayhere", "Stay here"),
        ("sethome", "Set Home here"),
        ("panel", "Open Hoodie's panel"),
        ("hide", "Hide / show Hoodie"),
    };

    public static void Register(Func<string, string> translate)
    {
        try
        {
            var exe = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
            using var key = Registry.CurrentUser.CreateSubKey(Root);
            key.SetValue("MUIVerb", "Hoodie");
            key.SetValue("SubCommands", "");
            key.SetValue("Icon", $"\"{exe}\",0");
            key.SetValue("Position", "Bottom");
            var i = 1;
            foreach (var (id, label) in Commands)
            {
                using var sub = key.CreateSubKey($@"shell\{i:00}{id}");
                sub.SetValue("MUIVerb", translate(label));
                using var cmd = sub.CreateSubKey("command");
                cmd.SetValue("", $"\"{exe}\" --cmd {id}");
                i++;
            }
        }
        catch (Exception ex)
        {
            Log.Error("desktop menu register", ex);
        }
    }

    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(Root, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            Log.Error("desktop menu unregister", ex);
        }
    }
}
