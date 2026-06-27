using System.Windows;
using Toolkit.Common.Ipc;

namespace MonitorArrangement;

/// <summary>
/// Module entry point. Shows the window and runs an IPC <see cref="ModuleServer"/> so the
/// host can Activate (focus), push settings, trigger Identify, or request shutdown — the
/// "managed mode" that lets the unified shell drive this window module.
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;
    private ModuleServer? _server;
    private MainWindow? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true,
            "DesktopToolkit.MonitorArrangement.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Already running — let the host's Activate reach the live instance.
            Shutdown();
            return;
        }

        _main = new MainWindow();
        MainWindow = _main;
        _main.Show();

        _server = new ModuleServer("DesktopToolkit.monitor-arrangement", HandleCommand);
        _server.Start();
    }

    private IpcResponse HandleCommand(IpcCommand cmd)
    {
        switch (cmd.Type)
        {
            case IpcCommandType.Ping:
                return IpcResponse.Success("pong");

            case IpcCommandType.Activate:
                Dispatcher.Invoke(() => _main?.SurfaceWindow());
                return IpcResponse.Success();

            case IpcCommandType.Identify:
                Dispatcher.Invoke(() => _main?.IdentifyMonitors());
                return IpcResponse.Success();

            case IpcCommandType.ApplySettings:
                if (cmd.Settings is not null)
                    Dispatcher.Invoke(() => _main?.ApplySettings(cmd.Settings));
                return IpcResponse.Success();

            case IpcCommandType.Shutdown:
                Dispatcher.Invoke(Shutdown);
                return IpcResponse.Success();

            default:
                return IpcResponse.Success();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _server?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
