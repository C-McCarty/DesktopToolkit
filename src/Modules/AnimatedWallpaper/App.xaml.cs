using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AnimatedDesktopBackground.Interop;
using AnimatedDesktopBackground.Playback;
using AnimatedDesktopBackground.Ui;
using AnimatedDesktopBackground.Wallpaper;
using Microsoft.Win32;
using Toolkit.Common.Ipc;

namespace AnimatedDesktopBackground;

/// <summary>
/// Application entry point and central controller. In managed mode there is no tray icon
/// (the suite host owns the single tray); the app lives headless, drives the wallpaper
/// windows, and exposes control over an IPC <see cref="ModuleServer"/>: Activate shows the
/// manager GUI, Identify flashes display numbers, Shutdown exits. The WorkerW/LibVLC
/// rendering path is unchanged.
/// </summary>
public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;

    private SettingsService _settingsService = null!;
    private WallpaperManager _wallpaper = null!;
    private FullscreenDetector _fullscreen = null!;
    private ModuleServer _server = null!;
    private ManagerWindow? _manager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "DesktopToolkit.AnimatedWallpaper.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        // Headless tray app: keep running with no visible window until the host asks.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settingsService = new SettingsService();
        var settings = _settingsService.Load();

        // Warm up LibVLC off the UI thread (first init is slow).
        Task.Run(() => { try { LibVlcRuntime.EnsureInitialized(); } catch { /* surfaced on play */ } });

        _wallpaper = new WallpaperManager(_settingsService);

        _fullscreen = new FullscreenDetector();
        _fullscreen.FullscreenChanged += OnFullscreenChanged;
        _fullscreen.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // The host owns "start with Windows"; do not touch the Run key here.

        // Start the IPC server BEFORE playing — the first cold LibVLC init / WorkerW
        // attach can block the UI thread for several seconds, and the host must still be
        // able to reach us (ping, shutdown) during that window.
        _server = new ModuleServer("DesktopToolkit.animated-wallpaper", HandleCommand);
        _server.Start();

        if (settings.PlayOnStartup)
            _wallpaper.Start();
    }

    // ----------------------------------------------------------------- IPC

    private IpcResponse HandleCommand(IpcCommand cmd)
    {
        switch (cmd.Type)
        {
            case IpcCommandType.Ping:
                return IpcResponse.Success("pong");

            case IpcCommandType.Activate:
                Dispatcher.Invoke(ShowManager);
                return IpcResponse.Success();

            case IpcCommandType.Identify:
                Dispatcher.Invoke(MonitorIdentifier.Show);
                return IpcResponse.Success();

            case IpcCommandType.ApplySettings:
                Dispatcher.Invoke(() =>
                {
                    if (_wallpaper.IsRunning) _wallpaper.ApplySettings();
                });
                return IpcResponse.Success();

            case IpcCommandType.Shutdown:
                Dispatcher.Invoke(ExitApp);
                return IpcResponse.Success();

            default:
                return IpcResponse.Success();
        }
    }

    private void OnFullscreenChanged(bool isFullscreen)
    {
        if (!_settingsService.Settings.PauseOnFullscreen || !_wallpaper.IsRunning) return;
        if (isFullscreen) _wallpaper.Pause();
        else _wallpaper.Resume();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _wallpaper.Rebuild();
        _manager?.RefreshMonitors();
    }

    public void ShowManager()
    {
        if (_manager == null)
        {
            _manager = new ManagerWindow(_settingsService, _wallpaper);
            _manager.Closed += (_, _) => _manager = null;
        }
        if (!_manager.IsVisible) _manager.Show();
        _manager.WindowState = WindowState.Normal;

        // The host (a different process) owns the foreground when it launches us via IPC,
        // so Activate() alone leaves us behind its dashboard. Toggling Topmost forces the
        // window above the others regardless of foreground-lock rules.
        _manager.Activate();
        _manager.Topmost = true;
        _manager.Topmost = false;
        _manager.Focus();
    }

    /// <summary>Kept as a no-op so the manager window's existing calls compile unchanged
    /// (there is no tray to update in managed mode).</summary>
    public void UpdateTrayState() { }

    private void ExitApp()
    {
        _settingsService.Save();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _server?.Dispose();
            _fullscreen?.Dispose();
            _wallpaper?.Dispose();
            _settingsService?.Save();
        }
        finally
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        base.OnExit(e);
    }
}
