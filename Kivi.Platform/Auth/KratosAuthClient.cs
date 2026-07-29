using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kivi.Platform.Auth;

/// <summary>
/// Ory Kratos identity flow client (map §3.4/§3.5). Pure HTTP + one browser hop — fully portable
/// from the Electron reference. Sequence (exact, per the map):
///
///   1. GET {KratosURL}/self-service/login/api?return_to=&lt;callback&gt;&amp;return_session_token_exchange_code=true
///      → a login flow (has an <c>action</c> URL for step 2).
///   2. POST the flow's action URL with {"method":"oidc","provider":"google"}
///      → expect HTTP 422 with a body containing <c>redirect_browser_to</c>.
///   3. Append <c>&amp;prompt=select_account</c> to that URL, open it in the default browser.
///   4. Await the loopback callback's <c>?code=</c> (missing code ⇒ email collision ⇒
///      account-linking-required).
///   5. GET {KratosURL}/sessions/token-exchange?init_code=&lt;code&gt;&amp;return_to_code=&lt;code&gt;
///      → a Kratos <c>session_token</c> (NOT the org JWT — see <see cref="OrgJwtClient"/>).
///   6. GET {KratosURL}/sessions/whoami with X-Session-Token — 401 = dead session; 5xx/403/network
///      errors = degraded-but-signed-in (never destroy the session over those).
///
/// Injectable <see cref="HttpClient"/> so response-parsing logic is unit-testable without a live
/// Kratos instance.
/// </summary>
public sealed class KratosAuthClient
{
    private readonly HttpClient _http;
    private readonly Uri _kratosBaseUrl;

    public KratosAuthClient(HttpClient http, Uri kratosBaseUrl)
    {
        _http = http;
        _kratosBaseUrl = kratosBaseUrl;
    }

    /// <summary>Step 1: create a login flow, return its <c>action</c> URL (used for the OIDC submit).</summary>
    public async Task<string> CreateLoginFlowAsync(string callbackUrl, CancellationToken ct)
    {
        var url = $"{_kratosBaseUrl}self-service/login/api" +
                  $"?return_to={Uri.EscapeDataString(callbackUrl)}&return_session_token_exchange_code=true";

        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            // Confirmed by direct testing (2026-07): Kratos's allowed_return_urls has NO loopback
            // (127.0.0.1/localhost) pattern registered on this identity instance — it rejects
            // return_to for ANY host/port combination with 400 self_service_flow_return_to_forbidden.
            // This is a server-side Kratos config gap, not fixable client-side (no port/host choice
            // here changes the outcome) — surface a specific, actionable message instead of a raw
            // HTTP exception dump, so the user knows to escalate rather than retry.
            if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var body = await SafeReadBodyAsync(resp, ct).ConfigureAwait(false);
                if (body.Contains("return_to_forbidden", StringComparison.OrdinalIgnoreCase))
                {
                    throw new KratosAuthException(
                        "Sign-in is blocked by a server config issue: Kratos doesn't allow the " +
                        "loopback redirect URL this desktop app uses (needed for native OAuth). " +
                        "Ask whoever administers Kratos to add a 127.0.0.1/localhost pattern to " +
                        "allowed_return_urls for this identity instance. Use \"skip / use local\" " +
                        "for now.");
                }
            }
            resp.EnsureSuccessStatusCode(); // any other failure: fall through to the generic exception
        }

        var flow = await resp.Content.ReadFromJsonAsync<KratosLoginFlow>(JsonOpts, ct).ConfigureAwait(false)
                   ?? throw new KratosAuthException("Kratos login flow response was empty.");

        var action = flow.Ui?.Action;
        if (string.IsNullOrEmpty(action))
            throw new KratosAuthException("Kratos login flow response had no ui.action URL.");
        return action;
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Step 2/3: submit the OIDC method to the flow's action URL. Kratos responds 422 with
    /// <c>redirect_browser_to</c> — we return that URL with <c>prompt=select_account</c> appended.
    /// </summary>
    public async Task<string> SubmitOidcAsync(string actionUrl, CancellationToken ct)
    {
        var body = new { method = "oidc", provider = "google" };
        using var resp = await _http.PostAsJsonAsync(actionUrl, body, JsonOpts, ct).ConfigureAwait(false);

        // Kratos's browser-redirect convention for the API-flavored flow is HTTP 422 carrying the
        // URL to send the user's browser to next.
        if (resp.StatusCode != System.Net.HttpStatusCode.UnprocessableEntity)
        {
            throw new KratosAuthException(
                $"Expected HTTP 422 redirect_browser_to from Kratos OIDC submit, got {(int)resp.StatusCode}.");
        }

        var payload = await resp.Content.ReadFromJsonAsync<KratosRedirectBrowserTo>(JsonOpts, ct).ConfigureAwait(false);
        var redirect = payload?.RedirectBrowserTo;
        if (string.IsNullOrEmpty(redirect))
            throw new KratosAuthException("Kratos 422 response had no redirect_browser_to field.");

        var separator = redirect.Contains('?') ? "&" : "?";
        return redirect + separator + "prompt=select_account";
    }

    /// <summary>Opens the given URL in the user's default browser.</summary>
    public void OpenInBrowser(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>
    /// Step 5: exchange the loopback <c>code</c> for a Kratos session token. Both query params carry
    /// the same code value per the map ("init_code and return_to_code").
    /// </summary>
    public async Task<string> ExchangeCodeForSessionTokenAsync(string code, CancellationToken ct)
    {
        var url = $"{_kratosBaseUrl}sessions/token-exchange" +
                  $"?init_code={Uri.EscapeDataString(code)}&return_to_code={Uri.EscapeDataString(code)}";

        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<KratosTokenExchangeResponse>(JsonOpts, ct).ConfigureAwait(false);
        var token = payload?.SessionToken;
        if (string.IsNullOrEmpty(token))
            throw new KratosAuthException("Kratos token-exchange response had no session_token.");
        return token;
    }

    /// <summary>
    /// Step 6: validate/fetch user info. Only a 401 means the session is dead; any other failure
    /// (5xx, 403, network) is "degraded but signed in" — caller should NOT destroy the session.
    /// </summary>
    public async Task<WhoamiResult> WhoamiAsync(string sessionToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_kratosBaseUrl}sessions/whoami");
        req.Headers.Add("X-Session-Token", sessionToken);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return WhoamiResult.Degraded();
        }

        using (resp)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return WhoamiResult.Dead();

            if (!resp.IsSuccessStatusCode)
                return WhoamiResult.Degraded();

            var session = await resp.Content.ReadFromJsonAsync<KratosSession>(JsonOpts, ct).ConfigureAwait(false);
            var identity = session?.Identity;
            var traits = identity?.Traits;
            return WhoamiResult.Alive(
                userId: identity?.Id,
                email: traits?.Email,
                displayName: traits?.Name is { Length: > 0 } name ? name : traits?.Email);
        }
    }

    internal static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}

