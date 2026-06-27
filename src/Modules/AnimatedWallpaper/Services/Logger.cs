using System;
using System.IO;

namespace AnimatedDesktopBackground;

/// <summary>Minimal append-only file logger under %AppData% for diagnosing wallpaper playback.</summary>
internal static class Logger
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AnimatedDesktopBackground", "log.txt");

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never throw */ }
    }
}
