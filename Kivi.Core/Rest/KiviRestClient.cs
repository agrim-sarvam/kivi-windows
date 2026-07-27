using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kivi.Core.Wire;

namespace Kivi.Core.Rest;

// HttpClient-based REST client. See docs/maps/service-client-wire.md §5 + backend-service-api.md §4.
// All requests carry the three X-Client-* headers + bearer (when non-anonymous), retry ONCE on
// 5xx/timeout and ONCE on a 401 re-mint. Bodies encoded deterministically (sorted keys, no slash
// escape) — same encoding as the WS layer.
//
// THE ASYMMETRY: WS + most REST are snake_case, but /v1/edit responds camelCase (requestId,
// latencyMs, resolvedPresetIds). Read `text`, NOT `edited`.

/// <summary>A non-2xx REST response.</summary>
public sealed class RestException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? Body { get; }
    public RestException(HttpStatusCode status, string? body)
        : base($"REST {(int)status}: {body}") { StatusCode = status; Body = body; }
}

/// <summary>Result of <c>POST /v1/edit</c> — note the camelCase wire keys.</summary>
public sealed record EditEndpointResult
{
    public string? RequestId { get; init; }
    public string? Text { get; init; } // read `text`, NOT `edited`
    public string? Mode { get; init; }
    public string? EditRequestText { get; init; }
    public string? ResolvedPersonaSlug { get; init; }
    public string? ResolvedPreset { get; init; }
    public IReadOnlyList<string>? ResolvedPresetIds { get; init; }
    public string? EvidenceEventId { get; init; }
    public string? ModelUsed { get; init; }
    public double? LatencyMs { get; init; }
}

/// <summary>Minimal request for <c>POST /v1/edit</c> (snake_case in).</summary>
public sealed record EditEndpointRequest
{
    public required string Text { get; init; }
    public string? EditRequestText { get; init; }
    public string Mode { get; init; } = "custom";
    public string? Preset { get; init; }
    public IReadOnlyList<string>? PresetIds { get; init; }
    public string? AppBundleId { get; init; }
    public string? AppName { get; init; }
    public string PersonaSlug { get; init; } = "global";
}

/// <summary>The REST surface the .NET client speaks. See map §5.</summary>
public sealed class KiviRestClient
{
    private readonly HttpClient _http;
    private readonly KiviEndpoint _endpoint;
    private readonly ClientIdentity _identity;
    private readonly Func<Task<string?>>? _bearerProvider;   // returns null ⇒ anonymous
    private readonly Func<Task<string?>>? _remintBearer;      // force re-mint on 401

    public KiviRestClient(
        HttpClient http,
        KiviEndpoint endpoint,
        ClientIdentity identity,
        Func<Task<string?>>? bearerProvider = null,
        Func<Task<string?>>? remintBearer = null)
    {
        _http = http;
        _endpoint = endpoint;
        _identity = identity;
        _bearerProvider = bearerProvider;
        _remintBearer = remintBearer;
        if (_http.Timeout == default || _http.Timeout == TimeSpan.FromSeconds(100))
            _http.Timeout = TimeSpan.FromSeconds(30); // map §3: 30s
    }

    /// <summary>GET <c>ready</c> — readiness (also verifies token non-nil for non-anon). 2xx = up.</summary>
    public async Task<bool> ReadyAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendAsync(HttpMethod.Get, "ready", body: null, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>POST <c>v1/edit</c> — second-pass text edit. Response is camelCase; read <c>text</c>.</summary>
    public async Task<EditEndpointResult> EditAsync(EditEndpointRequest req, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["text"] = req.Text,
            ["mode"] = req.Mode,
            ["persona_slug"] = req.PersonaSlug,
        };
        if (req.EditRequestText is not null) body["edit_request_text"] = req.EditRequestText;
        if (req.Preset is not null) body["preset"] = req.Preset;
        if (req.PresetIds is not null)
        {
            var arr = new JsonArray();
            foreach (var p in req.PresetIds) arr.Add(p);
            body["preset_ids"] = arr;
        }
        if (req.AppBundleId is not null) body["app_bundle_id"] = req.AppBundleId;
        if (req.AppName is not null) body["app_name"] = req.AppName;

        using var resp = await SendAsync(HttpMethod.Post, "v1/edit", body, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new RestException(resp.StatusCode, text);
        return ParseEditResult(text);
    }

    /// <summary>GET <c>v1/usage</c> — quota display. Returns the raw JSON object.</summary>
    public async Task<JsonObject?> UsageAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, "v1/usage", body: null, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new RestException(resp.StatusCode, text);
        return JsonNode.Parse(text) as JsonObject;
    }

    /// <summary>POST <c>v1/telemetry/dictation_completed</c> — fire-and-forget (best-effort).</summary>
    public async Task TelemetryDictationCompletedAsync(JsonObject payload, CancellationToken ct = default)
    {
        try { using var _ = await SendAsync(HttpMethod.Post, "v1/telemetry/dictation_completed", payload, ct).ConfigureAwait(false); }
        catch { /* fire-and-forget */ }
    }

