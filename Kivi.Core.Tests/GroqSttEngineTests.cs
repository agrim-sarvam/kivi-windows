using System.Text.Json;
using Kivi.Core.Config;
using Kivi.Core.Http;
using Kivi.Core.Stt;
using Xunit;

public class GroqSttEngineTests
{
    private sealed class FakeSecrets : Kivi.Core.Abstractions.ISecretStore
    {
        public string? GetApiKey() => "k";
        public void SetApiKey(string key) { }
    }

    private static GroqSttEngine Engine(string responseBody)
    {
        var fake = FakeHttpMessageHandler.Json(responseBody);
        return new GroqSttEngine(new OpenAiCompatibleClient(new HttpClient(fake)), AppConfig.Default(), new FakeSecrets());
    }

    [Fact]
    public async Task ReturnsText_ForNormalSpeech()
    {
        var engine = Engine("{\"text\":\"Hello world\",\"segments\":[{\"no_speech_prob\":0.01}]}");
        Assert.Equal("Hello world", await engine.TranscribeAsync(new byte[]{1}, default));
    }

    [Fact]
    public async Task FiltersHallucination_WhenHighNoSpeechProb()
    {
        var engine = Engine("{\"text\":\"Thank you.\",\"segments\":[{\"no_speech_prob\":0.9}]}");
        Assert.Equal("", await engine.TranscribeAsync(new byte[]{1}, default));
    }

    [Fact]
    public async Task KeepsPhrase_WhenLowNoSpeechProb()
    {
        var engine = Engine("{\"text\":\"Thank you\",\"segments\":[{\"no_speech_prob\":0.02}]}");
        Assert.Equal("Thank you", await engine.TranscribeAsync(new byte[]{1}, default));
    }

    [Fact]
    public async Task DoesNotFilter_WhenNoSegments()
    {
        var engine = Engine("{\"text\":\"you\"}");
        Assert.Equal("you", await engine.TranscribeAsync(new byte[]{1}, default));
    }
}
