using System;
using System.IO;

namespace Kivi.Platform.Paste;

/// <summary>
/// TEMPORARY diagnostic logger for the paste path — writes to %APPDATA%\Kivi\paste-debug.log so we
/// can see, after a real take, exactly what focus/target state the paste ran with. Remove once the
/// paste-lands-in-the-wrong-window bug is root-caused and fixed.
/// </summary>
internal static class PasteDebug
{
    private static readonly object Gate = new();
    private static readonly string LogPath = BuildPath();

    private static string BuildPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi");
        try { Directory.CreateDirectory(dir); } catch { /* best effort */ }
        return Path.Combine(dir, "paste-debug.log");
    }

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch { /* diagnostics must never throw into the paste path */ }
    }
}
