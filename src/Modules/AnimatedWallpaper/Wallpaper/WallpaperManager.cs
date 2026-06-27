using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using AnimatedDesktopBackground.Interop;
using AnimatedDesktopBackground.Models;

namespace AnimatedDesktopBackground.Wallpaper;

/// <summary>
/// Owns one <see cref="WallpaperWindow"/> per monitor, attaches them to the WorkerW, drives
/// playback, and re-attaches when the shell restarts or the display layout changes.
/// All methods must be called on the UI thread (the windows are pumped by the WPF dispatcher).
/// </summary>
internal sealed class WallpaperManager : IDisposable
{
    private readonly SettingsService _settings;
    private readonly List<WallpaperWindow> _windows = new();
    private readonly DispatcherTimer _watchdog;
    private MouseHook? _mouseHook;
    private long _lastMoveMs;
    private IntPtr _workerW;
    private bool _started;
    private bool _disposed;

    public WallpaperManager(SettingsService settings)
    {
        _settings = settings;
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _watchdog.Tick += (_, _) => CheckAttachment();
    }

    public bool IsPaused { get; private set; }
    public bool IsRunning => _started;

    /// <summary>Builds the wallpaper windows and starts playback.</summary>
    public void Start()
    {
        if (_disposed) return;
        Teardown();

        _workerW = DesktopWorkerW.GetWorkerW(out string method);
        Logger.Log($"[mgr] WorkerW=0x{_workerW.ToInt64():X} via {method}");
        if (_workerW == IntPtr.Zero) return;

        // Geometry diagnostics: compare the WorkerW client area against the physical monitors so
        // a sliver at a screen edge (e.g. the bottom row revealed when a taskbar is hidden) can be
        // localized — a child window is clipped to the WorkerW, so if WorkerW is short we see it.
        NativeMethods.GetWindowRect(_workerW, out var wwRect);
        Logger.Log($"[geo] WorkerW rect L{wwRect.Left} T{wwRect.Top} R{wwRect.Right} B{wwRect.Bottom} ({wwRect.Width}x{wwRect.Height})");

        var monitors = MonitorService.GetMonitors();
        foreach (var m in monitors)
            Logger.Log($"[geo] monitor {m.DeviceName} L{m.Bounds.Left} T{m.Bounds.Top} R{m.Bounds.Right} B{m.Bounds.Bottom}");
        foreach (var assignment in _settings.Settings.Assignments)
        {
            // Only place a window for assignments that have media on at least one live monitor;
            // unassigned monitors keep the normal wallpaper.
            if (assignment.MediaPath is not { Length: > 0 } path || !File.Exists(path))
                continue;

            var targets = monitors.Where(m => assignment.MonitorIds.Contains(m.DeviceName)).ToList();
            if (targets.Count == 0)
                continue;

            var bounds = WithOverscan(UnionBounds(targets)); // one window spanning the assignment
            var id = string.Join("+", assignment.MonitorIds);

            var win = new WallpaperWindow(id, bounds);
            win.Create(_workerW);
            win.SetMedia(path, assignment.Muted, assignment.FillMode, _settings.Settings.MediaEngine);
            Logger.Log($"[mgr] [{id}] window L{bounds.Left} T{bounds.Top} R{bounds.Right} B{bounds.Bottom} "
                       + $"({bounds.Width}x{bounds.Height}) child=0x{win.Hwnd.ToInt64():X} -> {Path.GetFileName(path)}");
            _windows.Add(win);
        }

        // Interactive web wallpapers need cursor events while behind the icons.
        if (_windows.Exists(w => w.IsWeb))
            SetupMouseHook();

        _started = true;
        IsPaused = false;
        _watchdog.Start();
    }

    private void SetupMouseHook()
    {
        // Hook events fire on the hook's own thread; throttle there (cheap) and marshal the
        // actual forward to the UI thread async so neither the system cursor nor rendering blocks.
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        _mouseHook = new MouseHook();
        _mouseHook.Moved += (x, y) =>
        {
            long now = Environment.TickCount64;
            if (now - _lastMoveMs < 16) return; // ~60 events/sec
            _lastMoveMs = now;
            dispatcher.BeginInvoke(() => { foreach (var w in _windows) w.ForwardPointer(x, y, "move"); });
        };
        _mouseHook.Pressed += (x, y) =>
            dispatcher.BeginInvoke(() => { foreach (var w in _windows) w.ForwardPointer(x, y, "down"); });
        _mouseHook.Released += (x, y) =>
            dispatcher.BeginInvoke(() => { foreach (var w in _windows) w.ForwardPointer(x, y, "up"); });
    }

    /// <summary>Stops playback and removes all wallpaper windows (restores the normal desktop).</summary>
    public void Stop()
    {
        _watchdog.Stop();
        Teardown();
        _started = false;
    }

    /// <summary>Recreates everything (used after a display change or shell restart).</summary>
    public void Rebuild()
    {
        if (!_started) return;
        Start();
    }

    /// <summary>Re-applies media assignments from settings by rebuilding the wallpaper windows.</summary>
    public void ApplySettings()
    {
        if (!_started) return;
        Start(); // Start() tears down and recreates from current settings.
    }

    public void Pause()
    {
        if (IsPaused) return;
        foreach (var w in _windows) w.Pause();
        IsPaused = true;
    }

    public void Resume()
    {
        if (!IsPaused) return;
        foreach (var w in _windows) w.Resume();
        IsPaused = false;
    }

    /// <summary>The smallest rect covering all of an assignment's monitors (virtual-desktop coords).</summary>
    private static NativeMethods.RECT UnionBounds(List<MonitorInfo> monitors) => new()
    {
        Left = monitors.Min(m => m.Bounds.Left),
        Top = monitors.Min(m => m.Bounds.Top),
        Right = monitors.Max(m => m.Bounds.Right),
        Bottom = monitors.Max(m => m.Bounds.Bottom),
    };

    private const int OverscanPx = 2;

    /// <summary>
    /// Extends the wallpaper rect a couple pixels past EVERY edge. The WorkerW clips the child at
    /// the virtual-desktop boundary, and adjacent monitors overlap by a few pixels — invisible
    /// when they share the same wallpaper. This pushes any decoder/compositor last-row/column
    /// artifact off every *visible* monitor edge, including the shared edges between stacked
    /// monitors (e.g. a top row's bottom edge) where a neighbour previously suppressed the overscan
    /// and a 1px sliver of the underlying wallpaper showed through once the taskbar was hidden.
    /// </summary>
    private static NativeMethods.RECT WithOverscan(NativeMethods.RECT rect) => new()
    {
        Left = rect.Left - OverscanPx,
        Top = rect.Top - OverscanPx,
        Right = rect.Right + OverscanPx,
        Bottom = rect.Bottom + OverscanPx,
    };

    /// <summary>If our windows were destroyed (explorer restart) or the WorkerW changed, rebuild.</summary>
    private void CheckAttachment()
    {
        if (!_started || _windows.Count == 0) return;

        bool needRebuild = false;
        foreach (var w in _windows)
        {
            if (!w.IsValid) { needRebuild = true; break; }
        }
        // Also rebuild if the WorkerW handle is gone.
        if (!needRebuild && !NativeMethods.IsWindow(_workerW))
            needRebuild = true;

        if (needRebuild)
            Rebuild();
    }

    private void Teardown()
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        foreach (var w in _windows) w.Dispose();
        _windows.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchdog.Stop();
        Teardown();
    }
}
