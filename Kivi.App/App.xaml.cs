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
            var envKey = configuration["GROQ_API_KEY"];
            var dpapi = new DpapiSecretStore();
            if (!string.IsNullOrEmpty(envKey)) dpapi.SetApiKey(envKey); // cache into store for this session
            return dpapi;
        });

        services.AddSingleton<ISttEngine, GroqSttEngine>();
        services.AddSingleton<IPolishClient, GroqPolishClient>();
        services.AddSingleton<IHotkeyService, LowLevelKeyboardHookService>();
        services.AddSingleton<IAudioCaptureService, WasapiAudioCaptureService>();
        services.AddSingleton<IScreenContextProvider, UiaScreenContextProvider>();
        services.AddSingleton<IPasteService, SendInputPasteService>();
        services.AddSingleton<IDictationOrchestrator, DictationOrchestrator>();

        Services = services.BuildServiceProvider();

        var logger = Services.GetRequiredService<ILogger<App>>();
        var metrics = Services.GetRequiredService<KiviMetrics>();
        _obs = Observability.Start(metricsEnabled, metrics);

        var orchestrator = Services.GetRequiredService<IDictationOrchestrator>();
        orchestrator.StateChanged += s => logger.LogInformation("state -> {State}", s); // no transcript content
        orchestrator.Start();

        logger.LogInformation("Kivi ready. Hold RIGHT-CTRL to dictate. Metrics={Metrics}.", metricsEnabled);

        // Temporary smoke-test wiring for the recovered orb overlay (Task 2).
        // Finalized into the real startup gate in Task 6.
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        Controls.KiviOrbControl.AccentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            ColorFromHex(appConfig.OrbAccentColor));
        var overlayVm = new ViewModels.OverlayViewModel(orchestrator, dispatcher);
        _overlayWindow = new Views.OverlayWindow(overlayVm);
    }

    private static Windows.UI.Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }
}
