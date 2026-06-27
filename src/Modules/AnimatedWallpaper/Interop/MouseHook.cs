using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AnimatedDesktopBackground.Interop;

/// <summary>
/// Process-wide low-level mouse hook (WH_MOUSE_LL) running on its OWN dedicated thread with a
/// message loop. A WH_MOUSE_LL hook is serviced on the thread that installed it, so installing it
/// on the UI thread (which also pumps all the LibVLC players and the WebView2 wallpaper) made both
/// the cursor and rendering lag. On its own thread the hook returns instantly; consumers marshal
/// the work to the UI thread themselves (non-blocking).
/// </summary>
internal sealed class MouseHook : IDisposable
{
    public event Action<int, int>? Moved;     // screen x, y
    public event Action<int, int>? Pressed;
    public event Action<int, int>? Released;

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_QUIT = 0x0012;

    private readonly LowLevelMouseProc _proc; // keep alive for the hook's lifetime
    private readonly Thread _thread;
    private IntPtr _hook;
    private uint _threadId;

    public MouseHook()
    {
        _proc = HookProc;
        _thread = new Thread(ThreadProc) { IsBackground = true, Name = "WallpaperMouseHook" };
        _thread.Start();
    }

    private void ThreadProc()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);

        // Message loop — required for the low-level hook to receive callbacks.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case WM_MOUSEMOVE: Moved?.Invoke(data.pt.x, data.pt.y); break;
                case WM_LBUTTONDOWN: Pressed?.Invoke(data.pt.x, data.pt.y); break;
                case WM_LBUTTONUP: Released?.Invoke(data.pt.x, data.pt.y); break;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0)
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (_thread.IsAlive)
            _thread.Join(1000);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
}
