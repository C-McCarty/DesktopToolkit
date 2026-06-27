using System.Collections.Generic;
using System.Linq;
using AnimatedDesktopBackground.Interop;
using CommonMonitors = Toolkit.Common.Services.MonitorService;

namespace AnimatedDesktopBackground;

/// <summary>Physical bounds and identity of a connected display.</summary>
internal sealed class MonitorInfo
{
    public required string DeviceName { get; init; }   // e.g. \\.\DISPLAY1 — stable-ish id
    public required NativeMethods.RECT Bounds { get; init; } // physical pixels (PerMonitorV2)
    public bool IsPrimary { get; init; }
    public int Index { get; init; }

    /// <summary>Human-friendly label for the manager UI.</summary>
    public string DisplayLabel =>
        $"Display {Index + 1}{(IsPrimary ? " (Primary)" : "")} — {Bounds.Width}×{Bounds.Height}";
}

/// <summary>
/// Enumerates connected displays in TRUE physical-pixel coordinates via the shared
/// Toolkit.Common monitor service (EnumDisplayDevices + EnumDisplaySettings / DEVMODE).
/// This matches the physical-pixel WorkerW rect the wallpaper windows are placed against —
/// WinForms' Screen.Bounds is DPI-virtualized and on mixed-DPI multi-monitor setups it
/// mis-sizes the child windows (leaving a 1px row of the underlying wallpaper).
/// </summary>
internal static class MonitorService
{
    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = CommonMonitors.GetMonitors()
            .OrderBy(m => m.X)
            .ThenBy(m => m.Y)
            .ToList();

        var list = new List<MonitorInfo>();
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            list.Add(new MonitorInfo
            {
                DeviceName = m.DeviceName,
                IsPrimary = m.IsPrimary,
                Index = i,
                Bounds = new NativeMethods.RECT
                {
                    Left = m.X,
                    Top = m.Y,
                    Right = m.X + m.Width,
                    Bottom = m.Y + m.Height,
                },
            });
        }
        return list;
    }
}
