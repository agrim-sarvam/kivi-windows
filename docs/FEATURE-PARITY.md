# Kivi Electron → .NET/Windows — Feature-Parity Matrix

> **Purpose.** A prioritized, source-cited inventory of *every* meaningful user-facing feature
> and capability of Kivi, mapped to **.NET/Windows port status**, **Windows-only difficulty**,
> milestone (M0–M9 from `MASTER-PLAN.md §5`), and priority. Use it to sequence work and to
> decide what ships for the first internal release.
>
> **Sources.** All 12 maps in `docs/maps/*.md`, `docs/MASTER-PLAN.md`, `docs/PROGRESS.md`, and
> the Electron reference under `_reference/sarvam-kivi-electron/`. Citations are `map:<name>`
> (our ported map) or the reference source path.
>
> **This is the START-OF-PORT snapshot: every port-status is `TODO` except capabilities that are
> pure server/protocol contracts already proven by the wire spike.** The reference's own
> Electron statuses are NOT carried over as ours — they belong to a different codebase. As the
> .NET port progresses, `PROGRESS.md` is the running log and this column is updated.

---

## Executive summary

Kivi is a **background agent + transparent floating overlay**, not a windowed app. Its identity
is one tight loop — *hold a key → speak → formatted text lands in the app you were already
typing in, without Kivi ever stealing focus* — wrapped in a hand-drawn, per-frame-eased "living
orb" and a rich turn surface, backed by a Rust `kivi-service` that does STT + Gemma formatting +
personalization.

**Where we are (Phase 0):** documentation port only. No .NET code has shipped yet. The MVP loop
(`MASTER-PLAN §5 M0`) is the first build target: `WH_KEYBOARD_LL` hotkey → WASAPI capture →
`ClientWebSocket` to `kivi-service` → `SendInput` paste.

**What makes it "feel like Kivi" (the bar for M0–M3):** (1) hold-to-talk hotkey (not a toggle);
(2) focus-preserving paste into the frontmost app; (3) the orb engine + visual clone + turn
surface; (4) exact design tokens + fonts.

**Totals:** **131 features catalogued.**

| Status | Count | Meaning |
|---|---:|---|
| **DONE** | 0 | Shipped + tested in the .NET port |
| **PARTIAL** | 0 | Present but degraded / scaffold-only |
| **TODO** | 119 | On the roadmap, not yet started |
| **DEFERRED** | 6 | Explicit v1 non-goal (`MASTER-PLAN §1`); server/loop degrades gracefully |
| **N-A-PLATFORM** | 6 | macOS-only mechanism with no Windows analog, or dead reference code we deliberately do **not** port |

(All 119 "on the roadmap" rows start as `TODO`; the reference reached DONE on ~12 of these in its
own Electron codebase, but that does not transfer — our count starts fresh.)

By priority: **P0 = 24**, P1 = 34, P2 = 44, P3 = 29.

---

## Legend

- **.NET status:** `TODO` · `PARTIAL` (works but degraded/scaffold) · `DONE` · `DEFERRED` (documented v1 non-goal) · `N-A-PLATFORM` (no Windows analog / dead reference code).
- **Windows difficulty:** `Low` (pure logic or straightforward .NET) · `Med` · `High` (native Win32 interop / deep OS API / native-addon signing).
- **Milestone:** `M0`…`M9` from `MASTER-PLAN §5`. A `/` means it spans milestones (e.g. `M0/M1` = stopgap now, real impl at M1).
- **Priority:** `P0` MVP / parity-critical (the internal-release bar) · `P1` important, soon after · `P2` full-parity · `P3` nice-to-have / dormant.

---

## P0 shortlist — the 10–15 that most define "feels like Kivi"

Close these for a credible internal release: the dictation loop end-to-end **plus** the orb
identity — the two things a teammate judges in the first 30 seconds.

| # | Feature | Status | Why it's the bar | Milestone |
|---|---|---|---|---|
| 1 | **Global hold-to-talk hotkey** (press-and-hold to talk, release to transcribe) | TODO | Push-to-talk *is* the interaction; a toggle feels like a different product | M0 |
| 2 | **Pure-time gesture classifier** (tap/hold/double, 420/450/600 ms) | TODO | The one authority on take intent; ports verbatim | M0/M2 |
| 3 | **Mic capture → 16 kHz Int16 mono LE, 100 ms frames** (WASAPI) | TODO | The audio contract the whole loop rides on | M0 |
| 4 | **Streaming STT wire** (`/v1/dictate/stream`: ack→context→PCM→drain→EOS→final) + budgets + wire-traps | TODO | The reason M0 is "tangible" | M0 |
| 5 | **Server formatting on** (`formatting_enabled:true` → Gemma `formatted_text`) | TODO | Raw STT is not the product; formatted output is | M0 |
| 6 | **Paste into frontmost app** (clipboard + synth Ctrl+V, focus-preserving) + clipboard restore | TODO | Text must land in *their* app, not Kivi | M0/M1 |
| 7 | **Frontmost-app targeting at key-down** (+ last-non-Kivi memo) | TODO | Wrong target = pasted into the void | M0/M1 |
| 8 | **`FlowEngine` state machine** (rest/listening/processing/done, pure injected-time) | TODO | The orb's brain; everything visual derives from it | M2 |
| 9 | **`FlowFrame` + render runtime** (dt-corrected `ease60`, rest-park, `nudge`) | TODO | Correct motion speed on any display; latency contract | M2 |
| 10 | **Orb visual clone** (geometry morph + fill/glow/gloss + **kiwi mark**) | TODO | The face of the brand; the primary user-named gate | M3 |
| 11 | **Transparent non-activating click-through overlay** + per-frame hit-test | TODO | Focus preservation is the behavioral heart | M0/M3 |
| 12 | **Interim/final transcript rendering** in the orb box (segment-by-index, dots) | TODO | Seeing your words appear is the feedback that sells it | M4 |
| 13 | **Design tokens + fonts** (Canon palette light/dark; Matter/Space Grotesk/Season Mix) | TODO | Without exact tokens+fonts the clone silently looks wrong | M0/M3 |
| 14 | **Windows platform seam** (`WH_KEYBOARD_LL` hotkey, `SendInput` paste, frontmost, `WS_EX_NOACTIVATE` overlay) | TODO | The whole native-cost surface | M0/M1 |
| 15 | **App-identity convention** (exe path / AppUserModelID → `app_context.bundle_id`) | TODO | Cross-team unblock for personas + telemetry; agree early | M1/M6 |

