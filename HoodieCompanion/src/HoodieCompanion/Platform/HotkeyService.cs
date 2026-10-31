using System;
using System.Collections.Generic;
using System.Windows.Interop;

namespace HoodieCompanion.Platform;

/// <summary>
/// Global hotkeys: Ctrl+Alt+H hides/shows Hoodie instantly, Ctrl+Alt+P turns manual control on and off.
/// Only these exact combinations are registered with Windows; no other key is ever seen.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    public const int HideId = 0x4843;   // "HC"
    public const int ManualId = 0x4850; // "HP"

    private readonly IntPtr _hwnd;
    private readonly HwndSource? _source;
    private readonly Dictionary<int, Action> _actions = new();
    private readonly HashSet<int> _registered = new();

    public HotkeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(Hook);
    }

    /// <summary>Registers one combination; false when another app already owns it.</summary>
    public bool Register(int id, uint modifiers, char key, Action onHotkey, string label)
    {
        _actions[id] = onHotkey;
        var ok = NativeMethods.RegisterHotKey(_hwnd, id, modifiers | NativeMethods.MOD_NOREPEAT, (uint)key);
        if (ok) _registered.Add(id);
        else Log.Info($"Hotkey {label} is used by another app.");
        return ok;
    }

    public bool IsRegistered(int id) => _registered.Contains(id);

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var act))
        {
            handled = true;
            act();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered) NativeMethods.UnregisterHotKey(_hwnd, id);
        _registered.Clear();
        _source?.RemoveHook(Hook);
    }
}
