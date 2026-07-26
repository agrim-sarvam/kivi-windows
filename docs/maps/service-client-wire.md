# MAP: service-client-wire

Windows-only .NET port of `_reference/sarvam-kivi-electron/docs/maps/service-client-wire.md`.
Both directions use a snake_case `{"type": ...}` envelope over WebSocket; REST bodies are plain
snake_case JSON (except `/v1/edit`, which responds **camelCase**). All constants are byte-exact.

> **Key finding for the port:** there is **NO `API-SUBSCRIPTION-KEY` / `X-API-Key` header
> anywhere in this client.** Auth is a short-lived **`Authorization: Bearer <JWT>`** minted from
> a Kratos session; a client replicates auth by attaching the bearer (or omitting it entirely
> for local/anonymous loopback). `System.Net.WebSockets.ClientWebSocket` sets the bearer +
> `X-Client-*` headers on the upgrade request; a WebView `WebSocket` cannot — which is why the
> socket lives in the .NET process, never in a WebView.

The target implementation is `Kivi.Core/Wire/*` (`KiviServiceClient`, `WireModels`,
`Endpoints`, `DictationBudgets`) and `Kivi.Core/Rest/*` (`KiviRestClient`).

---

## 1. Endpoint configuration (`Endpoints`)

One canonical WS path — **`/v1/dictate/stream`** — and every REST base derives from the same
host (ws→http, wss→https, path stripped), so WS and HTTP can never disagree.

| Endpoint | WebSocket URL | Derived HTTP base | Anonymous? |
|---|---|---|---|
| `qa` **(shipped default)** | `wss://kivi.aws-qa.sarvam.ai/v1/dictate/stream` | `https://kivi.aws-qa.sarvam.ai` | no |
| `staging` | `wss://kivi.aws-staging.sarvam.ai/v1/dictate/stream` | `https://kivi.aws-staging.sarvam.ai` | no |
| `prod` | `wss://kivi.sarvam.ai/v1/dictate/stream` | `https://kivi.sarvam.ai` | no |
| `local` | `ws://127.0.0.1:8788/v1/dictate/stream` | `http://127.0.0.1:8788` | **yes** (loopback) |
| `custom(URL)` | user-supplied, normalized | scheme/path derived | yes iff loopback host |

- **REST URL build:** `httpBase + "/" + path` where `path` is e.g. `"v1/format-preview"`.
- **`allowsAnonymous`** is true only when the WS host is loopback (`127.0.0.1`, `localhost`, `::1`, case-insensitive, tolerates bracketed `[::1]`). Anonymous ⇒ omit the `Authorization` header; pairs with server `DICTATE_AUTH_MODE=none`.
- **Custom-input normalization:** trim → if no scheme, prefix `ws://` for loopback else `wss://` → map `http→ws`,`https→wss` → force path to `/v1/dictate/stream` → strip query/fragment → collapse to a known case if it matches qa/staging/prod/local.
- **Storage form (settings key `kiviEndpoint`):** `"qa"|"staging"|"prod"|"local"|"custom:<absoluteURL>"`. Legacy value `"production"` maps to `qa`.

---

## 2. Auth model (`TokenProvider`, `KiviServiceClient`, `KiviRestClient`)

Two-tier token exchange (no API key):

1. **Kratos session token** — long-lived, stored via `ISecretStore` (**DPAPI** on Windows; key `kratosSessionToken`). Written by the sign-in flow.
2. **Org-service JWT** — short-lived (**15 min TTL**, `jwtTTLSeconds = 900`). Minted on demand:
   - `POST https://auth.sarvam.ai/api/v2/auth/jwt`
   - Header: `X-Session-Token: <kratos session>`, `Content-Type: application/json`
   - Body: `{}`
   - Response 200: `{ "token": "<jwt>", "expires_at": "<ISO8601>" }` → cached as `AuthToken{jwt, expiresAt}`; cached to DPAPI key `orgServiceJWT`.
   - 401 ⇒ `sessionExpired` (must re-sign-in); other non-200 ⇒ `mintFailed(status)`.
   - Re-mint lazily when `< 60 s` validity remains (`minValiditySeconds`). Single-flight: concurrent callers coalesce onto one mint task (a shared `Task<AuthToken>`).

