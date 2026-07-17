using System.Text.Json;
using System.Text.RegularExpressions;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;
using Kivi.Core.Http;

namespace Kivi.Core.Stt;

public sealed class GroqSttEngine : ISttEngine
{
    private static readonly HashSet<string> VerboseJsonModels = new(StringComparer.OrdinalIgnoreCase)
        { "whisper-1", "whisper-large-v3", "whisper-large-v3-turbo" };

    private readonly OpenAiCompatibleClient _http;
    private readonly AppConfig _config;
    private readonly ISecretStore _secrets;

    public GroqSttEngine(OpenAiCompatibleClient http, AppConfig config, ISecretStore secrets)
        => (_http, _config, _secrets) = (http, config, secrets);

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct)
    {
        var key = _secrets.GetApiKey() ?? throw new InvalidOperationException("Missing API key");
        var model = _config.TranscriptionModel;
        var format = VerboseJsonModels.Contains(model.Trim()) ? "verbose_json" : "json";
        var body = await _http.PostTranscriptionAsync(_config.TranscriptionBaseUrl, key, model, format,
            _config.TranscriptionLanguage, wav, "audio.wav", TimeSpan.FromSeconds(_config.TimeoutSeconds), ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (HallucinationFilter.IsHallucination(text, root)) return "";
        return text;
    }
}

internal static class HallucinationFilter
{
    private static readonly HashSet<string> Phrases = new(StringComparer.Ordinal)
    {
        "thank you", "thank you for watching", "thank you very much", "thank you so much",
        "thanks for watching", "please subscribe", "like and subscribe", "subtitles by",
        "subtitles by the amara.org community", "you"
    };
    private const double Threshold = 0.1;

    public static bool IsHallucination(string text, JsonElement root)
    {
        var normalized = Regex.Replace(text.ToLowerInvariant(), @"^[\p{P}\s]+|[\p{P}\s]+$", "");
        if (!Phrases.Contains(normalized)) return false;
        if (!root.TryGetProperty("segments", out var segs) || segs.ValueKind != JsonValueKind.Array) return false;
        var first = segs.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("no_speech_prob", out var p)) return false;
        return p.GetDouble() >= Threshold;
    }
}
