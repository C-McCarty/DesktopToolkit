using System.IO;
using System.Windows;
using System.Windows.Threading;
using Toolkit.Common.Hosting;
using Toolkit.Runner.Diagnostics;
using Toolkit.Runner.Tray;

namespace Toolkit.Runner;

/// <summary>
/// Tray-resident host. No main window: a single instance owns the only tray icon and
/// supervises module processes. Closing the dashboard hides it; Exit (tray) shuts down.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private ModuleSupervisor? _supervisor;
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface (don't swallow) any UI-thread exception — a silent failure on a button
        // click is itself a defect. Log it and tell the user instead of doing nothing.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        BindingErrorListener.Install();

        _singleInstanceMutex = new Mutex(initiallyOwned: true,
            "DesktopToolkit.Runner.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Already running. (A future enhancement can signal the live instance to
            // surface its dashboard; for now we simply exit.)
            Shutdown();
            return;
        }

        Logger.Info("Host starting.");
        _supervisor = new ModuleSupervisor(ResolveModulesRoot());
        _supervisor.Initialize();
        Logger.Info($"Discovered {_supervisor.Modules.Count} module(s): " +
                    string.Join(", ", _supervisor.Modules.Select(m => m.Id)));

        _tray = new TrayController(_supervisor);
        _tray.Show();

        // Diagnostic aid: DESKTOPTOOLKIT_AUTODASH=1 opens the dashboard on startup so its
        // bindings render (and any binding errors are logged) without needing the tray.
        if (Environment.GetEnvironmentVariable("DESKTOPTOOLKIT_AUTODASH") == "1")
            Dispatcher.BeginInvoke(() => _tray!.OpenDashboard());
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to:\n{Path.Combine(Toolkit.Common.Settings.SettingsStore.RootDir, "runner-log.txt")}",
            "Desktop Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // keep the host alive
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _supervisor?.ShutdownAll();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Locates the deployed <c>modules/</c> directory: an explicit env override wins,
    /// otherwise we walk up from the executable (handles the dev <c>bin/&lt;cfg&gt;/...</c>
    /// layout). A candidate must be a *deployed* folder — one whose subfolders contain a
    /// manifest AND its executable. This is what distinguishes the real <c>modules/</c>
    /// from the <c>src/Modules/</c> SOURCE tree, which collides by name on case-insensitive
    /// Windows but holds no built exes beside its manifests.
    /// </summary>
    private static string ResolveModulesRoot()
    {
        var env = Environment.GetEnvironmentVariable("DESKTOPTOOLKIT_MODULES");
        if (!string.IsNullOrEmpty(env))
            return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? firstSeen = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "modules");
            if (Directory.Exists(candidate))
            {
                firstSeen ??= candidate;
                if (LooksLikeDeployedModules(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }

        // No deployed folder found — fall back to the first 'modules' dir, then to a
        // default beside the exe.
        return firstSeen ?? Path.Combine(AppContext.BaseDirectory, "modules");
    }

    /// <summary>True if any subfolder holds a module.json alongside an .exe (a built module).</summary>
    private static bool LooksLikeDeployedModules(string dir)
    {
        try
        {
            foreach (var sub in Directory.GetDirectories(dir))
            {
                if (File.Exists(Path.Combine(sub, "module.json")) &&
                    Directory.EnumerateFiles(sub, "*.exe").Any())
                    return true;
            }
        }
        catch
        {
            // Unreadable directory — treat as not-a-match.
        }
        return false;
    }
}
