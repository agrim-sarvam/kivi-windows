// Kivi.App/Services/StartupLauncher.cs
using Microsoft.Win32;

namespace Kivi.App.Services;

/// <summary>
/// Launch-at-login via the per-user Windows Run registry key. No admin rights needed
/// (HKCU), no installer/MSIX required since this app runs unpackaged.
/// </summary>
public static class StartupLauncher
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Kivi";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