---

## Area 1 — Dictation core

The MVP loop and its correctness machinery. Most of the *logic* is pure and ports verbatim; the
*edges* (hotkey, paste, frontmost, secure-field) are the native cost.

| Feature | Reference behavior (cite) | .NET status | Win difficulty | Milestone | Priority | Notes |
|---|---|---|---|---|---|---|
| Global hold-to-talk hotkey | `WH_KEYBOARD_LL` consume addon on a dedicated thread; `fn`/CGEventTap on mac (map:platform-coupling-audit §2) | TODO | High | M0 | P0 | `SetWindowsHookEx(WH_KEYBOARD_LL)` on a dedicated native thread with its own message pump (R5); default **Right-Ctrl hold**, rebindable. No `fn` on Windows. |
| Gesture classification (tap/hold/double/long) | Pure time-injected `GestureClassifier` — hold 420 / doubleTap 450 / longHold 600 ms (map:orb-engine §4) | TODO | Low | M0/M2 | P0 | Port verbatim (pure). "Gesture is the only take authority — VAD never cancels a take." |
| Mic capture → 16 kHz Int16 mono LE PCM, ~100 ms (3200-byte) frames | `getUserMedia`+AudioWorklet (Electron) / AUHAL (mac) sums to mono (map:dictation-audio §3) | TODO | Med | M0 | P0 | **WASAPI** capture → resample to 16 k Int16 mono. Keep resampler-state-continuous (`.noDataNow` rule, R10). |
| Press-time early capture head-start | `PendingTakeCapture` arms mic on key-down, buffers `earlyPrefix` (64 frames ≈ 6.4 s) (map:dictation-audio §4) | TODO | Med | M1/M2 | P1 | Prevents first-word loss under load; reproduce `earlyPrefix` in C#. |
| AEC / NS / AGC | Apple VPIO (mac) / Chromium WebRTC APM (Electron) (map:platform-coupling §10) | TODO | Med | M0 | P1 | WASAPI voice-communication capture category (mic-path AEC where the device supports it). **NOT system-audio parity** (R2). |
| Streaming STT over WS `/v1/dictate/stream` | `KiviServiceClient` one per take (map:service-client-wire §4) | TODO | Med | M0 | P0 | `System.Net.WebSockets.ClientWebSocket` in-process (only a native socket can set upgrade headers + read 401/403). `Kivi.Core/Wire/KiviServiceClient.cs`. |
| Wire budgets (ack 4 s, final 20 s, ping 20 s, pong-miss 2, maxPending 50) | `DictationBudgets` (map:service-client-wire §4.6) | TODO | Low | M0 | P0 | Port to `Kivi.Core/Wire/DictationBudgets.cs`. |
| Pre-connect buffer drain-then-flip | `PreConnectBuffer` accumulates pre-handshake frames, flushes in order (map:dictation-audio §5.1) | TODO | Low | M0 | P0 | TOCTOU-safe; no frame dropped/reordered. |
| Drain-before-EOS ordering | Drain audio queue *then* send `end_of_speech` (server stops reading binary after EOS) (map:service-client-wire §4.2) | TODO | Low | M0 | P0 | Integration test asserts it. |
| Wire-trap guards | Always emit `formatting_enabled` (server default false); allowlist-guard the closed `general_app_style_preset` enum (`verbatim｜casual｜transliteration｜formal`) (map:service-client-wire §4.2 "A3 trap") | TODO | Low | M0 | P0 | Never funnel a `base_preset` slug (`custom`/`free_flowing`) here → PARSE_ERROR/stall. |
| Server formatting (Gemma) | `formatting_enabled:true` → `final.formatted_text` (map:backend-service-api §6) | TODO | Low | M0 | P0 | Context sends `true`; golden-transcript test verifies formatted output. |
| Generation-guarded intake | `takeGeneration++` on every start/cancel/resolve voids stale service events (map:orb-engine §5.3) | TODO | Low | M2 | P1 | Queue drained at frame top, generation-tagged. C# is single-threaded on the render loop → no lock, keep the generation tag. |
| Paste into frontmost app (clipboard + synth Ctrl+V) | write transient clipboard → 30 ms settle → synth paste → restore (map:dictation-audio §8) | TODO | High | M0/M1 | P0 | `SendInput` Ctrl+V; release held modifiers first; terminal → Ctrl+Shift+V; **paste without re-foregrounding** (R6). |
| Unicode keystroke insertion (no clipboard) | `keyboardSetUnicodeString` (mac) / SendInput KEYEVENTF_UNICODE (map:platform-coupling §3) | TODO | Med | M1 | P2 | Clipboard+paste is the primary path; `SendInput` with `KEYEVENTF_UNICODE` is the typed fallback (16-unit chunks). |
| Frontmost-app targeting (capture at key-down; last-non-Kivi memo) | `NSWorkspace` (mac) / `get-windows` (map:platform-coupling §3) | TODO | Med | M0/M1 | P0 | `GetForegroundWindow` + `GetWindowThreadProcessId` + `QueryFullProcessImageName` at key-down; last-non-Kivi memo. |
| Clipboard snapshot/restore around paste | Snapshot all items × types incl. `changeCount`, restore after paste (map:platform-coupling §5) | TODO | Med | M1 | P1 | Windows clipboard has no `changeCount`/transient UTI → in-process "we-just-wrote-this" guard; multi-type restore best-effort (R22). |
| Secure-field gate (no paste/clipboard in password fields) | `IsSecureEventInputEnabled` (mac) → keep text in orb + copy affordance (map:dictation-audio §8.3) | TODO | High | M1 | P1 | Best-effort password-field detect via UIA; no clean single API. |
| Insertion boundary planner (spacing) | Pure leading/trailing-space logic; no double-space, no space before `.,?!` (map:dictation-audio §8) | TODO | Low | M2 | P1 | Pure — port verbatim to `Kivi.Core/Planner`. |
| Join/rewrite continuation | `insertion_replace_before` → delete N chars before caret, re-verify vs snapshot (map:dictation-audio §8) | TODO | Med | M4/M9 | P2 | Needs caret UIA snapshot; degrades to standalone (strip leading punct, re-capitalize) when absent. |
| Newline = literal line break, never synth Return | Type each line, Return only between lines — never submit (map:platform-coupling §3) | TODO | Low | M1 | P1 | Implicit via clipboard paste; guard the typed path. |
| Auth bearer + `X-Client-*` headers on WS/REST | `Authorization: Bearer <15-min JWT>` + `X-Client-Platform/Version/Timezone` (map:service-client-wire §2–3) | TODO | Low | M7 | P1 | Local loopback anonymous only now. **`X-Client-Platform` value is a cross-team decision** (server version-gates on it). |
| Earcons (start/stop/complete tones) | `CueBus` gates: never mid-recording except start/stop; 0.25 s refractory; deferred `.start` (map:orb-engine §6.3) | TODO | Low | M2 | P2 | Lightweight audio player; **drop haptics** (no desktop analog). |
| Idle-timeout handling | `idle_timeout_secs` 180 → server `IDLE_TIMEOUT` → persistent notice (map:backend-service-api §2.4) | TODO | Low | M2 | P2 | Engine presents "idle timeout" notice. |
| Telemetry (completed / latency-trace / thumbs feedback) | `POST v1/telemetry/dictation_completed`, `.../dictation_latency_trace`, `v1/feedback` (map:service-client-wire §5.1) | TODO | Low | M5/M6 | P2 | Fire-and-forget; `paste_target` uses the app-identity convention. |
| Late-final recovery | `GET v1/sessions/{id}/final` after final-timeout (map:service-client-wire §5.3) | TODO | Low | M4 | P2 | Recovers a stranded formatted result. |
| Failed-take retry (retained audio replay) | `TakeAudioStore` (encrypted PCM) + replay re-streams (map:dictation-audio §9) | TODO | Med | M4 | P3 | AES-GCM per-install key (DPAPI-protected); upgrades History row in place. |
| Take ledger (crash journal) | Per-take fsync'd journal recovered on launch (map:dictation-audio §9) | TODO | Low | M9 | P3 | Durability nicety. |
| Link-status resilience (interrupted/lost/restored) | Take keeps recording locally through link blips; banner+hint (map:orb-engine §5.2) | TODO | Med | M4 | P3 | Pairs with keep-raw fallback. |

