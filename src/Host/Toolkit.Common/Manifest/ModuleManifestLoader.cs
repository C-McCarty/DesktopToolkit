using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toolkit.Common.Manifest;

/// <summary>Discovers and parses <c>module.json</c> manifests under a modules root.</summary>
public static class ModuleManifestLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Returns one manifest per <c>&lt;modulesRoot&gt;/&lt;name&gt;/module.json</c>.
    /// Malformed or id-less manifests are skipped rather than failing discovery.
    /// </summary>
    public static IReadOnlyList<ModuleManifest> DiscoverModules(string modulesRoot)
    {
        var result = new List<ModuleManifest>();
        if (!Directory.Exists(modulesRoot))
            return result;

        foreach (var dir in Directory.GetDirectories(modulesRoot))
        {
            // Skip dot-prefixed dirs (e.g. an installer's ".staging-*" working folder).
            if (Path.GetFileName(dir).StartsWith('.'))
                continue;

            var manifestPath = Path.Combine(dir, "module.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<ModuleManifest>(json, JsonOptions);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                    continue;

                manifest.Directory = dir;
                result.Add(manifest);
            }
            catch
            {
                // Skip a single broken manifest; one bad module must not break the suite.
            }
        }

        return result;
    }
}