**`AuthTokenProvider`** = an injected `Func<Task<AuthToken?>>` — the seam every client takes.
Returns `null` ⇒ run anonymous (omit `Authorization`).

**Attaching the bearer:**
- WS: `ws.Options.SetRequestHeader("Authorization", $"Bearer {token.Jwt}")` on the upgrade. Null token ⇒ header omitted.
- REST: same, per request on the `HttpRequestMessage`.

**401 recovery (both stacks):** On a 401 to a request that *did* attach a bearer, drop the
cached org JWT, re-mint, rebuild header, **retry exactly once**. A second 401 propagates. On WS
the upgrade 401/403 is surfaced distinctly (`ClientWebSocket` throws `WebSocketException` whose
inner exception / status you inspect) so auth rejection ≠ network blip.

**Local-dev bearer:** `LocalDevIdentity.bearer = "kivi-local-dev"`, `expiresAt = DateTime.MaxValue`
— a deterministic marker the no-auth local service accepts. Local user/org/workspace IDs come
from config keys `KiviLocalUserID` / `KiviLocalOrgID` / `KiviLocalWorkspaceID`.

---

## 3. Client identity headers (`ClientIdentity`)

Sent on **every WS upgrade and every REST call**. These are the server's version gate; a
missing `X-Client-Version` reads as "unknown/ancient client" and silently denies version-gated
features.

| Header | Value | Notes |
|---|---|---|
| `X-Client-Platform` | `"windows"` (**cross-team decision, see §7 note 2**) | Server may version-gate on it. If in doubt, mirror `"macos"` to inherit identical gated behavior; confirm with backend which strings it recognizes. |
| `X-Client-Version` | app version, else `"0.0.0-dev"` | never empty |
| `X-Client-Timezone` | IANA id, e.g. `Asia/Kolkata` | binds usage periods to first-seen tz. In .NET: `TimeZoneInfo.Local` mapped to IANA (`TimeZoneInfo.HasIanaId`/`TryConvertWindowsIdToIanaId`). |

- `installationID` (per-install GUID) exists but is **NOT** attached to any wire call yet. Ignore for the port until a settings-sync endpoint exists.
- `Content-Type: application/json` on any REST request with a body.
- Request timeout **30 s** (WS transport + REST).

---

## 4. WebSocket dictation protocol — `/v1/dictate/stream` (THE MVP CORE)

Files: `Kivi.Core/Wire/KiviServiceClient.cs` (lifecycle), `Kivi.Core/Wire/WireModels.cs`
(message shapes + encode/decode via `System.Text.Json`). One `KiviServiceClient` per take,
never reused.

