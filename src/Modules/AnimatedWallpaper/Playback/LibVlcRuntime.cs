using System;
using LibVLCSharp.Shared;

namespace AnimatedDesktopBackground.Playback;

/// <summary>
/// Owns the one-time LibVLC native init and a shared <see cref="LibVLC"/> instance.
/// Init is relatively slow on first run (parses VLC plugins), so callers should warm it
/// up off the UI thread via <see cref="EnsureInitialized"/>.
/// </summary>
internal static class LibVlcRuntime
{
    private static readonly object Gate = new();
    private static LibVLC? _shared;

    public static LibVLC Shared
    {
        get
        {
            EnsureInitialized();
            return _shared!;
        }
    }

    public static void EnsureInitialized()
    {
        if (_shared != null) return;
        lock (Gate)
        {
            if (_shared != null) return;
            Core.Initialize();
            // Keep LibVLC's smart defaults: hardware decode auto-selects, and late frames are
            // DROPPED to stay real-time. Forcing "keep every frame" (--no-drop-late-frames) made
            // playback stutter and tear under the multi-monitor load, so we don't force it.
            // Native-resolution output (the physical-pixel monitor sizing) is what keeps quality high.
            _shared = new LibVLC(
                "--input-repeat=65535",
                "--no-osd",
                "--quiet",
                "--no-video-title-show");
        }
    }
}
