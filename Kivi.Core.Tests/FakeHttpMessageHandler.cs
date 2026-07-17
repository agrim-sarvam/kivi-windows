using System.Net;
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        if (request.Content is not null) LastRequestBody = await request.Content.ReadAsStringAsync(ct);
        return _responder(request);
    }
    public static FakeHttpMessageHandler Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(code) { Content = new StringContent(body) });
}
