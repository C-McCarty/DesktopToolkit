using Microsoft.Win32;

namespace Toolkit.Common.Services;

/// <summary>
/// Owns the single suite-level "start with Windows" entry under the per-user Run key.
/// Centralizing it here replaces the individual Run entries each tool wrote, so the
/// host (and only the host) is launched at logon and then starts enabled modules.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopToolkit";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (key is null)
            return;

        if (enabled)
            key.SetValue(ValueName, $"\"{exePath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
