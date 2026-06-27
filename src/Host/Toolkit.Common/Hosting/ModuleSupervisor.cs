using System.Diagnostics;
using System.IO;
using Toolkit.Common.Catalog;
using Toolkit.Common.Ipc;
using Toolkit.Common.Manifest;
using Toolkit.Common.Settings;

namespace Toolkit.Common.Hosting;

/// <summary>
/// Discovers modules and supervises their processes — the host's "runner". Background
/// modules run while enabled and are (re)started here; window modules are launched on
/// demand. All control flows over the per-module named pipe. UI-agnostic by design so
/// it can be unit/smoke-tested without the WPF shell.
/// </summary>
public sealed class ModuleSupervisor
{
    private readonly string _modulesRoot;
    private readonly Dictionary<string, Process> _processes = new();

    public ModuleSupervisor(string modulesRoot) => _modulesRoot = modulesRoot;

    public SettingsStore Settings { get; } = new();

    /// <summary>The folder modules are installed into and discovered from.</summary>
    public string ModulesRoot => _modulesRoot;

    public IReadOnlyList<ModuleManifest> Modules { get; private set; } = Array.Empty<ModuleManifest>();

    /// <summary>Raised after enable/run state changes so a UI can refresh.</summary>
    public event Action? StateChanged;

    /// <summary>Raised when the set of installed modules changes (install/remove/rescan).</summary>
    public event Action? ModulesChanged;

    public void Initialize()
    {
        Settings.Load();
        Modules = ModuleManifestLoader.DiscoverModules(_modulesRoot);

        foreach (var m in Modules)
            Settings.EnsureDefaults(m);
        Settings.Save();

        foreach (var m in Modules)
        {
            if (m.Kind == ModuleKind.Background && Settings.GetModule(m.Id).Enabled)
                StartProcess(m);
        }

        StateChanged?.Invoke();
    }

    /// <summary>Re-discover the modules folder (after install/remove or an external change)
    /// without disturbing already-running processes.</summary>
    public void Rescan()
    {
        Settings.Load();
        Modules = ModuleManifestLoader.DiscoverModules(_modulesRoot);
        foreach (var m in Modules)
            Settings.EnsureDefaults(m);
        Settings.Save();
        ModulesChanged?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>Install a module from a local package zip; replaces an existing same-id module.</summary>
    public InstallResult InstallFromZip(string zipPath)
    {
        var result = ModuleInstaller.InstallFromZip(zipPath, _modulesRoot, StopModuleById);
        if (result.Ok)
            Rescan();
        return result;
    }

    /// <summary>Download a catalog entry's package and install it.</summary>
    public async Task<InstallResult> InstallFromCatalogAsync(CatalogEntry entry)
    {
        string zip;
        try
        {
            zip = await new CatalogService().DownloadPackageAsync(entry.Package);
        }
        catch (Exception ex)
        {
            return InstallResult.Fail($"Download failed: {ex.Message}");
        }

        try { return InstallFromZip(zip); }
        finally { try { File.Delete(zip); } catch { /* temp cleanup */ } }
    }

    /// <summary>Stop the module if running, delete its folder, and rescan.</summary>
    public void RemoveModule(ModuleManifest m)
    {
        StopProcess(m);
        TryDeleteDirectory(m.Directory);
        Rescan();
    }

    /// <summary>Reset a module's settings to its manifest defaults; restart it if running so
    /// the change takes effect.</summary>
    public void ResetModule(ModuleManifest m)
    {
        bool wasRunning = IsRunning(m);
        bool restartBackground = m.Kind == ModuleKind.Background && IsEnabled(m);

        Settings.ResetModule(m);

        if (wasRunning)
        {
            StopProcess(m);
            if (restartBackground)
                StartProcess(m);
        }
        StateChanged?.Invoke();
    }

    private void StopModuleById(string id)
    {
        var running = Modules.FirstOrDefault(x => x.Id == id);
        if (running is not null)
            StopProcess(running);
    }

    private static void TryDeleteDirectory(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { Directory.Delete(dir, recursive: true); return; }
            catch { Thread.Sleep(200); } // files may stay briefly locked after the process exits
        }
    }

    public bool IsRunning(ModuleManifest m) =>
        _processes.TryGetValue(m.Id, out var p) && !p.HasExited;

    public bool IsEnabled(ModuleManifest m) => Settings.GetModule(m.Id).Enabled;

    public void SetEnabled(ModuleManifest m, bool enabled)
    {
        Settings.GetModule(m.Id).Enabled = enabled;
        Settings.Save();

        if (m.Kind == ModuleKind.Background)
        {
            if (enabled) StartProcess(m);
            else StopProcess(m);
        }

        StateChanged?.Invoke();
    }

    /// <summary>Launch (or focus, if already running) a module — used for window modules.</summary>
    public void Open(ModuleManifest m)
    {
        if (IsRunning(m))
        {
            _ = ModuleClient.SendAsync(m.PipeName, new IpcCommand { Type = IpcCommandType.Activate });
            return;
        }
        StartProcess(m);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Open a module's own configuration window (for manifests with settingsWindow=true).
    /// Ensures the module is running — enabling a background module so it persists — then
    /// asks it to surface its window via Activate.
    /// </summary>
    public async Task ShowConfigAsync(ModuleManifest m)
    {
        if (m.Kind == ModuleKind.Background && !IsEnabled(m))
            SetEnabled(m, true); // starts the process + persists enabled
        else if (!IsRunning(m))
            StartProcess(m);

        await ModuleClient.SendAsync(m.PipeName, new IpcCommand { Type = IpcCommandType.Activate }, 5000);
        StateChanged?.Invoke();
    }

    /// <summary>Persist settings and push them to the module if it is running.</summary>
    public async Task ApplySettingsAsync(ModuleManifest m)
    {
        Settings.Save();
        if (IsRunning(m))
        {
            var state = Settings.GetModule(m.Id);
            await ModuleClient.SendAsync(m.PipeName,
                new IpcCommand { Type = IpcCommandType.ApplySettings, Settings = state.Settings });
        }
    }

    public Task<IpcResponse> IdentifyAsync(ModuleManifest m) =>
        ModuleClient.SendAsync(m.PipeName, new IpcCommand { Type = IpcCommandType.Identify });

    public Task<IpcResponse> PingAsync(ModuleManifest m) => ModuleClient.PingAsync(m.PipeName);

    private void StartProcess(ModuleManifest m)
    {
        if (IsRunning(m))
            return;
        if (string.IsNullOrEmpty(m.ExecutablePath) || !File.Exists(m.ExecutablePath))
            return;

        var psi = new ProcessStartInfo
        {
            FileName = m.ExecutablePath,
            WorkingDirectory = m.Directory,
            UseShellExecute = false,
        };
        var process = Process.Start(psi);
        if (process is not null)
            _processes[m.Id] = process;
    }

    private void StopProcess(ModuleManifest m)
    {
        if (!_processes.TryGetValue(m.Id, out var process))
            return;

        try
        {
            if (!process.HasExited)
            {
                // Ask nicely first, then force.
                try { ModuleClient.SendAsync(m.PipeName, new IpcCommand { Type = IpcCommandType.Shutdown }, 800).Wait(900); }
                catch { /* ignore */ }

                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort teardown.
        }
        finally
        {
            _processes.Remove(m.Id);
        }
    }

    public void ShutdownAll()
    {
        foreach (var m in Modules.ToList())
            StopProcess(m);
    }
}