---

## Area 2 — Orb surface (engine, visuals, maxi mini-app box)

The orb is a **pure function of one `FlowFrame` value type**, eased per-frame (no CSS/XAML
transitions). Two big ports: the engine and the kiwi mark (its own module). Baseline oracle =
the **maxi mini-app** documented in `map:orb-visual`.

| Feature | Reference behavior (cite) | .NET status | Win difficulty | Milestone | Priority | Notes |
|---|---|---|---|---|---|---|
| `FlowEngine` state machine (11 phases) | Pure injected-time `step(to:)→FlowFrame`; `rest/idle/listening/processing/done` + edit/act sub-flows (map:orb-engine §2–3) | TODO | Low | M2 | P0 | Port verbatim to `Kivi.Core/Orb`. |
| `FlowFrame` render contract (~90 fields) | Complete per-frame render output; views are pure functions of it (map:orb-engine §11) | TODO | Low | M2 | P0 | The single value type the whole renderer consumes. |
| Render runtime: dt-correction + fps bands + rest-park + nudge | `ease60(k)=1−pow(1−k,dt/16)`; 24/30/60 tiers; 0-fps park + 1 Hz heartbeat; `nudge()` same-pass render (map:orb-engine §8.5) | TODO | Med | M2 | P0 | Replicate dt-correction or morphs run at wrong speed on non-60 Hz displays; `nudge` is the "did it start?" latency contract. |
| Generation-guarded service intake (frame-top drain) | `enqueueServiceEvent` → `drainServiceEvents()` FIFO, drops stale generation (map:orb-engine §5.3) | TODO | Low | M2 | P0 | Single-threaded render loop → no lock; keep the generation tag. |
| Orb geometry morph (pill⇄orb⇄mini⇄pill-take, hinge-top expand) | `restSize 39×15` → `wakeSize 61×61` → `mini 42.7` → `pill-take 57×18`; hinge-top keeps orb top edge fixed (map:orb-visual §2) | TODO | Med | M3 | P1 | Fed logical px at display scale; the hinge-top expand is the signature move. |
| Orb surface layers (fill+alpha, paper grain, 4-layer glow, sphere gloss, backdrop) | Back-to-front z-stack; paper-grain LCG seed `0x4B49564950415045`; glow eased+quantized (map:orb-visual §3–3a) | TODO | Med | M3 | P1 | **Backdrop desktop-blur is physically unreproducible (R1)** — excluded from pixel gate, faked with a static frosted approximation. Win2D/Composition. |
| Kiwi mark engine (dotted walking bird) | `KiwiData` 120×162 mask, dark/light state color tables, 48×8 gait cache, `SpeechPace` walk (map:orb-engine §10) | TODO | High | M3 | P1 | **Biggest single visual port** — its own `Kivi.Core.KiwiMark` module + Win2D draw, with a coverage-readback dot-count numeric gate (R25). |
| State-color table (glow / pill-face / eyes-in-pill) | Per-state RGB (listening orange, processing blue, speaking green, done green, error red…) (map:orb-visual §3c) | TODO | Low | M3 | P1 | Drives glow, pill face, tray tint. Port the exact table. |
| Breath animation (2.6 s brand breath) | `b = 0.5+0.5·sin(now·2π/2.6s)`; drives glow swell, eye scale (map:orb-visual §3b) | TODO | Low | M2 | P1 | Quantized to 12 steps for glow-blur reuse. |
| Pill-face (mic bars ⇄ eyes) | 7 mic bars while listening, morph to glowing eyes while processing (map:orb-visual §3.6) | TODO | Low | M3 | P2 | Colored by live state. |
| Rest eyes (asleep/awake) | Two capsules, `eyeOpen` eases 0→1; shut = asleep, open = breathing dots (map:orb-visual §3.5) | TODO | Low | M3 | P2 | |
| Maxi mini-app box (envelope swap + plateau curve) | Envelope `.base 1480×720 ⇄ .maxi 1880×1760`; plateau `840×800`, half-W/¾-H at 14″ ref, slope 0.18 past it (map:orb-visual §1,§7) | TODO | Med | M4 | P1 | Resize the layered window between envelopes keeping the orb point; feed **logical px** (HiDPI). |
| Orb-box wedge popover + hinge-top reveal | `WedgeBoxShape` (radius 8, centered wedge W20 H9); box unfurls downward via height mask (map:orb-visual §6,§7) | TODO | Med | M4 | P1 | Box present full-width from frame 1, revealed by vertical mask (no slide). |
| Box status header (app chip, name, state narration) | `BoxHeaderRow`: app icon + name + "listening …/transcribing …/editing …" narration, expand/restore control (map:orb-visual §8a) | TODO | Low | M4 | P1 | Errors render red-tinted; mic-escalation copy ("are you there?"). |
| Interim/final transcript rendering | Segment-by-index accumulate; `is_final:false` renders nothing; dots at 600 ms; chunk fade 240 ms; prior→dim (map:orb-engine §9.2, map:backend §2.3) | TODO | Med | M4 | P1 | One interim per VAD utterance — append/replace by `segment_idx`. |
| Copy chip / copy affordance | `CopyChip` top-right of crisp card; ✓ flash on copy (map:orb-visual §8c) | TODO | Low | M4 | P1 | Also the manual-copy path when there's no paste target. |
| Thumbs 👍👎 rating | `takeRatable` footer thumbs → `POST v1/feedback` (map:orb-visual §8d) | TODO | Low | M4 | P2 | |
| Scroll behavior ("glitch fixed 4 ways") | Hysteresis (near 6 px / leave 18 px), dual progressive fades, momentum latch, follow-yield (map:orb-visual §8e) | TODO | Med | M4 | P2 | |
| Satellites / companions (hey-kivi, settings/cancel/copy, expand) | Cross layout around orb; hey-kivi wears host app icon; settings↔cancel↔copy tri-mode; expand reveal-on-hover (map:orb-visual §4) | TODO | Med | M4 | P2 | Geometric-hover driven, hit radius 1.5× visible. |
| Context / reference card (hey-kivi callout) | "◨ kind" + preview + "More…" (map:orb-visual §8b) | TODO | Low | M4 | P3 | |
| Pager dots + review frames (raw/diff/final) | Back/forward walks frames 0 raw → 1 diff → 2 clean final; pager dots capped 10 (map:orb-engine §9.3) | TODO | Med | M4 | P2 | |
| Wave sweep (processing/edit shimmer) | 46%-wide gradient band riding only the glyphs, period 2.6/2.4 s (map:orb-visual §9) | TODO | Low | M4 | P2 | |
| Idle rotating hint placeholder | One-line hints rotate every 3500 ms w/ crossfade (map:orb-visual §8c) | TODO | Low | M4 | P3 | |
| Hover model (single geometric classifier + hysteresis) | `FlowFrame.interactiveTarget(x,y)` topmost-first; 2px enter/10px leave (map:orb-engine §7) | TODO | Med | M3 | P1 | Drives **both** click-through and tooltips — keep unified. |
| Transparent non-activating click-through overlay | `NSPanel` (mac) / transparent `BrowserWindow` (Electron), always-on-top, click-through (map:platform-coupling §8) | TODO | High | M0/M3 | P0 | Native layered window, `WS_EX_NOACTIVATE` + always-on-top + click-through toggle (R20). Display-only through M3. |
| Per-frame click-through hit-test toggle | `syncCursorState` polls cursor, flips ignore-mouse each frame (map:electron-packaging §2) | TODO | High | M3 | P1 | Publish interactive-rect on geometry change; poll `GetCursorPos` against it. |
| Drag handle (movable orb) | 2×3 dot grid, open/closed-hand cursor (map:orb-visual §4) | TODO | Med | M4 | P3 | |
| Toast / hint / tip pills (narration) | `HintPill`/`Hint2Pill`/`ToastView`, gated on tooltips setting (map:orb-visual §4) | TODO | Low | M4 | P3 | ONE-narrator pill gate. |
| Reduce-motion / reduce-transparency | `reduceMotion` snaps eases; `reduceTransparency` zeroes paper grain (map:orb-visual §CROSS 8) | TODO | Low | M3 | P2 | Map to Windows animation + transparency settings (`SystemParametersInfo` / `UISettings.AnimationsEnabled`). |

