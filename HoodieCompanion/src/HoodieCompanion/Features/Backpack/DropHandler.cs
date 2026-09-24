using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace HoodieCompanion.Features.Backpack;

/// <summary>Extracts things the user can hand to Hoodie from OLE drag data: files, folders, shortcuts, links.</summary>
public static class DropHandler
{
    public static bool CanAccept(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || ExtractUrl(data) is not null;

    public static IReadOnlyList<string> Extract(IDataObject data)
    {
        var result = new List<string>();
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            {
                result.AddRange(files.Where(f => !string.IsNullOrWhiteSpace(f)));
            }
            else if (ExtractUrl(data) is string url)
            {
                result.Add(url);
            }
        }
        catch
        {
            // Some sources throw for unsupported formats; ignore.
        }
        return result;
    }

    private static string? ExtractUrl(IDataObject data)
    {
        try
        {
            foreach (var fmt in new[] { "UniformResourceLocatorW", "UniformResourceLocator" })
            {
                if (!data.GetDataPresent(fmt)) continue;
                if (data.GetData(fmt) is MemoryStream ms)
                {
                    var bytes = ms.ToArray();
                    var s = fmt.EndsWith("W") ? Encoding.Unicode.GetString(bytes) : Encoding.ASCII.GetString(bytes);
                    s = s.TrimEnd('\0').Trim();
                    if (InventoryService.LooksLikeUrl(s)) return s;
                }
            }
            if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text)
            {
                var line = text.Split('\n').FirstOrDefault()?.Trim() ?? "";
                if (InventoryService.LooksLikeUrl(line)) return line;
            }
        }
        catch
        {
        }
        return null;
    }
}
