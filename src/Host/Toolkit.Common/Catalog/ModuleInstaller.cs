using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Toolkit.Common.Manifest;

namespace Toolkit.Common.Catalog;

public sealed class InstallResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public ModuleManifest? Manifest { get; init; }

    public static InstallResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// Installs a module package (a .zip of a deployed module folder) into the modules root.
/// Pure file operation: extract → validate (manifest + its executable present) → move into
/// place. The caller (the supervisor) is responsible for stopping a running same-id module
/// first so its files aren't locked.
/// </summary>
public static class ModuleInstaller
{
    /// <param name="stopRunningModule">Invoked with the package's module id once validated and
    /// just before the existing copy is replaced, so the caller can stop a running instance
    /// whose files would otherwise be locked.</param>
    public static InstallResult InstallFromZip(string zipPath, string modulesRoot,
        Action<string>? stopRunningModule = null)
    {
        Directory.CreateDirectory(modulesRoot);

        // Stage on the SAME volume as modulesRoot so the final move is a fast, safe rename
        // (Directory.Move across volumes throws).
        var staging = Path.Combine(modulesRoot, $".staging-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging);

            var moduleDir = FindModuleDir(staging);
            if (moduleDir is null)
                return InstallResult.Fail("Package contains no module.json.");

            ModuleManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<ModuleManifest>(
                    File.ReadAllText(Path.Combine(moduleDir, "module.json")), ModuleManifestLoader.JsonOptions);
            }
            catch (Exception ex)
            {
                return InstallResult.Fail($"Invalid module.json: {ex.Message}");
            }

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                return InstallResult.Fail("module.json is missing an 'id'.");

            manifest.Directory = moduleDir;
            if (string.IsNullOrEmpty(manifest.Executable) || !File.Exists(manifest.ExecutablePath))
                return InstallResult.Fail($"Executable '{manifest.Executable}' is not present in the package.");

            var target = Path.Combine(modulesRoot, manifest.Id);

            // Stop a running instance so its exe/dlls aren't locked, then replace (update).
            stopRunningModule?.Invoke(manifest.Id);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);

            Directory.Move(moduleDir, target);
            manifest.Directory = target;
            return new InstallResult { Ok = true, Manifest = manifest };
        }
        catch (Exception ex)
        {
            return InstallResult.Fail(ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch
            {
                // Leftover staging is harmless (hidden by the dot-prefix); ignore.
            }
        }
    }

    /// <summary>The package may put module.json at the zip root or inside one top-level folder.</summary>
    private static string? FindModuleDir(string root)
    {
        if (File.Exists(Path.Combine(root, "module.json")))
            return root;
        foreach (var sub in Directory.GetDirectories(root))
            if (File.Exists(Path.Combine(sub, "module.json")))
                return sub;
        return null;
    }
}
