using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Kivi.Platform.Auth;

/// <summary>
/// Ory Kratos "code" (email OTP) login method — the unblock for the Google-OAuth
/// <c>return_to</c> allowlist gap (see <see cref="KratosAuthClient.CreateLoginFlowAsync"/>'s
/// doc comment). The <c>code</c> method group is a pure API call/response cycle with NO browser
/// redirect at all, so <c>return_to</c> never enters the picture — confirmed live against
/// <c>https://login.sarvam.ai/identity/self-service/login/api</c> on 2026-07-29:
///
///   GET  {KratosURL}self-service/login/api
///     → a flow whose <c>ui.nodes</c> include a <c>code</c> group ("Send sign in code") and a
///       shared <c>identifier</c> field (group "default") alongside the oidc/password groups.
///       No query params (no <c>return_to</c>) are needed for this GET — it's the SAME
///       flow-creation call <see cref="KratosAuthClient"/> uses for OIDC, just without the
///       return_to/return_session_token_exchange_code params that only matter for the browser
///       hop OIDC needs.
///
///   POST {flow.ui.action}  { "method": "code", "identifier": "&lt;email&gt;" }
///     → triggers Kratos to email a 6-digit code. Live-confirmed response shape: the SAME flow
///       envelope (id/ui/state/...) comes back, either:
///         - 200 with ui.state advancing to "sent_email" (documented Kratos behavior — the flow
///           needs a second submission with the code), or
///         - 400 with ui.state still "choose_method" and a ui.messages[] error (e.g. unknown
///           identifier: "This account does not exist or has not setup sign in with code.") —
///           confirmed live with a nonexistent probe address.
///       We tolerate both "shapes" (200 continuation or documented-error 400) structurally: any
///       response that parses as a flow is accepted as "code requested"; a 400 whose ui.messages
///       contains an error is surfaced as a clean, specific error instead.
///
///   POST {flow.ui.action}  { "method": "code", "identifier": "&lt;email&gt;", "code": "&lt;6 digits&gt;" }
///     → success: since this is an "api"-type flow (not browser/OIDC), Kratos returns a session
///       directly — NOT a redirect. We read <c>session_token</c> off the response body per Kratos's
///       api-flow convention (same field <see cref="KratosAuthClient.ExchangeCodeForSessionTokenAsync"/>
///       reads after its token-exchange hop). If that field is missing we throw a clear,
///       structural error rather than silently assuming success — this assumption is NOT yet
///       live-verified end-to-end (no real inbox was used), so a real server surprise here should
///       fail loud, not silently mis-parse.
///     → wrong/expired code: Kratos responds 400 with the flow's ui.messages[] carrying a
///       user-facing error (e.g. "the code you entered is incorrect or has expired") — we surface
///       a clean <see cref="KratosAuthException"/> with that text rather than a raw HTTP dump.
/// </summary>
public sealed class KratosOtpAuthClient
{
    private readonly HttpClient _http;
    private readonly Uri _kratosBaseUrl;

    public KratosOtpAuthClient(HttpClient http, Uri kratosBaseUrl)
    {
        _http = http;
        _kratosBaseUrl = kratosBaseUrl;
    }

    /// <summary>
    /// Creates a fresh Kratos login flow (no return_to needed — the code method never redirects).
    /// Returns the flow id + action URL the caller threads through <see cref="RequestCodeAsync"/>
    /// and <see cref="SubmitCodeAsync"/>.
    /// </summary>
    public async Task<KratosOtpFlow> StartFlowAsync(CancellationToken ct)
    {
        var url = $"{_kratosBaseUrl}self-service/login/api";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var flow = await resp.Content.ReadFromJsonAsync<KratosFlowEnvelope>(KratosAuthClient.JsonOpts, ct).ConfigureAwait(false)
                   ?? throw new KratosAuthException("Kratos login flow response was empty.");

        var action = flow.Ui?.Action;
        var id = flow.Id;
        if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(id))
            throw new KratosAuthException("Kratos login flow response had no ui.action/id.");

