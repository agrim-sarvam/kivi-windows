using System.Windows;
using Kivi.App.Drawing;
using Kivi.Core.Contracts;
using Kivi.Platform;
using Kivi.Platform.Overlay;
using Microsoft.Extensions.DependencyInjection;
using Application = System.Windows.Application;

namespace Kivi.App;

/// <summary>
/// The DI composition root and app lifetime host (tripwire T4). Mirrors Electron's src/main
/// bootstrap (index.ts): build the service graph, register the platform seams, create the windows,
/// start the dictation orchestrator, and show the living orb overlay driven by the render runtime.
///
/// There is exactly one startup path: the real hotkey/mouse-driven engine. No scripted demo
/// loop exists — the orb only ever animates in response to real input, matching the shipped app.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private FlowRuntime? _runtime;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();
        sc.AddKiviPlatform();
        sc.AddSingleton<DictationOrchestrator>();
        sc.AddSingleton<MainWindow>();
        _services = sc.BuildServiceProvider();

        var host = (LayeredOrbHost)_services.GetRequiredService<IOverlayHost>();

        // An invisible WPF window owns process lifetime + provides a DPI source for the runtime.
        var main = _services.GetRequiredService<MainWindow>();

        var orchestrator = _services.GetRequiredService<DictationOrchestrator>();
        orchestrator.Start();
        _runtime = new FlowRuntime(orchestrator.Engine, host);
        main.Show();
        _runtime.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runtime?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
