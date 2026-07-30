using System.Windows;

namespace Kivi.Installer;

public partial class App : Application
{
    /// <summary>True when the process was started with the --uninstall flag.</summary>
    public static bool IsUninstall { get; private set; }

    private static bool HasFlag(StartupEventArgs e, params string[] names) =>
        e.Args.Any(a => names.Any(n => string.Equals(a, n, StringComparison.OrdinalIgnoreCase)));

    protected override void OnStartup(StartupEventArgs e)
    {
        IsUninstall = HasFlag(e, "--uninstall", "/uninstall");
        bool silent = HasFlag(e, "--silent", "/silent", "/S");

        base.OnStartup(e);

        // Unattended mode (enterprise deploy / automated verification): no window, do the work, set
        // an exit code, quit. --silent installs; --silent --uninstall removes.
        if (silent)
        {
            RunSilent(IsUninstall);
            return;
        }

        var window = new MainWindow(IsUninstall);
        MainWindow = window;
        window.Show();
    }

    private void RunSilent(bool uninstall)
    {
        int code = 0;
        try
        {
            var progress = new Progress<(int, string)>();
            var installer = new Installer();
            if (uninstall)
            {
                installer.Uninstall(progress);
                Installer.ScheduleSelfDelete();
            }
            else
            {
                if (!Installer.HasPayload)
                    throw new InvalidOperationException("No payload bundled — cannot silently install.");
                // Silent defaults: desktop shortcut on, launch-at-login off.
                installer.Install(desktopShortcut: true, launchAtLogin: false, progress);
            }
        }
        catch
        {
            code = 1;
        }
        Shutdown(code);
    }
}
