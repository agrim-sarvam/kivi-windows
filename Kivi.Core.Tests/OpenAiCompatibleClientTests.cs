using System.Linq;
using System.Net;
using Kivi.Core.Http;
using Xunit;

public class OpenAiCompatibleClientTests
{
    [Fact]
    public async Task Transcription_SendsMultipart_WithBearerAndFields()
    {
        var fake = FakeHttpMessageHandler.Json("{\"text\":\"hi\"}");
        var client = new OpenAiCompatibleClient(new HttpClient(fake));
        var body = await client.PostTranscriptionAsync(
            "https://api.groq.com/openai/v1", "sk-test", "whisper-large-v3",
            "verbose_json", null, new byte[] { 1, 2, 3 }, "audio.wav",
            TimeSpan.FromSeconds(20), default);

        Assert.Contains("\"text\":\"hi\"", body);
        Assert.Equal("Bearer", fake.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", fake.LastRequest.Headers.Authorization.Parameter);
        Assert.EndsWith("/audio/transcriptions", fake.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("multipart/form-data", fake.LastRequest.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Chat_NonSuccess_ThrowsWithStatus()
    {
        var fake = FakeHttpMessageHandler.Json("rate limited", HttpStatusCode.TooManyRequests);
        var client = new OpenAiCompatibleClient(new HttpClient(fake));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.PostChatCompletionAsync("https://api.groq.com/openai/v1", "k",
                new { model = "m" }, TimeSpan.FromSeconds(20), default));
        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
    }

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
}
