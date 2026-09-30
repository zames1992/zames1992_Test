using System;
using System.Windows.Interop;

namespace HoodieCompanion.Platform;

/// <summary>Global emergency hotkey (Ctrl+Alt+H by default) to hide/show Hoodie instantly.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int Id = 0x4843; // "HC"
    private readonly IntPtr _hwnd;
    private readonly HwndSource? _source;
    private readonly Action _onHotkey;
    private bool _registered;

    public HotkeyService(IntPtr hwnd, Action onHotkey)
    {
        _hwnd = hwnd;
        _onHotkey = onHotkey;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(Hook);
        _registered = NativeMethods.RegisterHotKey(hwnd, Id, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, (uint)'H');
        if (!_registered) Log.Info("Emergency hotkey Ctrl+Alt+H is used by another app; tray Hide still works.");
    }

    public bool IsRegistered => _registered;

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            _onHotkey();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered) NativeMethods.UnregisterHotKey(_hwnd, Id);
        _registered = false;
        _source?.RemoveHook(Hook);
    }
}
