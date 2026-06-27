using System;
using AnimatedDesktopBackground.Models;

namespace AnimatedDesktopBackground.Playback;

/// <summary>
/// Renders looping media (video or GIF) into a native child HWND that lives in the WorkerW.
/// The engine is pluggable so a future installer can offer alternative backends; LibVLC is the
/// only one proven to composite behind the desktop icons on Windows 11 (see CLAUDE.md).
/// </summary>
internal interface IMediaEngine : IDisposable
{
    /// <summary>Starts (or restarts) looping playback of the given file into the target HWND.</summary>
    void Play(string mediaPath, bool muted, FillMode fillMode);

    /// <summary>Pauses playback (keeps the last frame).</summary>
    void Pause();

    /// <summary>Resumes after a <see cref="Pause"/>.</summary>
    void Resume();

    void SetMuted(bool muted);

    bool IsPlaying { get; }
}
