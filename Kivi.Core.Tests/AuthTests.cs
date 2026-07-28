using System.Net;
using System.Net.Http;
using System.Text;
using Kivi.Platform.Auth;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>
/// Unit tests for the auth pieces that are testable without a live browser/Kratos round-trip:
/// Kratos response parsing (login flow, 422 redirect_browser_to, token-exchange, whoami tri-state),
/// the org-JWT expiry/refresh-margin logic, and the account-linking-required detection in
/// AuthController. A real Google sign-in cannot be automated — that remains a documented manual
/// verification step (see the task report).
/// </summary>
public class AuthTests
{
    // ---- test doubles ----

    /// <summary>Routes requests to a caller-supplied responder by absolute URL (path+query).</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class FakeSecretStore : Kivi.Core.Contracts.ISecretStore
    {
        private readonly Dictionary<string, string> _store = new();
        public string? Read(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public void Write(string key, string value) => _store[key] = value;
    }

    // ---- KratosAuthClient: login flow ----

    [Fact]
    public async Task CreateLoginFlowAsync_ParsesActionUrl()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
            """{"ui":{"action":"https://login.sarvam.ai/identity/self-service/login?flow=abc"}}"""));
        var client = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var action = await client.CreateLoginFlowAsync("http://127.0.0.1:51234/callback", CancellationToken.None);

        Assert.Equal("https://login.sarvam.ai/identity/self-service/login?flow=abc", action);
        Assert.Contains("return_session_token_exchange_code=true", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task CreateLoginFlowAsync_MissingAction_Throws()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{"ui":{}}"""));
        var client = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        await Assert.ThrowsAsync<KratosAuthException>(() =>
            client.CreateLoginFlowAsync("http://127.0.0.1:51234/callback", CancellationToken.None));
    }

    // ---- KratosAuthClient: OIDC submit / 422 redirect ----

    [Fact]
    public async Task SubmitOidcAsync_On422_AppendsPromptSelectAccount()
    {
        var handler = new FakeHandler(_ => Json((HttpStatusCode)422,
            """{"redirect_browser_to":"https://accounts.google.com/o/oauth2/auth?client_id=x"}"""));
        var client = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var url = await client.SubmitOidcAsync("https://login.sarvam.ai/identity/self-service/login?flow=abc", CancellationToken.None);

        Assert.EndsWith("&prompt=select_account", url);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/auth?client_id=x", url);
    }

    [Fact]
    public async Task SubmitOidcAsync_NonExpectedStatus_Throws()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var client = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        await Assert.ThrowsAsync<KratosAuthException>(() =>
            client.SubmitOidcAsync("https://login.sarvam.ai/identity/self-service/login?flow=abc", CancellationToken.None));
    }

    // ---- KratosAuthClient: token exchange ----

    [Fact]
    public async Task ExchangeCodeForSessionTokenAsync_ParsesSessionToken()
    {
        var handler = new FakeHandler(req =>
        {
            var q = req.RequestUri!.Query;
            Assert.Contains("init_code=abc123", q);
            Assert.Contains("return_to_code=abc123", q);
            return Json(HttpStatusCode.OK, """{"session_token":"kratos-session-xyz"}""");
        });
        var client = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var token = await client.ExchangeCodeForSessionTokenAsync("abc123", CancellationToken.None);

        Assert.Equal("kratos-session-xyz", token);
    }

    // ---- KratosAuthClient: whoami tri-state arbiter ----

    [Fact]
    public async Task WhoamiAsync_200_ReturnsAliveWithIdentity()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
            """{"identity":{"id":"u1","traits":{"email":"a@b.com","name":{"first":"Ada","last":"Lovelace"}}}}"""));
        var client = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var who = await client.WhoamiAsync("sess", CancellationToken.None);

        Assert.False(who.IsDead);
        Assert.False(who.IsDegraded);
        Assert.Equal("u1", who.UserId);
        Assert.Equal("a@b.com", who.Email);
        Assert.Equal("Ada Lovelace", who.DisplayName);
    }

    [Fact]
    public async Task WhoamiAsync_401_IsDead()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var who = await client.WhoamiAsync("sess", CancellationToken.None);

        Assert.True(who.IsDead);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task WhoamiAsync_5xxOr403_IsDegraded_NotDead(HttpStatusCode status)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));
        var client = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var who = await client.WhoamiAsync("sess", CancellationToken.None);

        Assert.False(who.IsDead);
        Assert.True(who.IsDegraded);
    }

    [Fact]
    public async Task WhoamiAsync_NetworkError_IsDegraded_NotDead()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("network down"));
        var client = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var who = await client.WhoamiAsync("sess", CancellationToken.None);

        Assert.False(who.IsDead);
        Assert.True(who.IsDegraded);
    }

    // ---- OrgJwtClient: mint + expiry/refresh-margin logic ----

    [Fact]
    public async Task MintAsync_ParsesTokenAndExpiry_CachesIt()
    {
        var handler = new FakeHandler(req =>
        {
            Assert.Equal("sess-tok", req.Headers.GetValues("X-Session-Token").Single());
            return Json(HttpStatusCode.OK, """{"token":"jwt-abc","expires_at":"2026-07-28T12:15:00Z"}""");
        });
        var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        var client = new OrgJwtClient(new HttpClient(handler), new Uri("https://auth.sarvam.ai/"), () => now);

        var jwt = await client.MintAsync("sess-tok", ct: CancellationToken.None);

        Assert.Equal("jwt-abc", jwt.Token);
        Assert.Equal(new DateTimeOffset(2026, 7, 28, 12, 15, 0, TimeSpan.Zero), jwt.ExpiresAt);
        Assert.Equal("jwt-abc", client.CachedToken);
    }

    [Fact]
    public async Task RefreshIfNeeded_WithinMargin_DoesNotReMint()
    {
        var callCount = 0;
        var handler = new FakeHandler(_ =>
        {
            callCount++;
            return Json(HttpStatusCode.OK, """{"token":"jwt-1","expires_at":"2026-07-28T12:15:00Z"}""");
        });
        var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        var client = new OrgJwtClient(new HttpClient(handler), new Uri("https://auth.sarvam.ai/"), () => now);

        await client.MintAsync("sess-tok", ct: CancellationToken.None); // expires 12:15, now 12:00 -> fine
        Assert.Equal(1, callCount);

        // Still >2min from expiry (now unchanged) -> should NOT re-mint.
        var jwt = await client.RefreshIfNeeded("sess-tok", ct: CancellationToken.None);
        Assert.Equal(1, callCount);
        Assert.Equal("jwt-1", jwt.Token);
    }

    [Fact]
    public async Task RefreshIfNeeded_WithinTwoMinutesOfExpiry_ReMints()
    {
        var callCount = 0;
        var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        var handler = new FakeHandler(_ =>
        {
            callCount++;
            // Second mint issues a fresh token/expiry.
            var expires = callCount == 1 ? "2026-07-28T12:01:30Z" : "2026-07-28T12:16:30Z";
            return Json(HttpStatusCode.OK, $$"""{"token":"jwt-{{callCount}}","expires_at":"{{expires}}"}""");
        });
        var client = new OrgJwtClient(new HttpClient(handler), new Uri("https://auth.sarvam.ai/"), () => now);

        await client.MintAsync("sess-tok", ct: CancellationToken.None); // expires 12:01:30 -> within 2min margin of "now"=12:00
        Assert.Equal(1, callCount);

        // now (12:00) is within 2 minutes of expiry (12:01:30) -> IsExpiredOrExpiringSoon is true -> re-mint.
        var jwt = await client.RefreshIfNeeded("sess-tok", ct: CancellationToken.None);
        Assert.Equal(2, callCount);
        Assert.Equal("jwt-2", jwt.Token);
    }

    [Fact]
    public async Task MintAsync_Retries403TwiceThenSucceeds()
    {
        var attempt = 0;
        var handler = new FakeHandler(_ =>
        {
            attempt++;
            if (attempt <= 2) return new HttpResponseMessage(HttpStatusCode.Forbidden);
            return Json(HttpStatusCode.OK, """{"token":"jwt-ok","expires_at":"2026-07-28T12:15:00Z"}""");
        });
        var client = new OrgJwtClient(new HttpClient(handler), new Uri("https://auth.sarvam.ai/"));

        var jwt = await client.MintAsync("sess-tok", ct: CancellationToken.None);

        Assert.Equal(3, attempt);
        Assert.Equal("jwt-ok", jwt.Token);
    }

    [Fact]
    public async Task MintAsync_403ThreeTimes_ThrowsAfterTwoRetries()
    {
        var attempt = 0;
        var handler = new FakeHandler(_ =>
        {
            attempt++;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        var client = new OrgJwtClient(new HttpClient(handler), new Uri("https://auth.sarvam.ai/"));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.MintAsync("sess-tok", ct: CancellationToken.None));
        Assert.Equal(3, attempt); // initial + 2 retries
    }

    // ---- AuthController: account-linking-required detection ----

    [Fact]
    public async Task SignInWithGoogleAsync_MissingCodeNoError_ReturnsAccountLinkingRequired()
    {
        // Build a controller with fakes: the loopback listener resolves with neither code nor error.
        var loginFlowHandler = new FakeHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("self-service/login/api"))
                return Json(HttpStatusCode.OK, """{"ui":{"action":"https://login.sarvam.ai/identity/self-service/login?flow=abc"}}""");
            if (url.Contains("self-service/login?flow=abc"))
                return Json((HttpStatusCode)422, """{"redirect_browser_to":"https://accounts.google.com/auth"}""");
            throw new InvalidOperationException("unexpected URL: " + url);
        });
        var kratos = new KratosAuthClient(new HttpClient(loginFlowHandler), new Uri("https://login.sarvam.ai/identity/"));
        var orgJwt = new OrgJwtClient(new HttpClient(new FakeHandler(_ => throw new InvalidOperationException("should not mint"))), new Uri("https://auth.sarvam.ai/"));
        var secrets = new FakeSecretStore();

        var controller = new AuthController(
            kratos, orgJwt, secrets,
            listenerFactory: () => new FakeListenerNoCode(),
            openBrowser: _ => { /* no-op: don't actually launch a browser in tests */ });

        var result = await controller.SignInWithGoogleAsync();

        Assert.Equal(SignInOutcome.AccountLinkingRequired, result.Outcome);
        Assert.False(controller.IsSignedIn);
    }

    /// <summary>A loopback listener double that immediately resolves with no code and no error.</summary>
    private sealed class FakeListenerNoCode : IOAuthLoopbackListener
    {
        public string CallbackUrl => "http://127.0.0.1:51234/callback";
        public Task<OAuthCallbackResult> WaitForCodeAsync(CancellationToken ct) =>
            Task.FromResult(new OAuthCallbackResult(Code: null, Fragment: null, Error: null));
        public void Dispose() { }
    }
}
