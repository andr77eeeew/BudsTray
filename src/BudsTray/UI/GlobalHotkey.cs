using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using BudsTray.Core;

namespace BudsTray.UI;

/// <summary>Held only while the buds are connected, so the shortcut stays free for other programs otherwise.</summary>
public sealed class GlobalHotkey : IDisposable
{
    public const string Label = "CTRL+ALT+B";

    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint KeyB = 0x42;
    private static readonly IntPtr MessageOnlyParent = new(-3);

    private readonly HwndSource _window;

    public GlobalHotkey()
    {
        _window = new HwndSource(new HwndSourceParameters("BudsTray.Hotkey") { WindowStyle = 0, ParentWindow = MessageOnlyParent });
        _window.AddHook(OnMessage);
    }

    public event Action? Pressed;

    public bool IsRegistered { get; private set; }

    public void Register()
    {
        if (IsRegistered)
            return;
        IsRegistered = RegisterHotKey(_window.Handle, HotkeyId, ModControl | ModAlt | ModNoRepeat, KeyB);
        if (!IsRegistered)
            Log.Info($"{Label} is taken by another program");
    }

    public void Unregister()
    {
        if (IsRegistered)
            UnregisterHotKey(_window.Handle, HotkeyId);
        IsRegistered = false;
    }

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }

    private IntPtr OnMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
