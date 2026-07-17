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

namespace Kivi.App;

public class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Load a local .env (KEY=VALUE per line) into process env vars, if present, so
        // AddEnvironmentVariables() below picks them up. .env is git-ignored (never committed).
        DotEnv.Load();

        bool metricsEnabled = args.Contains("--metrics") || Environment.GetEnvironmentVariable("KIVI_METRICS") == "1";

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
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

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<Program>>();
        var metrics = provider.GetRequiredService<KiviMetrics>();
        using var _obs = Observability.Start(metricsEnabled, metrics);

        var orchestrator = provider.GetRequiredService<IDictationOrchestrator>();
        orchestrator.StateChanged += s => logger.LogInformation("state -> {State}", s); // no transcript content
        orchestrator.Start();

        logger.LogInformation("Kivi ready. Hold RIGHT-CTRL to dictate. Metrics={Metrics}. Ctrl+C to quit.", metricsEnabled);

        // STA message pump so the WH_KEYBOARD_LL hook delivers callbacks.
        MessagePump.Run();
    }
}
