using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HoodieCompanion.Platform;

/// <summary>
/// Minimal Windows shell interop: shell items (Recycle Bin, This PC, Store apps) dragged from the desktop,
/// display names, and thumbnails/icons through IShellItemImageFactory (image previews for pictures).
/// </summary>
public static class ShellInterop
{
    private const uint SIGDN_NORMALDISPLAY = 0x00000000;
    private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, out IntPtr ppszName);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int c, ref BITMAP pv);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx, cy;
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig]
        int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem rgelt, out uint pceltFetched);
    }

    private static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    private const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;

    /// <summary>
    /// Every app Windows lists in Start (desktop and Store apps, e.g. Calculator), as
    /// (display name, "shell:AppsFolder\AUMID"). Must run on an STA thread.
    /// </summary>
    public static List<(string Name, string Target)> EnumerateApps()
    {
        var result = new List<(string, string)>();
        var folder = Item("shell:AppsFolder");
        if (folder is null) return result;
        try
        {
            folder.BindToHandler(IntPtr.Zero, BHID_EnumItems, typeof(IEnumShellItems).GUID, out var ptr);
            if (ptr == IntPtr.Zero) return result;
            var e = (IEnumShellItems)Marshal.GetObjectForIUnknown(ptr);
            Marshal.Release(ptr);
            while (e.Next(1, out var it, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    it.GetDisplayName(SIGDN_NORMALDISPLAY, out var pn);
                    var name = Marshal.PtrToStringUni(pn);
                    Marshal.FreeCoTaskMem(pn);
                    it.GetDisplayName(SIGDN_PARENTRELATIVEPARSING, out var pp);
                    var id = Marshal.PtrToStringUni(pp);
                    Marshal.FreeCoTaskMem(pp);
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) result.Add((name!, "shell:AppsFolder\\" + id));
                }
                catch
                {
                }
                finally
                {
                    Marshal.ReleaseComObject(it);
                }
            }
            Marshal.ReleaseComObject(e);
        }
        catch (Exception ex)
        {
            Log.Debug("apps folder enumeration failed: " + ex.Message);
        }
        finally
        {
            Marshal.ReleaseComObject(folder);
        }
        return result;
    }

    public const string ShellIdListFormat = "Shell IDList Array";

    /// <summary>
    /// Reads the "Shell IDList Array" (CIDA) drag format and returns absolute parsing names. File-system
    /// items come back as paths, virtual objects as "::{GUID}" (the Recycle Bin is ::{645FF040-...}).
    /// </summary>
    public static List<string> ParseIdList(MemoryStream stream)
    {
        var result = new List<string>();
        var bytes = stream.ToArray();
        if (bytes.Length < 8) return result;
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var basePtr = handle.AddrOfPinnedObject();
            var count = Marshal.ReadInt32(basePtr);
            if (count <= 0 || count > 256) return result;
            var parentOffset = Marshal.ReadInt32(basePtr, 4);
            var parent = basePtr + parentOffset;
            for (var i = 0; i < count; i++)
            {
                var off = Marshal.ReadInt32(basePtr, 8 + 4 * i);
                var child = basePtr + off;
                var abs = ILCombine(parent, child);
                if (abs == IntPtr.Zero) continue;
                try
                {
                    if (SHGetNameFromIDList(abs, SIGDN_DESKTOPABSOLUTEPARSING, out var name) == 0 && name != IntPtr.Zero)
                    {
                        var s = Marshal.PtrToStringUni(name);
                        Marshal.FreeCoTaskMem(name);
                        if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
                    }
                }
                finally
                {
                    ILFree(abs);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("CIDA parse failed: " + ex.Message);
        }
        finally
        {
            handle.Free();
        }
        return result;
    }

    private static IShellItem? Item(string parsingName)
    {
        try
        {
            var name = parsingName.StartsWith("::{", StringComparison.Ordinal) ? "shell:" + parsingName : parsingName;
            SHCreateItemFromParsingName(name, IntPtr.Zero, typeof(IShellItem).GUID, out var obj);
            return obj as IShellItem;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Localised display name of a shell object ("Recycle Bin", "Calculator"...).</summary>
    public static string? DisplayName(string parsingName)
    {
        var item = Item(parsingName);
        if (item is null) return null;
        try
        {
            item.GetDisplayName(SIGDN_NORMALDISPLAY, out var p);
            var s = Marshal.PtrToStringUni(p);
            Marshal.FreeCoTaskMem(p);
            return s;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    /// <summary>
    /// Thumbnail (pictures, videos, documents with previews) or icon for anything the shell knows,
    /// as a frozen premultiplied BGRA bitmap. Null when unavailable.
    /// </summary>
    public static BitmapSource? Image(string parsingName, int size)
    {
        var item = Item(parsingName);
        if (item is null) return null;
        IntPtr hbm = IntPtr.Zero;
        try
        {
            if (item is not IShellItemImageFactory f) return null;
            // SIIGBF_RESIZETOFIT (0) returns the thumbnail when there is one, otherwise the icon.
            if (f.GetImage(new SIZE { cx = size, cy = size }, 0, out hbm) != 0 || hbm == IntPtr.Zero) return null;
            return FromHBitmap(hbm);
        }
        catch (Exception ex)
        {
            Log.Debug("shell image failed: " + ex.Message);
            return null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
            Marshal.ReleaseComObject(item);
        }
    }

    private static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        var bm = new BITMAP();
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0) return null;
        var w = bm.bmWidth;
        var h = bm.bmHeight;
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var bits = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbm, 0, (uint)h, bits, ref bmi, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
        // An all-black, fully transparent result means the shell gave us nothing useful.
        var anyColor = false;
        for (var i = 0; i < bits.Length; i++)
        {
            if ((i & 3) != 3 && bits[i] != 0) { anyColor = true; break; }
        }
        if (!anyColor) return null;
        // Thumbnails of photos come without alpha (all zero): treat them as opaque.
        var anyAlpha = false;
        for (var i = 3; i < bits.Length; i += 4)
        {
            if (bits[i] != 0) { anyAlpha = true; break; }
        }
        if (!anyAlpha) for (var i = 3; i < bits.Length; i += 4) bits[i] = 255;
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bits, w * 4);
        src.Freeze();
        return src;
    }
}
