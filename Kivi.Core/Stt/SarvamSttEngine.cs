using System.Text.Json;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;
using Kivi.Core.Http;

namespace Kivi.Core.Stt;

public sealed class SarvamSttEngine : ISttEngine
{
    private readonly OpenAiCompatibleClient _http;
    private readonly AppConfig _config;
    private readonly ISecretStore _secrets;

    public SarvamSttEngine(OpenAiCompatibleClient http, AppConfig config, ISecretStore secrets)
        => (_http, _config, _secrets) = (http, config, secrets);

    // mode: SttMode.Hinglish ("translit") romanizes Indic words into Latin letters mixed with
    // English; SttMode.English ("translate") renders everything as proper English. The caller
    // (the orchestrator) picks the mode based on which hotkey started the capture.
    public async Task<string> TranscribeAsync(byte[] wav, string mode, CancellationToken ct)
    {
        var key = _secrets.GetApiKey() ?? throw new InvalidOperationException("Missing API key");
        var body = await _http.PostSarvamTranscriptionAsync(_config.TranscriptionBaseUrl, key, _config.TranscriptionModel,
            mode, _config.TranscriptionLanguage, wav, "audio.wav", TimeSpan.FromSeconds(_config.TimeoutSeconds), ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return root.TryGetProperty("transcript", out var t) ? t.GetString() ?? "" : "";
    }
}
