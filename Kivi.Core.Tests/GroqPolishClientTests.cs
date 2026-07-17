using System.Net;
using Kivi.Core.Config;
using Kivi.Core.Http;
using Kivi.Core.Polish;
using Xunit;

public class GroqPolishClientTests
{
    private sealed class FakeSecrets : Kivi.Core.Abstractions.ISecretStore
    { public string? GetApiKey() => "k"; public void SetApiKey(string key) { } }

    private static string Chat(string content) =>
        "{\"choices\":[{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(content) + "}}]}";

    private static GroqPolishClient Client(string body)
        => new(new OpenAiCompatibleClient(new HttpClient(FakeHttpMessageHandler.Json(body))), AppConfig.Default(), new FakeSecrets());

    /// <summary>
    /// Fake handler that returns a queued sequence of responses, one per request,
    /// in order. Used to exercise multi-call control flow (primary fails -> fallback
    /// called) where the single-canned-response FakeHttpMessageHandler can't help.
    /// </summary>
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

    private static GroqPolishClient SequencedClient(SequencedFakeHttpMessageHandler handler)
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
        // raw asks to "write an email"; model responded with an assistant preamble -> guard trips
        var client = Client(Chat("Sure, here is the email you asked for: Dear team ..."));
        var raw = "write an email to the team asking if friday works";
        Assert.Equal(raw, await client.CleanupAsync(raw, "", default));
    }

    [Fact]
    public async Task RateLimited_FallsBackToSecondModel_AndReturnsCleanedText()
    {
        // First request (CleanupModel) comes back 429 -> client should put it in cooldown
        // and retry with FallbackModel, which returns a normal cleaned completion.
        var handler = new SequencedFakeHttpMessageHandler(
            ("{\"error\":\"rate limited\"}", HttpStatusCode.TooManyRequests),
            (Chat("Hello there."), HttpStatusCode.OK));
        var client = SequencedClient(handler);

        var result = await client.CleanupAsync("hello there", "", default);

        Assert.Equal("Hello there.", result);
        Assert.Equal(2, handler.RequestBodies.Count);
        // Confirm the two requests actually targeted different models (primary then fallback).
        Assert.Contains(AppConfig.Default().CleanupModel, handler.RequestBodies[0]);
        Assert.Contains(AppConfig.Default().FallbackModel, handler.RequestBodies[1]);
    }

    [Fact]
    public async Task EmptyContentFromPrimary_FallsBackToSecondModel_AndReturnsCleanedText()
    {
        // First request (CleanupModel) returns blank content -> client should skip it
        // (no cooldown needed) and retry with FallbackModel, which returns real cleaned text.
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
}
