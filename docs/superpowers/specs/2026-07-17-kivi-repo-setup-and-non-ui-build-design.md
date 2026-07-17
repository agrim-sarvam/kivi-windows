# Kivi for Windows — Repo Setup & Non-UI Build (Plan 1) — Design Spec

**Date:** 2026-07-17
**Status:** Approved (design sections approved by user; pending written-spec review)
**Scope:** Repository/toolchain setup + the entire **non-UI** application — full `Kivi.Core`
engine and a **fully hardened** `Kivi.Platform` (POA #1, #2, #3) — driven by a headless console
host and verified by tests. **No UI** (deferred until the Kivi design link arrives).

**Companion docs:** [`../../overview.md`](../../overview.md) ·
[`../../system-design.md`](../../system-design.md) ·
[`../../freeflow-research.md`](../../freeflow-research.md) ·
[`../../impl-01-screen-context-uia.md`](../../impl-01-screen-context-uia.md) ·
[`../../impl-02-audio-driver-resilience.md`](../../impl-02-audio-driver-resilience.md) ·
[`../../impl-03-winui3-kivi-ui.md`](../../impl-03-winui3-kivi-ui.md)

---

## 1. Goal & decisions

Build everything in the app **except the UI**, done properly to the implementation docs, so that
when the Kivi visual design arrives the only remaining work is the WinUI 3 skin on top. Prove it
works with a real end-to-end dictation (real Groq, real Windows APIs) plus automated tests.

**Decisions locked during brainstorming:**

| Decision | Choice |
|---|---|
| FreeFlow repos | Clone into git-ignored sibling `_reference/`; **read-only reference, never a dependency** |
| Plan 1 scope | **A+C combined, one plan** — full `Kivi.Core` + fully hardened `Kivi.Platform`; no UI |
| UI | **Not in this plan** — wait for the Claude/Kivi design link |
| Dev API key | `GROQ_API_KEY` env var or `dotnet user-secrets`; real DPAPI store present but secondary |
| Driver + observation | **Console host** (manual E2E) **+ tests** (fake-HTTP unit + canned-WAV integration) |
| Backend | Groq (OpenAI-compatible) behind `ISttEngine` / `IPolishClient` seams |
| SDK | .NET 8 (confirmed installed: 8.0.422) |

**Non-goals (explicitly deferred):** overlay pill, tray, settings window (POA #4 — design link);
perf pass to <100 MB (POA #5); WiX installer (POA #6).

---

## 2. Repository & toolchain setup

- `git init` in the existing `Kivi/` folder (preserves the `docs/` already written).
- `.gitignore`: standard .NET (`bin/`, `obj/`, `*.user`, publish output), user-secrets, and a rule
  ignoring **`_reference/`**.
- Clone FreeFlow references into `_reference/` (git-ignored, never committed):
  - `zachlatta-freeflow/` — exact prompts, model config, `no_speech_prob` hallucination filter
  - `freeflow-windows/` — Windows hotkey/audio/paste reference patterns
  - `mrinalwadhwa-freeflow/` — `PolishPipeline` regex tables
- Solution + projects created via `dotnet new`. **WinUI 3 / Windows App SDK workload not required**
  for this plan (no UI).
- Dev secret: `GROQ_API_KEY` via environment variable or `dotnet user-secrets` on `Kivi.App`.
  Never committed, never hardcoded.

---

## 3. Solution structure

```
Kivi.sln
├── Kivi.Core/         net8.0            — portable engine, NO OS/UI deps
├── Kivi.Platform/     net8.0-windows    — OS glue (hotkey, mic, paste, UIA, DPAPI)
├── Kivi.App/          net8.0-windows    — console host (WinUI 3 shell added in a later plan)
└── Kivi.Core.Tests/   net8.0            — xUnit; fake HttpMessageHandler + canned-WAV integration
```

Dependency direction (enforced): `Kivi.Core` depends on nothing in the other projects;
`Kivi.Platform` implements `Kivi.Core.Abstractions.*`; `Kivi.App` is the composition root and wires
everything via DI. Only `Kivi.Platform` and `Kivi.App` target `-windows`. Layout matches
[`system-design.md` §3](../../system-design.md).

---

## 4. What gets built (all non-UI, to impl-doc spec)

### 4.1 `Kivi.Core` — the portable engine (POA #1)
- `Http/OpenAiCompatibleClient` — `HttpClient` wrapper for `/audio/transcriptions` (multipart) and
  `/chat/completions` (JSON), dual independently-configurable base URLs + keys.
- `Stt/ISttEngine` + `GroqSttEngine` — STT call; **`no_speech_prob >= 0.1` hallucination filter**.
- `Polish/IPolishClient` + `GroqPolishClient` — cleanup call; **model fallback on 429/empty,
  rate-limit cooldown circuit breaker, prompt-injection guard**.
- `Polish/PolishPipeline` — deterministic regex cleanup ported from the mrinalwadhwa fork
  (punctuation substitution, filler stripping, `<keep>` tags, `sanitizeContextField`,
  `normalizeFormatting`). Optional `isClean` LLM-skip gate (implement the documented 4 conditions).
- `Prompts/Prompts` — ported FreeFlow system + user-message prompt constants (cleanup, command,
  translate, context) — copied verbatim from `_reference/`.
- `Macros/VoiceMacro` + `Vocabulary` — exact-match macro bypass, "press enter" parsing, custom-vocab
  merge appended to the cleanup system prompt.
- `Orchestration/DictationOrchestrator` (+ `IDictationOrchestrator`, `RecordingState`) — sequences
  the pipeline, owns `RecordingState` (Idle/Listening/Transcribing/Pasting/Error) under a lock.
- `Abstractions/` — `IAudioCaptureService`, `IHotkeyService`, `IPasteService`,
  `IScreenContextProvider`, `ISecretStore`.
- `Config/AppConfig` — base URLs, model IDs, hotkey, mic, timeouts, vocab, macros.

### 4.2 `Kivi.Platform` — OS glue, fully hardened (POA #1/#2/#3)
- `Hotkey/LowLevelKeyboardHookService` — `SetWindowsHookEx(WH_KEYBOARD_LL)`, **hardcoded right-Ctrl
  hold** for this plan, non-suppressing (`CallNextHookEx`), left/right-Ctrl distinction.
- `Audio/WasapiAudioCaptureService` + `Audio/DeviceNotificationClient` — NAudio `WasapiCapture`
  at `WaveFormat(16000,16,1)` → WAV `MemoryStream`; **`IMMNotificationClient` resilience**
  (OnDefaultDeviceChanged/OnDeviceStateChanged/OnDeviceRemoved → reinit on worker via `Channel`),
  **retry/backoff on mic init**, **re-enumerate at session start**. Per [`impl-02`](../../impl-02-audio-driver-resilience.md).
- `Context/UiaScreenContextProvider` — UIA3 focused element + `TextPattern`/`ValuePattern`, Win32
  identity fallback, **password/secure-field skip** (`UIA_IsPasswordPropertyId`), ≤500-char context
  string, STA + timeout-guarded, snapshot at hold-start. Per [`impl-01`](../../impl-01-screen-context-uia.md).
- `Paste/SendInputPasteService` — clipboard + `SendInput` Ctrl+V with **all four safeguards**
  (wait for modifier release, verify+rewrite clipboard, 400 ms delay before restore, optional Enter).
- `Secrets/DpapiSecretStore` — `ProtectedData` (DPAPI) implementation of `ISecretStore`, present but
  secondary to env/user-secrets for dev.
- `Interop/NativeMethods.txt` — CsWin32 generation manifest.

### 4.3 `Kivi.App` — headless console host
- Console app that builds the DI container (Core + Platform), constructs the orchestrator, logs
  pipeline state to the console, and runs real dictations. Replaced by the WinUI 3 shell in the UI plan.

---

## 5. End-to-end flow (this plan)

Matches [`system-design.md` §5](../../system-design.md), minus UI: hold right-Ctrl → snapshot UIA
context (concurrent) + start resilient recording → release → STT (Groq, hallucination-filtered) →
macro check → PolishPipeline + Groq cleanup (with context) → paste with 4 safeguards → back to Idle.
Console logs each state transition in place of the overlay.

---

## 6. Verification

- **Unit tests** (`Kivi.Core.Tests`, fake `HttpMessageHandler`): STT/cleanup request+response
  shapes, `no_speech_prob` filter, `PolishPipeline` regex cases, macro/vocab matching, orchestrator
  state transitions, config defaults.
- **Integration test:** canned WAV → **real Groq** → assert non-empty cleaned text. Gated on
  `GROQ_API_KEY`; skipped (not failed) when absent.
- **Manual E2E:** run console host → hold right-Ctrl, speak, release → observe console state log +
  text pasted into focused Notepad. **Password-skip check:** dictate into a password field, confirm
  no field content appears in the captured context.

---

## 7. Risks & caveats (from research/impl agents — verify at build time)

- **CsWin32 marshaling:** generated member signatures (marshaled vs `PreserveSig` HRESULT) depend on
  `allowMarshaling`; inspect generator output and adjust call sites when building the UIA code.
- **NAudio 2.x:** confirm managed method/enum names (`IMMNotificationClient` signatures,
  `Role.Communications`, `WasapiCapture(MMDevice)` ctor) and HRESULT hex constants
  (`AUDCLNT_E_DEVICE_INVALIDATED`/`DEVICE_IN_USE`) against NAudio source / `audioclient.h`.
- **WASAPI 16k shared-mode:** assert-but-smoke-test forced 16 kHz mono PCM on real hardware; keep a
  resampler fallback path.
- **Groq availability/rate limits:** integration test and manual E2E depend on Groq being reachable
  and the key being valid; the cooldown/fallback logic must be exercised.

---

## 8. Out of scope (future plans)

- **POA #4 — Kivi UI skin** (overlay pill, tray, settings): awaits the Claude/Kivi design link;
  token-swap workflow already specified in [`impl-03` §9](../../impl-03-winui3-kivi-ui.md).
- **POA #5 — perf pass** to <100 MB RSS (`dotnet-counters`, trimming, self-contained publish).
- **POA #6 — WiX MSI → signed `installer.exe`.**
