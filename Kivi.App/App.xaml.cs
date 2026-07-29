using System.Net.Http;
using System.Windows;
using Kivi.App.Drawing;
using Kivi.App.Views.Auth;
using Kivi.Core.Contracts;
using Kivi.Core.Orb;
using Kivi.Platform;
using Kivi.Platform.Auth;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();
        sc.AddKiviPlatform();
        // Local JSON persistence (%APPDATA%\Kivi\flowstore.json) for settings + playback history.
        sc.AddSingleton<IFlowStore, JsonFlowStore>();

        // Auth (map §3): Kratos + org-JWT mint, pure HTTP clients over a shared HttpClient.
        var authConfig = AuthConfig.Default;
        sc.AddSingleton(new HttpClient());
        sc.AddSingleton(sp => new KratosAuthClient(sp.GetRequiredService<HttpClient>(), authConfig.KratosUrl));
        sc.AddSingleton(sp => new KratosOtpAuthClient(sp.GetRequiredService<HttpClient>(), authConfig.KratosUrl));
        sc.AddSingleton(sp => new OrgJwtClient(sp.GetRequiredService<HttpClient>(), authConfig.OrgServiceUrl));
        sc.AddSingleton(sp => new AuthController(
            sp.GetRequiredService<KratosAuthClient>(),
            sp.GetRequiredService<OrgJwtClient>(),
            sp.GetRequiredService<ISecretStore>(),
            kratosOtp: sp.GetRequiredService<KratosOtpAuthClient>()));

        sc.AddSingleton<DictationOrchestrator>();
        sc.AddSingleton<MainWindow>();
        _services = sc.BuildServiceProvider();

        var host = (LayeredOrbHost)_services.GetRequiredService<IOverlayHost>();

        // Auth gate: restore any saved session, then — only if the hosted endpoint is actually
        // needed — offer sign-in with a "skip / use local" escape hatch (local anonymous dev must
        // never be blocked by a mandatory sign-in wall; see map §3.1 authGateDestination spirit).
        var auth = _services.GetRequiredService<AuthController>();
        await auth.RestoreSessionAsync().ConfigureAwait(true);

        var orchestrator = _services.GetRequiredService<DictationOrchestrator>();

        if (auth.IsSignedIn)
        {
            orchestrator.UseHostedEndpoint = true;
        }
        else
        {
            var signIn = new SignInScreen(auth);
            signIn.ShowDialog();
            orchestrator.UseHostedEndpoint = signIn.SignedIn && auth.IsSignedIn;
        }

        // An invisible WPF window owns process lifetime + provides a DPI source for the runtime.
        var main = _services.GetRequiredService<MainWindow>();

        var tray = _services.GetRequiredService<ITrayHost>();
        tray.Show();

        orchestrator.Start();
        _runtime = new FlowRuntime(orchestrator.Engine, host);
        main.Show();
        _runtime.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_services?.GetService<ITrayHost>() as System.IDisposable)?.Dispose();
        _runtime?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