### 4.1 Lifecycle / handshake sequence
```
open:  ClientWebSocket.ConnectAsync (w/ headers) → await "ack" (≤4s) → send "context"
run:   send binary audio frame × N   (+ "ping" every 20s)
stop:  drain audio queue → send "end_of_speech" → await "final" (or "error")
end:   close   |   cancel: send {"type":"cancel"} then close
```
- The client sends `context` **immediately** after `ack` (well inside the server's 30 s context window).
- **`ack` timeout = 4000 ms** (`DictationBudgets.AckTimeoutMs`). No ack ⇒ `ackTimeout`, take fails.
- Server **never sends binary frames**; client audio is binary, all client control messages are text.

### 4.2 Client → server messages

| type | frame | when | encoder |
|---|---|---|---|
| `context` | text JSON | once, right after `ack` | `WireEncoder.Encode(ContextMessage)` |
| *(audio)* | **binary** WS frame | repeatedly during capture | raw PCM `byte[]`, `ws.SendAsync(..., WebSocketMessageType.Binary, ...)` |
| `end_of_speech` | text JSON | at stop, after audio queue drains | `WireEncoder.Encode(EndOfSpeechMessage)` |
| `cancel` | text JSON | abandon take | literal `{"type":"cancel"}` |
| `ping` | text JSON | keepalive every 20 s | literal `{"type":"ping"}` |
| `auth_refresh` | text JSON | mid-session JWT refresh (~12 min) | `{"type":"auth_refresh","token":"<jwt>"}` |

JSON encoded **deterministically** (stable key order; do not escape slashes).

**`context` message** — full field set (snake_case wire keys). Minimal MVP fields in **bold**:

| wire key | type | default / notes |
|---|---|---|
| **`type`** | string | always `"context"` |
| `language_hint` | string? | BCP-47; omitted when empty ⇒ auto-detect |
| **`transcription_mode`** | string | **`"codemix"`** (shipped default; formatting stays on regardless). Other value: `"action"` |
| **`formatting_enabled`** | bool | **server serde default is FALSE — always emit explicitly**; client default `true` |
| **`session_id`** | string | client-generated take/session id (`Guid.NewGuid()`) |
| `client_take_id` | string? | |
| `trace_id` | string? | latency tracing |
| `frontmost_app` | string? | |
| `app_context` | object? | `{app_name, bundle_id, window_title}`; `AppContextWire.kiviInternal = {Kivi, <our app-id>, null}` for in-box takes |
| `general_app_style_preset` | enum? | **CLOSED enum**: `verbatim｜casual｜transliteration｜formal` only. NOT `custom`/`free_flowing`. A bad value fails the WHOLE message → PARSE_ERROR / stalled take (the "A3 trap") |
| `auto_persona_resolution` | bool | default `true` |
| `selected_persona_slug` | string? | |
| `user_app_assignments` | `{appKey: personaSlug}` | omitted if empty |
| `client_format_prefs` | `{scope: ClientFormatPrefsWire}` | omitted if empty; authoritative server-side when DB/auth absent (local). Rich nested shape (base_preset, active_cosmetic_style_id, preset_id/name, preset_ids, preset_stack[], preset_conflict_choices, noise_cleanup, formality, punctuation_caps, romanize, stacked_custom_rules[], custom_instructions_compiled) |
| `client_capabilities` | object | `{spoken_shortcuts_v1: true}` |
| `supports_formatting_progress` | bool | default `true` — advertise the client tolerates `eos_ack`/`formatting_progress` |
| `idle_timeout_secs` | int? | omitted ⇒ server default 180 s |

**MVP `context` can be as small as:**
```json
{"type":"context","transcription_mode":"codemix","formatting_enabled":true,
 "session_id":"<uuid>","auto_persona_resolution":true,
 "client_capabilities":{"spoken_shortcuts_v1":true},"supports_formatting_progress":true}
```

**`end_of_speech` message** — the encoder only adds fields when present:

| wire key | type | notes |
|---|---|---|
| `type` | string | `"end_of_speech"` |
| `trace_id` | string? | |
| `general_app_style_preset` | enum? | same closed enum caveat |
| `language_hint` | string? | |
| `screen_terms` | array? | screen context (Windows UI Automation — deferred) |
| `screen_summary` | string? | |
| `focused_field` | object? | UI Automation — deferred |
| `screen_nodes` | array? | UI Automation — deferred; server truncates at 2000 |
| `primary_surface_id` | string? | |
| `surface_contexts` | array | omitted if empty |
| `evidence_capture_enabled` | bool | **only emitted when `false`** (opt-out) |
| `cursor_context` | `{text_before, text_after}` | caret join context (UI Automation — deferred) |

**MVP `end_of_speech` = `{"type":"end_of_speech"}`** (everything else is screen-context
enrichment — safe to omit; deferred to M9).

**Ordering contract:** `end_of_speech` serializes *behind* the pending-audio FIFO — the client
**drains all queued audio before sending EOS** (server stops reading binary after EOS; unsent
audio would truncate the last words). `cancel` deliberately does NOT drain (preempts).

### 4.3 Server → client messages (`WireDecoder.Decode`)

Decoding is tolerant: non-JSON / non-object / missing `type` ⇒ dropped (`null`); unknown `type`
⇒ `.Unknown` (ignored). Client consumes `ack`/`pong`/`auth_refresh_ack` internally; everything
else is forwarded to the engine.

| `type` | fields (snake_case in) | meaning / handling |
|---|---|---|
| `ack` | `session_id` | handshake complete; resolves `open`, sets `sessionID` |
| `speech_start` | — | VAD detected speech onset; gates the "listening…" dots |
| `interim` | `segment_idx`(int), `text`, `latency_ms`(num?), `is_final`(bool, **absent⇒true**) | one settled transcript segment. **No partial rendering** — `is_final:false` renders nothing |
| `route_hint` | `route`, `raw_transcript`? | routing hint |
| `eos_ack` | `raw_words`(int?), `expected_format_ms`(num?) | server accepted EOS; formatter wait estimate. Client may extend final-timeout |
| `formatting_progress` | `elapsed_ms`?, `expected_format_ms`? | formatter heartbeat |
| `final` | see 4.4 | **the one result** — resolves the take |
| `error` | `code`(string), `message`? | see 4.5. During handshake ⇒ fails `open` (not forwarded) |
| `language_mismatch` | `selected`, `detected`, `message`? | |
| `action.parsed` (also `action_parsed`) | `intent`{...} | parsed intent; if `intent.envelope=="kivi_query"` decodes as app-query, else legacy ParsedAction |
| `memory_build_result` | `snippets_processed`, `corrections_processed` | |
| `pong` | — | keepalive; arms dead-socket detector |
| `auth_refresh_ack` | `ok`(bool) | resolves `authRefresh` |
| *(other)* | — | `.Unknown(type)` — ignore |

### 4.4 `final` payload (`FinalPayload`)
The MVP paste target is **`formatted_text`** (fall back to `raw_transcript`).

| wire key | type | notes |
|---|---|---|
| `request_id` | string | |
| `formatted_text` | string | **the text to paste** |
| `raw_transcript` | string | pre-format transcript |
| `detected_language` | string? | |
| `detected_languages` | [string] | |
| `route` | string | |
| `resolved_persona` / `resolved_preset` | string? | |
| `content_kind` | string? | |
| `insertion_replace_before` | string? | mid-text join: text to delete before caret before pasting |
| `latency` | `{stt_segments_ms:[num], formatting_ms, total_ms}` | |
| `usage` | `{billable_word_count:int, monthly_word_limit:int?}` | |
| `runtime_pack` / `style_context` | opaque JSON | echoed back verbatim on `POST /v1/entity/extract`; never inspected |
| `output_suspect` | bool (also under `metadata.output_suspect`) | degraded-success badge |
| `server_durable` | bool? | server committed its snippet/usage txn before final |

### 4.5 Error codes (`ServiceErrorCode`)
`SERVICE_BUSY`, `PROTOCOL_ERROR`, `PARSE_ERROR`, `CONTEXT_TIMEOUT`, `USAGE_LIMIT_EXCEEDED`,
`USAGE_PREFLIGHT_UNAVAILABLE`, `STT_CONNECT_FAILED`, `STT_ERROR`, `STT_NO_RESULT`,
`EMPTY_TRANSCRIPT`, `MEMORY_BUILDER_UNAVAILABLE`, `MEMORY_BUILD_FAILED`, `UNAUTHORIZED`,
`IDLE_TIMEOUT`, plus `other(raw)` (unknown preserved). Engine maps to `TakeFailure`:
`EMPTY_TRANSCRIPT→empty`, `UNAUTHORIZED→unauthorized`, `USAGE_LIMIT_EXCEEDED→usageLimit`,
`SERVICE_BUSY→busy`, `IDLE_TIMEOUT→idleTimeout`, `STT_*→server(code)`.

### 4.6 Timing / budget constants (`DictationBudgets`)
| const | value | use |
|---|---|---|
| `AckTimeoutMs` | 4000 | handshake budget |
| `AuthRefreshTimeoutMs` | 4000 | auth_refresh_ack budget |
| `PingIntervalMs` | 20000 | keepalive cadence |
| `PongMissLimit` | 2 | consecutive silent intervals ⇒ socket dead (**gated on ever having received a pong** — a never-ponging server is never torn down) |
| `MaxPendingAudioFrames` | 50 | ~5 s cap; past it, drop OLDEST frame + count (backpressure) |
| `FinalTimeoutMs` | 20000 | client waits for `final` after EOS |
| `AuthRefreshLeadSeconds` | 180 | refresh fires at TTL−180s (~12 min) |
| idle default | 180 s | server-side if `idle_timeout_secs` omitted |

Anonymous/local sessions: `auth_refresh` short-circuits (returns false without sending) because
a `DICTATE_AUTH_MODE=none` server has no JWT verifier and by design never acks.

### 4.7 Audio format (capture side)
- **16 kHz, Int16, mono, interleaved PCM**, little-endian.
- **~100 ms frames = 1600 samples = 3200 bytes/frame** (`bytesPerFrame = (16000/1000)*100*2`). One frame per binary WS message.
- Bitrate ≈ 32 KB/s. Hardware captured at native rate (e.g. 48 k float32 via WASAPI) then converted/decimated to 16 k Int16 — the .NET client must resample itself (continuous resampler state, see `dictation-audio-pipeline.md`).

---

## 5. HTTP REST surface (`KiviRestClient`, `HttpClient`)

All requests go through one `SendAsync(method, path, query?, body?)`: builds `restBase + path`,
sets the three `X-Client-*` headers, `application/json` if body, attaches bearer, **retries once
on 5xx / timeout**, and does the **once-only 401 re-mint retry**. Success = HTTP 200–299; else a
`RestException(statusCode, body)`. Bodies encoded deterministically.

### 5.1 MVP-relevant endpoints
| Method | Path | Request body (JSON) | Response | Purpose |
|---|---|---|---|---|
| GET | `ready` | — | 2xx = up | readiness (also verifies token non-nil for non-anon) |
| GET | `v1/usage` | — | usage object (billable/limits/period/…) | quota display |
| POST | `v1/telemetry/dictation_completed` | `TelemetryDictationCompleted` (see 5.4) | 202, fire-and-forget | dictation telemetry |
| POST | `v1/feedback` | `{verdict:"up"｜"down"｜"clear", client_take_id?, result_text?, target_app?, client_platform, client_version?}` | fire-and-forget | 👍/👎 thumbs |
| POST | `v1/telemetry/dictation_latency_trace` | large latency-span object | fire-and-forget | perf traces |
| GET | `v1/sessions/{session_id}/final` `?date=YYYY-MM-DD` | — | see 5.3 | late-final recovery after final-timeout |

### 5.2 Edit / formatting endpoints (post-MVP, same stack)
| Method | Path | Request | Response |
|---|---|---|---|
| POST | `v1/format-preview` | `FormatPreviewRequest` (`raw_text`, `base_preset`, optional cosmetic/preset/…) | `{formatted: string}` |
| POST | `v1/edit` | `EditEndpointRequest` (`text`, `mode`, `preset?`/`preset_ids?`, `edit_request_text?`, app/persona ctx, optional screen-context) | `EditEndpointResult` **camelCase**: `{requestId, text, mode, editRequestText?, resolvedPersonaSlug, resolvedPreset?, resolvedPresetIds[], evidenceEventId?, modelUsed, latencyMs, clientAction?{type:"move_to_textbox"}}` |
| POST | `v1/compile-custom-instructions` | `{raw_instructions, instruction_kind?}` | `CompiledInstructions {bullets[], compiled, rejected, rejection_reason?, directives[]{directive,status:kept｜dropped｜ambiguous,reason?}}` |

> Note the asymmetry: WS + most REST are **snake_case**, but **`/v1/edit` responds camelCase**
> (`requestId`, `latencyMs`, `resolvedPresetIds`). Replicate exactly (two different
> `JsonSerializerOptions` / naming policies, or per-DTO `[JsonPropertyName]`).

### 5.3 Late-final semantics (`GET /v1/sessions/{id}/final`)
- **200** with `{state:"completed", session_id, formatted_text, raw_transcript, metadata{…}}` ⇒ `completed(FinalPayload)`.
- **409/404** with body `{state, retry_after_ms?, code?, message?}` ⇒ `in_progress` / `not_ready(retryAfterMs)` / `failed(code,message)` / `unknown_or_expired`; unparseable ⇒ `routeUnavailable`.

### 5.4 `TelemetryDictationCompleted` body
`{request_id, started_at_ms, finished_at_ms?, paste_outcome, success?, paste_target?, language?,
formatting_enabled?, session_id?, billable_word_count?}`.

### 5.5 Full REST catalog (personalization/memory beyond the MVP)
Personas/styles: `GET v1/personas`, `GET v1/personas/apps`, `PUT v1/personas/apps/assignment`,
`GET/PUT/DELETE v1/format-preferences` (`?slug=`), `GET v1/preset-marketplace`,
`GET/POST/DELETE v1/transform-presets`, `GET/POST v1/persona-cosmetic-styles`,
`GET/PUT/DELETE v1/persona-app-style-overrides` (+ `/apply-to-persona`),
`GET/PUT v1/persona-preset-selections`, `GET/PUT/POST v1/preset-suggestions`. Memory:
`GET/POST v1/memory-forest`, `GET/POST/PUT/DELETE v1/spoken-shortcuts`,
`GET v1/data-imports/status`, `POST v1/data-imports`, `POST v1/account-memory/bootstrap`.
Usage/org: `GET v1/usage`, `GET v1/org/workspace-usage`, `POST v1/workspace-data/migrate`,
`GET v1/usage/analytics` (`?range=30d`), `GET v1/leaderboard`. Telemetry: the four POSTs +
`POST v1/telemetry/edit_paste_fallback`. Also `POST v1/entity/extract` (echoes
`runtime_pack`/`style_context`).

---

## 6. Transport behavior notes for the .NET reimplementation
- **WS close-code / HTTP-upgrade-status must be surfaced separately.** With `ClientWebSocket`, a 401/403 on the upgrade throws `WebSocketException` — inspect it (in .NET you can pre-flight the token, or use a small handshake wrapper that reads the HTTP status) to distinguish auth rejection (re-mint+retry) from a plain network drop (`CloseStatus`). This is the .NET analog of Node `ws`'s `unexpected-response`.
- **Custom headers on the WS upgrade** (`Authorization`, `X-Client-*`) are settable via `ws.Options.SetRequestHeader(...)`. This is why the socket lives in-process and never in a WebView (a WebView `WebSocket` can set neither headers nor read the upgrade status).
- Inbound event buffer is capped — a flooding server sheds oldest (`Channel` bounded, `BoundedChannelFullMode.DropOldest`).
- Keepalive is an **application-level** `{"type":"ping"}` text frame (not a WS control-frame PING), and the server replies `{"type":"pong"}`. Replicate at the app layer, not via `ClientWebSocket.Options.KeepAliveInterval`.

---

## 7. Windows/.NET notes (macOS/Electron → Windows/.NET)

1. **Auth key storage — Keychain (mac) / `safeStorage` (Electron).** `kratosSessionToken` and `orgServiceJWT` → the OS secret store on Windows: **DPAPI** (`System.Security.Cryptography.ProtectedData`) or **Windows Credential Manager** (`ISecretStore` in `Kivi.Platform.Secrets`). The mint flow itself (`POST auth.sarvam.ai/api/v2/auth/jwt`, `X-Session-Token`, `{token,expires_at}`) is pure HTTP and ports unchanged via `HttpClient`.
2. **`X-Client-Platform`** is a server-side **version/feature gate**. Decide deliberately: send `"windows"` (may unlock/lock different gated features than `"macos"`), or mirror `"macos"` to inherit identical behavior — **confirm with backend which platform strings it recognizes** (cross-team dependency, `FEATURE-PARITY.md`).
3. **Audio capture + resample.** The wire only needs **16 kHz Int16 mono PCM in ~100 ms (3200-byte) binary frames**. Windows captures via **WASAPI** and **resamples to 16 k Int16 mono itself** — no server-side resampling. Keep resampler state continuous.
4. **Screen-context enrichment** (`screen_terms`, `screen_nodes`, `focused_field`, `surface_contexts`, `cursor_context`, `app_context`) is **entirely optional** on the wire (encoder omits when absent). MVP sends `{"type":"end_of_speech"}` with none of them. Windows equivalent later = **UI Automation** (deferred to M9); the wire DTOs are pure and can be built without the capture.
5. **`frontmost_app` / paste target.** The wire is agnostic (`app_context.bundle_id`, telemetry `paste_target`), but capture (which app is frontmost) and delivery (synthetic paste) are Windows-native: `GetForegroundWindow`+exe path; `SendInput` Ctrl+V. macOS bundle IDs have no Windows analog — substitute a stable app-key convention (exe path / AppUserModelID).
6. **WS upgrade headers** reachable only from a native socket (`ClientWebSocket`), not a WebView `WebSocket`. Keep the socket in the .NET process.
7. **UUID/session id, ISO8601 parsing, timezone id** are all BCL: `Guid.NewGuid()`, `DateTimeOffset.Parse`, `TimeZoneInfo` (map Windows→IANA via `TimeZoneInfo.TryConvertWindowsIdToIanaId`).

**Deferred / v1 non-goals:** screen-context wire fields (UI Automation, M9); prod/staging/qa
auth + JWT mint (M7 — loopback anonymous for the MVP).

> **Not applicable — Windows-only.** The reference's Linux paste/frontmost/secret-store notes
> (X11/Wayland, libsecret/kwallet) are dropped.
