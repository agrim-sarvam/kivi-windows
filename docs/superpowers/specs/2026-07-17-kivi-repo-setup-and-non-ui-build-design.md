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

**Target frameworks & NuGet packages (pin these — CsWin32/UIA won't generate otherwise):**
- `Kivi.Core`, `Kivi.Core.Tests`: **`net8.0`**.
- `Kivi.Platform`, `Kivi.App`: **`net8.0-windows10.0.19041.0`** (an explicit
  `TargetPlatformVersion` is required for CsWin32 to generate the Windows/UIA APIs; bare
  `net8.0-windows` is not enough).
- Packages: `Microsoft.Windows.CsWin32` (build-time source generator, Platform),
  `NAudio` (Platform), `Microsoft.Extensions.DependencyInjection` +
  `Microsoft.Extensions.Hosting` (App composition root), `Microsoft.Extensions.Configuration`
  (+ `.UserSecrets`, `.EnvironmentVariables`) for the dev key, `Microsoft.Extensions.Logging`
  (+ `.Console`) for the no-sensitive-data logging. Tests: `xunit`,
  `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`.

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
  **baseURL validation:** transcription/chat base URLs are user-configurable, so validate each is a
  well-formed **HTTPS** absolute URI before use (reject non-https / malformed) — basic SSRF/open-
  redirect hygiene.

**Logging / no-sensitive-data rule (applies across all projects):** use
`Microsoft.Extensions.Logging`. Log only **state transitions, latencies, model IDs, and error
codes/messages**. **Never** log transcript text, audio bytes, captured screen-context, or the API
key (not even truncated). Mirrors FreeFlow's privacy promise ("only API calls leave the machine").

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

### 4.2b Observability — embedded metrics (OpenTelemetry, simple)

Goal: while running the app, **see CPU / memory / per-stage latency live** and catch spikes —
especially against the <100 MB RSS budget — without standing up Prometheus/Grafana infrastructure.

Design (OTel-standard but zero-infra by default):
- **Instrument with a plain `System.Diagnostics.Metrics.Meter("Kivi")`** in `Kivi.Core`
  (dependency-free at the instrumentation layer):
  - `kivi.dictation.stage.duration` (histogram, ms) tagged by stage (`record`/`stt`/`cleanup`/`paste`)
    and `kivi.dictation.total.duration` — emitted by the orchestrator around each stage.
  - A small background sampler emits **`kivi.process.rss`** (MB) and **`kivi.process.cpu`** (%)
    from `Process.GetCurrentProcess()` (`WorkingSet64`, `TotalProcessorTime` delta) every ~2 s.
- **Collect/display in `Kivi.App`** via an OpenTelemetry `MeterProvider`:
  - `Sdk.CreateMeterProviderBuilder().AddMeter("Kivi").AddRuntimeInstrumentation()` — the
    `OpenTelemetry.Instrumentation.Runtime` package adds CPU/GC/heap/thread counters for free.
  - Default reader = **`AddConsoleExporter()`** → metrics print to the same console you're watching.
    No dashboard, no server.
  - **Escape hatch (no code rewrite):** swapping `AddConsoleExporter()` for `AddOtlpExporter()`
    points at the free **.NET Aspire dashboard** (a single container) if graphs are ever wanted.
- **Toggleable** (protects the perf budget): metrics off by default; enabled by an
  `--metrics` arg or `KIVI_METRICS=1` / config flag. When off, no `MeterProvider` is built and the
  sampler doesn't run, so a clean RSS measurement isn't polluted by the observability overhead.
- **Also works with zero app config:** because instrumentation uses the standard `Meter`,
  `dotnet-counters monitor --name Kivi.App --counters Kivi` attaches live regardless of the toggle.
- **Privacy:** metrics are **numbers and stage names only** — never transcript/audio/context/key
  content (consistent with the logging rule).
- Packages (Platform/App): `OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`,
  `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Exporter.Console`.

### 4.3 `Kivi.App` — headless console host
- Console app that builds the DI container (Core + Platform), constructs the orchestrator, logs
  pipeline state to the console, and runs real dictations. Replaced by the WinUI 3 shell in the UI plan.
- **Message-loop requirement:** a `WH_KEYBOARD_LL` low-level hook only delivers callbacks on a
  thread that **pumps a Windows message loop**. A plain console `Main` does not, so the hotkey would
  silently never fire. The host must run a message pump on the hook's thread (e.g. an STA thread
  calling `GetMessage`/`TranslateMessage`/`DispatchMessage`, or a hidden-window pump) and keep the
  process alive until quit. This same STA thread is a natural home for the clipboard/paste work.

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
- **Orchestrator integration test (no hardware):** drive `DictationOrchestrator` with **real Core +
  fake Platform services** (fake hotkey trigger, fake audio returning a canned WAV, fake context,
  spy paste service) and assert the full state sequence `Idle → Listening → Transcribing → Pasting →
  Idle` and that the paste service received the cleaned text. This covers orchestration logic
  automatically so manual E2E only has to prove the real OS glue.
