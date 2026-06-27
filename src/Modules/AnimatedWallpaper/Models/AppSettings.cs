using System.Collections.Generic;

namespace AnimatedDesktopBackground.Models;

/// <summary>How the media is scaled to fill the monitor.</summary>
public enum FillMode
{
    /// <summary>Stretch to fill, ignoring aspect ratio.</summary>
    Fill,
    /// <summary>Scale to cover the screen, preserving aspect ratio (may crop).</summary>
    Cover,
    /// <summary>Scale to fit inside the screen, preserving aspect ratio (may letterbox).</summary>
    Contain
}

/// <summary>Which rendering engine plays the media.</summary>
public enum MediaEngineKind
{
    /// <summary>LibVLC — wide format support, renders into a native HWND. Default/only proven engine.</summary>
    LibVlc,
    /// <summary>Windows MediaElement/Media Foundation — lightweight, reserved for a future "lite" build.</summary>
    MediaElement
}

/// <summary>Legacy per-monitor wallpaper assignment (kept only for one-time migration).</summary>
public sealed class MonitorSetting
{
    public string MonitorId { get; set; } = string.Empty;
    public string? MediaPath { get; set; }
    public bool Muted { get; set; } = true;
    public FillMode FillMode { get; set; } = FillMode.Cover;
}

/// <summary>
/// One media assignment that covers a SET of monitors. A video assigned to multiple monitors
/// renders as a single window spanning their combined (union) bounds. A monitor belongs to at
/// most one assignment.
/// </summary>
public sealed class WallpaperAssignment
{
    /// <summary>Stable device identifiers (\\.\DISPLAYn) this media spans.</summary>
    public List<string> MonitorIds { get; set; } = new();

    /// <summary>Absolute path to the GIF/video file, or null/empty if none assigned.</summary>
    public string? MediaPath { get; set; }

    public bool Muted { get; set; } = true;

    public FillMode FillMode { get; set; } = FillMode.Cover;
}

/// <summary>Root application settings, persisted as JSON in the shared suite store.</summary>
public sealed class AppSettings
{
    /// <summary>Media assignments, each spanning one or more monitors.</summary>
    public List<WallpaperAssignment> Assignments { get; set; } = new();

    /// <summary>Legacy per-monitor settings — migrated into <see cref="Assignments"/> on load.</summary>
    public List<MonitorSetting> Monitors { get; set; } = new();

    public MediaEngineKind MediaEngine { get; set; } = MediaEngineKind.LibVlc;

    public bool StartWithWindows { get; set; }

    public bool PauseOnFullscreen { get; set; } = true;

    /// <summary>Start the app minimized to the tray (no manager window shown).</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Whether the wallpaper should be playing on launch.</summary>
    public bool PlayOnStartup { get; set; } = true;

    /// <summary>One-time migration of the legacy per-monitor list into single-monitor assignments.</summary>
    public void MigrateLegacy()
    {
        if (Assignments.Count > 0 || Monitors.Count == 0)
            return;

        foreach (var m in Monitors)
        {
            if (string.IsNullOrEmpty(m.MediaPath))
                continue;
            Assignments.Add(new WallpaperAssignment
            {
                MonitorIds = new List<string> { m.MonitorId },
                MediaPath = m.MediaPath,
                Muted = m.Muted,
                FillMode = m.FillMode,
            });
        }
        Monitors.Clear();
    }
}
