using System.Net.Http;
using System.Windows;
using Kivi.App.Drawing;
using Kivi.App.Services;
using Kivi.App.Views.Auth;
using Kivi.App.Controls.Shell;
using Kivi.Core.Contracts;
using Kivi.Core.Observability;
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

        // ---- Observation snapshot mode (`kivi-logs`): print the readable summary to the terminal
        // that launched us, then exit WITHOUT starting the GUI/orb. Attaches to the parent console so
        // output lands in the user's own terminal (a WPF app has no console by default). ----
        if (e.Args.Any(a => string.Equals(a, "--logs", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(a, "/logs", StringComparison.OrdinalIgnoreCase)))
        {
            PrintObservationsAndExit();
            return;
        }

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

        // App-level settings (%APPDATA%\Kivi\app-settings.json): chosen global hotkey chord +
        // onboarding-seen flag. Separate from the engine's IFlowStore (those feed golden-tested state).
        sc.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();

        // Observation center: background CPU/mem sampler + per-take TTFT/latency recorder → writes
        // %APPDATA%\Kivi\observations.json, which the `kivi-logs` command reads. Registering it here
        // auto-injects it into DictationOrchestrator's optional ctor param.
        sc.AddSingleton<ObservationRecorder>();

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

        // ---- Global talk-key: apply the saved chord. First-run onboarding is shown IN-WINDOW below
        // (after main.Show()), not as a separate modal window. ----
        var settings = _services.GetRequiredService<IAppSettingsStore>();
        var savedChord =
            Kivi.Core.Hotkey.HotkeyChord.TryParse(settings.HotkeyChord, out var sc0) && sc0 is not null
                ? sc0
                : HotkeyCatalog.Default;
        orchestrator.SetHotkeyChord(savedChord);
        bool needsOnboarding = !settings.HasOnboarded;

        // The main window owns process lifetime + provides a DPI source for the runtime. It's normally
        // created hidden (resident-agent style), but on first run we show it to host the onboarding
        // screen in-window.
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

        // First run: show onboarding IN-WINDOW (over the shell). Live-rebinds + persists each pick;
        // on "Start dictating" it hides itself, flips HasOnboarded, and reveals the normal shell.
        if (needsOnboarding)
        {
            main.ShowOnboarding(
                initial: savedChord,
                onChordChosen: chord =>
                {
                    orchestrator.SetHotkeyChord(chord);
                    settings.HotkeyChord = chord.ToStorageString();
                },
                onDone: chord =>
                {
                    orchestrator.SetHotkeyChord(chord);
                    settings.HotkeyChord = chord.ToStorageString();
                    settings.HasOnboarded = true;
                });
        }
    }

    // --- `kivi-logs` snapshot mode ---

    private const int ATTACH_PARENT_PROCESS = -1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);
    private const int STD_OUTPUT_HANDLE = -11;

    private void PrintObservationsAndExit()
    {
        // Attach to the launching terminal so the summary prints there; if there is no parent console
        // (e.g. double-clicked), allocate one so the output is still visible.
        // If stdout is already a valid handle (the user redirected `kivi-logs > file.txt`, or a
        // pipe), leave it alone and write there. Otherwise attach to the launching terminal (or
        // allocate a console) and rebind Console.Out to the console device (CONOUT$) — a WPF WinExe
        // starts with detached streams, so plain Console.Out writes would otherwise go nowhere.
        bool stdoutValid = GetStdHandle(STD_OUTPUT_HANDLE) is var h && h != IntPtr.Zero && h != new IntPtr(-1);
        if (!stdoutValid)
        {
            bool attached = AttachConsole(ATTACH_PARENT_PROCESS) || AllocConsole();
            if (attached)
            {
                try
                {
                    var stdout = new System.IO.StreamWriter(
                        System.IO.File.Open("CONOUT$", System.IO.FileMode.Open, System.IO.FileAccess.Write, System.IO.FileShare.Write))
                    { AutoFlush = true };
                    Console.SetOut(stdout);
                }
                catch { /* fall through — best effort */ }
            }
        }

        try
        {
            var snap = ObservationJson.TryLoad();
            Console.Out.Write(ObservationPrinter.Render(snap));
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine("kivi-logs: " + ex.Message); } catch { }
        }

        // Exit immediately — no GUI, no orb, no DI graph.
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_services?.GetService<ITrayHost>() as System.IDisposable)?.Dispose();
        _runtime?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
