using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Toolkit.Common.Catalog;
using Toolkit.Common.Hosting;
using Toolkit.Common.Ipc;
using Toolkit.Common.Manifest;

// End-to-end check of the Phase 2 host plumbing without the WPF shell:
//   discovery -> supervisor launch -> IPC ping/settings/identify -> shutdown.
// Usage: dotnet run --project tools/Toolkit.SmokeTest -- <modulesRoot>

var modulesRoot = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "modules"));

Console.WriteLine($"modules root: {modulesRoot}");

var failures = new List<string>();
void Check(string name, bool ok)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}");
    if (!ok) failures.Add(name);
}

var supervisor = new ModuleSupervisor(modulesRoot);
supervisor.Initialize();

Console.WriteLine($"discovered {supervisor.Modules.Count} module(s): " +
                  string.Join(", ", supervisor.Modules.Select(m => m.Id)));

foreach (var id in new[] { "monitor-arrangement", "taskbar-manager", "animated-wallpaper" })
    Check($"discovery finds '{id}'", supervisor.Modules.Any(m => m.Id == id));

// ---- Window module: Monitor Arrangement (launches its WPF window) ----
var ma = supervisor.Modules.FirstOrDefault(m => m.Id == "monitor-arrangement");
if (ma is not null)
{
    Console.WriteLine("launching monitor-arrangement window module...");
    supervisor.Open(ma); // window module: starts the process
    Check("monitor-arrangement launches + responds to IPC", await WaitForPingAsync(supervisor, ma, 9000));

    var maPing = await supervisor.PingAsync(ma);
    Check("monitor-arrangement ping returns pong", maPing.Ok && maPing.Status == "pong");

    var shutdown = await ModuleClient.SendAsync(ma.PipeName, new IpcCommand { Type = IpcCommandType.Shutdown });
    Check("monitor-arrangement accepts shutdown", shutdown.Ok);
    await Task.Delay(800);
    Check("monitor-arrangement process stopped", !supervisor.IsRunning(ma));
}
else
{
    Console.WriteLine("(monitor-arrangement not deployed; skipping its checks)");
}

// ---- Background module: Taskbar Manager (hides nothing by default, so safe to toggle) ----
var tb = supervisor.Modules.FirstOrDefault(m => m.Id == "taskbar-manager");
if (tb is not null)
{
    Console.WriteLine("enabling taskbar-manager...");
    supervisor.SetEnabled(tb, true);
    Check("taskbar-manager launches + responds to IPC", await WaitForPingAsync(supervisor, tb, 9000));

    var tbPing = await supervisor.PingAsync(tb);
    Check("taskbar-manager ping returns pong", tbPing.Ok && tbPing.Status == "pong");

    supervisor.SetEnabled(tb, false);
    await Task.Delay(800);
    Check("taskbar-manager process stopped after disable", !supervisor.IsRunning(tb));
}
else
{
    Console.WriteLine("(taskbar-manager not deployed; skipping its checks)");
}

// ---- Background module: Animated Wallpaper (no media assigned by default = nothing renders) ----
var aw = supervisor.Modules.FirstOrDefault(m => m.Id == "animated-wallpaper");
if (aw is not null)
{
    Console.WriteLine("enabling animated-wallpaper...");
    supervisor.SetEnabled(aw, true);
    Check("animated-wallpaper launches + responds to IPC", await WaitForPingAsync(supervisor, aw, 12000));

    var awPing = await supervisor.PingAsync(aw);
    Check("animated-wallpaper ping returns pong", awPing.Ok && awPing.Status == "pong");

    supervisor.SetEnabled(aw, false);
    await Task.Delay(1000);
    Check("animated-wallpaper process stopped after disable", !supervisor.IsRunning(aw));
}
else
{
    Console.WriteLine("(animated-wallpaper not deployed; skipping its checks)");
}

// ---- Installer round-trip (no network): package monitor-arrangement -> install into a
//      temp modules dir -> launch + ping -> remove. ----
var srcModuleDir = Path.Combine(modulesRoot, "monitor-arrangement");
if (Directory.Exists(srcModuleDir))
{
    Console.WriteLine("installer round-trip...");
    var tempZip = Path.Combine(Path.GetTempPath(), $"dt-pkg-{Guid.NewGuid():N}.zip");
    var tempModules = Path.Combine(Path.GetTempPath(), $"dt-modules-{Guid.NewGuid():N}");
    try
    {
        ZipFile.CreateFromDirectory(srcModuleDir, tempZip); // module.json + exe at archive root

        var sup2 = new ModuleSupervisor(tempModules);
        sup2.Initialize();

        var install = sup2.InstallFromZip(tempZip);
        Check("install from zip succeeds", install.Ok);
        Check("installed module is discovered", sup2.Modules.Any(m => m.Id == "monitor-arrangement"));

        // Re-install (update path) must replace cleanly.
        var reinstall = sup2.InstallFromZip(tempZip);
        Check("re-install (update) succeeds", reinstall.Ok);

        // Catalog parse + install-from-catalog using local files (no network).
        var catalogPath = Path.Combine(Path.GetTempPath(), $"dt-cat-{Guid.NewGuid():N}.json");
        var catModules = Path.Combine(Path.GetTempPath(), $"dt-modules-cat-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(catalogPath, JsonSerializer.Serialize(new
            {
                modules = new[] { new { id = "monitor-arrangement", name = "Monitor Arrangement", version = "1.0.0", package = tempZip } }
            }));

            var entries = await new CatalogService().FetchAsync(catalogPath);
            Check("catalog parses entries", entries.Count == 1 && entries[0].Id == "monitor-arrangement");

            var sup3 = new ModuleSupervisor(catModules);
            sup3.Initialize();
            var catInstall = await sup3.InstallFromCatalogAsync(entries[0]);
            Check("install from catalog succeeds", catInstall.Ok && sup3.Modules.Any(m => m.Id == "monitor-arrangement"));
            sup3.ShutdownAll();
        }
        finally
        {
            try { File.Delete(catalogPath); } catch { }
            try { if (Directory.Exists(catModules)) Directory.Delete(catModules, true); } catch { }
        }

        var installed = sup2.Modules.First(m => m.Id == "monitor-arrangement");
        sup2.Open(installed);
        Check("installed module launches + pings", await WaitForPingAsync(sup2, installed, 9000));
        await ModuleClient.SendAsync(installed.PipeName, new IpcCommand { Type = IpcCommandType.Shutdown });
        await Task.Delay(600);

        sup2.RemoveModule(installed);
        Check("removed module is gone", sup2.Modules.All(m => m.Id != "monitor-arrangement"));
        sup2.ShutdownAll();
    }
    finally
    {
        try { File.Delete(tempZip); } catch { }
        try { if (Directory.Exists(tempModules)) Directory.Delete(tempModules, true); } catch { }
    }
}

supervisor.ShutdownAll();
Report();

void Report()
{
    Console.WriteLine();
    if (failures.Count == 0)
    {
        Console.WriteLine("SMOKE TEST PASSED");
        Environment.Exit(0);
    }
    else
    {
        Console.WriteLine($"SMOKE TEST FAILED ({failures.Count}): {string.Join("; ", failures)}");
        Environment.Exit(1);
    }
}

static async Task<bool> WaitForPingAsync(ModuleSupervisor supervisor, ModuleManifest module, int timeoutMs)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs)
    {
        if (supervisor.IsRunning(module))
        {
            var resp = await supervisor.PingAsync(module);
            if (resp.Ok)
                return true;
        }
        await Task.Delay(200);
    }
    return false;
}
