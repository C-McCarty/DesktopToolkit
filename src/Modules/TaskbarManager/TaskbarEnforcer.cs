using Toolkit.Common.Ipc;

namespace TaskbarManager;

/// <summary>
/// Invisible message-pump window that enforces the per-monitor hidden-taskbar state.
/// This is the original TrayForm logic in "managed mode": the tray icon, context menu,
/// global hotkey and standalone autostart are gone (the host owns those); what remains
/// is the valuable always-on enforcement — anti-flicker Win event hook, work-area
/// reclaim, and re-assertion on display change / Explorer restart. Control now arrives
/// over IPC from the host instead of from a tray menu.
/// </summary>
public sealed class TaskbarEnforcer : Form
{
    private readonly ModuleSettings _settings;
    private readonly System.Windows.Forms.Timer _reapplyTimer = new() { Interval = 1000 };
    private readonly uint _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private readonly HashSet<string> _appliedHidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NativeMethods.RECT> _savedWorkAreas = new(StringComparer.OrdinalIgnoreCase);

    private IntPtr _winEventHook;
    private NativeMethods.WinEventProc? _winEventProc;

    private ConfigForm? _configForm;

    public TaskbarEnforcer(ModuleSettings settings)
    {
        _settings = settings;

        // Never show a window; we only need a message pump + window handle.
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Minimized;
        Opacity = 0;

        _reapplyTimer.Tick += (_, _) => Reapply();
        _reapplyTimer.Start();

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
        {
            ReassertHiddenWorkAreas();
            Reapply();
        };
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated)
            CreateHandle();
        base.SetVisibleCore(false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetupHook();
        Reapply();
    }

    // ----------------------------------------------------------------- IPC

    /// <summary>Handle a host command. Called from the IPC thread; marshals to the UI thread.</summary>
    public IpcResponse HandleIpc(IpcCommand cmd)
    {
        if (!IsHandleCreated)
            return IpcResponse.Success();

        switch (cmd.Type)
        {
            case IpcCommandType.Ping:
                return IpcResponse.Success("pong");

            case IpcCommandType.Activate:
                Invoke(ShowConfig);
                return IpcResponse.Success();

            case IpcCommandType.ApplySettings:
                Invoke(() => { _settings.Load(); Reapply(); });
                return IpcResponse.Success();

            case IpcCommandType.Shutdown:
                Invoke(() => { RestoreAll(); Application.Exit(); });
                return IpcResponse.Success();

            default:
                return IpcResponse.Success();
        }
    }

    private void ShowConfig()
    {
        if (_configForm is null || _configForm.IsDisposed)
        {
            _configForm = new ConfigForm(_settings, onChanged: Reapply);
            _configForm.FormClosed += (_, _) => _configForm = null;
            _configForm.Show();
        }

        _configForm.WindowState = FormWindowState.Normal;
        _configForm.Activate();
        _configForm.BringToFront();
        // We're launched by the host (another process) which holds the foreground, so force
        // the window above the dashboard via a brief TopMost toggle.
        _configForm.TopMost = true;
        _configForm.TopMost = false;
    }

    // ------------------------------------------------------------- enforcement