---

## Area 3 — Edit mode

Voice-edit and preset-edit over `POST /v1/edit`. v1 uses **select-all + paste-whole-field**;
UI-Automation range replace is the deepest native dependency and is deferred.

| Feature | Reference behavior (cite) | .NET status | Win difficulty | Milestone | Priority | Notes |
|---|---|---|---|---|---|---|
| Voice-edit flow (double-tap → editListen → editProcess → editDone) | Edit sub-flow in `FlowEngine`; `startVoiceEdit()`, spoken-instruction flush 800 ms (map:orb-engine §3.2) | TODO | Med | M4 | P2 | Runs over the dictation WS (`session_purpose:"voice_edit"` → `edit_final`) or `POST /v1/edit`. |
| Preset edit (pick an edit preset pill) | Edit pane preset pills dispatch immediately (map:orb-engine §3.2) | TODO | Low | M4 | P3 | |
| `POST /v1/edit` wire | JSON in / **camelCase** out; **read `text`** not `edited` (map:backend §4, map:service-client-wire §5.2) | TODO | Low | M4 | P2 | Note the snake_case↔camelCase asymmetry — replicate exactly. |
| Edit apply = select-all + paste-whole-field | Ctrl+A + paste (map:platform-coupling §4) | TODO | Med | M4 | P2 | The v1 edit-delivery path. |
| UIA range-level replace (set selected range) | AX `kAXSelectedTextRangeAttribute` (mac) (map:platform-coupling §4) | DEFERRED | High | M9 | P3 | Windows UI Automation `TextPattern`/`ValuePattern`. **Documented parity gap** — select-all corrupts multi-field/partial-selection edits. |
| Diff morph (red→green token animation) | `DiffMorph` 3 beats (compressed 150+100+250 ms in orb); del strikethrough+collapse, ins grow+underline (map:orb-engine §9.4, map:orb-visual §9) | TODO | Med | M4 | P2 | Client computes word-level LCS; skipped under reduce-motion. |
| Edit snapshot restore on cancel/fail | Byte-exact `tx.restorePrev()` (map:orb-engine §3.2) | TODO | Low | M4 | P2 | |
| In-box editing (typed text + scoped activation gesture) | Box editable when idle/typed/pasted (map:orb-engine §9.5) | TODO | Med | M4 | P2 | **Conflicts with non-activating overlay (R13)** — briefly make the window activatable on an explicit gesture; no foreground-restore. |

