using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HoodieCompanion.Features.Backpack;

namespace HoodieCompanion.Platform;

/// <summary>Native Windows shell icons for Backpack items, cached as PNG files. Fallbacks are fine.</summary>
public sealed class ShellIconService
{
    private readonly string _cacheDir;
    private readonly Dictionary<string, ImageSource?> _memory = new();

    public ShellIconService(string root)
    {
        _cacheDir = Path.Combine(root, "icons");
        try { Directory.CreateDirectory(_cacheDir); } catch { }
    }

    public ImageSource? Get(InventoryItem item)
    {
        if (_memory.TryGetValue(item.Id, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            if (item.IconCache is not null && item.IconCache.EndsWith(CacheSuffix, StringComparison.Ordinal) && File.Exists(item.IconCache))
            {
                img = LoadPng(item.IconCache);
            }
            else if (item.Type != InventoryItemType.Url)
            {
                // Thumbnails for pictures/videos/documents, large icons for everything else (incl. Recycle Bin, Store apps).
                img = ShellInterop.Image(item.Target, 96) ?? Extract(item.Target, item.Type == InventoryItemType.Folder);
                if (img is BitmapSource bmp)
                {
                    var path = Path.Combine(_cacheDir, item.Id + CacheSuffix);
                    using var fs = File.Create(path);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    enc.Save(fs);
                    item.IconCache = path;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("icon failed for " + item.Target + ": " + ex.Message);
        }
        _memory[item.Id] = img;
        return img;
    }

    private const string CacheSuffix = "-v2.png";

    public void Forget(string id) => _memory.Remove(id);

    /// <summary>Icon for something not (yet) in the backpack, e.g. an app in search results. Not cached on disk.</summary>
    public ImageSource? Preview(string parsingName)
    {
        if (_memory.TryGetValue("preview:" + parsingName, out var c)) return c;
        ImageSource? img = null;
        try { img = ShellInterop.Image(parsingName, 48); } catch { }
        _memory["preview:" + parsingName] = img;
        return img;
    }

    private static ImageSource LoadPng(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static ImageSource? Extract(string path, bool folder)
    {
        var info = new NativeMethods.SHFILEINFO();
        var exists = File.Exists(path) || Directory.Exists(path);
        var flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON | (exists ? 0 : NativeMethods.SHGFI_USEFILEATTRIBUTES);
        var attrs = folder ? NativeMethods.FILE_ATTRIBUTE_DIRECTORY : NativeMethods.FILE_ATTRIBUTE_NORMAL;
        var res = NativeMethods.SHGetFileInfo(path, attrs, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf(info), flags);
        if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }
}
