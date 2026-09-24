using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using HoodieCompanion.Companion.Behavior;

namespace HoodieCompanion.Platform;

/// <summary>
/// Finds platforms Hoodie can stand on: the visible parts of window top edges (z-order occlusion
/// applied) and the tops of desktop icons (through the desktop's IFolderView, the documented way to read
/// icon positions). Runs on its own STA thread; results are delivered on the UI dispatcher.
/// Read-only: never moves, activates or changes any window or icon.
/// </summary>
public sealed class SurfaceScanner : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<IReadOnlyList<Surface>> _deliver;
    private readonly Thread _thread;
    private readonly int _ownPid = Environment.ProcessId;
    private volatile bool _stop;
    private List<Surface> _icons = new();
    private DateTime _nextIconScan;

    public SurfaceScanner(Dispatcher dispatcher, Action<IReadOnlyList<Surface>> deliver)
    {
        _dispatcher = dispatcher;
        _deliver = deliver;
        _thread = new Thread(Loop) { IsBackground = true, Name = "surface-scanner" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Set false to pause scanning (e.g. while hidden).</summary>
    public bool Enabled { get; set; } = true;

    private void Loop()
    {
        while (!_stop)
        {
            try
            {
                if (Enabled)
                {
                    var windows = TopLevelWindows();
                    if (DateTime.UtcNow >= _nextIconScan)
                    {
                        _nextIconScan = DateTime.UtcNow.AddSeconds(3);
                        _icons = DesktopIcons();
                    }
                    var surfaces = WindowSurfaces(windows);
                    foreach (var icon in _icons)
                    {
                        // Icons hidden behind a window are not reachable.
                        var mid = (icon.Left + icon.Right) / 2;
                        if (windows.Any(w => w.Left <= mid && w.Right >= mid && w.Top <= icon.Y - 6 && w.Bottom >= icon.Y)) continue;
                        surfaces.Add(icon);
                    }
                    _dispatcher.BeginInvoke(() => _deliver(surfaces));
                }
            }
            catch (Exception ex)
            {
                Log.Debug("surface scan failed: " + ex.Message);
            }
            Thread.Sleep(350);
        }
    }

    // ------------------------------------------------------------------ windows

    private readonly record struct Win(IntPtr Hwnd, double Left, double Top, double Right, double Bottom);

    private static readonly HashSet<string> SkipClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "Xaml_WindowedPopupClass",
    };

    /// <summary>Visible top-level windows of other apps, topmost first (EnumWindows order is z-order).</summary>
    private List<Win> TopLevelWindows()
    {
        var list = new List<Win>();
        var cls = new StringBuilder(128);
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == _ownPid) return true;
            var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return true;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
            cls.Clear();
            GetClassName(h, cls, cls.Capacity);
            if (SkipClasses.Contains(cls.ToString())) return true;
            RECT r;
            if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(h, out r)) return true;
            if (r.Right - r.Left < 120 || r.Bottom - r.Top < 60) return true;
            list.Add(new Win(h, r.Left, r.Top, r.Right, r.Bottom));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Top edges of windows minus everything above them in z-order; maximised windows are skipped.</summary>
    private static List<Surface> WindowSurfaces(List<Win> windows)
    {
        var result = new List<Surface>();
        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            if (IsZoomed(w.Hwnd)) continue;
            var y = w.Top;
            var segments = new List<(double L, double R)> { (w.Left + 6, w.Right - 6) };
            for (var j = 0; j < i && segments.Count > 0; j++)
            {
                var o = windows[j];
                // A higher window covering the edge line (or the strip just above it) hides that part.
                if (o.Top > y + 2 || o.Bottom < y - 8) continue;
                segments = Subtract(segments, o.Left, o.Right);
            }
            var n = 0;
            foreach (var (l, r) in segments)
            {
                if (r - l < 90) continue;
                result.Add(new Surface($"w{w.Hwnd.ToInt64():X}:{n++}", SurfaceKind.Window, l, r, y));
            }
        }
        return result;
    }

    private static List<(double L, double R)> Subtract(List<(double L, double R)> segs, double l, double r)
    {
        var outList = new List<(double, double)>();
        foreach (var (a, b) in segs)
        {
            if (r <= a || l >= b) { outList.Add((a, b)); continue; }
            if (l > a) outList.Add((a, l));
            if (r < b) outList.Add((r, b));
        }
        return outList;
    }

    // ------------------------------------------------------------------ desktop icons

    private static List<Surface> DesktopIcons()
    {
        var result = new List<Surface>();
        object? sw = null;
        try
        {
            var t = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (t is null) return result;
            sw = Activator.CreateInstance(t);
            if (sw is not IShellWindows windows) return result;
            object loc = CSIDL_DESKTOP;
            object empty = null!;
            var disp = windows.FindWindowSW(ref loc, ref empty, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
            if (disp is not IServiceProvider sp) return result;
            var sid = SID_STopLevelBrowser;
            var iid = typeof(IShellBrowser).GUID;
            if (sp.QueryService(ref sid, ref iid, out var browserPtr) != 0 || browserPtr == IntPtr.Zero) return result;
            var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(browserPtr);
            Marshal.Release(browserPtr);
            if (browser.QueryActiveShellView(out var viewObj) != 0 || viewObj is not IFolderView view) return result;
            if (viewObj is not IOleWindow ole || ole.GetWindow(out var defView) != 0) return result;
            var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            if (listView == IntPtr.Zero || !IsWindowVisible(listView)) return result;
            var spacing = new POINT();
            view.GetSpacing(ref spacing);
            if (spacing.X <= 0) spacing.X = 75;
            var iidEnum = typeof(IEnumIDList).GUID;
            if (view.Items(SVGIO_ALLVIEW, ref iidEnum, out var enumPtr) != 0 || enumPtr == IntPtr.Zero) return result;
            var en = (IEnumIDList)Marshal.GetObjectForIUnknown(enumPtr);
            Marshal.Release(enumPtr);
            var index = 0;
            while (en.Next(1, out var pidl, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    if (view.GetItemPosition(pidl, out var pt) != 0) continue;
                    ClientToScreen(listView, ref pt);
                    var inset = spacing.X * 0.18;
                    result.Add(new Surface($"i{index}", SurfaceKind.Icon, pt.X + inset, pt.X + spacing.X - inset, pt.Y + 3));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                    index++;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("desktop icons unavailable: " + ex.Message);
        }
        finally
        {
            if (sw is not null && Marshal.IsComObject(sw)) Marshal.ReleaseComObject(sw);
        }
        return result;
    }

    public void Dispose() => _stop = true;

    // ------------------------------------------------------------------ interop

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    private const int CSIDL_DESKTOP = 0, SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_ALLVIEW = 2;
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        void _Count();
        void _Item();
        void _NewEnum();
        void _Register();
        void _RegisterPending();
        void _Revoke();
        void _OnNavigate();
        void _OnActivated();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW([In, MarshalAs(UnmanagedType.Struct)] ref object pvarLoc, [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot,
            int swClass, out int phwnd, int swfwOptions);
    }

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleWindow
    {
        [PreserveSig]
        int GetWindow(out IntPtr phwnd);
        void ContextSensitiveHelp(bool enter);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void _GetWindow();
        void _ContextSensitiveHelp();
        void _InsertMenusSB();
        void _SetMenuSB();
        void _RemoveMenusSB();
        void _SetStatusTextSB();
        void _EnableModelessSB();
        void _TranslateAcceleratorSB();
        void _BrowseObject();
        void _GetViewStateStream();
        void _GetControlWindow();
        void _SendControlMsg();
        [PreserveSig]
        int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object ppshv);
    }

    [ComImport, Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void _GetCurrentViewMode();
        void _SetCurrentViewMode();
        void _GetFolder();
        void _Item();
        void _ItemCount();
        [PreserveSig]
        int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
        void _GetSelectionMarkedItem();
        void _GetFocusedItem();
        [PreserveSig]
        int GetItemPosition(IntPtr pidl, out POINT ppt);
        [PreserveSig]
        int GetSpacing(ref POINT ppt);
    }

    [ComImport, Guid("000214F2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumIDList
    {
        [PreserveSig]
        int Next(uint celt, out IntPtr rgelt, out uint pceltFetched);
    }
}
