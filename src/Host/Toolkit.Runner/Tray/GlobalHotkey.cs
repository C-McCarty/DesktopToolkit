using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Toolkit.Runner.Tray;

/// <summary>
/// A process-wide hotkey backed by a message-only window. Lets the dashboard be opened
/// even when the host's tray icon is unreachable — e.g. after Taskbar Manager hides the
/// primary monitor's taskbar (which also hides the notification area).
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly int _id;
    private readonly HwndSource _source;
    private readonly Action _onPressed;
    private bool _registered;

    /// <param name="virtualKey">Virtual-key code, e.g. 0x44 for 'D'.</param>
    public GlobalHotkey(uint virtualKey, Action onPressed, int id = 0xB0B1)
    {
        _id = id;
        _onPressed = onPressed;

        // HWND_MESSAGE (-3) makes this a message-only window: no UI, just a message pump.
        var parameters = new HwndSourceParameters("DesktopToolkit.Hotkey")
        {
            ParentWindow = new IntPtr(-3),
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _registered = RegisterHotKey(_source.Handle, _id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, virtualKey);
    }

    /// <summary>True if the OS accepted the hotkey (it may be taken by another app).</summary>
    public bool IsRegistered => _registered;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            _onPressed();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, _id);
            _registered = false;
        }
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