---

## Area 4 — Main window shell + pages

Custom-drawn UI → XAML. Build against the **Canon** palette. `StylesPage`/`PresetsPage` are
**dead reference code — do not port** (routing sends `.styles`/`.presets` → `PersonasPage`).

| Feature | Reference behavior (cite) | .NET status | Win difficulty | Milestone | Priority | Notes |
|---|---|---|---|---|---|---|
| Frameless main window (custom title bar, 1180×760, min 980×640) | Single window, transparent titlebar (map:main-window §0) | TODO | Low | M5 | P2 | Custom title-bar drag region + own window controls (WinUI `AppWindowTitleBar`). |
| Rail navigation (264⇄76 collapse, Ctrl+\, taxonomy) | Custom rail, fold curve 0.24 s, `RailTaxonomy` groups (map:main-window §1,§3) | TODO | Low | M5 | P2 | Persist collapse state. |
| Hand-drawn SVG rail icons + glyphs | 24×24 2px monoline paths (`RailIcon.path()`, `HistoryGlyph`, `KiviInkArrow`, `PixelKiwi`; map:main-window §CROSS 4) | TODO | Low | M5 | P2 | Port each path → XAML `PathGeometry` verbatim; substitute the few native symbols. |
| Canon canvas + PaperGrain + ConstellationField background | Behind every page (map:main-window §2.7) | TODO | Low | M5 | P2 | Noise tile + radial-gradient dot grid. |
| Design token system (Canon + legacy, light/dark) | `KDS.Canon` primary; legacy `KDS.Theme` for Clipboard/Analytics (map:main-window §2, design-tokens map) | TODO | Low | M0/M3 | P1 | **Encode Canon-over-KDS dark override + two-cream split (R15)**; XAML theme dictionaries; validate via ported token-parity tests. |
| Fonts (Matter, Matter SemiMono, Space Grotesk, Season Mix) | Registered pre-render by PostScript name (map:main-window §2.5) | TODO | Low-Med | M3 | P1 | **Font license go/no-go is a cross-team gate (R12)** — Season Mix load-bearing; define metrics-compatible fallback now. Space Grotesk (OFL) shippable. |
| Record page (landing greeting + workspace sticker + bird rail) | Split layout, 52pt greeting, embedded transcript box, bird panel (map:main-window §5.1) | TODO | Med | M5 | P2 | Isolate the streaming leaf (own view-model) so 60 fps text doesn't re-layout the page. |
| History page (search + AI-ask + filters + inspector) | `HistoryPage` over local store + server ask; day groups; 430pt inspector (map:main-window §5.2) | TODO | Med | M5 | P2 | Ctrl+↵ ask; Esc cascade. |
| Local history store (SwiftData/`better-sqlite3` replacement) | Captures, tenant/user scoped (map:main-window §CROSS 10) | TODO | Med | M5 | P2 | **SQLite** (`Microsoft.Data.Sqlite`), same scoping. |
| Settings shell (two-pane, searchable, 8 panes, per-pane reset) | `SettingsShell` own 224pt rail + detail (map:main-window §5.10) | TODO | Med | M5/M7 | P2 | Panes: general / orb / system / plan / invite / org / account / advanced. |
| Hotkey capture field (rebind global hotkey) | `HotkeyCaptureField` records the chord (map:electron-packaging §0) | TODO | Med | M5 | P1 | Needed from day one so the new default is rebindable (R8). |
| System-permissions status panel (mic) | Reads status, refresh on activate (map:menubar-auth §2.2) | TODO | Med | M7 | P1 | Windows: no Accessibility trust gate; detect mic via capture failure, deep-link `ms-settings:privacy-microphone`. |
| Clipboard page (opted-in clipboard history) | `ClipboardPage` (legacy theme), filter chips, click-to-copy (map:main-window §5.3) | TODO | Med | M5 | P3 | No `changeCount`/transient UTI → in-process own-write guard (map:platform-coupling §5). |
| Analytics page (charts + scorecard + ranges) | `AnalyticsPage` uses Swift Charts, unlock gate ≥5 captures (map:main-window §5.7) | TODO | Med | M5 | P3 | **Swift Charts is Apple-only** → hand-drawn XAML `PathGeometry` / charting lib; `en-IN` grouping via `NumberFormatInfo`. |
| Leaderboard page (podium + race list + confetti) | `LeaderboardPage` REST, gold-border champion, pixel-bird (map:main-window §5.9) | TODO | Med | M5 | P3 | |
| Shared-terms page (coming-soon placeholder) | `SharedTermsPage` centered empty state (map:main-window §5.8) | TODO | Low | M5 | P3 | |
| Deep-links / one-shot payloads | `AppNavigation` park→consume-once (map:main-window §3) | TODO | Low | M5 | P3 | From tray / orb / rows. |
| Banners (connectivity / persistence / invitation) | Conditional strips above the page (map:main-window §1) | TODO | Low | M5 | P3 | |
| Org switcher + account footer (usage ring) | `RailFooter` avatar + usage ring (amber >90%) + org popover (map:main-window §3.2) | TODO | Med | M7 | P3 | Needs usage endpoint + auth. |
| Indian-locale number grouping | `IndianNumber.grouped` (1,00,000) (map:main-window §2.5) | TODO | Low | M5 | P3 | `NumberFormatInfo` for `en-IN` (custom digit grouping). |
| Legacy `StylesPage`/`PresetsPage` | never routed (map:main-window §5, personalization §0) | N-A-PLATFORM | — | — | — | **Do not port** — pre-convergence dead code; port `Personas/*` instead. |

