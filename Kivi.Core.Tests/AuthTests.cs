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
            """{"identity":{"id":"u1","traits":{"email":"a@b.com","name":"Ada Lovelace"}}}"""));
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

    // ---- KratosOtpAuthClient: email-OTP ("code" login method) ----

    [Fact]
    public async Task StartFlowAsync_ParsesIdAndActionUrl()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
            """{"id":"flow-1","ui":{"action":"https://login.sarvam.ai/identity/self-service/login?flow=flow-1"}}"""));
        var client = new KratosOtpAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var flow = await client.StartFlowAsync(CancellationToken.None);

        Assert.Equal("flow-1", flow.FlowId);
        Assert.Equal("https://login.sarvam.ai/identity/self-service/login?flow=flow-1", flow.ActionUrl);
        // No return_to at all for the code method's flow-creation GET.
        Assert.DoesNotContain("return_to", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task RequestCodeAsync_200_ReturnsUpdatedFlow()
    {
        var handler = new FakeHandler(req =>
        {
            Assert.Contains("self-service/login?flow=flow-1", req.RequestUri!.ToString());
            return Json(HttpStatusCode.OK,
                """{"id":"flow-1","state":"sent_email","ui":{"action":"https://login.sarvam.ai/identity/self-service/login?flow=flow-1"}}""");
        });
        var client = new KratosOtpAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var updated = await client.RequestCodeAsync("flow-1",
            "https://login.sarvam.ai/identity/self-service/login?flow=flow-1", "a@b.com", CancellationToken.None);

        Assert.Equal("flow-1", updated.FlowId);
    }

    [Fact]
    public async Task RequestCodeAsync_400WithUiMessage_ThrowsCleanError()
    {
        // Live-confirmed shape (2026-07-29, nonexistent probe email): 400 with the flow envelope
        // still present and ui.messages[] carrying the actual error text.
        var handler = new FakeHandler(_ => Json(HttpStatusCode.BadRequest,
            """{"id":"flow-1","state":"choose_method","ui":{"action":"https://login.sarvam.ai/identity/self-service/login?flow=flow-1","messages":[{"id":4000035,"text":"This account does not exist or has not setup sign in with code.","type":"error"}]}}"""));
        var client = new KratosOtpAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var ex = await Assert.ThrowsAsync<KratosAuthException>(() =>
            client.RequestCodeAsync("flow-1", "https://login.sarvam.ai/identity/self-service/login?flow=flow-1", "nobody@example.com", CancellationToken.None));

        Assert.Equal("This account does not exist or has not setup sign in with code.", ex.Message);
    }

    [Fact]
    public async Task RequestCodeAsync_400WithNoParseableBody_ThrowsWithRawBodyFallback()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("not json", Encoding.UTF8, "text/plain"),
        });
        var client = new KratosOtpAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var ex = await Assert.ThrowsAsync<KratosAuthException>(() =>
            client.RequestCodeAsync("flow-1", "https://login.sarvam.ai/identity/self-service/login?flow=flow-1", "a@b.com", CancellationToken.None));

        Assert.Contains("not json", ex.Message);
    }

    [Fact]
    public async Task SubmitCodeAsync_Success_ReturnsSessionToken()
    {
        var handler = new FakeHandler(req =>
        {
            return Json(HttpStatusCode.OK, """{"session_token":"kratos-session-otp-xyz"}""");
        });
        var client = new KratosOtpAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var token = await client.SubmitCodeAsync("flow-1",
            "https://login.sarvam.ai/identity/self-service/login?flow=flow-1", "a@b.com", "123456", CancellationToken.None);

        Assert.Equal("kratos-session-otp-xyz", token);
    }

    [Fact]
    public async Task SubmitCodeAsync_MissingSessionTokenField_ThrowsStructuralError()
    {
        // A 2xx whose body doesn't match the documented api-flow contract must fail loud, not
        // silently assume success.
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{"unexpected":"shape"}"""));
        var client = new KratosOtpAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var ex = await Assert.ThrowsAsync<KratosAuthException>(() =>
            client.SubmitCodeAsync("flow-1", "https://login.sarvam.ai/identity/self-service/login?flow=flow-1", "a@b.com", "123456", CancellationToken.None));

        Assert.Contains("session_token", ex.Message);
    }

    [Fact]
    public async Task SubmitCodeAsync_WrongCode_ThrowsCleanInvalidCodeError()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.BadRequest,
            """{"id":"flow-1","state":"choose_method","ui":{"action":"https://login.sarvam.ai/identity/self-service/login?flow=flow-1","messages":[{"id":4000006,"text":"The provided authentication code is invalid, please try again.","type":"error"}]}}"""));
        var client = new KratosOtpAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));

        var ex = await Assert.ThrowsAsync<KratosAuthException>(() =>
            client.SubmitCodeAsync("flow-1", "https://login.sarvam.ai/identity/self-service/login?flow=flow-1", "a@b.com", "000000", CancellationToken.None));

        Assert.Equal("The provided authentication code is invalid, please try again.", ex.Message);
    }

    // ---- AuthController: email-OTP orchestration ----

    [Fact]
    public async Task StartEmailOtpAsync_ThreadsFlowThroughToHandle()
    {
        var handler = new FakeHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("self-service/login/api"))
                return Json(HttpStatusCode.OK, """{"id":"flow-1","ui":{"action":"https://login.sarvam.ai/identity/self-service/login?flow=flow-1"}}""");
            if (url.Contains("self-service/login?flow=flow-1"))
                return Json(HttpStatusCode.OK, """{"id":"flow-1","state":"sent_email","ui":{"action":"https://login.sarvam.ai/identity/self-service/login?flow=flow-1"}}""");
            throw new InvalidOperationException("unexpected URL: " + url);
        });
        var kratos = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));
        var otp = new KratosOtpAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));
        var orgJwt = new OrgJwtClient(new HttpClient(new FakeHandler(_ => throw new InvalidOperationException("should not mint"))), new Uri("https://auth.sarvam.ai/"));
        var secrets = new FakeSecretStore();
        var controller = new AuthController(kratos, orgJwt, secrets, kratosOtp: otp);

        var handleResult = await controller.StartEmailOtpAsync("a@b.com");

        Assert.Equal("flow-1", handleResult.FlowId);
        Assert.Equal("a@b.com", handleResult.Email);
    }

    [Fact]
    public async Task SubmitEmailOtpAsync_Success_PersistsTokensAndSignsIn()
    {
        var handler = new FakeHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("self-service/login?flow=flow-1"))
                return Json(HttpStatusCode.OK, """{"session_token":"kratos-otp-session"}""");
            if (url.Contains("sessions/whoami"))
                return Json(HttpStatusCode.OK, """{"identity":{"id":"u1","traits":{"email":"a@b.com","name":"Ada Lovelace"}}}""");
            if (url.Contains("api/v2/auth/jwt"))
                return Json(HttpStatusCode.OK, """{"token":"jwt-otp","expires_at":"2026-07-28T12:15:00Z"}""");
            throw new InvalidOperationException("unexpected URL: " + url);
        });
        var kratos = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));
        var otp = new KratosOtpAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));
        var orgJwt = new OrgJwtClient(new HttpClient(handler), new Uri("https://auth.sarvam.ai/"));
        var secrets = new FakeSecretStore();
        var controller = new AuthController(kratos, orgJwt, secrets, kratosOtp: otp);

        var handle = new OtpFlowHandle("flow-1", "https://login.sarvam.ai/identity/self-service/login?flow=flow-1", "a@b.com");
        var result = await controller.SubmitEmailOtpAsync(handle, "123456");

        Assert.Equal(SignInOutcome.Success, result.Outcome);
        Assert.True(controller.IsSignedIn);
        Assert.Equal("a@b.com", controller.Email);
        Assert.Equal("kratos-otp-session", secrets.Read("kratosSessionToken"));
        Assert.Equal("jwt-otp", secrets.Read("orgServiceJWT"));
    }

    [Fact]
    public async Task SubmitEmailOtpAsync_WrongCode_ReturnsInvalidCode_NotSignedIn()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.BadRequest,
            """{"id":"flow-1","state":"choose_method","ui":{"action":"https://login.sarvam.ai/identity/self-service/login?flow=flow-1","messages":[{"id":4000006,"text":"The provided authentication code is invalid, please try again.","type":"error"}]}}"""));
        var kratos = new KratosAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));
        var otp = new KratosOtpAuthClient(new HttpClient(handler), new Uri("https://login.sarvam.ai/identity/"));
        var orgJwt = new OrgJwtClient(new HttpClient(new FakeHandler(_ => throw new InvalidOperationException("should not mint"))), new Uri("https://auth.sarvam.ai/"));
        var secrets = new FakeSecretStore();
        var controller = new AuthController(kratos, orgJwt, secrets, kratosOtp: otp);

        var handle = new OtpFlowHandle("flow-1", "https://login.sarvam.ai/identity/self-service/login?flow=flow-1", "a@b.com");
        var result = await controller.SubmitEmailOtpAsync(handle, "000000");

        Assert.Equal(SignInOutcome.InvalidCode, result.Outcome);
        Assert.False(controller.IsSignedIn);
    }

    [Fact]
    public async Task StartEmailOtpAsync_WithoutOtpClient_Throws()
    {
        var kratos = new KratosAuthClient(new HttpClient(new FakeHandler(_ => throw new InvalidOperationException())), new Uri("https://login.sarvam.ai/identity/"));
        var orgJwt = new OrgJwtClient(new HttpClient(new FakeHandler(_ => throw new InvalidOperationException())), new Uri("https://auth.sarvam.ai/"));
        var controller = new AuthController(kratos, orgJwt, new FakeSecretStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StartEmailOtpAsync("a@b.com"));
    }
}
