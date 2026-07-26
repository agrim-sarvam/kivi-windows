# MAP: backend-service-api

Windows-only .NET port of the Electron reference map
(`_reference/sarvam-kivi-electron/docs/maps/backend-service-api.md`). The backend
(`kivi-service`) is unchanged — it is OS-portable Rust + Postgres. This map describes the
wire the **.NET client** speaks; all parity constants are byte-exact from the reference.

---

# Kivi Backend Service — Map for the .NET Client

The client the .NET app must emulate is the Electron client (`_reference/.../src/main/wire/`),
which itself mirrors the macOS Swift client. The backend is the Rust `kivi-service`.

## 0. The one architectural fact that changes everything

The client does **NOT** talk to Sarvam's realtime STT directly. It talks to **kivi-service**,
a stateful WebSocket proxy + formatter. kivi-service opens its own WS to Sarvam's
`saaras:v3-realtime` and re-exposes a *different, richer* protocol.

```
.NET client ──ws──> kivi-service /v1/dictate/stream ──wss──> api.sarvam.ai/speech-to-text-realtime/ws
                    (own protocol: type-tagged JSON        (upstream contract — you never
                     + binary PCM; formats via Gemma)       speak this directly)
```

- The upstream Sarvam contract is what kivi-service consumes; useful for understanding behavior, **not** the wire the .NET client speaks.
- The wire the .NET client speaks is `/v1/dictate/stream`, defined by the `ClientMessage`/`ServerMessage` enums in the service's `main.rs`.

The service is a hand-rolled TCP server: `handle_connection()` peeks the raw HTTP request line
and dispatches by substring. Everything that is not `/v1/dictate/stream` or `/v1/edit` and not
one of the REST paths returns 404.

---

## 1. Endpoints the client uses

Base URL resolution (`Endpoints`): the WS path is canonical `/v1/dictate/stream`; every REST
base derives from the same host (ws→http, wss→https, path stripped).

| Endpoint | Local URL | Type | Purpose |
|---|---|---|---|
| **STT dictation** | `ws://127.0.0.1:8788/v1/dictate/stream` | WebSocket (`ClientWebSocket`) | Live transcription + formatting (the core loop) |
| **Text edit** | `http://127.0.0.1:8788/v1/edit` | POST JSON (`HttpClient`) | Second-pass edit of existing text |
| `/health` | `http://127.0.0.1:8788/health` | GET | `{"status":"ok","service":"sarvam-dictate-service-streaming"}` |
| `/ready` | `http://127.0.0.1:8788/ready` | GET | Readiness + DB diagnostics |
| `/metrics` | `http://127.0.0.1:8788/metrics` | GET | Prometheus text (loopback-open) |
| `/v1/format-preview` | POST | Format text without STT (settings preview) |
| `/v1/format-preferences` | GET/PUT/DELETE | Persona/app style prefs |
| `/v1/telemetry/*`, `/v1/personas`, `/v1/history/search`, `/v1/snippets`, `/v1/transform-presets`, `/admin/v1/*` | various | Personalization, history, admin. **Not needed for the transcription MVP.** |

Prod/staging/qa hosts (for reference): `wss://kivi.sarvam.ai/...`,
`wss://kivi.aws-staging.sarvam.ai/...`, `wss://kivi.aws-qa.sarvam.ai/...` (QA is the shipped
default). The client's Custom endpoint normalizer force-pins the `/v1/dictate/stream` path.

---

## 2. The `/v1/dictate/stream` WebSocket protocol (THE core contract)

Both directions are JSON objects tagged with a `"type"` field, **snake_case**. Audio is sent
as **binary frames** (no JSON wrapper).

### 2.1 Session lifecycle (exact order)

1. Client opens WS (auth via header — see §5).
2. Server immediately sends **`ack`**: `{"type":"ack","session_id":"<uuid-v4>"}`. session_id is server-generated.
3. Client sends the first control frame: **`context`** (must arrive within **30 s** or the slot is dropped).
4. Client streams **binary PCM audio frames**.
5. Server emits **`speech_start`** (once VAD fires), then a stream of **`interim`** segments (one per VAD utterance).
6. Client sends **`end_of_speech`** (drains queued audio first — see ordering contract below).
7. Server emits optional **`eos_ack`** / **`formatting_progress`**, then exactly one **`final`** with `formatted_text`.
8. Client closes (server does not always close; treat `final` as terminal).

