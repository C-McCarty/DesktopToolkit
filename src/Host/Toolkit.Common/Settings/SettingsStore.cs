using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Toolkit.Common.Manifest;

namespace Toolkit.Common.Settings;

/// <summary>Persisted state for one module: whether it is enabled and its setting values.</summary>
public sealed class ModuleState
{
    public bool Enabled { get; set; }
    public bool StartWithWindows { get; set; }
    public Dictionary<string, JsonElement> Settings { get; set; } = new();
}

/// <summary>Top-level settings document for the whole suite.</summary>
public sealed class ToolkitSettings
{
    public bool StartWithWindows { get; set; }

    /// <summary>Raw URL of the module catalog's <c>catalog.json</c> (e.g. a GitHub raw URL).</summary>
    public string? CatalogUrl { get; set; }

    public Dictionary<string, ModuleState> Modules { get; set; } = new();
}

/// <summary>
/// Single source of truth for suite + per-module settings, persisted to
/// <c>%AppData%\DesktopToolkit\settings.json</c>. Replaces the per-app settings
/// files each tool used to own.
/// </summary>
public sealed class SettingsStore
{
    public static string RootDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopToolkit");

    public static string FilePath => Path.Combine(RootDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _lock = new();

    public ToolkitSettings Data { get; private set; } = new();

    public void Load() => Data = ReadFromDisk() ?? new ToolkitSettings();

    /// <summary>
    /// Persists in-memory state by MERGING it over the current on-disk file. The host and
    /// each module are separate processes writing this one file, so a plain overwrite would
    /// clobber sections/keys another process wrote (e.g. the host toggling "enabled" would
    /// wipe the wallpaper module's "config"). Merge keeps every writer's own keys intact.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            var merged = ReadFromDisk() ?? new ToolkitSettings();
            merged.StartWithWindows = Data.StartWithWindows;
            if (Data.CatalogUrl is not null)
                merged.CatalogUrl = Data.CatalogUrl;

            foreach (var (id, mem) in Data.Modules)
            {
                if (!merged.Modules.TryGetValue(id, out var disk))
                {
                    disk = new ModuleState();
                    merged.Modules[id] = disk;
                }
                disk.Enabled = mem.Enabled;
                disk.StartWithWindows = mem.StartWithWindows;
                foreach (var (key, value) in mem.Settings)
                    disk.Settings[key] = value; // in-memory keys win; on-disk-only keys preserved
            }

            Directory.CreateDirectory(RootDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(merged, JsonOpts));
            Data = merged; // keep in-memory view consistent with what was written
        }
    }

    private static ToolkitSettings? ReadFromDisk()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ToolkitSettings>(File.ReadAllText(FilePath), JsonOpts);
        }
        catch
        {
            // Corrupt/unreadable settings fall back to defaults rather than crashing.
        }
        return null;
    }

    public ModuleState GetModule(string id)
    {
        if (!Data.Modules.TryGetValue(id, out var state))
        {
            state = new ModuleState();
            Data.Modules[id] = state;
        }
        return state;
    }

    /// <summary>
    /// Replace a module's stored settings with only its manifest defaults, preserving the
    /// module's enabled / start-with-Windows flags and every other section. Unlike
    /// <see cref="Save"/> (which merges and never deletes), this REMOVES extra stored keys
    /// (e.g. a module's "config"), so it is the correct primitive for "reset to defaults".
    /// </summary>
    public void ResetModule(ModuleManifest manifest)
    {
        lock (_lock)
        {
            var disk = ReadFromDisk() ?? new ToolkitSettings();
            disk.Modules.TryGetValue(manifest.Id, out var existing);

            var fresh = new ModuleState
            {
                Enabled = existing?.Enabled ?? false,
                StartWithWindows = existing?.StartWithWindows ?? false,
            };
            foreach (var schema in manifest.Settings)
                if (schema.Default.HasValue)
                    fresh.Settings[schema.Key] = schema.Default.Value;

            disk.Modules[manifest.Id] = fresh;

            Directory.CreateDirectory(RootDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(disk, JsonOpts));
            Data = disk;
        }
    }

    /// <summary>Fills any setting the module hasn't stored yet from its manifest defaults.</summary>
    public void EnsureDefaults(ModuleManifest manifest)
    {
        var state = GetModule(manifest.Id);
        foreach (var schema in manifest.Settings)
        {
            if (!state.Settings.ContainsKey(schema.Key) && schema.Default.HasValue)
                state.Settings[schema.Key] = schema.Default.Value;
        }
    }
}
