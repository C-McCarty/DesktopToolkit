using System;
using System.Windows.Threading;

namespace AnimatedDesktopBackground.Interop;

/// <summary>
/// Polls the foreground window once per second and raises <see cref="FullscreenChanged"/> when a
/// borderless, monitor-filling app (a game or video) takes/loses the foreground — used to
/// auto-pause wallpaper playback and save GPU.
/// </summary>
internal sealed class FullscreenDetector : IDisposable
{
    private readonly DispatcherTimer _timer;
    private bool _lastState;

    /// <summary>Raised with true when a fullscreen app is foreground, false when it clears.</summary>
    public event Action<bool>? FullscreenChanged;

    public FullscreenDetector()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void Poll()
    {
        bool fullscreen = IsForegroundFullscreen();
        if (fullscreen != _lastState)
        {
            _lastState = fullscreen;
            FullscreenChanged?.Invoke(fullscreen);
        }
    }

    private static bool IsForegroundFullscreen()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        // Ignore the desktop/shell so the wallpaper itself never counts.
        string cls = NativeMethods.GetClass(fg);
        if (cls is "Progman" or "WorkerW") return false;

        IntPtr hMon = NativeMethods.MonitorFromWindow(fg, NativeMethods.MONITOR_DEFAULTTONULL);
        if (hMon == IntPtr.Zero) return false;

        var mi = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(hMon, ref mi)) return false;

        if (!NativeMethods.GetWindowRect(fg, out var wr)) return false;

        // Foreground window covers the entire monitor (not just the work area).
        var m = mi.rcMonitor;
        return wr.Left <= m.Left && wr.Top <= m.Top && wr.Right >= m.Right && wr.Bottom >= m.Bottom;
    }

    public void Dispose() => _timer.Stop();
}