- **Manual E2E:** run console host → hold right-Ctrl, speak, release → observe console state log +
  text pasted into focused Notepad. **Password-skip check:** dictate into a password field, confirm
  no field content appears in the captured context.

- **Observability check:** run `Kivi.App --metrics`, perform a dictation, and confirm the console
  shows RSS/CPU samples + per-stage latency (`record`/`stt`/`cleanup`/`paste`) and runtime counters;
  confirm RSS stays within sight of the <100 MB target and no stage latency spikes unexpectedly.
  Also confirm `dotnet-counters monitor --name Kivi.App --counters Kivi` attaches.

**Privacy checklist (verify before this plan is done):**
- [ ] Only outbound traffic is API calls to the configured Groq (transcription + chat) endpoints —
      nothing else leaves the machine.
- [ ] API key encrypted at rest via DPAPI (`DpapiSecretStore`); never in plaintext config, never logged.
- [ ] No audio, transcript, or captured context persisted to disk or logs.
- [ ] Backend base URLs validated (HTTPS + well-formed) before any request.
- [ ] Password/secure fields never read by the context provider.

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

## 8. Definition of done

This plan is complete when:
1. `git` repo initialized; `_reference/` cloned (3 repos) and git-ignored; `dotnet build` of the
   solution succeeds on a clean checkout.
2. `Kivi.Core` and `Kivi.Platform` implement every component in §4; `Kivi.Core` has **zero**
   Windows/UI dependencies.
3. `dotnet test` passes: unit tests (fake HTTP) + the orchestrator integration test (fake Platform)
   green; the real-Groq integration test green when `GROQ_API_KEY` is set, skipped otherwise.
4. **Manual E2E verified:** holding right-Ctrl, speaking, and releasing pastes cleaned text into a
   focused Notepad, and the password-skip check passes (no secure-field content captured).
5. Build-time caveats in §7 are resolved (not merely noted) in the actual code.

**Suggested build order** (writing-plans will detail): repo/toolchain → `Kivi.Core` abstractions +
config → Groq client + STT/cleanup + PolishPipeline + prompts + macros/vocab (TDD) → orchestrator
(+ orchestrator integration test) → `Kivi.Platform` services (hotkey → audio+resilience → paste →
UIA context → DPAPI) → console host + message pump → manual E2E.

---

## 9. Out of scope (future plans)

- **POA #4 — Kivi UI skin** (overlay pill, tray, settings): awaits the Claude/Kivi design link;
  token-swap workflow already specified in [`impl-03` §9](../../impl-03-winui3-kivi-ui.md).
- **POA #5 — perf pass** to <100 MB RSS (`dotnet-counters`, trimming, self-contained publish).
- **POA #6 — WiX MSI → signed `installer.exe`.**