---

## Area 5 — Personalization (personas / styles / presets)

One page (`PersonasPage`). Domain model + resolver + all copy strings port **verbatim**
(content-parity contract); the native cost is app-icon/running-app discovery and the app-identity
convention.

| Feature | Reference behavior (cite) | .NET status | Win difficulty | Milestone | Priority | Notes |
|---|---|---|---|---|---|---|
| Personas overview (your apps / your styles / code-switching) | `PersonasOverview` ranked apps, voice cards, romanize toggle (map:personalization §3.2) | TODO | Med | M6 | P2 | Content column 980, Canon palette. |
| Detail pane (writing style + custom rules + apps) | 700pt right pane (map:personalization §3.3) | TODO | Med | M6 | P2 | |
| 3-card cosmetic style picker (standard/formal/custom + live preview) | `PersonasConfiguration` with mini-app preview surfaces (map:personalization §3.3) | TODO | Med | M6 | P2 | **Content-parity contract** — reproduce `PersonasStyleCatalog` strings verbatim. |
| Custom rules (note → compiled bullets) | `PersonasVoiceNotes` → `POST v1/compile-custom-instructions` (`instruction_kind:"cosmetic_style"`) (map:personalization §3.3, §5) | TODO | Low | M6 | P2 | Persists raw note as freeform if server returns 0 bullets. |
| App-scope overrides (per-app voice) | `saveAppOverride` coalescing loop; apply-to-persona offer (map:personalization §2) | TODO | Med | M6 | P2 | Keyed by app id → **needs app-identity convention**. |
| Style resolver (preset floor → global → scope; romanize) | Pure `StyleResolver` folds overrides (map:personalization §4) | TODO | Low | M6 | P2 | Pure — port verbatim; 1–4 level knobs. |
| Format-preferences REST (GET/PUT/DELETE) | `v1/format-preferences?slug=` (map:personalization §5) | TODO | Low | M6 | P2 | 409 → `StyleConflict`/`PresetControlConflicts`. |
| Cosmetic-styles + app-overrides + apply-to-persona REST | `v1/persona-cosmetic-styles`, `v1/persona-app-style-overrides` (+ `/apply-to-persona`) (map:personalization §5) | TODO | Low | M6 | P2 | |
| Local `StyleCatalog` cache (cache-first paint + hot-path context) | Mirrors server prefs; feeds `user_app_assignments`/`client_format_prefs` on the WS context (map:personalization §5) | TODO | Med | M6 | P1 | Reuse **exact key names** (`kiviStyles.appAssignments`…); JSON store under `%APPDATA%\Kivi`. |
| Format preview (debounced 500 ms) | `PreviewViewModel` bundled instant → server truth crossfade (`v1/format-preview`) (map:personalization §4) | TODO | Low | M6 | P3 | |
| Create-voice sheet | picker uses `appsAssignedOutsideSelectedScope` (map:personalization §3.4) | TODO | Med | M6 | P3 | The **fixed rule** (excludes global+selected scope so dictated-in apps stay pickable). |
| Manage-voice sheet (rename/delete) | custom-only rename/delete (map:personalization §3.5) | TODO | Low | M6 | P3 | |
| Marketplace / recipes (transform presets) | over `v1/preset-marketplace`, `v1/transform-presets` (map:personalization §3.6–3.7) | TODO | Med | M6 | P3 | Mostly wired-but-dormant. |
| Code-switching romanize toggle (global) | `globalRomanize` mirrored across all scopes (map:personalization §1) | TODO | Low | M6 | P3 | Global runtime state, not per-card. |
| App icons + running-app discovery | `/Applications` walk (mac) / `NSWorkspace` (map:personalization §CROSS 1) | TODO | High | M6 | P2 | **Biggest native dep here** — Windows app identity (exe path / AppUserModelID) + icon extraction (`SHGetFileInfo` / PE resources); re-map `PersonaSeedRegistry` bundle-IDs. |

---

## Area 6 — Memory / spoken shortcuts / snippets

Pure REST surfaces against the same service — port directly; only the native app-discovery bits
differ.

| Feature | Reference behavior (cite) | .NET status | Win difficulty | Milestone | Priority | Notes |
|---|---|---|---|---|---|---|
| Dictionary / Memory (terms kivi never misspells) | teach/edit/forget over REST (`v1/memory-forest`) (map:main-window §5.4) | TODO | Low | M5 | P3 | Inline 2-step forget; progressive show-more. |
| Spoken shortcuts (trigger → replacement) | over REST (`v1/spoken-shortcuts`) (map:main-window §5.5) | TODO | Low | M5 | P3 | Literal static map. |
| `spoken_shortcuts_v1` capability advertised | `client_capabilities:{spoken_shortcuts_v1:true}` in context (map:service-client-wire §4.2) | TODO | Low | M0 | P2 | Emitted by `buildContext` from M0. |
| Data import (terms/shortcuts) | `DataImportPanel` → `POST v1/data-imports` (map:main-window §5.4) | TODO | Low | M5 | P3 | Trailing slide-in overlay. |
| Memory-brain / account-memory bootstrap | `v1/account-memory/bootstrap`, style-suggestions (map:service-client-wire §5.5) | DEFERRED | Low | M9 | P3 | Dormant in UI; per-event cost/battery bleed. |