    /// <summary>POST <c>v1/feedback</c> — 👍/👎 thumbs, fire-and-forget.</summary>
    public async Task FeedbackAsync(JsonObject payload, CancellationToken ct = default)
    {
        try { using var _ = await SendAsync(HttpMethod.Post, "v1/feedback", payload, ct).ConfigureAwait(false); }
        catch { /* fire-and-forget */ }
    }

    // ---- personas / memory REST: signatures only (P6 fills these) ----

    /// <summary>GET <c>v1/personas</c> — P6. Not implemented for the MVP.</summary>
    public Task<JsonObject?> GetPersonasAsync(CancellationToken ct = default)
        => throw new NotImplementedException("personas REST is P6");

    /// <summary>GET/POST memory-forest — P6. Not implemented for the MVP.</summary>
    public Task<JsonObject?> GetMemoryForestAsync(CancellationToken ct = default)
        => throw new NotImplementedException("memory REST is P6");

    // ---- core send with headers + retry-once-on-5xx/timeout + once-on-401-remint ----

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        var bodyJson = body is null ? null : WireEncoder.Encode(body);

        var resp = await SendOnceAsync(method, path, bodyJson, forceRemint: false, ct).ConfigureAwait(false);

        // Retry ONCE on 5xx (timeouts throw and are handled by SendOnceAsync's caller policy below).
        if ((int)resp.StatusCode >= 500)
        {
            resp.Dispose();
            resp = await SendOnceAsync(method, path, bodyJson, forceRemint: false, ct).ConfigureAwait(false);
        }

        // Retry ONCE on 401 with a forced re-mint (only if we actually attach a bearer).
        if (resp.StatusCode == HttpStatusCode.Unauthorized && _remintBearer is not null)
        {
            resp.Dispose();
            resp = await SendOnceAsync(method, path, bodyJson, forceRemint: true, ct).ConfigureAwait(false);
        }

        return resp;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, string? bodyJson, bool forceRemint, CancellationToken ct)
    {
        var uri = _endpoint.RestUri(path);
        using var req = new HttpRequestMessage(method, uri);
        req.Headers.TryAddWithoutValidation("X-Client-Platform", _identity.Platform);
        req.Headers.TryAddWithoutValidation("X-Client-Version", _identity.Version);
        req.Headers.TryAddWithoutValidation("X-Client-Timezone", _identity.Timezone);

        // Bearer only when non-anonymous (local/loopback runs anonymous — omit Authorization).
        if (!_endpoint.AllowsAnonymous)
        {
            string? token = forceRemint && _remintBearer is not null
                ? await _remintBearer().ConfigureAwait(false)
                : _bearerProvider is not null ? await _bearerProvider().ConfigureAwait(false) : null;
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (bodyJson is not null)
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        try
        {
            return await _http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout: retry once (only on the first, non-remint attempt).
            if (!forceRemint)
            {
                using var retryReq = CloneForRetry(method, uri, bodyJson);
                if (!_endpoint.AllowsAnonymous && _bearerProvider is not null)
                {
                    var token = await _bearerProvider().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(token))
                        retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                return await _http.SendAsync(retryReq, ct).ConfigureAwait(false);
            }
            throw;
        }
    }

    private HttpRequestMessage CloneForRetry(HttpMethod method, Uri uri, string? bodyJson)
    {
        var req = new HttpRequestMessage(method, uri);
        req.Headers.TryAddWithoutValidation("X-Client-Platform", _identity.Platform);
        req.Headers.TryAddWithoutValidation("X-Client-Version", _identity.Version);
        req.Headers.TryAddWithoutValidation("X-Client-Timezone", _identity.Timezone);
        if (bodyJson is not null)
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        return req;
    }

    /// <summary>Parse the camelCase /v1/edit body. Public for unit testing.</summary>
    public static EditEndpointResult ParseEditResult(string json)
    {
        var obj = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        List<string>? ids = null;
        if (obj["resolvedPresetIds"] is JsonArray arr)
        {
            ids = new List<string>();
            foreach (var it in arr)
                if (it is JsonValue jv && jv.TryGetValue<string>(out var s) && s is not null) ids.Add(s);
        }
        return new EditEndpointResult
        {
            RequestId = Str(obj, "requestId"),
            Text = Str(obj, "text"),
            Mode = Str(obj, "mode"),
            EditRequestText = Str(obj, "editRequestText"),
            ResolvedPersonaSlug = Str(obj, "resolvedPersonaSlug"),
            ResolvedPreset = Str(obj, "resolvedPreset"),
            ResolvedPresetIds = ids,
            EvidenceEventId = Str(obj, "evidenceEventId"),
            ModelUsed = Str(obj, "modelUsed"),
            LatencyMs = obj["latencyMs"] is JsonValue lv && lv.TryGetValue<double>(out var d) ? d : (double?)null,
        };
    }

    private static string? Str(JsonObject o, string k) =>
        o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
