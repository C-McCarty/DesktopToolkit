using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toolkit.Common.Manifest;

/// <summary>How a module is run by the host.</summary>
public enum ModuleKind
{
    /// <summary>Launched on demand from the dashboard/tray; exits when its window closes.</summary>
    Window,

    /// <summary>Runs in the background while enabled; restarted by the host if it dies.</summary>
    Background,
}

/// <summary>One configurable setting a module exposes, rendered generically by the host.</summary>
public sealed class SettingSchema
{
    public string Key { get; set; } = "";

    /// <summary>One of: bool, int, enum, path, string.</summary>
    public string Type { get; set; } = "string";

    public string Label { get; set; } = "";

    public JsonElement? Default { get; set; }

    public int? Min { get; set; }

    public int? Max { get; set; }

    /// <summary>Allowed values when <see cref="Type"/> is "enum".</summary>
    public List<string>? Options { get; set; }
}

/// <summary>
/// The contract every module ships as <c>module.json</c> beside its executable.
/// This is the extensibility seam: drop a folder with a manifest into the modules
/// directory and the host discovers it without a recompile.
/// </summary>
public sealed class ModuleManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Executable { get; set; } = "";
    public ModuleKind Kind { get; set; } = ModuleKind.Window;
    public string? Icon { get; set; }

    /// <summary>When true the host launches the module's own configuration window
    /// instead of rendering the <see cref="Settings"/> schema.</summary>
    public bool SettingsWindow { get; set; }

    public List<SettingSchema> Settings { get; set; } = new();

    // ---- Populated at load time, never read from JSON ----

    [JsonIgnore]
    public string Directory { get; set; } = "";

    [JsonIgnore]
    public string ExecutablePath =>
        string.IsNullOrEmpty(Executable) ? "" : Path.Combine(Directory, Executable);

    [JsonIgnore]
    public string PipeName => $"DesktopToolkit.{Id}";
}
