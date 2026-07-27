using System;
using System.Windows;
using Kivi.App.Drawing;
using Kivi.Core.Contracts;
using Kivi.Core.Orb;
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
/// Demo mode (env KIVI_ORB_DEMO=1 or --demo): constructs a standalone FlowEngine with the built-in
/// demo dictation/edit services and drives it through rest→listening→processing→done with NO backend
/// or mic — this is how the orb visuals are watched side-by-side against the Electron app.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private FlowRuntime? _runtime;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool demo = IsDemo(e.Args);

        var sc = new ServiceCollection();
        sc.AddKiviPlatform();
        sc.AddSingleton<DictationOrchestrator>();
        sc.AddSingleton<MainWindow>();
        _services = sc.BuildServiceProvider();

        var host = (LayeredOrbHost)_services.GetRequiredService<IOverlayHost>();

        // An invisible WPF window owns process lifetime + provides a DPI source for the runtime.
        var main = _services.GetRequiredService<MainWindow>();

        if (demo)
        {
            // Standalone engine with the engine's built-in demo services (no socket, no mic).
            var engine = new FlowEngine();
            _runtime = new FlowRuntime(engine, host);
            var driver = DemoDriver.Install(engine, new FlowSettings { Page = PageStyle.Dark, Orb = OrbStyle.Forest });
            _runtime.SetDriver(driver);
            // Show the host window minimized so a PresentationSource exists for DPI resolution,
            // but the desktop stays visible behind the always-on-top orb overlay (the star of demo).
            main.WindowState = WindowState.Minimized;
            main.ShowInTaskbar = false;
            main.Show();
            _runtime.Start();
        }
        else
        {
            var orchestrator = _services.GetRequiredService<DictationOrchestrator>();
            orchestrator.Start();
            _runtime = new FlowRuntime(orchestrator.Engine, host);
            main.Show();
            _runtime.Start();
        }
    }

    private static bool IsDemo(string[] args)
    {
        foreach (var a in args)
            if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)) return true;
        var env = Environment.GetEnvironmentVariable("KIVI_ORB_DEMO");
        return env == "1" || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runtime?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
