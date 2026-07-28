using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Kivi.Platform.Auth;

/// <summary>
/// Mints/refreshes the org-service JWT that <c>KiviServiceClient</c> uses as its bearer against
/// the hosted (non-loopback) endpoints (map §3.5). Pure HTTP — no secrets-storage coupling; the
/// caller (<see cref="AuthController"/>) persists the minted JWT via <c>ISecretStore</c>.
///
///   POST {OrgServiceURL}/api/v2/auth/jwt, header X-Session-Token: &lt;kratos session&gt;, body {}
///   → { "token": "...", "expires_at": "..." } (15-minute TTL).
///
/// <see cref="RefreshIfNeeded"/> is clock-driven and called on demand before use — NOT a background
/// timer. A 403 is retried twice with 0.3s/0.7s backoff per the map.
/// </summary>
public sealed class OrgJwtClient
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(700) };

    private readonly HttpClient _http;
    private readonly Uri _orgServiceBaseUrl;
    private readonly Func<DateTimeOffset> _now;

    private string? _cachedToken;
    private DateTimeOffset _cachedExpiresAt;

    public OrgJwtClient(HttpClient http, Uri orgServiceBaseUrl, Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _orgServiceBaseUrl = orgServiceBaseUrl;
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The currently cached JWT, if any (may be expired — check with <see cref="IsExpiredOrExpiringSoon"/>).</summary>
    public string? CachedToken => _cachedToken;
    public DateTimeOffset CachedExpiresAt => _cachedExpiresAt;

    /// <summary>True when there's no cached token, or it's within <see cref="RefreshMargin"/> of expiry.</summary>
    public bool IsExpiredOrExpiringSoon =>
        _cachedToken is null || _now() >= _cachedExpiresAt - RefreshMargin;

    /// <summary>
    /// Mint a fresh JWT unconditionally and cache it. Retries a 403 twice (0.3s/0.7s backoff).
    /// </summary>
    public async Task<OrgJwt> MintAsync(string kratosSessionToken, string? orgId = null, string? workspaceId = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(orgId)) body["org_id"] = orgId;
        if (!string.IsNullOrEmpty(workspaceId)) body["workspace_id"] = workspaceId;

        Exception? lastError = null;
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_orgServiceBaseUrl}api/v2/auth/jwt")
            {
                Content = JsonContent.Create(body),
            };
            req.Headers.Add("X-Session-Token", kratosSessionToken);

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
                if (attempt < RetryDelays.Length) { await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false); continue; }
                throw;
            }

            using (resp)
            {
                if (resp.StatusCode == HttpStatusCode.Forbidden && attempt < RetryDelays.Length)
                {
                    await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false);
                    continue;
                }

                resp.EnsureSuccessStatusCode();
                var payload = await resp.Content.ReadFromJsonAsync<OrgJwtResponse>(KratosAuthClient.JsonOpts, ct).ConfigureAwait(false)
                              ?? throw new InvalidOperationException("Org JWT mint response was empty.");

                if (string.IsNullOrEmpty(payload.Token))
                    throw new InvalidOperationException("Org JWT mint response had no token.");

                var expiresAt = payload.ExpiresAt is { } ts
                    ? DateTimeOffset.Parse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind)
                    : _now() + TimeSpan.FromMinutes(15);

                _cachedToken = payload.Token;
                _cachedExpiresAt = expiresAt;
                return new OrgJwt(payload.Token, expiresAt);
            }
        }

        throw lastError ?? new InvalidOperationException("Org JWT mint failed after retries.");
    }

    /// <summary>
    /// Re-mints only when <see cref="IsExpiredOrExpiringSoon"/>; otherwise returns the cached token.
    /// Clock-driven, called on demand — never a background timer.
    /// </summary>
    public async Task<OrgJwt> RefreshIfNeeded(string kratosSessionToken, string? orgId = null, string? workspaceId = null, CancellationToken ct = default)
    {
        if (!IsExpiredOrExpiringSoon && _cachedToken is not null)
            return new OrgJwt(_cachedToken, _cachedExpiresAt);

        return await MintAsync(kratosSessionToken, orgId, workspaceId, ct).ConfigureAwait(false);
    }

    /// <summary>Seed the cache (e.g. from a previously persisted JWT) without hitting the network.</summary>
    public void Seed(string token, DateTimeOffset expiresAt)
    {
        _cachedToken = token;
        _cachedExpiresAt = expiresAt;
    }
}

public readonly record struct OrgJwt(string Token, DateTimeOffset ExpiresAt);

internal sealed class OrgJwtResponse
{
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("expires_at")] public string? ExpiresAt { get; set; }
}
