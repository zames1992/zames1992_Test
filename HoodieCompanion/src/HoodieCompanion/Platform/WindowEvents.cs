using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HoodieCompanion.Companion.Perception;
using HoodieCompanion.Geometry;

namespace HoodieCompanion.Platform;

/// <summary>
/// Event-driven window perception through SetWinEventHook (out of context, no DLL injection): a window of
/// another app appeared, closed, was minimised, started/stopped being dragged, or came to the foreground.
/// Only the process name and the window rectangle are reported; titles and contents are never read.
/// Must be created on the UI thread (the hook callbacks arrive through its message loop).
/// </summary>
public sealed class WindowEvents : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_HIDE = 0x8003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    private const uint GA_ROOT = 2;

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private readonly WinEventProc _proc;
    private readonly List<IntPtr> _hooks = new();
    private readonly Action<WindowEvent> _sink;
    private readonly Dictionary<uint, string?> _names = new();
    // Remember which top-level windows we reported as shown, so hide/destroy of random internal windows is ignored.
    private readonly Dictionary<IntPtr, (string? Process, RectD Bounds)> _known = new();

    public WindowEvents(Action<WindowEvent> sink)
    {
        _sink = sink;
        _proc = OnEvent; // keep the delegate alive
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        Hook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND);
        Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART);
        Hook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE);
    }

    /// <summary>Fires for every accepted event (the surface scanner rescans at once).</summary>
    public event Action? Changed;

    /// <summary>True while the user drags a window (the scanner follows faster).</summary>
    public bool Dragging { get; private set; }

    private void Hook(uint min, uint max)
    {
        try
        {
            var h = SetWinEventHook(min, max, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (h != IntPtr.Zero) _hooks.Add(h);
        }
        catch (Exception ex)
        {
            Log.Debug("win event hook failed: " + ex.Message);
        }
    }

    private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != 0) return;
            switch (evt)
            {
                case EVENT_OBJECT_SHOW:
                {
                    if (!IsAppWindow(hwnd, out var proc, out var bounds)) return;
                    if (_known.ContainsKey(hwnd)) return;
                    if (_known.Count > 512) _known.Clear();
                    _known[hwnd] = (proc, bounds);
                    Emit(new WindowEvent(WindowEventKind.Opened, proc, bounds));
                    break;
                }
                case EVENT_OBJECT_HIDE:
                case EVENT_OBJECT_DESTROY:
                    if (!_known.Remove(hwnd, out var k)) return;
                    Emit(new WindowEvent(WindowEventKind.Closed, k.Process, k.Bounds));
                    break;
                case EVENT_SYSTEM_MINIMIZESTART:
                    if (!IsAppWindow(hwnd, out var p2, out var b2, requireVisible: false)) return;
                    Emit(new WindowEvent(WindowEventKind.Minimized, p2, b2));
                    break;
                case EVENT_SYSTEM_MOVESIZESTART:
                    Dragging = true;
                    if (IsAppWindow(hwnd, out var p3, out var b3)) Emit(new WindowEvent(WindowEventKind.MoveStarted, p3, b3));
                    break;
                case EVENT_SYSTEM_MOVESIZEEND:
                    Dragging = false;
                    if (IsAppWindow(hwnd, out var p4, out var b4))
                    {
                        if (_known.ContainsKey(hwnd)) _known[hwnd] = (p4, b4);
                        Emit(new WindowEvent(WindowEventKind.MoveEnded, p4, b4));
                    }
                    break;
                case EVENT_SYSTEM_FOREGROUND:
                    if (IsAppWindow(hwnd, out var p5, out var b5))
                    {
                        _known.TryAdd(hwnd, (p5, b5));
                        Emit(new WindowEvent(WindowEventKind.Foreground, p5, b5));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("win event: " + ex.Message);
        }
    }

    private void Emit(in WindowEvent e)
    {
        _sink(e);
        Changed?.Invoke();
    }

    /// <summary>A real, top-level, non-tool window of another process, of a reasonable size.</summary>
    private bool IsAppWindow(IntPtr h, out string? process, out RectD bounds, bool requireVisible = true)
    {
        process = null;
        bounds = default;
        if (GetAncestor(h, GA_ROOT) != h) return false;
        if (requireVisible && !IsWindowVisible(h)) return false;
        if (GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return false;
        var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
        if (!GetWindowRect(h, out var r) || r.Right - r.Left < 160 || r.Bottom - r.Top < 100) return false;
        GetWindowThreadProcessId(h, out var pid);
        if (pid == Environment.ProcessId) return false;
        process = Name(pid);
        bounds = new RectD(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return true;
    }

    private string? Name(uint pid)
    {
        if (_names.TryGetValue(pid, out var n)) return n;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            n = p.ProcessName;
        }
        catch
        {
            n = null;
        }
        if (_names.Count > 256) _names.Clear();
        _names[pid] = n;
        return n;
    }

    public void Dispose()
    {
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
    }
}
