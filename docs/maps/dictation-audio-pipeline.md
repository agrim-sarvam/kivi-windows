# MAP: dictation-audio-pipeline

Windows-only .NET port of `_reference/sarvam-kivi-electron/docs/maps/dictation-audio-pipeline.md`.
Scope: hotkey → WASAPI mic capture → AEC → 16 kHz PCM framing → WebSocket streaming to
kivi-service STT → partials/finals → insertion planning → paste into frontmost app →
ledger/history. All timing budgets and framing constants are byte-exact.

## 1. End-to-end flow (one dictation take)

```
[hotkey held]  HotkeyService (WH_KEYBOARD_LL, dedicated thread + message pump)
   → DictationOrchestrator.HotkeyDown → FlowEngine.FnDown() → BeginTake(.Dictation)
   → IDictationService.Begin → DictationCoordinator.Begin()   [the SOLE owner of mic+WS]
        ├─ ArmEarlyCapture()  → PendingTakeCapture.Arm → WasapiCapture.StartEarly()  (mic ALREADY recording)
        ├─ generation++ ; build ContextMessage
        └─ RunTakeDriver (background Task):
             1. mic permission (skipped if pre-authorized)
             2. audio.Start() → adopts press-time capture (flush prefix) or cold-starts
                └─ capture loop: each 16kHz Int16 frame → PreConnectBuffer.Enqueue OR client.SendAudio
             3. KiviServiceClient.OpenAsync(): WS connect → await `ack` (≤4s) → send `context`
             4. PreConnectBuffer.Flush → drains buffered frames to socket in order
             5. event consumer: WS `interim`/`final`/`error` → DictationEvent → engine
[hotkey released]  → FlowEngine.FnUp() → StopListening() → dictation.RequestStop()
   → stop quarantine (if no audio yet) → 200ms trailing tail → mic.Stop() → drain → send `end_of_speech`
   → server `final` → ResolveFinal → TakeResult
   → engine .Final event → orchestrator paste dispatch:
        DictationJoinRewritePlanner → DictationInsertionPlanner → PasteService (clipboard + synth Ctrl+V)
   → onCapture(draft) → History; TakeLedger closed
```

The **engine never touches mic or network**. `DictationCoordinator` is the single owner; it
translates the WS stream into `DictationEvent`s the frame-driven `FlowEngine` consumes. In .NET
this is all in-process (`async`/`await` + events) — there is no IPC boundary.

## 2. Front of pipeline: hotkey → gesture → engine

| Component (.NET) | Namespace | Role |
|---|---|---|
| `LowLevelKeyboardHookService` | `Kivi.Platform.Hotkey` | `SetWindowsHookEx(WH_KEYBOARD_LL)` on a **dedicated native thread with its own message pump** (`GetMessage` loop) — never on the UI thread (a busy pump makes the OS drop the hook). Sees key-down + key-up. Can optionally swallow the key (return `1` from the hook proc). Forwards edges to the orchestrator asynchronously. |
| Hotkey policy | `Kivi.Platform.Hotkey` | **No `fn` key on Windows.** Default trigger = **Right-Ctrl hold** (rebindable). Chord windows carried from the classifier. |
| `GestureClassifier` | `Kivi.Core.Orb` | **Pure, time-injected.** Classifies `Press(down,up,secondDown)` → tap / hold / doublePress / longHold. Ported verbatim. |
| `ShortcutScheme` | `Kivi.Core.Orb` | Default: **hold = dictate** (push-to-talk), doublePress = edit, longHold = act, tap = home, Esc = cancel. |

`FlowEngine.BeginTake(kind)` bumps `takeGeneration`, resets the segment accumulator, and calls
`dictation.Begin(...)` with a sink that re-enters via `EnqueueServiceEvent` (drained at top of
`Step()`). `StopListening()` sets phase `.Processing`, arms the 20 s final-timeout budget, and
calls `dictation.RequestStop()`. **Product invariant**: "gesture is the only take authority" —
VAD/decibel level never cancels a take; the server owns the "heard nothing" verdict.

