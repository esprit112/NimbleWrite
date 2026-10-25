using System;
using System.IO;

namespace NimbleWrite.Desktop.Core;

/// <summary>Small rolling log in %LOCALAPPDATA%\NimbleWrite (no text from other apps is ever logged).</summary>
internal static class Log
{
    static readonly object Gate = new();
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NimbleWrite");
    static readonly string FilePath = Path.Combine(Dir, "desktop.log");

    /// <summary>Set by the unit tests, so they never write to the real log in %LOCALAPPDATA%.</summary>
    internal static bool Muted { get; set; }

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string where, Exception? ex) => Write("ERROR", $"{where}: {ex}");

    static void Write(string level, string message)
    {
        if (Muted) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000) File.Move(FilePath, FilePath + ".old", true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
