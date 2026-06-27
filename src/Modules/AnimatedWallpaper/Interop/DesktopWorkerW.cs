using System;
using System.Diagnostics;

namespace AnimatedDesktopBackground.Interop;

/// <summary>
/// Locates the WorkerW window that the wallpaper renders into, so our native child
/// windows draw behind the desktop icons. PROVEN on Windows 11 Home 25H2 (see CLAUDE.md):
/// the target is the WorkerW that is a CHILD of Progman, below SHELLDLL_DefView.
/// </summary>
internal static class DesktopWorkerW
{
    // Undocumented message that tells Progman to spawn the WorkerW/DefView split.
    private const uint WM_SPAWN_WORKERW = 0x052C;

    /// <summary>
    /// Returns a WorkerW handle suitable to parent wallpaper child windows into,
    /// asking the shell to create it first. Returns IntPtr.Zero if none can be found.
    /// </summary>
    public static IntPtr GetWorkerW(out string method)
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            method = "no-progman";
            return IntPtr.Zero;
        }

        // Ask Progman to create the WorkerW/SHELLDLL_DefView split.
        NativeMethods.SendMessageTimeout(progman, WM_SPAWN_WORKERW, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.SMTO_NORMAL, 1000, out _);

        // Strategy A (Win11 24H2/25H2): the wallpaper layer is a WorkerW that is a CHILD of Progman.
        IntPtr childWorkerW = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (childWorkerW != IntPtr.Zero)
        {
            method = "progman-child-WorkerW";
            return childWorkerW;
        }

        // Strategy B (Win10/older Win11): a top-level WorkerW sibling of the SHELLDLL_DefView host.
        IntPtr sibling = FindWorkerWSiblingOfDefView();
        if (sibling != IntPtr.Zero)
        {
            method = "sibling-of-DefView";
            return sibling;
        }

        // Strategy C: fall back to Progman itself (renders behind icons on some builds).
        method = "progman-direct";
        return progman;
    }

    private static IntPtr FindWorkerWSiblingOfDefView()
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            IntPtr defView = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                IntPtr sibling = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                if (sibling != IntPtr.Zero)
                {
                    found = sibling;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Convenience wrapper that also logs which discovery path succeeded.</summary>
    public static IntPtr GetWorkerWLogged()
    {
        IntPtr w = GetWorkerW(out string method);
        Debug.WriteLine($"[WorkerW] handle=0x{w.ToInt64():X} via {method}");
        return w;
    }
}
