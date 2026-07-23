# Kivi

Kivi is a Windows dictation app: hold a hotkey, speak, and your cleaned-up
transcript is pasted into whatever app has focus. It's a Windows port of the
macOS app FreeFlow, re-architected around Sarvam AI's hosted STT + LLM APIs
instead of rewriting the whole pipeline from scratch.

Pipeline: **hotkey (hold right-Ctrl) → mic capture (WASAPI) → Saaras v3
transcription → Sarvam-30B LLM cleanup/polish → paste into the focused app.**

It also captures on-screen context (focused app name, window title, selected
text) via Windows UI Automation to help the cleanup step, with a fail-closed
guard that skips capture entirely for password fields.

## Project layout

- **Kivi.Core** — portable engine (net8.0, no Windows/UI dependencies).
  Orchestration, Sarvam HTTP clients, polish/cleanup pipeline, config, secrets
  interface, metrics. Fully unit-tested.
- **Kivi.Platform** — Windows-specific glue (net8.0-windows). WASAPI audio
  capture with device-change resilience, the global low-level keyboard hook,
  UI Automation screen-context capture, SendInput-based paste, DPAPI secret
  storage.
- **Kivi.App** — composition root. Currently a console app wiring everything
  together via DI; a WinUI 3 UI skin is planned (see `docs/impl-03`) once a
  design is ready.
- **Kivi.Core.Tests** — xUnit tests for `Kivi.Core`.

## Running locally

1. Copy `.env.example` to `.env` and set your Sarvam API key:
   ```
   SARVAM_API_KEY=sk_...
   ```
   (`.env` is git-ignored — never commit real keys. Get a key at
   https://dashboard.sarvam.ai.)

2. Run the app:
   ```powershell
   dotnet run --project Kivi.App
   ```
   Hold **right-Ctrl**, speak, release — the cleaned-up transcript pastes
   into whatever window has focus.

3. To also enable OpenTelemetry metrics (RSS/CPU/per-stage latency, printed
   to the console) and log the session to a file:
   ```powershell
   dotnet run --project Kivi.App -- --metrics 2>&1 | Tee-Object -FilePath logs/run-$(Get-Date -Format "yyyyMMdd-HHmmss").log
   ```
   (`logs/` is git-ignored.)

## Running tests

```powershell
dotnet test
```

## docs/

- **overview.md** — original project brief: goals and the plan-of-action
  (POA) checklist for the whole app.
- **freeflow-research.md** — research notes on FreeFlow and its Windows
  ports: what's reusable as a "wrapper" vs. what needs a Windows-native
  rewrite, and the backend/UI framework decisions (Groq, WinUI 3,
  UIA + NAudio) and how each is implemented on Windows.
- **impl-01-screen-context-uia.md** — detailed design for screen-context
  capture via UI Automation (UIA3), including the password-field guard.
- **impl-02-audio-driver-resilience.md** — detailed design for WASAPI mic
  capture and driver/device-change resilience (reconnect, backoff).
- **impl-03-winui3-kivi-ui.md** — detailed design for the planned WinUI 3
  UI skin (not yet built — waiting on a design reference).
- **system-design.md** — architecture map of the whole app: folder
  structure, data flow, and a build-status table showing what's actually
  implemented vs. planned per POA item.
- **deep-research-report-gpt.md** — external research report used to
  sanity-check the spec before implementation (logging/privacy rules,
  URL validation) — kept for reference.
- **superpowers/** — the brainstormed spec and implementation plan used to
  build the non-UI engine (`Kivi.Core` + `Kivi.Platform`) via a subagent-
  driven, test-driven workflow.
