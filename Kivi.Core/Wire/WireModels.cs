using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kivi.Core.Wire;

// Wire message shapes + deterministic encode/decode. See docs/maps/service-client-wire.md §4.
// JSON is snake_case both directions, encoded with SORTED KEYS and NO slash-escaping so the
// bytes are deterministic. Audio is a raw binary frame, never JSON.
//
// THE A3 TRAP (see map §4.2):
//   - formatting_enabled is ALWAYS emitted (server serde default is FALSE; client default true).
//   - general_app_style_preset is a CLOSED enum verbatim|casual|transliteration|formal — a bad
//     value fails the WHOLE message (PARSE_ERROR / stalled take), so it is allowlist-guarded and
//     simply omitted when it is not one of the four.

/// <summary>Client identity headers sent on every WS upgrade and every REST call (map §3).</summary>
public readonly record struct ClientIdentity(string Platform, string Version, string Timezone)
{
    /// <summary>
    /// X-Client-Platform is a cross-team server-side gate. Per docs/maps §3/§7.2 the deliberate
    /// choice is "windows"; if the backend does not recognize it, mirror "macos" to inherit the
    /// same gated behavior. Flagged in the report.
    /// </summary>
    public const string PlatformWindows = "windows";

    public const string DefaultVersion = "0.0.0-dev";
}

/// <summary>The closed enum accepted for <c>general_app_style_preset</c> (the "A3 trap").</summary>
public static class GeneralAppStylePreset
{
    public const string Verbatim = "verbatim";
    public const string Casual = "casual";
    public const string Transliteration = "transliteration";
    public const string Formal = "formal";

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        Verbatim, Casual, Transliteration, Formal,
    };

    /// <summary>True iff <paramref name="value"/> is one of the four server-accepted presets.</summary>
    public static bool IsValid(string? value) => value is not null && Allowed.Contains(value);
}

/// <summary>Optional <c>app_context</c> block on a context message.</summary>
public sealed record AppContextWire(string? AppName = null, string? BundleId = null, string? WindowTitle = null);

/// <summary>Options that shape the <c>context</c> message. Mirrors the TS <c>ContextOptions</c>.</summary>
public sealed record ContextOptions
{
    public string? LanguageHint { get; init; }

    /// <summary>Shipped default "codemix" (NOT "transcribe"). Other value: "action".</summary>
    public string TranscriptionMode { get; init; } = "codemix";

    /// <summary>Client default true. Always emitted (server serde default is FALSE).</summary>
    public bool FormattingEnabled { get; init; } = true;

    public bool AutoPersonaResolution { get; init; } = true;

    /// <summary>Closed enum — allowlist-guarded; omitted when not one of the four valid values.</summary>
    public string? GeneralAppStylePreset { get; init; }

    public AppContextWire? AppContext { get; init; }
}

/// <summary>Builds wire messages. Pure — unit-tested. See map §4.2.</summary>
public static class WireEncoder
{
    /// <summary>
    /// Build the <c>context</c> object graph (JsonObject). Encode with <see cref="Encode"/> to get
    /// deterministic bytes.
    /// </summary>
    public static JsonObject BuildContext(string sessionId, ContextOptions? ctx = null)
    {
        ctx ??= new ContextOptions();
        var c = new JsonObject
        {
            ["type"] = "context",
            ["transcription_mode"] = ctx.TranscriptionMode,
            ["formatting_enabled"] = ctx.FormattingEnabled, // ALWAYS emitted — the A3 trap
            ["session_id"] = sessionId,
            ["auto_persona_resolution"] = ctx.AutoPersonaResolution,
            ["client_capabilities"] = new JsonObject { ["spoken_shortcuts_v1"] = true },
            ["supports_formatting_progress"] = true,
        };
        if (!string.IsNullOrEmpty(ctx.LanguageHint)) c["language_hint"] = ctx.LanguageHint;
        if (ctx.AppContext is { } ac)
        {
            var acObj = new JsonObject();
            if (ac.AppName is not null) acObj["app_name"] = ac.AppName;
            if (ac.BundleId is not null) acObj["bundle_id"] = ac.BundleId;
            if (ac.WindowTitle is not null) acObj["window_title"] = ac.WindowTitle;
            c["app_context"] = acObj;
        }
        // Allowlist-guard the closed enum: a bad value fails the WHOLE message, so we OMIT it.
        if (GeneralAppStylePreset.IsValid(ctx.GeneralAppStylePreset))
            c["general_app_style_preset"] = ctx.GeneralAppStylePreset;
        return c;
    }

    /// <summary>MVP <c>end_of_speech</c> = <c>{"type":"end_of_speech"}</c> (screen-context omitted).</summary>
    public static JsonObject BuildEndOfSpeech() => new() { ["type"] = "end_of_speech" };

