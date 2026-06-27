using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AnimatedDesktopBackground.Models;
using Microsoft.Web.WebView2.Core;

namespace AnimatedDesktopBackground.Playback;

/// <summary>
/// Renders an interactive web wallpaper (an HTML bundle) into the WorkerW child HWND using
/// WebView2 in windowed (controller) mode — the GPU-composited equivalent of the LibVLC path.
/// The page receives forwarded cursor events (it cannot get real input behind the icons) via
/// <c>window.chrome.webview</c> messages: {"type":"move|down|up","x":0..1,"y":0..1}.
/// </summary>
internal sealed class WebWallpaperEngine : IMediaEngine
{
    private readonly IntPtr _hwnd;
    private readonly int _width;
    private readonly int _height;
    private CoreWebView2Controller? _controller;
    private bool _disposed;

    public WebWallpaperEngine(IntPtr hwnd, int width, int height)
    {
        _hwnd = hwnd;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
    }

    public bool IsPlaying => _controller is not null;

    public void Play(string mediaPath, bool muted, FillMode fillMode) => _ = InitAsync(mediaPath);

    private async Task InitAsync(string mediaPath)
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AnimatedDesktopBackground", "WebView2");
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(null, userData, null);
            if (_disposed) return;

            _controller = await env.CreateCoreWebView2ControllerAsync(_hwnd);
            if (_disposed) { try { _controller.Close(); } catch { } _controller = null; return; }

            _controller.Bounds = new Rectangle(0, 0, _width, _height);
            _controller.IsVisible = true;

            var web = _controller.CoreWebView2;
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.IsZoomControlEnabled = false;
            web.Settings.AreBrowserAcceleratorKeysEnabled = false;
            web.Settings.IsStatusBarEnabled = false;
            web.Settings.AreDevToolsEnabled = false;

            string url = ResolveUrl(mediaPath);
            web.Navigate(url);
            Logger.Log($"[web] loaded {url} ({_width}x{_height})");
        }
        catch (Exception ex)
        {
            Logger.Log($"[web] init failed: {ex.Message}");
        }
    }

    private static string ResolveUrl(string mediaPath)
    {
        // A web wallpaper is an .html file, or a folder containing index.html.
        if (Directory.Exists(mediaPath))
            mediaPath = Path.Combine(mediaPath, "index.html");
        return new Uri(mediaPath).AbsoluteUri; // file:///...
    }

    /// <summary>Forward a cursor event in normalized (0..1) wallpaper coordinates to the page.</summary>
    public void PostPointer(string type, double nx, double ny)
    {
        if (_controller is null) return;
        try
        {
            var json = string.Create(CultureInfo.InvariantCulture,
                $"{{\"type\":\"{type}\",\"x\":{nx:0.####},\"y\":{ny:0.####}}}");
            _controller.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch { /* page not ready / navigating */ }
    }

    public void Pause() { /* leave the page running; pausing JS rAF is the page's concern */ }
    public void Resume() { }

    public void SetMuted(bool muted)
    {
        if (_controller is not null)
            try { _controller.CoreWebView2.IsMuted = muted; } catch { }
    }

    public void Dispose()
    {
        _disposed = true;
        try { _controller?.Close(); } catch { /* ignore */ }
        _controller = null;
    }
}
