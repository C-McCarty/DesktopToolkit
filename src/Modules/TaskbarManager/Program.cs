using Toolkit.Common.Ipc;

namespace TaskbarManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true,
            "DesktopToolkit.TaskbarManager.SingleInstance", out bool isNew);
        if (!isNew)
            return; // already running

        ApplicationConfiguration.Initialize();

        var settings = new ModuleSettings();
        settings.Load();

        // The enforcer is an invisible message-pump form; it applies and keeps the
        // saved hidden-taskbar state and exposes control over IPC to the host.
        var enforcer = new TaskbarEnforcer(settings);

        using var server = new ModuleServer("DesktopToolkit.taskbar-manager", enforcer.HandleIpc);
        server.Start();

        Application.Run(enforcer);

        GC.KeepAlive(mutex);
    }
}
