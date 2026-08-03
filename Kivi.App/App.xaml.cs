using System.Net.Http;
using System.Windows;
using Kivi.App.Drawing;
using Kivi.App.Services;
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

        // Dictation history (%APPDATA%\Kivi\dictation-history.json) + per-app icon extraction, read
        // by the Record/History pages. Registering the store here also auto-activates the
        // orchestrator's optional IDictationHistoryStore? ctor param, so every completed take is
        // recorded into this same singleton instance.
        sc.AddSingleton<IDictationHistoryStore, JsonDictationHistoryStore>();
        sc.AddSingleton<IAppIconResolver, AppIconResolver>();

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

        // Bridge the two page-facing singletons to the new()'d workspace pages. Must run before any
        // page is created (MainWindow is shown later in OnStartup, so this ordering is safe).
        AppServices.Init(_services);

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
            // Sign-in is MANDATORY in this build. The hosted (prod) endpoint requires a real
            // @sarvam.ai org JWT — there is no anonymous fallback that works on a recipient's
            // machine (the old "skip" path pointed at ws://127.0.0.1:8788, a local kivi-service
            // they won't be running, so dictation silently did nothing). So we loop the sign-in
            // dialog until they actually sign in, and quit if they dismiss it entirely — never
            // proceed into the dead local-anonymous state on a hand-off build.
            while (!auth.IsSignedIn)
            {
                var signIn = new SignInScreen(auth);
                signIn.ShowDialog();
                if (auth.IsSignedIn) break;

                var retry = System.Windows.MessageBox.Show(
                    "You must sign in with your Sarvam account to use Kivi.\n\nTry again?",
                    "Sign in required",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Information);
                if (retry != System.Windows.MessageBoxResult.OK)
                {
                    Shutdown();
                    return;
                }
            }
            orchestrator.UseHostedEndpoint = true;
        }

        // An invisible WPF window owns process lifetime + provides a DPI source for the runtime.
        var main = _services.GetRequiredService<MainWindow>();

        var tray = _services.GetRequiredService<ITrayHost>();
        tray.Show();

        orchestrator.Start();

        // The transcript box only renders live text when BoxLive (_expanded || _boxHostCount > 0)
        // is true (FlowEngine.cs) — DefaultExpansion defaults to Collapsed (matches the TS
        // reference exactly), and nothing was registering a box host, so live dictation text never
        // appeared until the user manually clicked expand. In the reference, whichever view embeds
        // a live transcript box (e.g. the main window's RecordPage) calls AddBoxHost()/RemoveBoxHost()
        // as it mounts/unmounts. This app's orb overlay is the one surface that's always present, so
        // it registers as a permanent host from launch — live text always shows while dictating,
        // with no click required.
        orchestrator.Engine.AddBoxHost();

        // Windows hotkey labels for the orb footer keycaps + hint text. Kivi.Core keeps the
        // reference defaults ("fn" / "⌃") so the golden-frame tests don't regress; the real Windows
        // app overrides them here to the actual bindings.
        orchestrator.Engine.HotkeyLabel = "right ctrl";
        orchestrator.Engine.EditComboLabel = "ctrl";

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
