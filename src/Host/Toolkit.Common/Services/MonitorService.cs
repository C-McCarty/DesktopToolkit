using System.Runtime.InteropServices;
using Toolkit.Common.Native;
using static Toolkit.Common.Native.DisplayNativeMethods;

namespace Toolkit.Common.Services;

/// <summary>A single display as reported by Windows. Mutable X/Y so callers can rearrange.</summary>
public sealed class MonitorInfo
{
    public required string DeviceName { get; init; }
    public required string FriendlyName { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int RefreshRate { get; init; }
    public bool IsPrimary { get; init; }
    public int Index { get; init; }
}

/// <summary>
/// Shared monitor enumeration and layout application for the whole suite. Ported
/// from the original Python <c>monitors.py</c>; the two-phase commit in
/// <see cref="ApplyLayout"/> is the documented pattern for moving multiple
/// monitors at once on Windows.
/// </summary>
public static class MonitorService
{
    private static readonly Dictionary<int, string> ChangeMessages = new()
    {
        [1] = "The computer must be restarted for the change to take effect.",
        [-1] = "The display driver rejected the layout (DISP_CHANGE_FAILED). "
             + "Usually means a gap between monitors or no monitor at the (0,0) origin.",
        [-2] = "The layout is not a valid graphics mode (DISP_CHANGE_BADMODE).",
        [-3] = "The settings could not be written to the registry (DISP_CHANGE_NOTUPDATED).",
        [-4] = "Invalid flags passed (DISP_CHANGE_BADFLAGS).",
        [-5] = "An invalid parameter was passed (DISP_CHANGE_BADPARAM).",
        [-6] = "Invalid for a dual-view display (DISP_CHANGE_BADDUALVIEW).",
    };

    private static string Explain(int code) =>
        ChangeMessages.TryGetValue(code, out var msg) ? msg : $"Unknown error (code {code}).";

    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var index = 1;
        uint adapterIdx = 0;

        while (true)
        {
            var dd = NewDisplayDevice();
            if (!EnumDisplayDevicesW(null, adapterIdx, ref dd, 0))
                break;
            adapterIdx++;

            if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                continue;

            var deviceName = dd.DeviceName;
            var isPrimary = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;

            var friendlyName = dd.DeviceString.Trim();
            var monDd = NewDisplayDevice();
            if (EnumDisplayDevicesW(deviceName, 0, ref monDd, 0))
            {
                var candidate = monDd.DeviceString.Trim();
                if (!string.IsNullOrEmpty(candidate))
                    friendlyName = candidate;
            }

            var dm = NewDevMode();
            if (!EnumDisplaySettingsW(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
                continue;

            monitors.Add(new MonitorInfo
            {
                DeviceName = deviceName,
                FriendlyName = friendlyName,
                X = dm.dmPositionX,
                Y = dm.dmPositionY,
                Width = (int)dm.dmPelsWidth,
                Height = (int)dm.dmPelsHeight,
                RefreshRate = (int)dm.dmDisplayFrequency,
                IsPrimary = isPrimary,
                Index = index,
            });
            index++;
        }

        return monitors;
    }

    /// <summary>
    /// Applies new X/Y positions via per-monitor UPDATEREGISTRY+NORESET, then a final
    /// NULL commit. Positions are anchored on the primary monitor so it lands at (0,0).
    /// </summary>
    public static (bool ok, string error) ApplyLayout(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
            return (false, "No monitors to apply.");

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        int anchorX = primary.X, anchorY = primary.Y;

        foreach (var m in monitors)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettingsW(m.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                return (false, $"Could not read settings for {m.DeviceName}");

            dm.dmPositionX = m.X - anchorX;
            dm.dmPositionY = m.Y - anchorY;
            dm.dmFields = DM_POSITION;

            var ret = ChangeDisplaySettingsExW(
                m.DeviceName, ref dm, IntPtr.Zero,
                CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
            if (ret < 0)
                return (false, $"Failed on {m.FriendlyName}: {Explain(ret)}");
        }

        var commit = ChangeDisplaySettingsExW(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (commit < 0)
            return (false, $"Commit failed: {Explain(commit)}");

        return (true, "");
    }

    /// <summary>Monitors with a gap on every side (Windows rejects non-contiguous layouts).</summary>
    public static List<MonitorInfo> FindStranded(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count < 2)
            return new List<MonitorInfo>();

        return monitors
            .Where(m => !monitors.Any(o => !ReferenceEquals(o, m) && RectsTouch(m, o)))
            .ToList();
    }

    private static bool RectsTouch(MonitorInfo a, MonitorInfo b)
    {
        bool hOverlap = a.X <= b.X + b.Width && b.X <= a.X + a.Width;
        bool vOverlap = a.Y <= b.Y + b.Height && b.Y <= a.Y + a.Height;
        return hOverlap && vOverlap;
    }

    private static DEVMODE NewDevMode()
    {
        var dm = new DEVMODE { dmDeviceName = "", dmFormName = "" };
        dm.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        return dm;
    }

    private static DISPLAY_DEVICE NewDisplayDevice()
    {
        var dd = new DISPLAY_DEVICE
        {
            DeviceName = "",
            DeviceString = "",
            DeviceID = "",
            DeviceKey = "",
        };
        dd.cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>();
        return dd;
    }
}
