using System.Text.Json;
using Toolkit.Common.Settings;

namespace TaskbarManager;

/// <summary>
/// Per-monitor hidden-taskbar state, persisted in the shared suite settings store
/// (<c>%AppData%\DesktopToolkit\settings.json</c>, section "taskbar-manager") rather
/// than the tool's old standalone file. Migrates the legacy file on first run.
/// </summary>
public sealed class ModuleSettings
{
    public const string ModuleId = "taskbar-manager";
    private const string Key = "hiddenMonitors";

    private readonly SettingsStore _store = new();

    /// <summary>GDI device names whose taskbar should be kept hidden.</summary>
    public HashSet<string> HiddenMonitors { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Load()
    {
        _store.Load();
        HiddenMonitors = ReadSet();
        if (HiddenMonitors.Count == 0)
            TryMigrateLegacy();
    }

    public void Save()
    {
        var state = _store.GetModule(ModuleId);
        state.Settings[Key] = JsonSerializer.SerializeToElement(HiddenMonitors.ToArray());
        _store.Save();
    }

    /// <summary>Toggle a monitor's hidden state and persist. Returns the new hidden value.</summary>
    public bool Toggle(string deviceName)
    {
        bool nowHidden;
        if (HiddenMonitors.Contains(deviceName))
        {
            HiddenMonitors.Remove(deviceName);
            nowHidden = false;
        }
        else
        {
            HiddenMonitors.Add(deviceName);
            nowHidden = true;
        }
        Save();
        return nowHidden;
    }

    public bool IsHidden(string deviceName) => HiddenMonitors.Contains(deviceName);

    private HashSet<string> ReadSet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var state = _store.GetModule(ModuleId);
        if (state.Settings.TryGetValue(Key, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    set.Add(item.GetString()!);
        }
        return set;
    }

    private void TryMigrateLegacy()
    {
        try
        {
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TaskbarManager", "settings.json");
            if (!File.Exists(legacy))
                return;

            using var doc = JsonDocument.Parse(File.ReadAllText(legacy));
            if (doc.RootElement.TryGetProperty("HiddenMonitors", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String)
                        HiddenMonitors.Add(item.GetString()!);

                if (HiddenMonitors.Count > 0)
                    Save();
            }
        }
        catch
        {
            // Migration is best-effort.
        }
    }
}
