using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;
using Kivi.Core.Http;

namespace Kivi.Core.Polish;

/// <summary>
/// LLM cleanup client for Sarvam's OpenAI-compatible /v1/chat/completions endpoint.
/// Builds the cleanup system/user prompts, posts to the default model with the
/// reasoning/token overrides, falls back to a secondary model on rate limit / empty
/// output, tracks a per-model cooldown after a 429, and runs a prompt-injection guard
/// that returns the raw transcript if the model appears to have executed the
/// transcript as an instruction instead of cleaning it. Never logs the
/// transcript, cleaned text, or the API key.
/// </summary>
public sealed class SarvamPolishClient : IPolishClient
{
    private readonly OpenAiCompatibleClient _http;
    private readonly AppConfig _config;
    private readonly ISecretStore _secrets;
    private readonly Dictionary<string, DateTimeOffset> _cooldownUntil = new();

    public event Action<string>? EnteringCooldown;

    public SarvamPolishClient(OpenAiCompatibleClient http, AppConfig config, ISecretStore secrets)
        => (_http, _config, _secrets) = (http, config, secrets);

    public async Task<string> CleanupAsync(string transcript, string context, CancellationToken ct)
    {
        var key = _secrets.GetApiKey() ?? throw new InvalidOperationException("Missing API key");
        var system = BuildSystemPrompt();
        var user = Kivi.Core.Prompts.Prompts.CleanupUserMessage(PolishPipeline.SanitizeContextField(context), transcript);

        foreach (var model in Models())
        {
            if (InCooldown(model)) continue;
            try
            {
                var payload = BuildPayload(model, system, user, transcript.Length);
                var body = await _http.PostChatCompletionAsync(_config.ChatBaseUrl, key, payload,
                    TimeSpan.FromSeconds(_config.TimeoutSeconds), ct);
                var content = ExtractContent(body);
                if (model == _config.FallbackModel) content = StripThinkTags(content);
                if (string.IsNullOrWhiteSpace(content)) continue; // truly blank output -> try fallback
                var cleaned = Sanitize(content);
                if (string.IsNullOrWhiteSpace(_config.OutputLanguage)
                    && AppearsToHaveExecutedInstruction(transcript, cleaned))
                    return transcript; // injection guard: return raw
                return cleaned;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _cooldownUntil[model] = DateTimeOffset.UtcNow.AddSeconds(30);
                EnteringCooldown?.Invoke(model);
            }
            catch (OperationCanceledException) { throw; } // capture cancelled -> let it unwind
            catch { /* any other error on this model (bad request, transient 5xx) -> try next */ }
        }
        return transcript; // all models failed -> safe fallback to raw
    }

    private IEnumerable<string> Models()
    {
        yield return _config.CleanupModel;
        if (!string.Equals(_config.FallbackModel, _config.CleanupModel, StringComparison.Ordinal))
            yield return _config.FallbackModel;
    }

    private bool InCooldown(string model)
        => _cooldownUntil.TryGetValue(model, out var until) && until > DateTimeOffset.UtcNow;

    private string BuildSystemPrompt()
    {
        var s = Kivi.Core.Prompts.Prompts.DefaultCleanupSystem;
        var vocab = string.Join(", ", _config.CustomVocabulary
            .Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct());
        if (vocab.Length > 0) s += "\n\n" + Kivi.Core.Prompts.Prompts.VocabularyAppend(vocab);
        if (!string.IsNullOrWhiteSpace(_config.OutputLanguage)) s += Kivi.Core.Prompts.Prompts.OutputLanguageAppend(_config.OutputLanguage!);
        return s;
    }

    private object BuildPayload(string model, string system, string user, int transcriptChars)
    {
        var msgs = new object[] {
            new { role = "system", content = system },
            new { role = "user", content = user }
        };
        if (model == _config.CleanupModel)
        {
            // Cleanup is a mechanical rewrite (strip fillers, fix self-corrections), not a
            // reasoning task -- "minimal" effort skips the slow reasoning pass that dominated the
            // post-speech latency. Size the output budget to the transcript (~1 token per 3 chars,
            // 1.5x headroom) instead of a flat 4096, so the model never plans for a huge response.
            int budget = Math.Clamp((int)(transcriptChars / 3.0 * 1.5) + 64, 128, 2048);
            return new { model, temperature = 0.0, max_completion_tokens = budget, reasoning_effort = "low", include_reasoning = false, messages = msgs };
        }
        return new { model, temperature = 0.0, messages = msgs };
    }

    private static string ExtractContent(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    private static string Sanitize(string value)
    {
        var r = value.Trim();
        if (r.Length == 0) return "";
        if (r.Length > 1 && r.StartsWith('"') && r.EndsWith('"')) r = r[1..^1].Trim();
        return r == "EMPTY" ? "" : r;
    }

    private static string StripThinkTags(string text)
    {
        var c = Regex.Replace(text, @"^(?:\s*<think>[\s\S]*?</think>)+", "");
        c = Regex.Replace(c, @"^\s*<think>[\s\S]*$", "");
        return c.Trim();
    }

    private static readonly HashSet<string> Markers = new(StringComparer.Ordinal)
    { "ask","answer","compose","create","draft","email","generate","make","message","prompt",
      "reply","respond","response","summarize","tell","translate","write","claude","chatgpt","ai","llm" };

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    { "a","an","and","are","as","at","be","but","by","can","could","for","from","had","has","have",
      "he","her","him","his","i","if","in","into","is","it","its","just","me","my","of","on","or","our",
      "please","she","so","that","the","their","them","then","there","this","to","um","uh","was","we",
      "were","what","when","where","who","with","would","you","your" };

    private const string PreamblePattern = @"^\s*(sure|certainly|absolutely|here(?:'s| is)|i(?:'d| would) be happy to|i can)\b";

    private static HashSet<string> SignificantTokens(string text)
        => Regex.Split(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
                .Where(t => t.Length > 1 && !StopWords.Contains(t)).ToHashSet(StringComparer.Ordinal);

    private static bool AppearsToHaveExecutedInstruction(string raw, string cleaned)
    {
        var rawTokens = SignificantTokens(raw);
        var cleanedTokens = SignificantTokens(cleaned);
        if (rawTokens.Count == 0 || cleanedTokens.Count == 0) return false;
        var rawMarkers = new HashSet<string>(rawTokens); rawMarkers.IntersectWith(Markers);
        if (rawMarkers.Count == 0) return false;
        var preserved = new HashSet<string>(rawMarkers); preserved.IntersectWith(cleanedTokens);
        var overlap = new HashSet<string>(rawTokens); overlap.IntersectWith(cleanedTokens);
        var ratio = (double)overlap.Count / Math.Max(rawTokens.Count, 1);
        var cleanedPre = Regex.IsMatch(cleaned, PreamblePattern, RegexOptions.IgnoreCase);
        var rawPre = Regex.IsMatch(raw, PreamblePattern, RegexOptions.IgnoreCase);
        return (cleanedPre && !rawPre) || (preserved.Count == 0 && ratio < 0.35);
    }
}