---

## Area 7 — Auth + onboarding + tray / menu-bar

Auth logic is pure HTTP (ports cleanly); the native cost is the OAuth callback delivery, secret
storage, permission preflight, and the tray.

| Feature | Reference behavior (cite) | .NET status | Win difficulty | Milestone | Priority | Notes |
|---|---|---|---|---|---|---|
| Auth-gate routing (splash/signIn/onboarding/shell) | Pure `authGateDestination`; `auth==nil` → shell (map:menubar-auth §3.1) | TODO | Low | M7 | P2 | Anonymous local dev renders shell directly. |
| OAuth callback (Kratos `?code=` + Supabase `#fragment`) | Default-browser hop + `kivi://` (mac) / `second-instance` argv (Electron) (map:menubar-auth §3.4) | TODO | Med | M7 | P1 | **Loopback `http://127.0.0.1:<port>/callback`** (`HttpListener`) — handles both uniformly. |
| Org-JWT mint (X-Session-Token → 15-min JWT) | `POST auth.sarvam.ai/api/v2/auth/jwt`, on-demand re-mint (map:menubar-auth §3.5) | TODO | Low | M7 | P1 | Single-flight; re-mint at <60 s validity. Pure HTTP. |
| whoami arbiter (401-only kills session) | `GET sessions/whoami`; 5xx/403 = degraded-but-signed-in (map:menubar-auth §3.5) | TODO | Low | M7 | P1 | Honest expiry caption. |
| Token storage | Keychain (mac) / `safeStorage` (Electron) (map:menubar-auth §3.6) | TODO | Low | M7 | P1 | **DPAPI** (`ProtectedData`) or Credential Manager; per-install AES key for retained audio. |
| Sign-in screen (email OTP / password / Google / linking) | state-driven card set (map:menubar-auth §3.2) | TODO | Med | M7 | P2 | Password rules ≥8/≥1 digit/mixed case. |
| Voice-feature gate (authed && permissions && tenant) | `canUseVoiceFeatures` (map:menubar-auth §3.7) | TODO | Low | M7 | P2 | "sign in to dictate →" fallbacks call this. |
| Onboarding flow (permissions → tour → personalization → handoff) | 4-phase; suppresses resident Kivi during (map:menubar-auth §2) | TODO | Med | M7 | P3 | Tour + personalization are pure-UI ports. |
| Guided orb tour (scrubbable timeline) | video-style, 3 chapters, unit-tested `TourTimeline` (map:menubar-auth §2.3) | TODO | Low | M7 | P3 | Pure UI. |
| Permissions preflight + self-heal | polls mic, deep-links to settings (map:menubar-auth §2.2) | TODO | Med | M7 | P1 | **Simpler on Windows** — mic only (no Accessibility trust gate); mirror OpenWhispr's failure surface (`onAccessibilityMissing` has no Windows analog). |
| Tray / menu-bar (live state-tinted breathing pill) | `NSStatusItem` + `NSPopover`, 20 Hz breathing (map:menubar-auth §1) | TODO | Med | M7 | P2 | Notification-area icon + frameless popover window; pre-render discrete state frames. |
| Menu-bar popover content (dictate/stop, history, settings) | 320-wide dropdown + embedded transcript box (map:menubar-auth §1.6) | TODO | Med | M7 | P3 | Own runtime, keep-in-box (never pastes elsewhere). |
| Resident-agent mode (no taskbar icon, stays alive) | `.accessory` policy (mac) (map:menubar-auth §0) | TODO | Low | M7 | P2 | Tool-window orb (no taskbar button); keep process alive with windows hidden. |
| URL-scheme / deep-link handling | `kivi://` (map:menubar-auth §4) | TODO | Med | M7 | P2 | Register `kivi` protocol (registry) + single-instance argv; loopback callback preferred. |
| Launch-at-login | `SMAppService` (mac) (map:platform-coupling §14) | TODO | Low | M8 | P3 | Registry `Run` key / Startup shortcut. |

---

## Area 8 — Windows system integration

The native-interop and packaging tier. This is where "one solution, seams behind interfaces"
(`MASTER-PLAN §4`) earns its keep.

| Feature | Reference behavior (cite) | .NET status | Win difficulty | Milestone | Priority | Notes |
|---|---|---|---|---|---|---|
| `Kivi.Platform` seam | — (new) | TODO | Low | M0 | P0 | Interfaces in `Kivi.Core/Contracts`; Windows impls in `Kivi.Platform`. |
| Windows platform seam | — | TODO | High | M0/M1 | P0 | Hotkey `WH_KEYBOARD_LL` **on a dedicated native thread** (R5), SendInput Ctrl+V (+ release held modifiers, terminal→Ctrl+Shift+V), `GetForegroundWindow` frontmost, `WS_EX_NOACTIVATE` overlay. |
| Native-interop signing pipeline | — | TODO | High | M1/M8 | P1 | **EV-sign the hook/inject binaries early** for SmartScreen reputation (keylogger AV signature, R11). |
| App-identity convention (exe path / AppUserModelID) | macOS bundle-id drives `app_context.bundle_id`, personas, telemetry `paste_target` (map:platform-coupling §12) | TODO | Med | M1/M6 | P1 | **Cross-team dependency** — agree the scheme with backend in M1, lands with personas in M6 (R23). Let it be `null` where unresolvable. |
| Secrets store | Keychain (mac) / `safeStorage` (Electron) (map:platform-coupling §7) | TODO | Low | M7 | P1 | **DPAPI** (`ProtectedData`) or Windows Credential Manager. |
| Packaging (MSIX / MSI) | Sparkle + hardened entitlements (mac) (map:electron-packaging §5) | TODO | High | M8 | P2 | .NET publish → MSIX or WiX/MSI; Win EV-signed. |
| Auto-update | Sparkle / electron-updater (map:platform-coupling §16) | TODO | Med | M8 | P3 | MSIX auto-update or a Squirrel/Velopack-style feed (see `RELEASE.md`). |
| CI matrix (Windows runners) | — | TODO | Med | M1/M8 | P2 | build + xunit + golden-frame + UI e2e + visual-diff + OS-integration harness per PR; nightly real-STT. |
| Screen / AX context enrichment (`screen_nodes`, `focused_field`, `cursor_context`) | System-wide AX tree (mac) (map:platform-coupling §6) | DEFERRED | High | M9 | P3 | All optional on the wire — server degrades. Later: Windows UI Automation; preserve secure-field redaction. |
| Overlay transparency | (mac always composits) | TODO | Med | M3 | P2 | Native layered window via `UpdateLayeredWindow` / DirectComposition; DWM composition is always on for modern Windows. |

