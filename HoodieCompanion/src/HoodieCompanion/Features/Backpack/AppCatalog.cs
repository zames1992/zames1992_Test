using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HoodieCompanion.Platform;

namespace HoodieCompanion.Features.Backpack;

/// <summary>
/// The apps installed on this PC, as Start lists them (desktop and Store apps such as Calculator), so they
/// can be put into the Backpack by searching. Loaded once in the background on an STA thread.
/// </summary>
public sealed class AppCatalog
{
    private List<(string Name, string Target)>? _apps;
    private Task? _loading;

    public bool IsLoaded => _apps is not null;

    public Task LoadAsync()
    {
        if (_loading is not null) return _loading;
        var tcs = new TaskCompletionSource();
        var t = new Thread(() =>
        {
            try
            {
                var apps = ShellInterop.EnumerateApps();
                if (apps.Count == 0) apps = StartMenuShortcuts();
                _apps = apps.GroupBy(a => a.Target, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                Log.Error("app catalog", ex);
                _apps = new List<(string, string)>();
            }
            tcs.SetResult();
        }) { IsBackground = true, Name = "AppCatalog" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        _loading = tcs.Task;
        return _loading;
    }

    public IReadOnlyList<(string Name, string Target)> Search(string query, int max = 12)
    {
        if (_apps is null) return Array.Empty<(string, string)>();
        var q = query.Trim();
        if (q.Length == 0) return _apps.Take(max).ToList();
        return _apps.Where(a => a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                    .OrderBy(a => a.Name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
                    .ThenBy(a => a.Name.Length)
                    .Take(max).ToList();
    }

    /// <summary>Fallback when the Apps folder cannot be enumerated: Start menu shortcuts.</summary>
    private static List<(string, string)> StartMenuShortcuts()
    {
        var list = new List<(string, string)>();
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                 })
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                foreach (var f in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    list.Add((Path.GetFileNameWithoutExtension(f), f));
            }
            catch
            {
            }
        }
        return list;
    }
}
