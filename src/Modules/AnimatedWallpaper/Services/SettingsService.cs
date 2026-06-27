using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnimatedDesktopBackground.Models;
using Toolkit.Common.Settings;

namespace AnimatedDesktopBackground;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> into the shared suite store
/// (<c>%AppData%\DesktopToolkit\settings.json</c>, section "animated-wallpaper", under a
/// single "config" value) so all modules share one settings file. The legacy standalone
/// file (<c>%AppData%\AnimatedDesktopBackground\settings.json</c>) is imported once.
/// </summary>
internal sealed class SettingsService
{
    private const string ModuleId = "animated-wallpaper";
    private const string ConfigKey = "config";

    private static readonly string LegacyDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnimatedDesktopBackground");

    private static readonly string LegacyFile = Path.Combine(LegacyDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SettingsStore _store = new();

    public AppSettings Settings { get; private set; } = new();

    public AppSettings Load()
    {
        _store.Load();
        var state = _store.GetModule(ModuleId);

        if (state.Settings.TryGetValue(ConfigKey, out var el))
        {
            try { Settings = el.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings(); }
            catch { Settings = new AppSettings(); }
        }
        else if (TryLoadLegacy(out var migrated))
        {
            Settings = migrated;
            Save(); // persist the imported settings into the shared store
        }
        else
        {
            Settings = new AppSettings();
        }

        Settings.MigrateLegacy(); // fold any legacy per-monitor entries into assignments
        return Settings;
    }

    public void Save()
    {
        try
        {
            var state = _store.GetModule(ModuleId);
            state.Settings[ConfigKey] = JsonSerializer.SerializeToElement(Settings, JsonOptions);
            _store.Save();
        }
        catch
        {
            // Non-fatal: settings simply won't persist this session.
        }
    }

    private static bool TryLoadLegacy(out AppSettings settings)
    {
        settings = new AppSettings();
        try
        {
            if (!File.Exists(LegacyFile))
                return false;
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(LegacyFile), JsonOptions) ?? new AppSettings();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