## 3. Audio capture — the heart of the MVP

### 3.1 Canonical output format (what streams to STT)
**16 kHz, Int16, mono, little-endian, packed PCM.** No container, no header. Emitted as `byte[]`
blocks, forwarded verbatim as **binary WS frames**. Frame cadence ≈ **100 ms** ⇒ 1600 samples ⇒
**3200 bytes/frame**, ≈ **32 KB/s**. Fixed regardless of device native rate — on-device
downsampling always happens.

### 3.2 Capture backend (Windows)
The macOS three-backend split (AUHAL / VPIO / experimental Speex) collapses to **one WASAPI
path** on Windows:

| Concern | Windows/.NET decision |
|---|---|
| Device capture | **WASAPI** (`IAudioClient`/`IAudioCaptureClient`) via **NAudio** (`WasapiCapture`) or thin CsWin32 interop. Bind a specific device by its WASAPI id string (persist the **stable device id string**, not a volatile handle). Capture float32 at the device's native rate. |
| Mix to mono | Sum/average all captured channels to mono (a mic on any channel of a multi-in interface is captured), mirroring the reference `sumInterleavedToMono`. |
| AEC / NS | Enable the **WASAPI voice-communication / communications capture category** so the OS applies mic-path AEC/NS where the device supports it. This is **NOT** system-audio AEC parity (see R2). No VPIO warm-up cost applies. |
| Realtime discipline | No allocation / no async on the capture callback thread — preallocate the scratch + resampler buffers, hand each frame off via a bounded `Channel<byte[]>`. |

### 3.3 Conversion / resample
- Build a resampler from `Float32 @ hwRate mono` → `Int16 @ 16000 mono`. Options: NAudio `MediaFoundationResampler` / `WdlResamplingSampleProvider`, `libsamplerate` binding, or a hand-written polyphase resampler. **Whatever you use, keep resampler state continuous across frames.**
- **The `.noDataNow` continuity rule (critical):** the macOS converter returned `.noDataNow` (NOT `.endOfStream`) on the input-request probe; `.endOfStream` would kill the converter after the first buffer, capping the session at one 100 ms frame. **The .NET reimplementation must keep the resampler primed/continuous across frames** — do not reset per-frame, or you get the "one-frame-then-dead" bug (R10). Validate via the golden-transcript test on real audio.
- Convert Float32→Int16 (clamp, little-endian), accumulate to 1600-sample (100 ms) frames, hand off in order off the capture thread.

### 3.4 Level meter (`AudioLevelMeter`)
RMS → dBFS curve: `20·log10(rms)` clamped to **[-45, -3] dBFS**, normalized 0…1. EMA smoothing
**α = 0.3**. Drives the orb "listening" animation only — **never gates the take**. Port the math
verbatim.

## 4. Press-time capture head-start (latency-critical)

`PendingTakeCapture` — a single-slot handoff. **Root problem:** the take's driver could start the
mic up to ~1 s after the press under load → first words physically never captured.

Fix: `Begin()` synchronously fires `ArmEarlyCapture()` → a background task starts a **fresh
WASAPI capture immediately** (mic recording before the driver even schedules). Frames buffer into
a bounded `earlyPrefix` (cap **64 frames ≈ 6.4 s**). The take's audio source **claims** it —
flushes prefix into the stream first, then live frames. Contract:
- single slot, claim-once; newest press replaces + discards unclaimed predecessor;
- unclaimed capture expires after **1500 ms** (`defaultExpiryMs`);
- **device revalidation at claim** — if the resolved device ≠ armed device, discard and cold-start on the right mic;
- one 30 ms re-claim retry (arm task can lose the schedule race on a quiet machine).

## 5. Buffering & timing budgets

