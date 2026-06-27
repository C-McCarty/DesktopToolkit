using System.IO;
using Toolkit.Common.Settings;

namespace Toolkit.Runner.Diagnostics;

/// <summary>Append-only diagnostic log at %AppData%\DesktopToolkit\runner-log.txt.</summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(SettingsStore.RootDir, "runner-log.txt");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.RootDir);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff}  [{level}]  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
