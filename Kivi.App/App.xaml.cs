using Kivi.App;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;
using Kivi.Core.Diagnostics;
using Kivi.Core.Http;
using Kivi.Core.Orchestration;
using Kivi.Core.Polish;
using Kivi.Core.Stt;
using Kivi.Platform.Audio;
using Kivi.Platform.Context;
using Kivi.Platform.Hotkey;
using Kivi.Platform.Paste;
using Kivi.Platform.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Kivi.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private IDisposable? _obs;
    private Views.OverlayWindow? _overlayWindow;

    public App()
    {
        InitializeComponent();

        // Log any unhandled UI-thread exception to a file so a crash surfaces a real stack
        // trace instead of the process silently vanishing (WinUI swallows these into a
        // combase/XAML 0xc000027b fast-fail with no managed detail on stdout/stderr).
        UnhandledException += (_, e) =>
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"{DateTime.Now:o}\n{e.Exception}\n\n");
            }
            catch { /* last resort: nothing more we can do */ }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Load a local .env (KEY=VALUE per line) into process env vars, if present, so
        // AddEnvironmentVariables() below picks them up. .env is git-ignored (never committed).
        DotEnv.Load();

        bool metricsEnabled = Environment.GetEnvironmentVariable("KIVI_METRICS") == "1";

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddUserSecrets(typeof(App).Assembly, optional: true)
            .Build();

        var configStore = new JsonAppConfigStore();
        var appConfig = configStore.Load();
        appConfig.MetricsEnabled = metricsEnabled;
        appConfig.Validate();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole());
        services.AddSingleton(appConfig);
        services.AddSingleton<IAppConfigStore>(configStore);
        services.AddSingleton(new HttpClient());
        services.AddSingleton<OpenAiCompatibleClient>();
        services.AddSingleton(new KiviMetrics());

        // Secrets: env/user-secrets first, else DPAPI store.
        services.AddSingleton<ISecretStore>(_ =>
        {
            var envKey = configuration["SARVAM_API_KEY"];
            var dpapi = new DpapiSecretStore();
            if (!string.IsNullOrEmpty(envKey)) dpapi.SetApiKey(envKey); // cache into store for this session
            return dpapi;
        });

        services.AddSingleton<ISttEngine, SarvamSttEngine>();
        services.AddSingleton<IPolishClient, SarvamPolishClient>();
        services.AddSingleton<IHotkeyService, LowLevelKeyboardHookService>();
        services.AddSingleton<IAudioCaptureService, WasapiAudioCaptureService>();
        services.AddSingleton<IScreenContextProvider, UiaScreenContextProvider>();
        services.AddSingleton<IPasteService, SendInputPasteService>();
        services.AddSingleton<IDictationOrchestrator, DictationOrchestrator>();
        services.AddTransient<ViewModels.ConfigViewModel>();

        Services = services.BuildServiceProvider();

        var logger = Services.GetRequiredService<ILogger<App>>();
        var metrics = Services.GetRequiredService<KiviMetrics>();
        _obs = Observability.Start(metricsEnabled, metrics);

        var orchestrator = Services.GetRequiredService<IDictationOrchestrator>();
        orchestrator.StateChanged += s => logger.LogInformation("state -> {State}", s); // no transcript content
        orchestrator.Start();

        logger.LogInformation("Kivi ready. Hold RIGHT-CTRL to dictate. Metrics={Metrics}.", metricsEnabled);

        // Re-apply the user's saved hotkey on every launch.
        var hotkey = Services.GetRequiredService<IHotkeyService>();
        hotkey.SetHotkey(appConfig.HotkeyVirtualKeyCode);
        hotkey.SetRewriteHotkey(appConfig.RewriteHotkeyVirtualKeyCode);

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        Controls.KiviOrbControl.AccentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            ColorFromHex(appConfig.OrbAccentColor));

        _ = RunStartupGateAsync(appConfig, orchestrator, dispatcher, logger);
    }

    /// <summary>
    /// First launch: Login -> Permissions -> Config, then the orb. Later launches:
    /// re-check mic permission; if revoked, re-show only Permissions (which completes
    /// without re-running Config); otherwise go straight to the orb.
    /// </summary>
    private async Task RunStartupGateAsync(AppConfig appConfig, IDictationOrchestrator orchestrator, DispatcherQueue dispatcher, ILogger<App> logger)
    {
        void ShowOrb()
        {
            var overlayVm = new ViewModels.OverlayViewModel(orchestrator, dispatcher);
            var languageLabel = string.IsNullOrWhiteSpace(appConfig.TranscriptionLanguage) ? "auto" : appConfig.TranscriptionLanguage!;
            var hotkeyLabel = HotkeyLabel(appConfig.HotkeyVirtualKeyCode);
            _overlayWindow = new Views.OverlayWindow(overlayVm, GdiColorFromHex(appConfig.OrbAccentColor), languageLabel, hotkeyLabel);
        }

        try
        {
            if (!appConfig.OnboardingCompleted)
            {
                var win = new Views.Onboarding.OnboardingWindow(startAtPermissions: false);
                // Show the orb BEFORE closing onboarding: a WinUI app exits when its last
                // window closes, so a live overlay window must already exist at that moment.
                win.Completed += () => { ShowOrb(); win.Close(); };
                win.Activate();
                return;
            }

            // Onboarding done previously — but re-check mic; if revoked, re-show only Permissions.
            bool micOk = await Kivi.App.Services.MicPermission.CheckAsync();
            if (!micOk)
            {
                var win = new Views.Onboarding.OnboardingWindow(startAtPermissions: true);
                // Show the orb BEFORE closing onboarding: a WinUI app exits when its last
                // window closes, so a live overlay window must already exist at that moment.
                win.Completed += () => { ShowOrb(); win.Close(); };
                win.Activate();
                return;
            }

            ShowOrb();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup gate failed; falling back to showing the orb.");
            try { ShowOrb(); } catch { /* last-resort: nothing more we can do */ }
        }
    }

    private static Windows.UI.Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }

    private static System.Drawing.Color GdiColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return System.Drawing.Color.FromArgb(255, r, g, b);
    }

    private static string HotkeyLabel(uint vk) => vk switch
    {
        0xA3 => "Right Ctrl",
        0xA2 => "Left Ctrl",
        0xA0 => "Left Shift",
        0xA1 => "Right Shift",
        0xA4 => "Left Alt",
        0xA5 => "Right Alt",
        _ => ((Windows.System.VirtualKey)vk).ToString(),
    };
}
