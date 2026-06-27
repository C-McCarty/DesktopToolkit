using System;
using System.IO;
using AnimatedDesktopBackground.Interop;
using AnimatedDesktopBackground.Models;
using AnimatedDesktopBackground.Playback;

namespace AnimatedDesktopBackground.Wallpaper;

/// <summary>
/// A single raw Win32 child window parented into the WorkerW, covering an arbitrary rect
/// (one monitor, or the union of several for a spanning wallpaper). LibVLC renders the media
/// into this HWND so it composites behind the desktop icons.
/// </summary>
internal sealed class WallpaperWindow : IDisposable
{
    private const string ClassName = "ADB_WallpaperWindow";
    private static ushort _classAtom;
    private static NativeMethods.WndProc? _sharedWndProc; // keep delegate alive for the lifetime of the app

    private readonly string _id;
    private readonly NativeMethods.RECT _bounds; // target area in physical pixels (virtual desktop coords)
    private IntPtr _hwnd;
    private IMediaEngine? _engine;
    private string? _currentPath;
    private bool _disposed;

    public WallpaperWindow(string id, NativeMethods.RECT bounds)
    {
        _id = id;
        _bounds = bounds;
    }

    public IntPtr Hwnd => _hwnd;
    public string Id => _id;
    public bool HasMedia => _engine != null;

    /// <summary>Creates the child window inside <paramref name="workerW"/> at the target bounds.</summary>
    public void Create(IntPtr workerW)
    {
        EnsureClassRegistered();

        NativeMethods.GetWindowRect(workerW, out var ww);
        // Position relative to the WorkerW client origin (multi-monitor virtual-desktop aware).
        int x = _bounds.Left - ww.Left;
        int y = _bounds.Top - ww.Top;

        _hwnd = NativeMethods.CreateWindowEx(
            0, ClassName, "ADB",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE,
            x, y, _bounds.Width, _bounds.Height,
            workerW, IntPtr.Zero, NativeMethods.GetModuleHandle(null), IntPtr.Zero);

        // Sit at the bottom of the z-order (behind the icon DefView, above the wallpaper).
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>Loads and starts looping playback of the given media file (or web wallpaper).</summary>
    public void SetMedia(string mediaPath, bool muted, FillMode fill, MediaEngineKind kind)
    {
        if (_hwnd == IntPtr.Zero) return;

        _engine?.Dispose();
        _engine = IsWebWallpaper(mediaPath)
            ? new WebWallpaperEngine(_hwnd, _bounds.Width, _bounds.Height)
            : MediaEngineFactory.Create(kind, _hwnd, _bounds.Width, _bounds.Height);
        _engine.Play(mediaPath, muted, fill);
        _currentPath = mediaPath;
    }

    /// <summary>True if this window hosts an interactive web wallpaper.</summary>
    public bool IsWeb => _engine is WebWallpaperEngine;

    /// <summary>Forward a screen-space cursor event to a web wallpaper as normalized 0..1 coords.</summary>
    public void ForwardPointer(int screenX, int screenY, string type)
    {
        if (_engine is not WebWallpaperEngine web) return;
        double nx = (screenX - _bounds.Left) / (double)Math.Max(1, _bounds.Width);
        double ny = (screenY - _bounds.Top) / (double)Math.Max(1, _bounds.Height);
        web.PostPointer(type, nx, ny);
    }

    private static bool IsWebWallpaper(string path) =>
        Directory.Exists(path)
        || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    public void ClearMedia()
    {
        _engine?.Dispose();
        _engine = null;
        _currentPath = null;
    }

    public void Pause() => _engine?.Pause();
    public void Resume() => _engine?.Resume();
    public void SetMuted(bool muted) => _engine?.SetMuted(muted);

    public bool IsValid => _hwnd != IntPtr.Zero && NativeMethods.IsWindow(_hwnd);

    private static void EnsureClassRegistered()
    {
        if (_classAtom != 0) return;
        _sharedWndProc = WndProc;
        var wc = new NativeMethods.WNDCLASS
        {
            lpfnWndProc = _sharedWndProc,
            hInstance = NativeMethods.GetModuleHandle(null),
            lpszClassName = ClassName,
            hbrBackground = IntPtr.Zero // no GDI paint; LibVLC owns the surface
        };
        _classAtom = NativeMethods.RegisterClass(ref wc);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_ERASEBKGND = 0x0014;
        if (msg == WM_ERASEBKGND)
            return new IntPtr(1); // claim erase so the existing wallpaper shows until video paints
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine?.Dispose();
        _engine = null;
        if (_hwnd != IntPtr.Zero && NativeMethods.IsWindow(_hwnd))
            NativeMethods.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }
}
