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
        Assert.Equal("Hello world", await engine.TranscribeAsync(new byte[] { 1 }, SttMode.Hinglish, default));
    }

    [Fact]
    public async Task ReturnsEmptyString_WhenTranscriptFieldMissing()
    {
        var engine = Engine("{\"request_id\":\"r1\",\"language_code\":\"en-IN\"}");
        Assert.Equal("", await engine.TranscribeAsync(new byte[] { 1 }, SttMode.Hinglish, default));
    }

    [Fact]
    public async Task SendsTheRequestedModeToSarvam()
    {
        var fake = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("{\"transcript\":\"x\"}") });
        var engine = new SarvamSttEngine(new OpenAiCompatibleClient(new HttpClient(fake)), AppConfig.Default(), new FakeSecrets());

        await engine.TranscribeAsync(new byte[] { 1 }, SttMode.English, default);

        // multipart body carries the mode field the orchestrator selected for this hotkey.
        Assert.Contains("translate", fake.LastRequestBody);
    }

    [Fact]
    public async Task ThrowsInvalidOperationException_WhenApiKeyMissing()
    {
        var fake = FakeHttpMessageHandler.Json("{\"transcript\":\"x\"}");
        var noKeySecrets = new NoKeySecrets();
        var engine = new SarvamSttEngine(new OpenAiCompatibleClient(new HttpClient(fake)), AppConfig.Default(), noKeySecrets);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.TranscribeAsync(new byte[] { 1 }, SttMode.Hinglish, default));
    }

    private sealed class NoKeySecrets : Kivi.Core.Abstractions.ISecretStore
    {
        public string? GetApiKey() => null;
        public void SetApiKey(string key) { }
    }
}
