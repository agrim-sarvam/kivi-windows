namespace Kivi.Core.Wire;

// Endpoint configuration. See docs/maps/service-client-wire.md §1.
// One canonical WS path — /v1/dictate/stream — and every REST base derives from the same host
// (ws→http, wss→https, path stripped), so WS and HTTP can never disagree.

/// <summary>The known deployment targets (settings key <c>kiviEndpoint</c>).</summary>
public enum KiviEndpointKind
{
    Qa,
    Staging,
    Prod,
    Local,
    Custom,
}

/// <summary>
/// A resolved endpoint: the WebSocket dictation URL + the derived REST base, and whether the
/// host is loopback (anonymous, omit <c>Authorization</c>).
/// </summary>
public sealed record KiviEndpoint(KiviEndpointKind Kind, Uri WebSocketUrl, Uri RestBase, bool AllowsAnonymous)
{
    /// <summary>The canonical dictation path, force-pinned on every WS URL.</summary>
    public const string DictatePath = "/v1/dictate/stream";

    /// <summary>Build <c>RestBase + "/" + path</c> (path e.g. <c>"v1/edit"</c>, <c>"ready"</c>).</summary>
    public Uri RestUri(string path)
    {
        var baseStr = RestBase.ToString();
        if (!baseStr.EndsWith('/')) baseStr += "/";
        return new Uri(new Uri(baseStr), path.TrimStart('/'));
    }
}

/// <summary>Resolves / normalizes dictation endpoints. See map §1.</summary>
public static class Endpoints
{
    // Shipped hosts. QA is the shipped default.
    private const string QaWs = "wss://kivi.aws-qa.sarvam.ai" + KiviEndpoint.DictatePath;
    private const string StagingWs = "wss://kivi.aws-staging.sarvam.ai" + KiviEndpoint.DictatePath;
    private const string ProdWs = "wss://kivi.sarvam.ai" + KiviEndpoint.DictatePath;
    private const string LocalWs = "ws://127.0.0.1:8788" + KiviEndpoint.DictatePath;

    /// <summary>The shipped default (QA).</summary>
    public static KiviEndpoint Default => Qa;

    public static KiviEndpoint Qa => FromWsUrl(KiviEndpointKind.Qa, QaWs);
    public static KiviEndpoint Staging => FromWsUrl(KiviEndpointKind.Staging, StagingWs);
    public static KiviEndpoint Prod => FromWsUrl(KiviEndpointKind.Prod, ProdWs);
    public static KiviEndpoint Local => FromWsUrl(KiviEndpointKind.Local, LocalWs);

    /// <summary>
    /// Parse a storage-form value (settings key <c>kiviEndpoint</c>):
    /// <c>"qa"|"staging"|"prod"|"local"|"custom:&lt;absoluteURL&gt;"</c>.
    /// Legacy value <c>"production"</c> maps to QA.
    /// </summary>
    public static KiviEndpoint Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return Default;
        var s = stored.Trim();
        var lower = s.ToLowerInvariant();
        switch (lower)
        {
            case "qa":
            case "production": // legacy alias
                return Qa;
            case "staging":
                return Staging;
            case "prod":
                return Prod;
            case "local":
                return Local;
        }
        if (lower.StartsWith("custom:", StringComparison.Ordinal))
            return Custom(s.Substring("custom:".Length));
        // Bare URL ⇒ treat as custom.
        return Custom(s);
    }

    /// <summary>
    /// Normalize a user-supplied endpoint string into a resolved endpoint.
    /// trim → if no scheme, prefix ws:// for loopback else wss:// → map http→ws, https→wss →
    /// force path to /v1/dictate/stream → strip query/fragment → collapse to a known case if it
    /// matches qa/staging/prod/local.
    /// </summary>
    public static KiviEndpoint Custom(string input)
    {
        var raw = (input ?? string.Empty).Trim();
        if (raw.Length == 0) return Default;

        // Determine scheme.
        var hasScheme = raw.Contains("://", StringComparison.Ordinal);
        string hostPortForLoopbackGuess = hasScheme
            ? raw.Substring(raw.IndexOf("://", StringComparison.Ordinal) + 3)
            : raw;

        if (!hasScheme)
        {
            var pref = IsLoopbackHostString(hostPortForLoopbackGuess) ? "ws://" : "wss://";
            raw = pref + raw;
        }

        var ub = new UriBuilder(raw);
        // Map http→ws, https→wss (leave ws/wss as-is).
        ub.Scheme = ub.Scheme.ToLowerInvariant() switch
        {
            "http" => "ws",
            "https" => "wss",
            "ws" => "ws",
            "wss" => "wss",
            var other => other,
        };
        ub.Path = KiviEndpoint.DictatePath; // force canonical path
        ub.Query = string.Empty;            // strip query
        ub.Fragment = string.Empty;         // strip fragment

        var wsUrl = ub.Uri;

        // Collapse to a known case if it matches a shipped host exactly.
        var normalized = wsUrl.ToString();
        if (string.Equals(normalized, new Uri(QaWs).ToString(), StringComparison.OrdinalIgnoreCase)) return Qa;
        if (string.Equals(normalized, new Uri(StagingWs).ToString(), StringComparison.OrdinalIgnoreCase)) return Staging;
        if (string.Equals(normalized, new Uri(ProdWs).ToString(), StringComparison.OrdinalIgnoreCase)) return Prod;
        if (string.Equals(normalized, new Uri(LocalWs).ToString(), StringComparison.OrdinalIgnoreCase)) return Local;

        return Build(KiviEndpointKind.Custom, wsUrl);
    }

    private static KiviEndpoint FromWsUrl(KiviEndpointKind kind, string wsUrl) => Build(kind, new Uri(wsUrl));

    private static KiviEndpoint Build(KiviEndpointKind kind, Uri wsUrl)
    {
        var restScheme = wsUrl.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        var restBuilder = new UriBuilder(wsUrl) { Scheme = restScheme, Path = "/", Query = string.Empty, Fragment = string.Empty };
        var anon = IsLoopbackHost(wsUrl.Host);
        return new KiviEndpoint(kind, wsUrl, restBuilder.Uri, anon);
    }

    /// <summary>True iff <paramref name="host"/> is a loopback host (127.0.0.1 / localhost / ::1).</summary>
    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var h = host.Trim().Trim('[', ']').ToLowerInvariant(); // tolerate bracketed [::1]
        return h == "127.0.0.1" || h == "localhost" || h == "::1";
    }

    private static bool IsLoopbackHostString(string hostPortPath)
    {
        // Extract just the host from something like "127.0.0.1:8788/path".
        var h = hostPortPath;
        var slash = h.IndexOf('/');
        if (slash >= 0) h = h.Substring(0, slash);
        var colon = h.LastIndexOf(':');
        // Keep IPv6 [::1] intact; only strip a trailing :port on a non-bracketed host.
        if (colon >= 0 && !h.Contains('[')) h = h.Substring(0, colon);
        return IsLoopbackHost(h);
    }
}
