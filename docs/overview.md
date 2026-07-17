# Kivi for Windows — Project Overview

## Context

Kivi is Sarvam's in-house voice dictation app, currently macOS-only (Swift UI app `sarvam-kivi-UI` + Rust backend `sarvam-kivi-service`). The goal is to bring Kivi to Windows.

**Change in plan:** We do not currently have direct access to the `sarvam-kivi` repo. Instead of porting Kivi's actual codebase, the plan is to:

1. Take the open-source macOS dictation app **[FreeFlow](https://github.com/zachlatta/freeflow)** as a reference implementation.
2. Rebuild its core dictation pipeline (hotkey → mic capture → STT → cleanup → paste) natively in **.NET for Windows**.
3. Apply the **Kivi UI** on top as the presentation layer — FreeFlow's engine is the "core," Kivi UI is the "skin."
4. This becomes a placeholder/Phase 0 Kivi-for-Windows client until real repo access to `sarvam-kivi` is available.

This is essentially a wrapper/port project: **FreeFlow (macOS, Swift) → .NET (Windows) → Kivi UI skin**.

---

## Reference material / links

**FreeFlow (original + ports)**
- [zachlatta/freeflow](https://github.com/zachlatta/freeflow) — original macOS app (Swift). Hold-to-talk hotkey, context-aware cleanup, custom vocab, voice macros, OpenAI-compatible providers. This is our primary reference for pipeline logic and prompts.
- [stha-hardik/freeflow-windows](https://github.com/stha-hardik/freeflow-windows) — existing community Windows port (Python/PyQt6). Not code we reuse (we're building in .NET), but a useful reference for how hotkeys/audio/paste were already solved on Windows.
- [mrinalwadhwa/freeflow](https://github.com/mrinalwadhwa/freeflow) — another FreeFlow variant/fork, worth a skim for pipeline/prompt ideas (local heuristic to skip LLM polish, on-device transcription approach).

**Windows platform APIs**
- [Develop accessible Windows apps — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/accessibility) — entry point for UI Automation (UIA), the Windows equivalent of macOS's Accessibility (AX) API.
- [Control patterns and interfaces reference](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/control-patterns-and-interfaces) — how to read focused element + surrounding text via `TextPattern` / `ValuePattern`. This is how we'll implement screen context capture.
- [WinUI 3 Gallery (GitHub)](https://github.com/Microsoft/WinUI-Gallery) — controls/sample reference if we go WinUI 3 for the UI shell.
- [Windows App SDK samples (GitHub)](https://github.com/microsoft/WindowsAppSDK-Samples) — general Windows App SDK usage patterns.

**Internal Kivi docs (macOS reference architecture, for parity)**
- [Kivi Design](https://app.notion.com/p/37339c96b62d80008970e9e3dad3480b?pvs=1)
- [kivi — Data Flow, Deployment & Security Reference](https://app.notion.com/p/39339c96b62d80b1ae3be2ab59c11115?pvs=1) — describes mic capture, AX-based screen context, Keychain token storage, paste mechanism, password-field skip logic on macOS. Use this as the parity checklist for the Windows port.
- [Organization-Scoped Kivi Integration Plan](https://app.notion.com/p/39339c96b62d809fa06ae4167d14be25?pvs=1)
- [Billing & Metering — Seat Subscriptions for Agents, Code & Kivi](https://app.notion.com/p/39839c96b62d81f4b06af84bc433d4d8?pvs=1)

**Packaging**
- [WiX Toolset](https://wixtoolset.org/) — for building the MSI + bundling into a single installer.exe.

---

## POA (Plan of Action) — in build order

| # | Deliverable | Approach / Tech |
|---|---|---|
| 1 | Core dictation pipeline (hotkey → mic → STT → paste) | .NET (WinUI 3 or WPF), ported from zachlatta/freeflow logic |
| 2 | Screen context capture | Windows UI Automation (UIA) — `FocusedElement` + `TextPattern`, explicitly skip password/secure fields |
| 3 | Weekly driver update resilience | NAudio (WASAPI wrapper) + device-change event handling (`IMMNotificationClient`), retry/backoff specifically around mic init, re-enumerate devices at session start rather than caching handles |
| 4 | Kivi UI skin | Applied on top of the ported FreeFlow engine |
| 5 | CPU/memory optimization (target: <100MB RSS) | Native WinUI/WPF controls (avoid Electron/webview), streaming audio buffers (no full-utterance buffering), profiled with `dotnet-counters` / Windows Performance Recorder |
| 6 | Installer | MSI via WiX, bundled into a single signed `installer.exe` for website distribution |

### Notes per deliverable
- **Driver resilience** is about *not hard-failing* when Windows pushes weekly audio/driver updates — treat device-busy/reinit errors as transient and auto-reconnect to the default device rather than crashing.
- **Screen context** should mirror how Kivi's macOS app uses AX (see internal Data Flow doc) — read the focused field + nearby text for context-aware cleanup, never read masked/password fields.
- **Performance budget**: idle state should be near-zero CPU; spikes only during active dictation. Validate against the 100MB ceiling before each milestone demo, not just at the end.

### Open questions (not yet resolved)
- Which backend does this wrapper call — Kivi's real backend (`kivi-service`), or FreeFlow's own provider (Groq/OpenAI-compatible) as a stopgap until `sarvam-kivi` repo access is sorted?
- WinUI 3 vs. WPF — WinUI 3 is more modern/Fluent (closer to matching Kivi UI aesthetics) but has some packaging quirks; WPF is more mature/stable for a first Windows app. Worth a quick spike before committing.

---

## Working principle for this project

This is a **wrapper/port**, not a from-scratch app: FreeFlow's macOS pipeline defines *what* the app should do, the internal Kivi Data Flow doc defines *what parity with Kivi's mac app looks like*, and everything else in this doc defines *how* to build it on Windows in .NET. When in doubt about a design decision, check FreeFlow's macOS behavior first, then check Kivi's internal architecture doc for how Kivi does the equivalent thing, then implement the Windows-native equivalent.