> **Removed rows (not applicable — Windows-only):** the Electron matrix's "Linux X11 platform
> shell", "Wayland decision (portal toggle / uinput setup)", "Suppress OS fn behavior", and the
> Linux-tray/`.desktop` autostart lines. There is no Linux target and no `fn` key. The mac-only
> `IsSecureEventInputEnabled`/`AppleFnUsageType` mechanisms map to the notes above or are dropped.

---

## Open cross-team dependencies

These block or de-risk multiple features and need an owner + a decision date.

1. **App-identity convention** (`P1`, M1→M6). Agree a Windows app key (exe path / AppUserModelID) with the **backend** — it drives `app_context.bundle_id`, telemetry `paste_target`, per-app personas. The `PersonaSeedRegistry` bundle-ID table must be re-mapped to Windows identifiers. Raise in M1, land in M6. (R23)
2. **`X-Client-Platform` header value** (`P1`, M7). The server **version-gates features** on this string (currently hard-coded `"macos"` in the reference). Sending `"windows"` may unlock/lock different gated behavior. Confirm with backend which strings it recognizes; if in doubt, mirror `"macos"` to inherit identical behavior. (map:service-client-wire §CROSS 2)
3. **Font licensing** (`P1`, M0 Track B → M3). Legal go/no-go on **Matter + Season Mix** redistribution in a shipped .NET installer. Season Mix (the marque serif) is load-bearing for the wordmark + every page title; a "no" degrades the parity claim **only if** a metrics-compatible fallback + documented font-region tolerance is defined now. Space Grotesk (OFL) is shippable. (R12)
4. **Windows EV code-signing** (`P1`, M1/M8). EV cert for the Windows hook/inject binaries must be procured **early** to accrue SmartScreen reputation (they trip keylogger AV heuristics). (R11)
5. **Baseline visual oracle = the running Electron app** (`P0`, M0 Track B → M3). Pin a reference commit, capture the full named-state × forest/mist × light/dark baseline set from the Electron app. Without this there is nothing to pixel-diff against. (R4)
6. **Local backend prerequisite** (`P0`, ongoing). `kivi-service` needs **Postgres 16** or it `exit(78)`; `LOAD_TEST_MODE=synthetic` bypasses Sarvam/Gemini **but not Postgres**. Keep it green for the parity harness. (R21)

---

## Known parity gaps we are accepting for v1

Pulled from `MASTER-PLAN §1` non-goals and the risk register. These are **documented, not
silently dropped** — each degrades gracefully.

| Gap | Why accepted | Fallback / degradation | Tracked at |
|---|---|---|---|
| **UI-Automation range-level edit** | No clean single API; deepest native dependency | v1 uses select-all + paste-whole-field (corrupts multi-field / partial-selection edits — documented) | R-edit, M9 |
| **System-audio echo cancellation** | WASAPI voice-comm processing only cleans the mic path, not other apps' output | Adequate for dictate-into-text (no concurrent playback assumed); system-audio AEC (WASAPI-loopback → APM) descoped | R2, M9 |
| **Screen-context enrichment** (`screen_nodes` / `cursor_context` / `focused_field`) | Heavy; optional on the wire | Server degrades to plain dictation (all fields optional); join-rewrite falls back to standalone text | R-screenctx, M9 |
| **Desktop-behind-window blur** (orb's glass) | Cannot blur the desktop behind a layered window | Excluded from the pixel gate; faked with a static frosted gradient / blurred-screenshot approximation | R1, M3 |
| **Rich-format clipboard paste** (custom types) | Rich custom clipboard types are painful to write | Degrades to plain text (fine for MVP); needs a native clipboard helper later | map:platform-coupling §5, M9 |
| **Haptics** | No desktop analog | Dropped entirely; earcons retained | map:orb-engine §CROSS 5 |
| **Memory-brain per-event jobs** | Cost/battery bleed; dormant in UI | Deferred to M9; not surfaced | M9 |
| **Dead reference pages** (`StylesPage`/`PresetsPage`) | Superseded by Personas convergence | Not ported; `.styles`/`.presets` route → `PersonasPage` | map:personalization §0 |

---

*Generated in Phase 0 from `docs/maps/*` + `MASTER-PLAN.md`, sourced from the Electron reference
under `_reference/sarvam-kivi-electron/`. **Value-conflict resolutions carried forward from the
reference (prefer the shipped-client value):** (a) the **hotkey wake lerp** is `0.30` in the
current engine (orb-engine/orb-visual maps) vs `0.20` in the platform-coupling quick-reference —
trust `0.30`; (b) `transcription_mode` default is **`codemix`** in the shipped client
(service-client-wire map) vs `transcribe` in the backend-service map's MVP example — use
`codemix`; (c) the maxi mini-app orb (the visual baseline) is the design documented in
`map:orb-visual`, not any earlier prototype.*