### 2.2 Client → Server messages (`ClientMessage`)

| `type` | Payload | Notes |
|---|---|---|
| `context` | `ClientContext` (see §2.4) | First frame. Configures the session. |
| *(binary frame)* | raw PCM bytes | 16 kHz **Int16 mono little-endian** (`linear16`). ~100 ms/frame ≈ **3200 bytes**. |
| `end_of_speech` | `{general_app_style_preset?, language_hint?, focused_field?, screen_nodes?, primary_surface_id?, surface_contexts?, evidence_capture_enabled?, cursor_context?}` — all optional | Finalize. For the MVP send just `{"type":"end_of_speech"}`. |
| `cancel` | — | Abort take, no final. |
| `ping` | — | Server replies `{"type":"pong"}`. |
| `auth_refresh` | `{token}` | Swap JWT mid-session (prod auth only). |
| `memory_build` | `{snippets,corrections}` | Personalization batch job (not MVP). |

### 2.3 Server → Client messages (`ServerMessage`)

| `type` | Key fields | Notes |
|---|---|---|
| `ack` | `session_id` | First frame after connect. |
| `speech_start` | — | VAD detected speech onset → switch UI to "listening". |
| `interim` | `segment_idx:int`, `text:string`, `latency_ms` | Live transcript. **One per VAD utterance.** Concatenate segments for the live view. |
| `eos_ack` | `raw_words`, `expected_format_ms` | Sent after `end_of_speech` when the client advertises `supports_formatting_progress`. |
| `formatting_progress` | `elapsed_ms`, `expected_format_ms` | Optional progress ticks while Gemma formats. |
| **`final`** | `request_id`, **`formatted_text`**, `raw_transcript`, `detected_language`, `detected_languages[]`, `route`, `latency`, `server_durable:bool`, +optional `usage`, `resolved_persona`, `resolved_preset`, `content_kind`, `formatting_meta`, `runtime_pack`, `style_context` | **The deliverable.** Paste `formatted_text`. `raw_transcript` is the unformatted STT. |
| `route_hint` | `route`, `raw_transcript` | Early hint before the final. |
| `edit_final` | `request_id`, `text`, `edit_request_text`, `resolved_persona_slug`, `model_used`, `latency_ms` | Only for `session_purpose:"voice_edit"` sessions. |
| `action.parsed` | `intent` | Only if `transcription_mode:"action"` (rule-based stub). |
| `language_mismatch` | `selected`, `detected`, `message` | Advisory after final. |
| `error` | `code`, `message` | e.g. `SERVICE_BUSY`, `IDLE_TIMEOUT`. Not always fatal. |
| `pong`, `auth_refresh_ack` | — | Keepalive / refresh ack. |

`route` values: `no_format`, `deterministic`, `llm_small`, `llm_large`, and `formatting_disabled`
(when `formatting_enabled:false`). When formatting is off, `formatted_text == raw_transcript`
and `route:"formatting_disabled"`.

### 2.4 `ClientContext` — the session-open payload

Full field set (snake_case). For the **transcription MVP** only a handful matter; the rest are
personalization/screen-context and can be omitted (all default on the server).

**MVP-critical fields (as the shipped client sends them):**
```json
{
  "type": "context",
  "transcription_mode": "codemix",     // shipped client default (NOT "transcribe" — see resolution note)
  "formatting_enabled": true,          // server serde default is FALSE — ALWAYS emit explicitly
  "session_id": "<echo the ack session_id or client-generated>",
  "auto_persona_resolution": true,
  "client_capabilities": { "spoken_shortcuts_v1": true },
  "supports_formatting_progress": true
}
```

What the client actually encodes (minimal set): `type, language_hint?, transcription_mode,
formatting_enabled, session_id, client_take_id?, trace_id?, frontmost_app?, app_context?,
general_app_style_preset?, auto_persona_resolution, selected_persona_slug?,
user_app_assignments?, client_format_prefs?, client_capabilities, supports_formatting_progress,
idle_timeout_secs?`.