        return new KratosOtpFlow(id, action);
    }

    /// <summary>
    /// Step 1: request Kratos email a 6-digit code to <paramref name="email"/>. Accepts either a
    /// 200 continuation (flow advances toward "sent_email") or throws a clean, specific error if
    /// Kratos's ui.messages[] carries one (e.g. unknown identifier / code sign-in not set up).
    /// Returns the (possibly updated) flow the caller should carry into <see cref="SubmitCodeAsync"/>.
    /// </summary>
    public async Task<KratosOtpFlow> RequestCodeAsync(string flowId, string actionUrl, string email, CancellationToken ct)
    {
        var body = new { method = "code", identifier = email };
        using var resp = await _http.PostAsJsonAsync(actionUrl, body, KratosAuthClient.JsonOpts, ct).ConfigureAwait(false);

        var envelope = await TryReadFlowEnvelopeAsync(resp, ct).ConfigureAwait(false);

        if (resp.IsSuccessStatusCode)
        {
            var updatedAction = envelope?.Ui?.Action ?? actionUrl;
            var updatedId = envelope?.Id ?? flowId;
            return new KratosOtpFlow(updatedId, updatedAction);
        }

        // Non-2xx: surface Kratos's own ui.messages[] text if present (e.g. "account does not
        // exist"), otherwise fall back to the raw body so a future live-test can diagnose it.
        var message = ExtractUiMessage(envelope);
        if (message is not null)
            throw new KratosAuthException(message);

        var rawBody = await SafeReadBodyAsync(resp, ct).ConfigureAwait(false);
        throw new KratosAuthException(
            $"Kratos rejected the code request (HTTP {(int)resp.StatusCode}): {Truncate(rawBody)}");
    }

    /// <summary>
    /// Step 2: submit the 6-digit code. Success: this is an "api"-type flow, so Kratos returns a
    /// session directly (no return_to/redirect) — we read <c>session_token</c> off the body,
    /// structurally verifying the field exists rather than assuming. Failure (wrong/expired code):
    /// surfaces Kratos's ui.messages[] text as a clean "invalid or expired code" style error.
    /// </summary>
    public async Task<string> SubmitCodeAsync(string flowId, string actionUrl, string email, string code, CancellationToken ct)
    {
        var body = new { method = "code", identifier = email, code };
        using var resp = await _http.PostAsJsonAsync(actionUrl, body, KratosAuthClient.JsonOpts, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var envelope = await TryReadFlowEnvelopeAsync(resp, ct).ConfigureAwait(false);
            var message = ExtractUiMessage(envelope);
            if (message is not null)
                throw new KratosAuthException(message);

            var rawBody = await SafeReadBodyAsync(resp, ct).ConfigureAwait(false);
            throw new KratosAuthException(
                $"Kratos rejected the code (HTTP {(int)resp.StatusCode}): {Truncate(rawBody)}");
        }

        var success = await resp.Content.ReadFromJsonAsync<KratosOtpSuccessResponse>(KratosAuthClient.JsonOpts, ct).ConfigureAwait(false);
        var token = success?.SessionToken;
        if (string.IsNullOrEmpty(token))
        {
            // Structural surprise: a 2xx we didn't expect the shape of. Don't assume success —
            // fail loud with the raw body so this can be diagnosed against the live server.
            throw new KratosAuthException(
                "Kratos accepted the code but the response had no session_token field — " +
                "the live response shape differs from the documented api-flow contract.");
        }

        return token;
    }

    private static async Task<KratosFlowEnvelope?> TryReadFlowEnvelopeAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            // Content may have already been consumed by a prior read attempt in some paths; guard
            // defensively since this is a diagnostic best-effort read.
            return await resp.Content.ReadFromJsonAsync<KratosFlowEnvelope>(KratosAuthClient.JsonOpts, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractUiMessage(KratosFlowEnvelope? envelope)
    {
        var messages = envelope?.Ui?.Messages;
        if (messages is null || messages.Count == 0) return null;
        var text = messages[0].Text;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { return string.Empty; }
    }

    private static string Truncate(string s) => s.Length > 500 ? s[..500] + "..." : s;
}

/// <summary>Handle threaded between <see cref="KratosOtpAuthClient.StartFlowAsync"/>,
/// <see cref="KratosOtpAuthClient.RequestCodeAsync"/> and <see cref="KratosOtpAuthClient.SubmitCodeAsync"/>.</summary>
public readonly record struct KratosOtpFlow(string FlowId, string ActionUrl);

// ---- Kratos wire shapes (only the fields we consume) ----

internal sealed class KratosFlowEnvelope
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("ui")] public KratosUiWithMessages? Ui { get; set; }
}

internal sealed class KratosUiWithMessages
{
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("messages")] public List<KratosUiMessage>? Messages { get; set; }
}

internal sealed class KratosUiMessage
{
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}

internal sealed class KratosOtpSuccessResponse
{
    [JsonPropertyName("session_token")] public string? SessionToken { get; set; }
}