### 5.1 PreConnectBuffer
Accumulates frames captured **before the WS handshake completes**; flushes in order after
`ack`+`context`. After flush, new frames bypass (direct send). Default `maxFrames = 500` (ring:
drop oldest). **Drain-then-flip** contract: `isFlushed` flips only after the queue empties; the
pump calls `Enqueue` unconditionally and direct-sends on `false` — closes the check-then-enqueue
TOCTOU so **no frame is ever dropped or reordered**. (In .NET, a `lock` or a single-consumer
`Channel` gives the same guarantee.)

### 5.2 Stop chain (`RequestStop` → `StartStopDrainIfPossible` → `SendStopIfReady`)
On release:
1. **Stop quarantine**: if the "first_audio_frame_captured" mark is missing (stop beat the first frame — quick tap outracing cold capture), hold the mic-stop until the take has heard ≥ **500 ms** (`stopQuarantineMinCaptureMs`) of audio or hits the **1200 ms** hard cap (`stopQuarantineMaxWaitMs`, poll 100 ms). Guarantees a gesture always ships its audio.
2. **Trailing tail**: sleep **200 ms** (`stopTrailingTailMs`) — mic keeps running so the VAD gets the last word's decay + room tone (cutting the mic dead at release clipped the final word).
3. Stop mic → await the capture drain → then send `end_of_speech` (ordering: fresh frames must not race behind EOS).

### 5.3 All timing constants (`DictationBudgets`)

| Budget | Value | Meaning |
|---|---|---|
| `FinalTimeoutMs` | **20 000** | Engine waits this long after EOS for `final` before raw transcript becomes the final |
| `AckTimeoutMs` | **4 000** | WS handshake `ack` budget |
| `AuthRefreshTimeoutMs` | **4 000** | `auth_refresh_ack` budget |
| `PingIntervalMs` | **20 000** | Keepalive ping cadence |
| `PongMissLimit` | **2** | 2 silent intervals (~40 s) ⇒ proven-live socket declared dead |
| `MaxPendingAudioFrames` | **50** | Client send-queue cap (~5 s); drop oldest past cap |
| `SpokenEditFlushMs` | 800 | Spoken-instruction trailing flush |
| `ProcessingStillWorkingMs` | 3 000 | "still working…" hint |
| `ProcessingLongerThanUsualMs` | 9 000 | "taking longer than usual" hint |
| `FormattingProgressAbsoluteCapMs` | 120 000 | Hard cap for formatter-driven final waits |
| `StaleStopChainRecoveryMs` | 15 000 | Force-recover a stranded stopped take |
| `StaleListeningTakeCapMs` | 600 000 | A live listening take is exempt until 10 min |
| `AudioDegradedFrameThreshold` | 10 | Dropped frames (~1 s) ⇒ flag `audioDegraded` |

## 6. WebSocket streaming (`Kivi.Core/Wire/KiviServiceClient.cs`)

- **Endpoint**: `/v1/dictate/stream`. local `ws://127.0.0.1:8788` (anonymous, `DICTATE_AUTH_MODE=none`, no bearer); qa/staging/prod are `wss://…`. REST base = same host.
- **Transport** = `System.Net.WebSockets.ClientWebSocket` (request timeout 30 s). Receive loop yields text / binary / close. **401/403 on the HTTP upgrade** surfaces as a `WebSocketException` distinct from a network drop.
- **Client is per take**, one instance. Lifecycle:
  1. `OpenAsync`: connect → set headers `X-Client-Platform`, `X-Client-Version`, `X-Client-Timezone`, `Authorization: Bearer <jwt>` (omitted if no token) → await `ack` (4 s) → send `context` text frame → start ping loop.
  2. `SendAudio(frame)`: appends to `pendingAudio`, a pump sends binary frames FIFO. Past 50-frame cap: drop oldest, count.
  3. `EndOfSpeechAsync(msg)`: **drain pump to empty first**, then send EOS text frame.
  4. events forwarded to the engine minus handshake artifacts (`ack`/`pong`/`auth_refresh_ack` consumed internally).
  5. `CancelAsync()`: sends `{"type":"cancel"}` (bounded 800 ms best-effort, does NOT drain) then closes.