**Formatting/personalization fields (needed only once `formatting_enabled:true`):**
- `frontmost_app`, `app_context {app_name,bundle_id,window_title,field_type,...}` — resolve the persona/style for the active app. On Windows, `bundle_id` is filled from the agreed app-identity convention (exe path / AppUserModelID; see `platform-coupling-audit.md §12`).
- `general_app_style_preset` (closed enum), `selected_persona_slug`, `auto_persona_resolution` (default `true`), `user_app_assignments {appKey→slug}`, `client_format_prefs {slug→ClientFormatPrefs}` — full offline style state so the server can format without a DB read under no-auth.
- `screen_nodes[]`, `focused_field`, `surface_contexts[]` — screen-context grounding. **All UI-Automation-derived on Windows; optional; skip for MVP** (deferred to M9).
- `idle_timeout_secs` (default **180**): if no VAD speech for this window while mic open → server ends session with `error{code:"IDLE_TIMEOUT"}`.

### 2.5 Audio framing details

- Format: **16 kHz, Int16, mono, little-endian PCM**. Sample rate is hardcoded `16000` server-side.
- Frame cadence: **~100 ms** chunks. 16000 samples/s × 2 bytes × 0.1 s = **3200 bytes/frame** = 1600 samples. Bitrate ~32 KB/s.
- Backpressure: client caps `maxPendingAudioFrames = 50` (~5 s), drops oldest past cap.
- **Ordering contract**: `end_of_speech` must be sent *after* all queued audio has flushed, because on EOS the server stops reading binary frames — any audio still queued is discarded, truncating the last words. The .NET client must drain its send queue before the EOS text frame.

---

## 3. Upstream Sarvam leg (what kivi-service does on your behalf)

You don't implement this, but it explains the timing/segmentation you'll observe:

- Default upstream base `wss://api.sarvam.ai/speech-to-text-realtime/ws`.
- Pinned params: `model=saaras:v3-realtime`, `stream_type=simulated`, `mode=codemix`, `endpointing=vad`, `encoding=linear16`, `sample_rate=16000`, `return_timestamps=true`, `silence_duration_ms=1000`.
- Auth to Sarvam (server-side): header `API-SUBSCRIPTION-KEY: <SARVAM_API_KEY>`.
- `stream_type=simulated` ⇒ **no upstream partials**; Sarvam emits one non-streaming `transcript.final` per VAD-detected utterance. kivi-service surfaces each as an **`interim`** to your client, then formats the concatenation into the single `final`.
- Language allowlist: `en-US`→`en-IN`; anything not in the known set (`en-IN,hi-IN,ta-IN,te-IN,bn-IN,kn-IN,mr-IN,gu-IN,ml-IN,pa-IN,or-IN,…`) → `auto`.

---

## 4. `/v1/edit` — second-pass text edit

HTTP `POST /v1/edit`, JSON in / JSON out (via `HttpClient`).

**Request** (`EditRequest`, snake_case) — minimal:
```json
{ "text": "<current text to edit>",
  "edit_request_text": "<instruction, e.g. 'make it formal'>",
  "mode": "custom",
  "app_bundle_id": "...", "app_name": "...",
  "persona_slug": "global", "preset": "...", "preset_ids": [] }
```
Rich optional fields for selection/screen context: `target_text, context_before, context_after,
full_context, selected_range{location,length}, field_role, screen_nodes[], surface_contexts[],
…` (all optional).

**Response** (`EditResponse`, **camelCase**):
```json
{ "requestId": "...", "text": "<edited output>", "mode": "...",
  "editRequestText": "...", "resolvedPersonaSlug": "global",
  "resolvedPreset": "...", "resolvedPresetIds": [],
  "modelUsed": "gemma-4-sarvam-flow", "latencyMs": 812 }
```
**Read the `text` field** (not `edited` — this is a known gotcha). Under `DICTATE_AUTH_MODE=none`
there's no user scope, so edit takes the no-preferences path (persona `global`, empty preset
list — a benign WARN is expected). Voice-edit variant runs over the dictation WS instead
(`session_purpose:"voice_edit"` in context, final comes back as `edit_final`).