public sealed class KratosAuthException : Exception
{
    public KratosAuthException(string message) : base(message) { }
}

/// <summary>Result of a Kratos <c>/sessions/whoami</c> call — the tri-state arbiter from map §3.5.</summary>
public sealed class WhoamiResult
{
    public bool IsDead { get; private init; }
    public bool IsDegraded { get; private init; }
    public string? UserId { get; private init; }
    public string? Email { get; private init; }
    public string? DisplayName { get; private init; }

    public static WhoamiResult Dead() => new() { IsDead = true };
    public static WhoamiResult Degraded() => new() { IsDegraded = true };
    public static WhoamiResult Alive(string? userId, string? email, string? displayName) =>
        new() { UserId = userId, Email = email, DisplayName = displayName };
}

// ---- Kratos wire shapes (only the fields we consume) ----

internal sealed class KratosLoginFlow
{
    [JsonPropertyName("ui")] public KratosUi? Ui { get; set; }
}

internal sealed class KratosUi
{
    [JsonPropertyName("action")] public string? Action { get; set; }
}

internal sealed class KratosRedirectBrowserTo
{
    [JsonPropertyName("redirect_browser_to")] public string? RedirectBrowserTo { get; set; }
}

internal sealed class KratosTokenExchangeResponse
{
    [JsonPropertyName("session_token")] public string? SessionToken { get; set; }
}

internal sealed class KratosSession
{
    [JsonPropertyName("identity")] public KratosIdentity? Identity { get; set; }
}

internal sealed class KratosIdentity
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("traits")] public KratosTraits? Traits { get; set; }
}

internal sealed class KratosTraits
{
    [JsonPropertyName("email")] public string? Email { get; set; }

    // Live-verified against https://login.sarvam.ai/identity/schemas: the identity schema declares
    // `traits.name` as {"type":"string","title":"Full Name"} — a plain string, NOT a {first,last}
    // object. The original {first,last} assumption threw a JsonException deserializing whoami
    // ("could not be converted to KratosName") the first time a real identity round-tripped.
    [JsonPropertyName("name")] public string? Name { get; set; }
}