- **Keepalive/liveness**: app-level ping every 20 s. Dead-socket detection gated on `everReceivedPong`. 2 consecutive silent intervals ⇒ link lost + teardown.
- **Auth rejection recovery**: one forced JWT re-mint + one reopen on `unauthorizedUpgrade`/wire `UNAUTHORIZED`.

## 7. Wire protocol

Envelope: snake_case `{"type": ...}` over WS. Decoding is **tolerant** (unknown types → ignored,
additive fields ignored). Full field tables in `service-client-wire.md`. Audio = **raw binary
frames** (16 kHz Int16 mono PCM), not JSON. The `final` paste target is **`formatted_text`**
(fall back to `raw_transcript`).

## 8. Final resolution → insertion → paste

`ResolveFinal` builds `TakeResult{rawSegments, finalLines, metrics, pasteContext, audioDegraded}`,
fires `onFinal`, emits `CaptureDraft` (→ History), delivers `.Final` to the engine, then awaits
persistence (gates only destructive cleanup, never the paste).

Paste dispatch (`Kivi.App/DictationOrchestrator.cs` → `Kivi.Platform.Paste`):
1. **`DictationJoinRewritePlanner`** (`Kivi.Core.Planner`): if `insertion_replace_before` present + span starts with join punctuation, re-verify the caret's preceding text against the commit-time snapshot (UI Automation, deferred); approve (delete N chars via synthetic backspaces) or degrade to standalone (strip leading punct, re-capitalize). **Pure logic — port verbatim; the UIA snapshot is the only native dependency (deferred).**
2. **`DictationInsertionPlanner`** / `PasteBoundaryPlanner` (`Kivi.Core.Planner`): pure boundary logic — computes leading/trailing spaces from the char left/right of the caret (no double-space, no space before `.,?!:;)`, blind fields always self-delimit to avoid `"fast.Is it"` fusion). **Port verbatim.**
3. **Secure-input gate**: if a password field is detected (best-effort via UI Automation), make **no paste, no clipboard write** — keep text in orb with a manual-copy affordance.
4. **`SendInputPasteService`** (`Kivi.Platform.Paste`): write payload to the clipboard (mark it "we-just-wrote-this" for the history poller — Windows has no transient-UTI/`changeCount`) → sleep **30 ms** → optional Ctrl+A (full-field replace for terminals/some editors) → synthesize **Ctrl+V** via `SendInput`; **release any held modifiers first** (PTT means Ctrl may be down); detect terminal → **Ctrl+Shift+V**; **paste without re-foregrounding** (the orb is non-activating so the target never lost focus — avoids the restricted `SetForegroundWindow`). Restore the clipboard after confirmed paste.
   - Newlines: **type each line, use a literal line break between lines** — never synthesize Return-as-submit.
   - Unicode-typing fallback (no clipboard): `SendInput` with `KEYEVENTF_UNICODE`, 16-unit chunks, small inter-char delay (the reference's Cursor `@`-picker nicety) — deferred; clipboard+paste is the primary path.
5. Post-paste verification (UI Automation readback) runs separately — orb completion boundary is at Ctrl+V-post time, not readback.

`ForegroundAppResolver` (`Kivi.Platform.Frontmost`): tracks the last non-Kivi foreground app via
`GetForegroundWindow` + `GetWindowThreadProcessId` + `QueryFullProcessImageName` (polled +
captured at key-down) — the paste/context target.

## 9. Durability / recovery (secondary but load-bearing)
- `TakeLedger` — per-take crash journal (`opened`/`segment`/`eosSent`/`finalReceived`/`closed`), flushed per settled segment; recovered on next launch. (JSON/SQLite under `%APPDATA%\Kivi`.)
- `TakeAudioStore` — encrypted PCM retention for failed-take retry; replay re-streams retained frames through the *same* `RunTakeDriver` (same take id → upgrades History row in place). AES-GCM per-install key, the key itself DPAPI-protected.
- `DictationLatencyTrace` — ~30 marks uploaded on failure/degraded for triage.

---

## Windows/.NET notes (macOS/Electron → Windows/.NET)

The entire **wire protocol, framing format, buffering logic, timing budgets, and
insertion/boundary planning are pure and platform-agnostic** — port them verbatim to `Kivi.Core`.
The native edges:

**Audio capture (fully replace):**
- AUHAL / `AVAudioEngine` / `AVAudioConverter` (mac) → **WASAPI** (`IAudioClient`, via NAudio or CsWin32). Keep the **contract**: device-native capture → downsample to **16 kHz Int16 mono LE**, ~100 ms frames. Persist a **stable device id string**.
- **Resampler continuity** (the `.noDataNow` equivalent): keep resampler state continuous across frames — do not reset per-frame, or you get the "one-frame-then-dead" bug.
- **AEC/NS/AGC**: enable the WASAPI voice-communication capture category (mic-path AEC where the device supports it). Full system-audio AEC (WASAPI-loopback capture of other apps + a WebRTC-style APM) is **deferred to M9** (R2). The no-AEC WASAPI path is the honest baseline.

**Hotkey (fully replace):**
- `CGEventTap` / `fn`=63 (mac) → **`SetWindowsHookEx(WH_KEYBOARD_LL)`** on a dedicated native thread with its own `GetMessage` pump. There is **no `fn` key** on Windows — default = **Right-Ctrl hold**. The hook sees key-down + key-up (push-to-talk release) and can optionally consume. The `GestureClassifier`/`ShortcutScheme` timing logic is pure and portable (holdMs 420 / doubleTap 450 / longHold 600).

**Paste into frontmost app (fully replace):**
- `NSPasteboard` + synthetic `CGEvent` ⌘V (mac) → **clipboard + `SendInput` Ctrl+V**. Keep the **sequence**: write clipboard → ~30 ms settle → synthesize paste → restore clipboard. **Change ⌘ to Ctrl**; terminal → Ctrl+Shift+V; release held modifiers first; paste without re-foregrounding.
- **Secure-input detection** — best-effort password-field detect via UI Automation.
- **Frontmost-app resolution** — `GetForegroundWindow` + exe path; `app_context.bundle_id` uses the agreed Windows app-key convention.

**Screen context (deferred for MVP):**
- `cursor_context`, `screen_terms`, `focused_field`, `screen_nodes`, commit-time caret snapshots all come from macOS Accessibility → the Windows equivalent is **UI Automation** (deferred to M9). These are optional enrichment on `end_of_speech` — the MVP sends EOS without them. The join-rewrite (`insertion_replace_before`) degrades gracefully to standalone text when no caret snapshot is available.

**Portable as-is (reuse directly, `Kivi.Core`):** the `KiviServiceClient` state machine, wire
schemas + snake_case JSON, `PreConnectBuffer` drain-then-flip, press-time capture handoff logic,
all `DictationBudgets` constants, `DictationInsertionPlanner`/`PasteBoundaryPlanner` spacing
rules, `DictationJoinRewritePlanner`, the `AudioLevelMeter` dBFS math, and the whole
gesture-classification layer. Binary frames = `byte[]`/`ReadOnlyMemory<byte>`.

**Deferred / v1 non-goals:** press-time early capture is P1 (M1/M2, not M0-blocking); UIA
screen-context + secure-field gate (M9); Unicode-typing fallback (M1); rich-clipboard restore
(M9).

> **Not applicable — Windows-only.** The reference's Linux capture (PulseAudio/PipeWire),
> Linux paste (XTest/ydotool/uinput/wtype), and Wayland caveats are dropped.
