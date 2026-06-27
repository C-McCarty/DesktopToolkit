using System.IO;
using System.Text.Json;
using Toolkit.Common.Services;

namespace MonitorArrangement.Services;

/// <summary>Save/load named layout profiles as JSON (ported from profiles.py).</summary>
public static class ProfileService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public sealed class ProfileEntry
    {
        public string DeviceName { get; set; } = "";
        public string FriendlyName { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class ProfileFile
    {
        public string Name { get; set; } = "";
        public List<ProfileEntry> Monitors { get; set; } = new();
    }

    public static void Save(IEnumerable<MonitorInfo> monitors, string path, string name)
    {
        var data = new ProfileFile
        {
            Name = name,
            Monitors = monitors.Select(m => new ProfileEntry
            {
                DeviceName = m.DeviceName,
                FriendlyName = m.FriendlyName,
                X = m.X,
                Y = m.Y,
            }).ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOpts));
    }

    public static (List<ProfileEntry> entries, string name) Load(string path)
    {
        var data = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(path), JsonOpts) ?? new ProfileFile();
        return (data.Monitors, data.Name);
    }

    /// <summary>Match saved entries to live monitors by device name; returns (row, x, y) tuples.</summary>
    public static List<(MonitorInfo monitor, int x, int y)> ApplyToMonitors(
        IReadOnlyList<ProfileEntry> entries, IEnumerable<MonitorInfo> monitors)
    {
        var byDevice = entries.GroupBy(e => e.DeviceName).ToDictionary(g => g.Key, g => g.First());
        var matched = new List<(MonitorInfo, int, int)>();
        foreach (var m in monitors)
        {
            if (byDevice.TryGetValue(m.DeviceName, out var e))
                matched.Add((m, e.X, e.Y));
        }
        return matched;
    }
}
