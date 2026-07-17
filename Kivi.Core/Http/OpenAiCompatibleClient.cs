using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kivi.Core.Http;

public sealed class OpenAiCompatibleClient
{
    private readonly HttpClient _http;
    public OpenAiCompatibleClient(HttpClient http) => _http = http;

    public async Task<string> PostTranscriptionAsync(string baseUrl, string apiKey, string model,
        string responseFormat, string? language, byte[] wav, string fileName, TimeSpan timeout, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(model), "model");
        content.Add(new StringContent(responseFormat), "response_format");
        if (!string.IsNullOrEmpty(language)) content.Add(new StringContent(language), "language");
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file, "file", fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/audio/transcriptions") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await SendAsync(req, timeout, ct);
    }

    public async Task<string> PostChatCompletionAsync(string baseUrl, string apiKey, object payload, TimeSpan timeout, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await SendAsync(req, timeout, ct);
    }

    private async Task<string> SendAsync(HttpRequestMessage req, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var resp = await _http.SendAsync(req, cts.Token);
        var body = await resp.Content.ReadAsStringAsync(cts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode}", null, resp.StatusCode);
        return body;
    }
}
