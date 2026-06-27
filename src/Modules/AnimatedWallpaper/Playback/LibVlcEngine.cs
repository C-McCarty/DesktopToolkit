using System;
using System.Globalization;
using AnimatedDesktopBackground.Models;
using LibVLCSharp.Shared;

namespace AnimatedDesktopBackground.Playback;

/// <summary>
/// LibVLC-backed engine that renders into a native child HWND via <see cref="MediaPlayer.Hwnd"/>.
/// Handles video and animated GIFs alike. This is the proven behind-icons render path on Win11.
/// </summary>
internal sealed class LibVlcEngine : IMediaEngine
{
    private readonly IntPtr _hwnd;
    private readonly int _width;
    private readonly int _height;
    private readonly object _gate = new();
    private MediaPlayer? _player;
    private string? _currentPath;
    private bool _disposed;

    public LibVlcEngine(IntPtr hwnd, int width, int height)
    {
        _hwnd = hwnd;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
    }

    public bool IsPlaying => _player is { IsPlaying: true };

    public void Play(string mediaPath, bool muted, FillMode fillMode)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _currentPath = mediaPath;
            StopInternal();

            var libvlc = LibVlcRuntime.Shared;
            _player = new MediaPlayer(libvlc)
            {
                Hwnd = _hwnd,
                EnableKeyInput = false,
                EnableMouseInput = false
            };
            ApplyFill(_player, fillMode);

            _player.EncounteredError += (_, _) => Logger.Log($"[vlc] ERROR playing {mediaPath}");

            using var media = new Media(libvlc, new Uri(mediaPath));
            media.AddOption(":input-repeat=65535");
            if (mediaPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                // Force the avformat demuxer so the GIF animates (the default "image" demux shows one
                // frame), and keep every frame rather than dropping "late" ones.
                media.AddOption(":demux=avformat");
                media.AddOption(":no-drop-late-frames");
                media.AddOption(":no-skip-frames");
                media.AddOption(":avcodec-skip-frame=0");
            }
            _player.Play(media);
            Logger.Log($"[vlc] Play {mediaPath} ({_width}x{_height}, fill={fillMode})");

            SetMuted(muted);
        }
    }

    public void Pause()
    {
        if (_player is { CanPause: true, IsPlaying: true })
            _player.SetPause(true);
    }

    public void Resume()
    {
        if (_player is { IsPlaying: false })
            _player.SetPause(false);
    }

    public void SetMuted(bool muted)
    {
        if (_player == null) return;
        // Mute property can be ignored before media is parsed; set volume as well.
        _player.Mute = muted;
        _player.Volume = muted ? 0 : 100;
    }

    private void ApplyFill(MediaPlayer player, FillMode mode)
    {
        string ratio = string.Create(CultureInfo.InvariantCulture, $"{_width}:{_height}");
        switch (mode)
        {
            case FillMode.Fill:
                // Stretch to the monitor ratio, ignoring source aspect.
                player.AspectRatio = ratio;
                player.Scale = 0; // fit to window using the forced aspect ratio
                break;
            case FillMode.Cover:
                // Crop source to the monitor ratio so it fills with no letterbox.
                player.CropGeometry = ratio;
                player.Scale = 0;
                break;
            case FillMode.Contain:
            default:
                // Preserve aspect, letterbox as needed (LibVLC default).
                player.Scale = 0;
                break;
        }
    }

    private void StopInternal()
    {
        if (_player == null) return;
        try { _player.Stop(); } catch { /* ignore */ }
        _player.Hwnd = IntPtr.Zero;
        _player.Dispose();
        _player = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopInternal();
        }
    }
}
