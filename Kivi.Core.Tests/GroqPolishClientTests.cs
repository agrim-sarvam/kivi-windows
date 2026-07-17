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
}