    public static JsonObject BuildCancel() => new() { ["type"] = "cancel" };

    public static JsonObject BuildPing() => new() { ["type"] = "ping" };

    public static JsonObject BuildAuthRefresh(string jwt) => new() { ["type"] = "auth_refresh", ["token"] = jwt };

    /// <summary>
    /// Serialize a JSON node deterministically: keys sorted (ordinal, recursive) and slashes NOT
    /// escaped. This is the byte-exact encoding the wire contract requires.
    /// </summary>
    public static string Encode(JsonNode node)
    {
        var sorted = SortKeys(node);
        return sorted.ToJsonString(DeterministicOptions);
    }

    // JsonNode.ToJsonString honors JavaScriptEncoder for string escaping but writes object
    // properties in insertion order — so we rebuild the graph with sorted keys ourselves.
    private static readonly JsonSerializerOptions DeterministicOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // no slash escaping
        WriteIndented = false,
    };

    private static JsonNode SortKeys(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var sorted = new JsonObject();
                foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sorted[kvp.Key] = kvp.Value is null ? null : SortKeys(kvp.Value);
                return sorted;
            }
            case JsonArray arr:
            {
                var outArr = new JsonArray();
                foreach (var item in arr)
                    outArr.Add(item is null ? null : SortKeys(item));
                return outArr;
            }
            default:
                // Value node — clone so we never re-parent a node that still has an owner.
                return JsonNode.Parse(node.ToJsonString(DeterministicOptions))!;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Server → client decoded messages.
// ---------------------------------------------------------------------------------------------

/// <summary>Discriminates a decoded server frame.</summary>
public enum ServerMessageKind
{
    Ack,
    SpeechStart,
    Interim,
    RouteHint,
    EosAck,
    FormattingProgress,
    Final,
    Error,
    Pong,
    AuthRefreshAck,
    Unknown,
}

/// <summary>One decoded server frame. Only the fields relevant to <see cref="Kind"/> are populated.</summary>
public sealed record ServerMessage
{
    public required ServerMessageKind Kind { get; init; }

    /// <summary>The raw <c>type</c> string (useful for Unknown / error triage).</summary>
    public string? RawType { get; init; }

    // ack
    public string? SessionId { get; init; }

    // interim
    public int SegmentIdx { get; init; }
    public string? Text { get; init; }
    public double? LatencyMs { get; init; }

    // route_hint
    public string? Route { get; init; }
    public string? RawTranscript { get; init; }

    // eos_ack / formatting_progress
    public int? RawWords { get; init; }
    public double? ExpectedFormatMs { get; init; }
    public double? ElapsedMs { get; init; }

    // final
    public FinalPayload? Final { get; init; }

    // error
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    // pong / auth_refresh_ack
    public bool? Ok { get; init; }
}

/// <summary>The one result of a take. Paste target is <see cref="FormattedText"/> (fall back to raw).</summary>
public sealed record FinalPayload
{
    public string? RequestId { get; init; }
    public string? FormattedText { get; init; }
    public string? RawTranscript { get; init; }
    public string? DetectedLanguage { get; init; }
    public IReadOnlyList<string>? DetectedLanguages { get; init; }
    public string? Route { get; init; }
    public string? ResolvedPersona { get; init; }
    public string? ResolvedPreset { get; init; }
    public string? ContentKind { get; init; }
    public string? InsertionReplaceBefore { get; init; }
    public FinalLatency? Latency { get; init; }
    public FinalUsage? Usage { get; init; }
    public bool OutputSuspect { get; init; }
    public bool? ServerDurable { get; init; }

    /// <summary>The text to paste: formatted_text if present, else raw_transcript.</summary>
    public string PasteText => !string.IsNullOrEmpty(FormattedText) ? FormattedText! : (RawTranscript ?? string.Empty);
}

public sealed record FinalLatency(IReadOnlyList<double>? SttSegmentsMs, double? FormattingMs, double? TotalMs);

public sealed record FinalUsage(int? BillableWordCount, int? MonthlyWordLimit);

