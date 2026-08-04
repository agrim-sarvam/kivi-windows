using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace Kivi.Installer;

/// <summary>
/// Per-user install engine for Kivi. NO admin/elevation, NO machine-wide writes.
/// Everything lands under %LocalAppData%\Kivi, the user Start Menu / Desktop, and HKCU.
/// </summary>
public sealed class Installer
{
    // Bump this on every shipped installer build.
    public const string Version = "1.0.0";

    private const string AppExeName = "Kivi.App.exe";
    private const string AppProcessName = "Kivi.App"; // Process.GetProcessesByName wants no extension
    private const string PayloadResourceName = "Kivi.payload.zip";
    private const string RegUninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Kivi";
    private const string RegRunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegRunValue = "Kivi";
    private const string ShortcutName = "Kivi.lnk";

    // ---- Well-known per-user paths -----------------------------------------

    /// <summary>%LocalAppData%\Kivi — the install root.</summary>
    public static string KiviDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kivi");

    /// <summary>%LocalAppData%\Kivi\app — where the payload is extracted.</summary>
    public static string TargetDir => Path.Combine(KiviDir, "app");

    /// <summary>The installed app exe.</summary>
    public static string AppExe => Path.Combine(TargetDir, AppExeName);

    /// <summary>The stable uninstaller (a copy of this exe).</summary>
    public static string UninstallExe => Path.Combine(KiviDir, "uninstall.exe");

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", ShortcutName);

    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);

    /// <summary>True when this build actually carries a payload to install.</summary>
    public static bool HasPayload =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName) is not null;

    // ---- Install -----------------------------------------------------------

    public void Install(bool desktopShortcut, bool launchAtLogin, IProgress<(int percent, string status)> progress)
    {
        progress.Report((2, "preparing…"));
        StopRunningApp();

        progress.Report((10, "copying files…"));
        ExtractPayload();

        progress.Report((70, "creating shortcuts…"));
        CreateShortcut(StartMenuShortcut);
        if (desktopShortcut)
            CreateShortcut(DesktopShortcut);
        else
            TryDelete(DesktopShortcut);

        progress.Report((82, "registering…"));
        CopySelfAsUninstaller();
        WriteUninstallRegistry();
        SetLaunchAtLogin(launchAtLogin);

        progress.Report((92, "setting up logs command…"));
        WriteLogsCommand();
        AddTargetDirToUserPath();

        progress.Report((100, "done"));
    }

    /// <summary>Kill any running Kivi.App so its files aren't locked during extract.</summary>
    private static void StopRunningApp()
    {
        foreach (var p in Process.GetProcessesByName(AppProcessName))
        {
            try
            {
                p.Kill();
                p.WaitForExit(5000);
            }
            catch { /* best effort */ }
            finally { p.Dispose(); }
        }
    }

    private static void ExtractPayload()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException(
                "No payload embedded in this installer (Kivi.payload.zip missing).");

        // Clean/overwrite an existing app dir.
        if (Directory.Exists(TargetDir))
            Directory.Delete(TargetDir, recursive: true);
        Directory.CreateDirectory(TargetDir);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            // Skip directory entries.
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destPath = Path.GetFullPath(Path.Combine(TargetDir, entry.FullName));
            // Guard against zip-slip.
            if (!destPath.StartsWith(Path.GetFullPath(TargetDir) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    /// <summary>Create a .lnk via late-bound WScript.Shell (no COM reference needed).</summary>
    private static void CreateShortcut(string shortcutPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            var lnk = shell.CreateShortcut(shortcutPath);
            lnk.TargetPath = AppExe;
            lnk.IconLocation = AppExe;
            lnk.WorkingDirectory = TargetDir;
            lnk.Description = "Kivi — hold-to-talk dictation";
            lnk.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void CopySelfAsUninstaller()
    {
        var self = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot resolve the running installer path.");

        Directory.CreateDirectory(KiviDir);
        // If we ARE the uninstaller already (rare re-run), skip self-copy onto itself.
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(UninstallExe),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(self, UninstallExe, overwrite: true);
        }
    }

    private static void WriteUninstallRegistry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegUninstallKey);
        key.SetValue("DisplayName", "Kivi");
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "Sarvam AI");
        key.SetValue("DisplayIcon", AppExe);
        key.SetValue("InstallLocation", KiviDir);
        key.SetValue("UninstallString", $"\"{UninstallExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateSizeKb(), RegistryValueKind.DWord);
    }

    private static int EstimateSizeKb()
    {
        try
        {
            if (!Directory.Exists(TargetDir)) return 0;
            long bytes = Directory.EnumerateFiles(TargetDir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            return (int)Math.Min(int.MaxValue, bytes / 1024);
        }
        catch { return 0; }
    }

    private static void SetLaunchAtLogin(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegRunKey);
        if (enabled)
            key.SetValue(RegRunValue, $"\"{AppExe}\"");
        else if (key.GetValue(RegRunValue) is not null)
            key.DeleteValue(RegRunValue, throwOnMissingValue: false);
    }

    // ---- Observation logs command (`kivi-logs`) ----------------------------

    private const string LogsCmdName = "kivi-logs.cmd";
    private static string LogsCmdPath => Path.Combine(TargetDir, LogsCmdName);

    /// <summary>
    /// Write the tiny <c>kivi-logs.cmd</c> next to the app. Typing <c>kivi-logs</c> in any terminal
    /// runs the installed app in snapshot mode (<c>--logs</c>), which prints the observation summary
    /// to that terminal and exits. Non-technical users need no path — the .cmd resolves it.
    /// </summary>
    private static void WriteLogsCommand()
    {
        // %~dp0 = the folder this .cmd lives in (the app dir), so it always finds the exe next to it.
        var lines = new[]
        {
            "@echo off",
            "rem Kivi observation snapshot — prints CPU / memory / TTFT / latency for the running app.",
            "\"%~dp0Kivi.App.exe\" --logs %*",
        };
        try { File.WriteAllLines(LogsCmdPath, lines); } catch { /* best effort */ }
    }

    public static void LaunchApp()
    {
        Process.Start(new ProcessStartInfo(AppExe)
        {
            UseShellExecute = true,
            WorkingDirectory = TargetDir,
        });
    }

    // ---- User PATH (so `kivi-logs` resolves in any new terminal) -----------

    /// <summary>
    /// Add the app dir to the USER PATH (HKCU\Environment) so <c>kivi-logs</c> resolves from any
    /// terminal. Idempotent (never duplicates the entry). Reads/writes the raw registry value as
    /// REG_EXPAND_SZ WITHOUT expanding it (so existing tokens like %USERPROFILE% are preserved — the
    /// classic PATH-corruption footgun is expanding on read then writing back as a plain string).
    /// Broadcasts WM_SETTINGCHANGE so newly-launched terminals pick it up (already-open ones won't).
    /// </summary>
    private static void AddTargetDirToUserPath()
    {
        try
        {
            using var env = Registry.CurrentUser.OpenSubKey("Environment", writable: true)
                            ?? Registry.CurrentUser.CreateSubKey("Environment");
            var current = env.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";

            var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in parts)
                if (string.Equals(p.TrimEnd('\\'), TargetDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return; // already present

            var updated = current.Length == 0 ? TargetDir : current.TrimEnd(';') + ";" + TargetDir;
            env.SetValue("Path", updated, RegistryValueKind.ExpandString);
            BroadcastEnvChange();
        }
        catch { /* best effort — PATH is a convenience, not required for the app to work */ }
    }

    private static void RemoveTargetDirFromUserPath()
    {
        try
        {
            using var env = Registry.CurrentUser.OpenSubKey("Environment", writable: true);
            if (env?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) is not string current) return;
            var kept = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => !string.Equals(p.TrimEnd('\\'), TargetDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            env.SetValue("Path", string.Join(";", kept), RegistryValueKind.ExpandString);
            BroadcastEnvChange();
        }
        catch { /* best effort */ }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    private static void BroadcastEnvChange()
    {
        // HWND_BROADCAST=0xffff, WM_SETTINGCHANGE=0x1A, SMTO_ABORTIFHUNG=0x2. Tells shells to reload
        // the environment block so a NEW terminal sees the updated PATH without a logoff.
        try
        {
            SendMessageTimeout(new IntPtr(0xffff), 0x1A, IntPtr.Zero, "Environment", 0x2, 3000, out _);
        }
        catch { /* best effort */ }
    }

    // ---- Uninstall ---------------------------------------------------------

    public void Uninstall(IProgress<(int percent, string status)> progress)
    {
        progress.Report((5, "closing kivi…"));
        StopRunningApp();

        progress.Report((30, "removing shortcuts…"));
        TryDelete(StartMenuShortcut);
        TryDelete(DesktopShortcut);

        progress.Report((55, "cleaning registry…"));
        RemoveRunValue();
        RemoveUninstallRegistry();
        RemoveTargetDirFromUserPath();

        progress.Report((80, "removing files…"));
        // Delete the app dir now; the Kivi root + uninstall.exe (which is running)
        // are removed by the detached self-delete step.
        if (Directory.Exists(TargetDir))
        {
            try { Directory.Delete(TargetDir, recursive: true); } catch { /* handled by self-delete */ }
        }

        progress.Report((100, "done"));
    }

    private static void RemoveRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegRunKey, writable: true);
        if (key?.GetValue(RegRunValue) is not null)
            key.DeleteValue(RegRunValue, throwOnMissingValue: false);
    }

    private static void RemoveUninstallRegistry()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(RegUninstallKey, throwOnMissingSubKey: false); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// A running exe can't delete itself. Spawn a detached cmd that waits, then
    /// deletes uninstall.exe and the whole %LocalAppData%\Kivi dir. Call last, then exit.
    /// </summary>
    public static void ScheduleSelfDelete()
    {
        var uninstallExe = UninstallExe;
        var kiviDir = KiviDir;
        var args =
            $"/c timeout /t 2 /nobreak >nul & del /f /q \"{uninstallExe}\" & rmdir /s /q \"{kiviDir}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