    private void SetupHook()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }

        uint explorerPid = TaskbarController.GetTaskbarProcessId();
        if (explorerPid == 0)
            return;

        _winEventProc ??= OnWinEvent;
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_SHOW,
            IntPtr.Zero, _winEventProc, explorerPid, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        if (eventType != NativeMethods.EVENT_OBJECT_SHOW || idObject != NativeMethods.OBJID_WINDOW || hwnd == IntPtr.Zero)
            return;

        if (!TaskbarController.IsTaskbarClass(TaskbarController.GetClassName(hwnd)))
            return;

        string device = TaskbarController.GetDeviceNameFor(hwnd);
        if (device.Length > 0 && _settings.HiddenMonitors.Contains(device))
            TaskbarController.SetVisible(hwnd, false); // re-hide before it paints
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)_taskbarCreatedMsg)
        {
            ResetAppliedState();
            SetupHook();
            Reapply();
        }

        base.WndProc(ref m);
    }

    private void Reapply()
    {
        Dictionary<string, TaskbarInfo> live = TaskbarController.Enumerate()
            .Where(b => !string.IsNullOrEmpty(b.DeviceName))
            .GroupBy(b => b.DeviceName)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (string device in _settings.HiddenMonitors)
        {
            if (!live.TryGetValue(device, out TaskbarInfo? tb))
                continue;

            if (_appliedHidden.Contains(device))
            {
                if (TaskbarController.IsVisible(tb.Handle))
                    TaskbarController.SetVisible(tb.Handle, false);
                if (!TaskbarController.WorkAreaIsFull(tb.Handle)
                    && TaskbarController.TryGetMonitorInfo(tb.Handle, out NativeMethods.RECT mon, out _))
                    TaskbarController.SetWorkArea(mon, sendChange: false);
                continue;
            }

            if (TaskbarController.TryGetMonitorInfo(tb.Handle, out NativeMethods.RECT monitor, out NativeMethods.RECT work))
            {
                _savedWorkAreas[device] = work;
                TaskbarController.SetVisible(tb.Handle, false);
                TaskbarController.SetWorkArea(monitor, sendChange: false);
                TaskbarController.ReflowMaximizedWindows(TaskbarController.GetMonitorHandle(tb.Handle));
                _appliedHidden.Add(device);
            }
        }

        foreach (string device in _appliedHidden.ToList())
        {
            if (_settings.HiddenMonitors.Contains(device))
                continue;

            if (live.TryGetValue(device, out TaskbarInfo? tb))
                TaskbarController.SetVisible(tb.Handle, true);

            if (_savedWorkAreas.Remove(device, out NativeMethods.RECT original))
                TaskbarController.SetWorkArea(original, sendChange: true);

            _appliedHidden.Remove(device);
        }
    }

    private void ReassertHiddenWorkAreas()
    {
        Dictionary<string, TaskbarInfo> live = TaskbarController.Enumerate()
            .Where(b => !string.IsNullOrEmpty(b.DeviceName))
            .GroupBy(b => b.DeviceName)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (string device in _appliedHidden)
        {
            if (live.TryGetValue(device, out TaskbarInfo? tb)
                && TaskbarController.TryGetMonitorInfo(tb.Handle, out NativeMethods.RECT monitor, out _))
            {
                TaskbarController.SetWorkArea(monitor, sendChange: false);
                TaskbarController.ReflowMaximizedWindows(TaskbarController.GetMonitorHandle(tb.Handle));
            }
        }
    }

    private void ResetAppliedState()
    {
        foreach (NativeMethods.RECT original in _savedWorkAreas.Values)
            TaskbarController.SetWorkArea(original, sendChange: true);

        _savedWorkAreas.Clear();
        _appliedHidden.Clear();
    }

    /// <summary>Show every taskbar we hid and restore its work area — used on shutdown so a
    /// monitor is never left without a taskbar. Saved preferences are left untouched.</summary>
    private void RestoreAll()
    {
        Dictionary<string, TaskbarInfo> live = TaskbarController.Enumerate()
            .Where(b => !string.IsNullOrEmpty(b.DeviceName))
            .GroupBy(b => b.DeviceName)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (string device in _appliedHidden.ToList())
        {
            if (live.TryGetValue(device, out TaskbarInfo? tb))
                TaskbarController.SetVisible(tb.Handle, true);
            if (_savedWorkAreas.Remove(device, out NativeMethods.RECT original))
                TaskbarController.SetWorkArea(original, sendChange: true);
            _appliedHidden.Remove(device);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_winEventHook != IntPtr.Zero)
                NativeMethods.UnhookWinEvent(_winEventHook);
            _reapplyTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
