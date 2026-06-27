namespace AnimatedDesktopBackground;

/// <summary>
/// In managed mode the suite host owns the single "start with Windows" entry and
/// launches enabled modules, so this module must NOT write its own Run-key value
/// (that would double-launch, bypassing the host). These methods are intentionally
/// inert; start-with-Windows is configured from the host dashboard instead.
/// </summary>
internal static class StartupService
{
    public static bool IsEnabled() => false;

    public static void SetEnabled(bool enabled)
    {
        // No-op: handled by the host (Toolkit.Common.Services.StartupManager).
    }
}
