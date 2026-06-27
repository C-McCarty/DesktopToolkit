using System;
using AnimatedDesktopBackground.Models;

namespace AnimatedDesktopBackground.Playback;

/// <summary>Creates the configured <see cref="IMediaEngine"/> for a wallpaper HWND.</summary>
internal static class MediaEngineFactory
{
    public static IMediaEngine Create(MediaEngineKind kind, IntPtr hwnd, int width, int height)
    {
        switch (kind)
        {
            case MediaEngineKind.MediaElement:
                // Reserved for a future "lite" build. WPF MediaElement cannot composite into a
                // native WorkerW child on Win11 (see CLAUDE.md), so we fall back to LibVLC for now.
                return new LibVlcEngine(hwnd, width, height);
            case MediaEngineKind.LibVlc:
            default:
                return new LibVlcEngine(hwnd, width, height);
        }
    }
}
