using System.Text.Json;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;
using Kivi.Core.Http;

namespace Kivi.Core.Stt;

public sealed class SarvamSttEngine : ISttEngine
{
    private const string Mode = "codemix";

    private readonly OpenAiCompatibleClient _http;
    private readonly AppConfig _config;
    private readonly ISecretStore _secrets;

    public SarvamSttEngine(OpenAiCompatibleClient http, AppConfig config, ISecretStore secrets)
        => (_http, _config, _secrets) = (http, config, secrets);

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct)
    {
        var key = _secrets.GetApiKey() ?? throw new InvalidOperationException("Missing API key");
        var body = await _http.PostSarvamTranscriptionAsync(_config.TranscriptionBaseUrl, key, _config.TranscriptionModel,
            Mode, _config.TranscriptionLanguage, wav, "audio.wav", TimeSpan.FromSeconds(_config.TimeoutSeconds), ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return root.TryGetProperty("transcript", out var t) ? t.GetString() ?? "" : "";
    }
}