/// <summary>Tolerant decoder for server frames. See map §4.3.</summary>
public static class WireDecoder
{
    /// <summary>
    /// Decode one text frame. Non-JSON / non-object / missing string <c>type</c> ⇒ null (dropped).
    /// Unknown <c>type</c> ⇒ <see cref="ServerMessageKind.Unknown"/> (caller ignores).
    /// </summary>
    public static ServerMessage? Decode(string raw)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(raw); }
        catch { return null; }
        if (node is not JsonObject obj) return null;
        var type = GetString(obj, "type");
        if (type is null) return null;

        switch (type)
        {
            case "ack":
                return new ServerMessage { Kind = ServerMessageKind.Ack, RawType = type, SessionId = GetString(obj, "session_id") };
            case "speech_start":
                return new ServerMessage { Kind = ServerMessageKind.SpeechStart, RawType = type };
            case "interim":
            {
                // is_final absent ⇒ true.
                var isFinal = obj["is_final"] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : true;
                return new ServerMessage
                {
                    Kind = ServerMessageKind.Interim,
                    RawType = type,
                    SegmentIdx = (int)(GetDouble(obj, "segment_idx") ?? 0),
                    Text = GetString(obj, "text"),
                    LatencyMs = GetDouble(obj, "latency_ms"),
                    // Fold is_final into Ok so the client can render only settled segments.
                    Ok = isFinal,
                };
            }
            case "route_hint":
                return new ServerMessage
                {
                    Kind = ServerMessageKind.RouteHint,
                    RawType = type,
                    Route = GetString(obj, "route"),
                    RawTranscript = GetString(obj, "raw_transcript"),
                };
            case "eos_ack":
                return new ServerMessage
                {
                    Kind = ServerMessageKind.EosAck,
                    RawType = type,
                    RawWords = (int?)GetDouble(obj, "raw_words"),
                    ExpectedFormatMs = GetDouble(obj, "expected_format_ms"),
                };
            case "formatting_progress":
                return new ServerMessage
                {
                    Kind = ServerMessageKind.FormattingProgress,
                    RawType = type,
                    ElapsedMs = GetDouble(obj, "elapsed_ms"),
                    ExpectedFormatMs = GetDouble(obj, "expected_format_ms"),
                };
            case "final":
                return new ServerMessage { Kind = ServerMessageKind.Final, RawType = type, Final = DecodeFinal(obj) };
            case "error":
                return new ServerMessage
                {
                    Kind = ServerMessageKind.Error,
                    RawType = type,
                    ErrorCode = GetString(obj, "code") ?? "ERROR",
                    ErrorMessage = GetString(obj, "message"),
                };
            case "pong":
                return new ServerMessage { Kind = ServerMessageKind.Pong, RawType = type };
            case "auth_refresh_ack":
            {
                var ok = obj["ok"] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : (bool?)null;
                return new ServerMessage { Kind = ServerMessageKind.AuthRefreshAck, RawType = type, Ok = ok };
            }
            default:
                return new ServerMessage { Kind = ServerMessageKind.Unknown, RawType = type };
        }
    }

    private static FinalPayload DecodeFinal(JsonObject obj)
    {
        FinalLatency? latency = null;
        if (obj["latency"] is JsonObject lat)
        {
            List<double>? seg = null;
            if (lat["stt_segments_ms"] is JsonArray arr)
            {
                seg = new List<double>();
                foreach (var it in arr)
                    if (it is JsonValue jv && jv.TryGetValue<double>(out var d)) seg.Add(d);
            }
            latency = new FinalLatency(seg, GetDouble(lat, "formatting_ms"), GetDouble(lat, "total_ms"));
        }

        FinalUsage? usage = null;
        if (obj["usage"] is JsonObject use)
            usage = new FinalUsage((int?)GetDouble(use, "billable_word_count"), (int?)GetDouble(use, "monthly_word_limit"));

        List<string>? langs = null;
        if (obj["detected_languages"] is JsonArray la)
        {
            langs = new List<string>();
            foreach (var it in la)
                if (it is JsonValue jv && jv.TryGetValue<string>(out var s) && s is not null) langs.Add(s);
        }

        // output_suspect may live at top level or under metadata.
        var suspect = GetBool(obj, "output_suspect")
                      ?? (obj["metadata"] is JsonObject md ? GetBool(md, "output_suspect") : null)
                      ?? false;

        return new FinalPayload
        {
            RequestId = GetString(obj, "request_id"),
            FormattedText = GetString(obj, "formatted_text"),
            RawTranscript = GetString(obj, "raw_transcript"),
            DetectedLanguage = GetString(obj, "detected_language"),
            DetectedLanguages = langs,
            Route = GetString(obj, "route"),
            ResolvedPersona = GetString(obj, "resolved_persona"),
            ResolvedPreset = GetString(obj, "resolved_preset"),
            ContentKind = GetString(obj, "content_kind"),
            InsertionReplaceBefore = GetString(obj, "insertion_replace_before"),
            Latency = latency,
            Usage = usage,
            OutputSuspect = suspect,
            ServerDurable = GetBool(obj, "server_durable"),
        };
    }

    private static string? GetString(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? GetDouble(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : (double?)null;

    private static bool? GetBool(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : (bool?)null;
}
