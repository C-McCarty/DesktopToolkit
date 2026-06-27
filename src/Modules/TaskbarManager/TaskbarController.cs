using System.Runtime.InteropServices;

namespace TaskbarManager;

/// <summary>A single taskbar window bound to the GDI device name of its monitor.</summary>
public sealed class TaskbarInfo
{
    public required IntPtr Handle { get; init; }

    /// <summary>GDI device name, e.g. "\\.\DISPLAY5". Matches <see cref="System.Windows.Forms.Screen.DeviceName"/>.</summary>
    public required string DeviceName { get; init; }

    public required bool IsPrimary { get; init; }
}

/// <summary>
/// Finds the Explorer taskbar windows and shows/hides them per monitor.
/// Primary monitor uses the "Shell_TrayWnd" window; each secondary monitor
/// (when "Show taskbar on all displays" is on) gets a "Shell_SecondaryTrayWnd".
/// </summary>
public static class TaskbarController
{
    public static List<TaskbarInfo> Enumerate()
    {
        var list = new List<TaskbarInfo>();

        IntPtr primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero)
        {
            list.Add(new TaskbarInfo
            {
                Handle = primary,
                DeviceName = GetDeviceName(primary),
                IsPrimary = true,
            });
        }

        IntPtr child = IntPtr.Zero;
        while ((child = NativeMethods.FindWindowEx(IntPtr.Zero, child, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            list.Add(new TaskbarInfo
            {
                Handle = child,
                DeviceName = GetDeviceName(child),
                IsPrimary = false,
            });
        }

        return list;
    }

    public static void SetVisible(IntPtr hwnd, bool visible)
        => NativeMethods.ShowWindow(hwnd, visible ? NativeMethods.SW_SHOWNA : NativeMethods.SW_HIDE);

    public static bool IsVisible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);

    /// <summary>Reads the monitor bounds and current work area of the monitor a window sits on.</summary>
    public static bool TryGetMonitorInfo(IntPtr hwnd, out NativeMethods.RECT monitor, out NativeMethods.RECT work)
    {
        monitor = default;
        work = default;

        IntPtr hmon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONULL);
        if (hmon == IntPtr.Zero)
            return false;

        var mi = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(hmon, ref mi))
            return false;

        monitor = mi.rcMonitor;
        work = mi.rcWork;
        return true;
    }

    /// <summary>
    /// Sets the work area of the monitor that contains <paramref name="rect"/>.
    /// When reclaiming space (expanding to full bounds) pass <paramref name="sendChange"/> = false,
    /// otherwise Explorer reacts to the broadcast by re-reserving the taskbar strip. When restoring,
    /// pass true so apps reflow and Explorer recomputes the correct work area.
    /// </summary>
    public static void SetWorkArea(NativeMethods.RECT rect, bool sendChange)
    {
        var copy = rect;
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETWORKAREA, 0, ref copy,
            sendChange ? NativeMethods.SPIF_SENDCHANGE : 0);
    }

    public static IntPtr GetMonitorHandle(IntPtr hwnd)
        => NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONULL);

    /// <summary>True when the monitor's current work area already fills the whole monitor.</summary>
    public static bool WorkAreaIsFull(IntPtr hwnd)
        => TryGetMonitorInfo(hwnd, out NativeMethods.RECT mon, out NativeMethods.RECT work)
           && mon.Left == work.Left && mon.Top == work.Top && mon.Right == work.Right && mon.Bottom == work.Bottom;

    /// <summary>
    /// Re-maximizes every maximized top-level window on the given monitor so it
    /// picks up a freshly expanded work area (a maximized window keeps its old
    /// bounds until it is restored and maximized again).
    /// </summary>
    public static void ReflowMaximizedWindows(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero)
            return;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && NativeMethods.IsZoomed(hwnd)
                && NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONULL) == hMonitor)
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
            }

            return true; // keep enumerating
        }, IntPtr.Zero);
    }

    public static string GetClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        int len = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return len > 0 ? new string(buffer, 0, len) : string.Empty;
    }

    public static bool IsTaskbarClass(string className)
        => className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    public static string GetDeviceNameFor(IntPtr hwnd) => GetDeviceName(hwnd);

    /// <summary>Process id that owns the taskbars (Explorer), or 0 if not found.</summary>
    public static uint GetTaskbarProcessId()
    {
        IntPtr primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary == IntPtr.Zero)
            return 0;

        NativeMethods.GetWindowThreadProcessId(primary, out uint pid);
        return pid;
    }

    private static string GetDeviceName(IntPtr hwnd)
    {
        IntPtr hmon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONULL);
        if (hmon == IntPtr.Zero)
            return string.Empty;

        var mi = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        return NativeMethods.GetMonitorInfo(hmon, ref mi) ? mi.szDevice : string.Empty;
    }
}