---

## 5. Auth & feature flags

**Auth modes** (`DICTATE_AUTH_MODE`):
- `none` (local dev): no JWT. Identity resolved from optional headers: `x-kivi-local-user-id`, `x-kivi-local-org-id`, `x-kivi-local-workspace-id` — each a UUID; missing → default **nil UUID** `00000000-0000-0000-0000-000000000000`. **The .NET MVP can send no auth headers at all** and get the fixture tenant.
- `org_service_v2` (prod/staging/qa): org-service v2 JWT required.

**How the token is carried on the WS** (matters for native vs WebView socket):
1. `Authorization: Bearer <token>` — for native callers. **`ClientWebSocket` sets this via `Options.SetRequestHeader("Authorization", ...)`.**
2. `Sec-WebSocket-Protocol: auth.<base64url-token>` — the WebView-safe method (a WebView `WebSocket` can't set arbitrary headers). Not needed for the .NET client, which owns a native socket.
3. `?token=` query — disabled unless `DICTATE_ALLOW_QUERY_TOKEN=1`.

**Feature flags / env of note**: `LOAD_TEST_MODE=synthetic` (bypass Sarvam+Gemini with stubs —
handy for client E2E without real STT), `LOG_FORMATTER_PROMPT=1`. Per-replica caps:
`MAX_WS_SESSIONS_PER_REPLICA` (10), `MAX_SARVAM_STREAMS_PER_REPLICA` (8),
`MAX_LIVE_GEMINI_PER_REPLICA` (8) → over cap ⇒ `error{code:"SERVICE_BUSY"}`.

---

## 6. Formatting pipeline (Gemma)

Runs only when `formatting_enabled:true`.

- **LLM routing**: local dev uses the **Gemma OpenAI-compatible endpoint** (`OPENAI_COMPAT_API_KEY`/`GEMMA_API_TOKEN`/…, default model `gemma-4-sarvam-flow`). Without any key it falls back to Gemini/Vertex via Google ADC. **Core dictate+format needs NO Google ADC if a Gemma key is set.**
- **Route decision**: `no_format` / `deterministic` / `llm_small` / `llm_large`.
- Output returned on the `final` frame as `formatted_text` (+ `route`, `resolved_persona`, `resolved_preset`, `formatting_meta`).

For the trimmed MVP you may **set `formatting_enabled:false`** to paste raw (`formatted_text ==
raw_transcript`), but the shipped client sends `true` — do the same for real output.

---

## 7. EXACT steps to run the service locally (auth off + local Postgres)

**Postgres is a hard prerequisite** — `DATABASE_URL` missing or unreachable ⇒
`std::process::exit(78)` (no sqlite/in-memory fallback).

The service is Rust; the steps below match the reference. On the developer's box, run Postgres
16 and the service; the .NET app just points its `ClientWebSocket` at `ws://127.0.0.1:8788`.

```bash
# 1. Rust toolchain (pinned by rust-toolchain.toml; rustup auto-installs).
# 2. Local Postgres 16, DB "kivi_db" (or per the service .env):
#      DATABASE_URL=postgres://<user>@localhost:5432/kivi_db
#    (tables are created by the service's migrations at startup — do NOT create by hand)
# 3. .env.local (secrets git-ignored). Minimum to boot with auth off:
#      DATABASE_URL=postgres://<user>@localhost:5432/kivi_db
#      SARVAM_API_KEY=<real key>            (real STT needs this)
#      DICTATE_AUTH_MODE=none
#      PORT=8788   STREAMING_BIND_HOST=127.0.0.1
#      CORS_ALLOW_ORIGIN=*
#    For LOCAL formatting without Google ADC, also set a Gemma key:
#      GEMMA_API_TOKEN=<key>  (+ optional GEMMA_BASE_URL / GEMMA_MODEL)
# 4. Run:
#      DICTATE_AUTH_MODE=none PORT=8788 cargo run -p kivi-service
#    Verify: curl -s http://127.0.0.1:8788/health  -> {"status":"ok",...}
```

**Healthy startup** logs: `Connected to direct Postgres`, migration lines, then
`Sarvam dictate streaming service listening on http://127.0.0.1:8788 (…, auth=None)`.

**Failure signatures**: `DATABASE_URL is required` (env not loaded); `DATABASE_URL set but RDS
connection failed` (Postgres down/wrong URL); `ADC unavailable … fail closed` = **non-fatal**
(dictation still works, only memory/persona jobs skip).

**Fast client-E2E without real STT/LLM**: `LOAD_TEST_MODE=synthetic` bypasses Sarvam + Gemini
with stubs — exercises the full WS handshake and framing offline (but still needs Postgres).

Minimal smoke of the WS from the .NET client:
```
ws://127.0.0.1:8788/v1/dictate/stream  → recv {type:ack,session_id}
→ send {type:context,transcription_mode:"codemix",formatting_enabled:true,session_id,
        auto_persona_resolution:true,client_capabilities:{spoken_shortcuts_v1:true},
        supports_formatting_progress:true}
→ send binary 3200-byte 16k Int16 mono PCM frames
→ send {type:end_of_speech}  → recv interim* then final{formatted_text}
```

---

## 8. Windows/.NET notes (what changes from the reference, what doesn't)

The backend is unchanged; only the client edges differ. Everything here is a pure network
contract identical regardless of client OS.

1. **WS auth header vs subprotocol.** `System.Net.WebSockets.ClientWebSocket` **can** set `Authorization` + `X-Client-*` upgrade headers (`Options.SetRequestHeader`), so use the `Authorization: Bearer` form (option 1 in §5). This is *why the socket must live in the .NET process and never inside a WebView* — a WebView `WebSocket` can set neither headers nor read the upgrade status. For local `DICTATE_AUTH_MODE=none` this is moot: send no auth, get the nil-UUID tenant.

2. **Audio capture → 16 kHz Int16 mono PCM.** Windows uses **WASAPI** (via NAudio or CsWin32 interop) → resample native rate → 16 000 Int16 mono LE, chunk to ~100 ms (3200-byte) frames, send as binary WS messages. **Down-mix to mono** — the server trusts the declared format and silently mis-decodes stereo. Keep resampler state continuous across frames.

3. **Echo cancellation.** Enable the WASAPI voice-communication capture category for mic-path AEC/NS where the device supports it. Server is agnostic — it just wants clean 16 k mono PCM.

4. **Screen context (`screen_nodes`, `focused_field`).** Windows equivalent is **UI Automation** — **deferred to M9**. All these wire fields are optional; **omit them for the MVP** (send `{"type":"end_of_speech"}`). Map each element to `ClientScreenNode {bundle_id, role, subrole, title, value, frame{x,y,w,h}, is_focused,…}` when eventually built.

5. **Paste into active app** is entirely client-side, not in this service. Windows = clipboard + `SendInput` Ctrl+V (see `dictation-audio-pipeline.md §8`, `platform-coupling-audit.md §3`).

6. **Global hotkey + frontmost-app detection** (feeds `frontmost_app`, `app_context.bundle_id`) is Windows-native and client-side: `WH_KEYBOARD_LL` hotkey, `GetForegroundWindow`+`QueryFullProcessImageName` for the app id.

7. **Segmentation model to reproduce in the UI.** Upstream runs `simulated`/VAD, so you get **one `interim` per spoken utterance** (not token-by-token), then a single `final`. Append/replace by `segment_idx`, swap to `formatted_text` on `final`. `speech_start` is your "listening" cue.

**Deferred / v1 non-goals here:** screen-context enrichment fields (UI Automation, M9);
prod/staging/qa auth (loopback anonymous only for the MVP).

> **Not applicable — Windows-only.** The reference's Linux paste (`xdotool`/`wtype`/Wayland)
> and Linux mic notes are dropped; there is no Linux client.

Key reference files: the service wire (`_reference/.../src/main/wire/*`) and the immutable Rust
service. Nothing under `_reference/` is edited.
