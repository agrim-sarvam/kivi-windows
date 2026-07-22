# Sarvam Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Groq with Sarvam AI as Kivi's speech-to-text and text-polish provider, so the app uses `saaras:v3` (codemix mode) for transcription and `sarvam-30b`/`sarvam-105b` for cleanup/rewrite, with the embedded-shared-key distribution model.

**Architecture:** `Kivi.Core` is pure portable C# with provider-agnostic interfaces (`ISttEngine`, `IPolishClient`) already isolating Groq specifics behind a thin `OpenAiCompatibleClient` HTTP wrapper. This plan adds `SarvamSttEngine`/`SarvamPolishClient` implementations, extends `OpenAiCompatibleClient` with a Sarvam-shaped transcription call (different auth header and multipart fields than Groq's), swaps `AppConfig` defaults and DI registrations, and deletes the Groq classes outright. No UI changes are in scope.

**Tech Stack:** C#/.NET, `System.Text.Json`, `System.Net.Http`, xUnit, existing `FakeHttpMessageHandler`/`SequencedFakeHttpMessageHandler` test doubles.

## Global Constraints

- STT model: `saaras:v3`, `mode=codemix` (per spec Part 1 — Sarvam's current recommended ASR model, codemix mode is the correct behavior for Hinglish).
- Polish model: `sarvam-30b` primary, `sarvam-105b` fallback (per spec Part 1 — OpenAI-compatible chat completions, 64K context, better rate-limit headroom than 105B).
- STT endpoint: `POST https://api.sarvam.ai/speech-to-text`, multipart (`file`, `model`, `mode`, `language_code`), auth header `api-subscription-key: <key>` — NOT Bearer.
- Chat endpoint: `POST https://api.sarvam.ai/v1/chat/completions`, OpenAI-shaped body/response, `Authorization: Bearer <key>`.
- Secret env var renames from `GROQ_API_KEY` to `SARVAM_API_KEY` everywhere (`.env.example`, `App.xaml.cs`).
- Old Groq classes (`GroqSttEngine`, `GroqPolishClient`) and their tests are deleted outright — no flags, no dead code left behind.
- Never log the transcript, cleaned text, or the API key (existing rule, carried over from `GroqPolishClient`'s doc comment).
- The prompt-injection guard, per-model 429 cooldown, and primary/fallback retry chain in the polish client are provider-agnostic behaviors and must be preserved unchanged in the new implementation.

---

### Task 1: Extend `OpenAiCompatibleClient` with a Sarvam-shaped transcription call

Sarvam's STT endpoint uses a different auth header (`api-subscription-key`, not `Bearer`) and different multipart fields (`mode`, `language_code`) than Groq's `PostTranscriptionAsync`. Rather than overload the existing method with divergent parameters, add a parallel method so both providers' shapes stay legible.

**Files:**
- Modify: `Kivi.Core/Http/OpenAiCompatibleClient.cs`
- Test: `Kivi.Core.Tests/OpenAiCompatibleClientTests.cs`

**Interfaces:**
- Produces: `Task<string> PostSarvamTranscriptionAsync(string baseUrl, string apiKey, string model, string mode, string? languageCode, byte[] wav, string fileName, TimeSpan timeout, CancellationToken ct)` on `OpenAiCompatibleClient`. Posts to `{baseUrl}/speech-to-text` with header `api-subscription-key: {apiKey}` and multipart fields `file`, `model`, `mode`, and `language_code` (omitted if null/empty).

- [ ] **Step 1: Read the existing test file to match its conventions**

Read `Kivi.Core.Tests/OpenAiCompatibleClientTests.cs` in full before writing new tests, so the new tests match its existing `FakeHttpMessageHandler` usage style exactly (check how it asserts on `LastRequest`/`LastRequestBody`).

- [ ] **Step 2: Write the failing test**

Add to `Kivi.Core.Tests/OpenAiCompatibleClientTests.cs`:

```csharp
[Fact]
public async Task PostSarvamTranscriptionAsync_SendsSubscriptionKeyHeader_AndCorrectFields()
{
    var fake = FakeHttpMessageHandler.Json("{\"transcript\":\"hi\"}");
    var client = new OpenAiCompatibleClient(new HttpClient(fake));

    var result = await client.PostSarvamTranscriptionAsync(
        "https://api.sarvam.ai", "sk_test123", "saaras:v3", "codemix", "hi-IN",
        new byte[] { 1, 2, 3 }, "audio.wav", TimeSpan.FromSeconds(20), default);

    Assert.Equal("{\"transcript\":\"hi\"}", result);
    Assert.Equal("https://api.sarvam.ai/speech-to-text", fake.LastRequest!.RequestUri!.ToString());
    Assert.Null(fake.LastRequest.Headers.Authorization);
    Assert.Equal("sk_test123", fake.LastRequest.Headers.GetValues("api-subscription-key").Single());
}

[Fact]
public async Task PostSarvamTranscriptionAsync_OmitsLanguageCode_WhenNull()
{
    var fake = FakeHttpMessageHandler.Json("{\"transcript\":\"hi\"}");
    var client = new OpenAiCompatibleClient(new HttpClient(fake));

    await client.PostSarvamTranscriptionAsync(
        "https://api.sarvam.ai", "sk_test123", "saaras:v3", "codemix", null,
        new byte[] { 1 }, "audio.wav", TimeSpan.FromSeconds(20), default);

    Assert.DoesNotContain("language_code", fake.LastRequestBody);
}
```

Add `using System.Linq;` at the top of the test file if not already present (needed for `.Single()`).

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~OpenAiCompatibleClientTests"`
Expected: FAIL — `PostSarvamTranscriptionAsync` does not exist on `OpenAiCompatibleClient`.

- [ ] **Step 4: Implement the method**

In `Kivi.Core/Http/OpenAiCompatibleClient.cs`, add after `PostTranscriptionAsync` (after line 26):

```csharp
    public async Task<string> PostSarvamTranscriptionAsync(string baseUrl, string apiKey, string model,
        string mode, string? languageCode, byte[] wav, string fileName, TimeSpan timeout, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(model), "model");
        content.Add(new StringContent(mode), "mode");
        if (!string.IsNullOrEmpty(languageCode)) content.Add(new StringContent(languageCode), "language_code");
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file, "file", fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/speech-to-text") { Content = content };
        req.Headers.Add("api-subscription-key", apiKey);
        return await SendAsync(req, timeout, ct);
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~OpenAiCompatibleClientTests"`
Expected: PASS (all tests in the file, including the two new ones).

- [ ] **Step 6: Commit**

```bash
git add Kivi.Core/Http/OpenAiCompatibleClient.cs Kivi.Core.Tests/OpenAiCompatibleClientTests.cs
git commit -m "feat(core): add Sarvam-shaped transcription call to OpenAiCompatibleClient"
```

---

### Task 2: `AppConfig` — Sarvam defaults

**Files:**
- Modify: `Kivi.Core/Config/AppConfig.cs:6-10`
- Test: `Kivi.Core.Tests/AppConfigTests.cs`

**Interfaces:**
- Produces: `AppConfig.Default()` now returns `TranscriptionBaseUrl = "https://api.sarvam.ai"`, `ChatBaseUrl = "https://api.sarvam.ai"`, `TranscriptionModel = "saaras:v3"`, `CleanupModel = "sarvam-30b"`, `FallbackModel = "sarvam-105b"`.
- Consumes: nothing new.

- [ ] **Step 1: Update the failing test first**

Modify `Kivi.Core.Tests/AppConfigTests.cs:6-14` (`Default_HasGroqBaseUrls_AndValidates`) — rename and change expected values:

```csharp
    [Fact]
    public void Default_HasSarvamBaseUrls_AndValidates()
    {
        var cfg = AppConfig.Default();
        Assert.Equal("https://api.sarvam.ai", cfg.TranscriptionBaseUrl);
        Assert.Equal("https://api.sarvam.ai", cfg.ChatBaseUrl);
        Assert.Equal("saaras:v3", cfg.TranscriptionModel);
        cfg.Validate(); // must not throw
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~AppConfigTests.Default_HasSarvamBaseUrls_AndValidates"`
Expected: FAIL — asserts don't match current Groq defaults.

- [ ] **Step 3: Update `AppConfig` defaults**

In `Kivi.Core/Config/AppConfig.cs`, replace lines 6-10:

```csharp
    public string TranscriptionBaseUrl { get; set; } = "https://api.sarvam.ai";
    public string ChatBaseUrl { get; set; } = "https://api.sarvam.ai";
    public string TranscriptionModel { get; set; } = "saaras:v3";
    public string CleanupModel { get; set; } = "sarvam-30b";
    public string FallbackModel { get; set; } = "sarvam-105b";
```

- [ ] **Step 4: Run the full AppConfigTests suite**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~AppConfigTests"`
Expected: PASS — all tests, including the unrelated onboarding/orb/hotkey default tests further down the file, which are untouched by this change.

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Config/AppConfig.cs Kivi.Core.Tests/AppConfigTests.cs
git commit -m "feat(core): default AppConfig to Sarvam base URLs and model names"
```

---

### Task 3: `SarvamSttEngine`

Replaces `GroqSttEngine`. Sarvam's response is flatter than Groq's Whisper `verbose_json` (no `segments[].no_speech_prob`), so the existing hallucination filter has no direct signal to key off. Per the spec's open items, this task drops the hallucination filter rather than inventing an unproven replacement — it can be reintroduced later against `language_probability` if false transcriptions show up in practice.

**Files:**
- Create: `Kivi.Core/Stt/SarvamSttEngine.cs`
- Delete: `Kivi.Core/Stt/GroqSttEngine.cs`
- Create: `Kivi.Core.Tests/SarvamSttEngineTests.cs`
- Delete: `Kivi.Core.Tests/GroqSttEngineTests.cs`

**Interfaces:**
- Consumes: `ISttEngine` (`Kivi.Core/Stt/ISttEngine.cs:2`, unchanged), `OpenAiCompatibleClient.PostSarvamTranscriptionAsync` (Task 1), `AppConfig` (Task 2 defaults), `ISecretStore.GetApiKey()`.
- Produces: `SarvamSttEngine : ISttEngine`, constructor `SarvamSttEngine(OpenAiCompatibleClient http, AppConfig config, ISecretStore secrets)` — same shape as the old `GroqSttEngine` constructor so DI registration is a one-line type swap.

- [ ] **Step 1: Write the failing tests**

Create `Kivi.Core.Tests/SarvamSttEngineTests.cs`:

```csharp
using Kivi.Core.Config;
using Kivi.Core.Http;
using Kivi.Core.Stt;
using Xunit;

public class SarvamSttEngineTests
{
    private sealed class FakeSecrets : Kivi.Core.Abstractions.ISecretStore
    {
        public string? GetApiKey() => "k";
        public void SetApiKey(string key) { }
    }

    private static SarvamSttEngine Engine(string responseBody)
    {
        var fake = FakeHttpMessageHandler.Json(responseBody);
        return new SarvamSttEngine(new OpenAiCompatibleClient(new HttpClient(fake)), AppConfig.Default(), new FakeSecrets());
    }

    [Fact]
    public async Task ReturnsTranscript_ForNormalSpeech()
    {
        var engine = Engine("{\"request_id\":\"r1\",\"transcript\":\"Hello world\",\"language_code\":\"en-IN\"}");
        Assert.Equal("Hello world", await engine.TranscribeAsync(new byte[] { 1 }, default));
    }

    [Fact]
    public async Task ReturnsEmptyString_WhenTranscriptFieldMissing()
    {
        var engine = Engine("{\"request_id\":\"r1\",\"language_code\":\"en-IN\"}");
        Assert.Equal("", await engine.TranscribeAsync(new byte[] { 1 }, default));
    }

    [Fact]
    public async Task ThrowsInvalidOperationException_WhenApiKeyMissing()
    {
        var fake = FakeHttpMessageHandler.Json("{\"transcript\":\"x\"}");
        var noKeySecrets = new NoKeySecrets();
        var engine = new SarvamSttEngine(new OpenAiCompatibleClient(new HttpClient(fake)), AppConfig.Default(), noKeySecrets);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.TranscribeAsync(new byte[] { 1 }, default));
    }

    private sealed class NoKeySecrets : Kivi.Core.Abstractions.ISecretStore
    {
        public string? GetApiKey() => null;
        public void SetApiKey(string key) { }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~SarvamSttEngineTests"`
Expected: FAIL — `SarvamSttEngine` type does not exist yet.

- [ ] **Step 3: Implement `SarvamSttEngine`**

Create `Kivi.Core/Stt/SarvamSttEngine.cs`:

```csharp
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
```

- [ ] **Step 4: Delete the Groq STT engine and its tests**

```bash
rm Kivi.Core/Stt/GroqSttEngine.cs Kivi.Core.Tests/GroqSttEngineTests.cs
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~SarvamSttEngineTests"`
Expected: PASS — 3 tests.

Also run the full test project to confirm nothing else references the deleted `GroqSttEngine`/`HallucinationFilter` types:

Run: `dotnet build Kivi.Core.Tests`
Expected: builds clean, no missing-type errors. If `App.xaml.cs` or anything else references `GroqSttEngine`, this build will not catch it (different project) — Task 5 handles the App-side reference.

- [ ] **Step 6: Commit**

```bash
git add Kivi.Core/Stt/SarvamSttEngine.cs Kivi.Core.Tests/SarvamSttEngineTests.cs
git rm Kivi.Core/Stt/GroqSttEngine.cs Kivi.Core.Tests/GroqSttEngineTests.cs
git commit -m "feat(core): replace GroqSttEngine with SarvamSttEngine (saaras:v3, codemix)"
```

---

### Task 4: `SarvamPolishClient`

Replaces `GroqPolishClient`. Preserves the primary/fallback model chain, per-model 429 cooldown, `<think>`-tag stripping for the fallback model, and the prompt-injection guard unchanged — only the concrete class name, constructor is otherwise identical, and doc comment change (mentions Sarvam models instead of Groq).

**Files:**
- Create: `Kivi.Core/Polish/SarvamPolishClient.cs`
- Delete: `Kivi.Core/Polish/GroqPolishClient.cs`
- Create: `Kivi.Core.Tests/SarvamPolishClientTests.cs`
- Delete: `Kivi.Core.Tests/GroqPolishClientTests.cs`

**Interfaces:**
- Consumes: `IPolishClient` (`Kivi.Core/Polish/IPolishClient.cs:2-7`, unchanged), `OpenAiCompatibleClient.PostChatCompletionAsync` (existing, unchanged — Sarvam chat completions are OpenAI-shaped), `AppConfig` (Task 2 defaults: `CleanupModel="sarvam-30b"`, `FallbackModel="sarvam-105b"`), `Kivi.Core.Prompts.Prompts` (unchanged).
- Produces: `SarvamPolishClient : IPolishClient`, constructor `SarvamPolishClient(OpenAiCompatibleClient http, AppConfig config, ISecretStore secrets)` — identical shape to `GroqPolishClient`'s constructor.

- [ ] **Step 1: Copy the Groq polish test file as the starting point for the new one, renaming references**

Create `Kivi.Core.Tests/SarvamPolishClientTests.cs` with the exact content of `Kivi.Core.Tests/GroqPolishClientTests.cs` (read above), but:
- Rename the class from `GroqPolishClientTests` to `SarvamPolishClientTests`
- Rename every use of `GroqPolishClient` to `SarvamPolishClient`
- Keep every test method, assertion, and the `SequencedFakeHttpMessageHandler` nested class byte-for-byte identical otherwise — this test suite is asserting provider-agnostic behavior (cooldown, fallback chain, injection guard, EMPTY sentinel) that must not change.

```csharp
using System.Net;
using Kivi.Core.Config;
using Kivi.Core.Http;
using Kivi.Core.Polish;
using Xunit;

public class SarvamPolishClientTests
{
    private sealed class FakeSecrets : Kivi.Core.Abstractions.ISecretStore
    { public string? GetApiKey() => "k"; public void SetApiKey(string key) { } }

    private static string Chat(string content) =>
        "{\"choices\":[{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(content) + "}}]}";

    private static SarvamPolishClient Client(string body)
        => new(new OpenAiCompatibleClient(new HttpClient(FakeHttpMessageHandler.Json(body))), AppConfig.Default(), new FakeSecrets());

    private sealed class SequencedFakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<string?> RequestBodies { get; } = new();

        public SequencedFakeHttpMessageHandler(params (string body, HttpStatusCode code)[] responses)
            => _responses = new Queue<HttpResponseMessage>(
                responses.Select(r => new HttpResponseMessage(r.code) { Content = new StringContent(r.body) }));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            if (_responses.Count == 0) throw new InvalidOperationException("No more queued responses.");
            return _responses.Dequeue();
        }
    }

    private static SarvamPolishClient SequencedClient(SequencedFakeHttpMessageHandler handler)
        => new(new OpenAiCompatibleClient(new HttpClient(handler)), AppConfig.Default(), new FakeSecrets());

    [Fact]
    public async Task ReturnsCleanedText_StrippingOuterQuotes()
    {
        var client = Client(Chat("\"Hello there.\""));
        Assert.Equal("Hello there.", await client.CleanupAsync("hello there", "", default));
    }

    [Fact]
    public async Task EmptySentinel_ReturnsEmpty()
    {
        var client = Client(Chat("EMPTY"));
        Assert.Equal("", await client.CleanupAsync("uh", "", default));
    }

    [Fact]
    public async Task InjectionGuard_ReturnsRawTranscript_WhenModelAnswered()
    {
        var client = Client(Chat("Sure, here is the email you asked for: Dear team ..."));
        var raw = "write an email to the team asking if friday works";
        Assert.Equal(raw, await client.CleanupAsync(raw, "", default));
    }

    [Fact]
    public async Task RateLimited_FallsBackToSecondModel_AndReturnsCleanedText()
    {
        var handler = new SequencedFakeHttpMessageHandler(
            ("{\"error\":\"rate limited\"}", HttpStatusCode.TooManyRequests),
            (Chat("Hello there."), HttpStatusCode.OK));
        var client = SequencedClient(handler);

        var result = await client.CleanupAsync("hello there", "", default);

        Assert.Equal("Hello there.", result);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains(AppConfig.Default().CleanupModel, handler.RequestBodies[0]);
        Assert.Contains(AppConfig.Default().FallbackModel, handler.RequestBodies[1]);
    }

    [Fact]
    public async Task EmptyContentFromPrimary_FallsBackToSecondModel_AndReturnsCleanedText()
    {
        var handler = new SequencedFakeHttpMessageHandler(
            (Chat("   "), HttpStatusCode.OK),
            (Chat("Hello there."), HttpStatusCode.OK));
        var client = SequencedClient(handler);

        var result = await client.CleanupAsync("hello there", "", default);

        Assert.Equal("Hello there.", result);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains(AppConfig.Default().CleanupModel, handler.RequestBodies[0]);
        Assert.Contains(AppConfig.Default().FallbackModel, handler.RequestBodies[1]);
    }

    [Fact]
    public async Task RewriteAsync_ReturnsRewrittenText()
    {
        var client = Client(Chat("Confirming tomorrow at 3 PM."));
        var result = await client.RewriteAsync("Kal 3 PM works.", "make it formal", default);
        Assert.Equal("Confirming tomorrow at 3 PM.", result);
    }

    [Fact]
    public async Task RewriteAsync_RateLimited_FallsBackToSecondModel()
    {
        var handler = new SequencedFakeHttpMessageHandler(
            ("{\"error\":\"rate limited\"}", HttpStatusCode.TooManyRequests),
            (Chat("Confirming tomorrow at 3 PM."), HttpStatusCode.OK));
        var client = SequencedClient(handler);

        var result = await client.RewriteAsync("Kal 3 PM works.", "make it formal", default);

        Assert.Equal("Confirming tomorrow at 3 PM.", result);
        Assert.Equal(2, handler.RequestBodies.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~SarvamPolishClientTests"`
Expected: FAIL — `SarvamPolishClient` type does not exist yet.

- [ ] **Step 3: Implement `SarvamPolishClient`**

Create `Kivi.Core/Polish/SarvamPolishClient.cs` with the exact content of `Kivi.Core/Polish/GroqPolishClient.cs` (read above), renaming only the class name and updating the doc comment:

```csharp
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
                var payload = BuildPayload(model, system, user);
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
        }
        return transcript; // all models failed -> safe fallback to raw
    }

    public async Task<string> RewriteAsync(string selectedText, string voiceCommand, CancellationToken ct)
    {
        var key = _secrets.GetApiKey() ?? throw new InvalidOperationException("Missing API key");
        var system = BuildRewriteSystemPrompt();
        var user = Kivi.Core.Prompts.Prompts.CommandModeUserMessage(selectedText, voiceCommand);

        foreach (var model in Models())
        {
            if (InCooldown(model)) continue;
            try
            {
                var payload = BuildPayload(model, system, user);
                var body = await _http.PostChatCompletionAsync(_config.ChatBaseUrl, key, payload,
                    TimeSpan.FromSeconds(_config.TimeoutSeconds), ct);
                var content = ExtractContent(body);
                if (model == _config.FallbackModel) content = StripThinkTags(content);
                if (string.IsNullOrWhiteSpace(content)) continue; // truly blank output -> try fallback
                return Sanitize(content);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _cooldownUntil[model] = DateTimeOffset.UtcNow.AddSeconds(30);
                EnteringCooldown?.Invoke(model);
            }
        }
        return selectedText; // all models failed -> safe fallback to the unmodified text
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

    private string BuildRewriteSystemPrompt()
    {
        var s = Kivi.Core.Prompts.Prompts.CommandModeSystem;
        var vocab = string.Join(", ", _config.CustomVocabulary
            .Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct());
        if (vocab.Length > 0) s += "\n\n" + Kivi.Core.Prompts.Prompts.VocabularyAppend(vocab);
        return s;
    }

    private object BuildPayload(string model, string system, string user)
    {
        var msgs = new object[] {
            new { role = "system", content = system },
            new { role = "user", content = user }
        };
        if (model == _config.CleanupModel)
            return new { model, temperature = 0.0, max_completion_tokens = 4096, reasoning_effort = "low", include_reasoning = false, messages = msgs };
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
```

- [ ] **Step 4: Delete the Groq polish client and its tests**

```bash
rm Kivi.Core/Polish/GroqPolishClient.cs Kivi.Core.Tests/GroqPolishClientTests.cs
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~SarvamPolishClientTests"`
Expected: PASS — 8 tests.

- [ ] **Step 6: Commit**

```bash
git add Kivi.Core/Polish/SarvamPolishClient.cs Kivi.Core.Tests/SarvamPolishClientTests.cs
git rm Kivi.Core/Polish/GroqPolishClient.cs Kivi.Core.Tests/GroqPolishClientTests.cs
git commit -m "feat(core): replace GroqPolishClient with SarvamPolishClient (sarvam-30b/105b)"
```

---

### Task 5: Wire `Kivi.App` DI to Sarvam, rename env var, update `.env.example`

**Files:**
- Modify: `Kivi.App/App.xaml.cs:78,84-85`
- Modify: `.env.example`

**Interfaces:**
- Consumes: `SarvamSttEngine` (Task 3), `SarvamPolishClient` (Task 4).
- Produces: no new public interface — this is composition-root wiring only.

- [ ] **Step 1: Update `.env.example`**

Replace the full content of `.env.example`:

```
# Kivi environment template — copy to .env and fill in your values.
# .env is git-ignored; .env.example is committed as documentation.

# Sarvam API key (used for both /speech-to-text and /v1/chat/completions).
# Get one at https://dashboard.sarvam.ai
SARVAM_API_KEY=

# Optional: set to 1 to enable embedded OpenTelemetry metrics
# (process RSS/CPU + per-stage dictation latency, printed to the console).
# Equivalent to passing --metrics on the command line.
KIVI_METRICS=0
```

- [ ] **Step 2: Update `App.xaml.cs` DI registrations**

In `Kivi.App/App.xaml.cs`, change line 78:

```csharp
            var envKey = configuration["SARVAM_API_KEY"];
```

Change lines 84-85:

```csharp
        services.AddSingleton<ISttEngine, SarvamSttEngine>();
        services.AddSingleton<IPolishClient, SarvamPolishClient>();
```

Check the top of the file for `using Kivi.Core.Stt;` / `using Kivi.Core.Polish;` — these using directives already exist (they imported `GroqSttEngine`/`GroqPolishClient` from the same namespaces), so no using-directive changes are needed since `SarvamSttEngine`/`SarvamPolishClient` live in the identical namespaces (`Kivi.Core.Stt`, `Kivi.Core.Polish`).

- [ ] **Step 3: Search the whole App project for any other Groq references**

Run: `grep -rn "Groq" Kivi.App/ --include="*.cs"`
Expected: no output. If any references remain (e.g. in comments, XAML, or other files), update them to reference Sarvam/remove them.

- [ ] **Step 4: Build the App project**

Run: `dotnet build Kivi.App`
Expected: builds clean, 0 errors.

- [ ] **Step 5: Update your local `.env` file (not committed)**

If you have a local `.env` file with `GROQ_API_KEY=...`, manually rename the variable to `SARVAM_API_KEY=` and set it to a real Sarvam API key before running the app, since `.env` is git-ignored and this plan cannot modify it for you.

- [ ] **Step 6: Commit**

```bash
git add Kivi.App/App.xaml.cs .env.example
git commit -m "feat(app): wire DI and env var to Sarvam (SARVAM_API_KEY)"
```

---

### Task 6: Full solution build and test verification

**Files:** none (verification-only task).

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`
Expected: all tests pass, including `Kivi.Core.Tests` (with the new `SarvamSttEngineTests`/`SarvamPolishClientTests`, without the deleted `GroqSttEngineTests`/`GroqPolishClientTests`), `AppConfigTests`, `AppConfigStoreTests`, `OrchestratorTests`, `PolishPipelineTests`, `OpenAiCompatibleClientTests` (with the two new Sarvam-transcription tests), `KiviMetricsTests`, `MacroTests`, `PromptsTests`, `WordDiffTests`.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build Kivi.sln`
Expected: builds clean, 0 errors, 0 warnings about missing Groq types.

- [ ] **Step 3: Grep the whole repo for any remaining "Groq" references outside `_reference/`**

Run: `grep -rln "Groq" --include="*.cs" --include="*.md" --include="*.json" . | grep -v "_reference/" | grep -v "/obj/" | grep -v "/bin/"`
Expected: no output, or only historical references inside `docs/superpowers/specs/` and `docs/superpowers/plans/` prior-dated design docs (which document history and should not be rewritten) and `README.MD` (update if it mentions Groq — see Step 4).

- [ ] **Step 4: Update `README.MD` if it references Groq**

Read `README.MD` in full. If it describes the pipeline as "hotkey hold → WASAPI mic → Groq Whisper → Groq LLM cleanup → paste" or similar, update those provider names to Sarvam (`Saaras v3` for STT, `Sarvam-30B` for cleanup).

- [ ] **Step 5: Commit any README fix**

```bash
git add README.MD
git commit -m "docs: update README pipeline description to reference Sarvam"
```

(Skip this commit if README.MD required no changes.)

---

## Self-Review Notes

- **Spec coverage:** Part 1 of the spec (model choice, API shape, implementation, key distribution) is fully covered — Tasks 1-2 handle config/HTTP plumbing, Tasks 3-4 handle the two provider implementations, Task 5 handles DI/env wiring, Task 6 verifies. The "key distribution" sub-section of the spec (embedded shared key, no per-user onboarding step) requires no code change in this plan — it's already how `App.xaml.cs`'s env-var-into-DPAPI caching works today (Task 5 just renames which env var it reads), and the actual key-baking-into-the-shipped-build mechanism belongs to the installer plan (Plan D), not here.
- **Open item from spec not covered here (deliberately deferred, not forgotten):** the hallucination-filter replacement is explicitly dropped rather than reimplemented, per the spec's own open-items list ("decide during implementation based on whether false transcriptions show up in testing") — there's no test data yet to design a `language_probability` threshold against.
- **Type consistency check:** `SarvamSttEngine`/`SarvamPolishClient` constructors match `GroqSttEngine`/`GroqPolishClient`'s exact parameter order and types, so Task 5's DI swap is a pure type substitution with no cascading signature changes elsewhere.
