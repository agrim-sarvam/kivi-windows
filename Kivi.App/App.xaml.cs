using System.Windows;
using Kivi.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace Kivi.App;

/// <summary>
/// The DI composition root and app lifetime host (tripwire T4). Mirrors Electron's src/main
/// bootstrap (index.ts): build the service graph, register the platform seams, create the windows,
/// and start the dictation orchestrator. Resident-agent model — closing the main window does not
/// quit (full wiring in later phases).
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();

        // Platform seams (Kivi.Platform → Kivi.Core.Contracts).
        sc.AddKiviPlatform();

        // App-level services / views.
        sc.AddSingleton<DictationOrchestrator>();
        sc.AddSingleton<MainWindow>();

        _services = sc.BuildServiceProvider();

        // Exercise the DI graph so the skeleton proves the seams resolve.
        var orchestrator = _services.GetRequiredService<DictationOrchestrator>();
        orchestrator.Start();

        var main = _services.GetRequiredService<MainWindow>();
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
