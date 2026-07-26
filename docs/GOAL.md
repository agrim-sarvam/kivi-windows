# Kivi .NET/Windows — North-Star Goal

> This is the persistent brief for the port. If context is reset, re-read this file +
> `PROGRESS.md` to resume. Do not lose the thread.
>
> This is a **Windows-only .NET** port of the Electron implementation of Kivi. The
> Electron reference lives read-only under `_reference/sarvam-kivi-electron/`; it was
> itself a cross-platform (Windows + Linux) port of a native macOS Swift app. We keep
> only the Windows target: every macOS primitive is translated to its Windows/.NET
> equivalent, every Linux/Wayland/X11 concern is dropped, and every Electron/Node
> primitive is translated to its .NET equivalent.

## Mission

Build a **Windows-native .NET clone** of Kivi — a voice-dictation app. Hold a hotkey →
speak → formatted text lands in the app you were already typing in, without Kivi ever
stealing focus, wrapped in a hand-drawn per-frame-eased "living orb." Reuse the same
design primitives and wire it to the **same local `kivi-service` backend** the Electron
and macOS apps use. Grow toward as much functional parity as possible.

## Source of truth (do not modify these — they are references)

- **Electron reference:** `_reference/sarvam-kivi-electron/` (`packages/orb-core`, `packages/design-tokens`, `src/main`, `src/renderer`, `docs/`, `test/golden-frames`). **Immutable — read constantly, never edit.**
- The Electron app's own reference chain (the macOS Swift app, the Rust `kivi-service`) is cited inside the reference docs for provenance; we port from the **Electron** implementation, which is the closest to our target platform.
- Rust backend service `kivi-service` — same protocol, unchanged; run it locally against `ws://127.0.0.1:8788`.

## Definition of done (success criteria)

1. **Visual parity**: side-by-side screenshots of the .NET app vs the Electron app
   (orb + main window) match. This is the primary gate the user named. The orb baseline
   is the "maxi mini-app" documented in `docs/maps/orb-visual-and-box.md`.
2. **Functional parity (transcription first)**: global hotkey → WASAPI mic capture →
   16 kHz Int16 mono LE PCM stream to the local `kivi-service` STT → paste result into
   the active app — works on Windows, producing the same/similar output as the Electron
   app against the same service.
3. **Feature parity**: after the tangible MVP, close as many features as possible
   (see `FEATURE-PARITY.md`).
4. **Ship to internal team** with UX on par with WisprFlow / Willow Voice / Sarvam.

## Ordered plan

0. Port the Electron reference docs into Windows-only .NET docs (this doc set). **(Phase 0 — in progress.)**
1. **M0 — tangible MVP**: trimmed-down transcription (OpenWhispr-style, see `docs/maps/openwhispr-reference.md`) wired to the local `kivi-service`. One .NET solution (`Kivi.Core` / `Kivi.Platform` / `Kivi.App`), Windows-native throughout.
2. If tangible: deep-dive the feature-parity list, close as many as possible.
3. **Visual clone**: build the WinUI/XAML + Composition UI, test piece-by-piece against the Electron app, keep fixing until it's a visually exact clone.
4. **Wire the service**: piece-by-piece against the current local `kivi-service`; keep testing that output is the same/similar.
5. **Integration**: full integration testing + parity with the Electron app.
6. **UX polish**: emulate WisprFlow / Willow Voice / Sarvam quality → release internally.

> **Dropped from the Electron mission (Windows-only repo):** the "cross-platform / Linux
> port" milestone and all Wayland/X11 tiers. There is no Linux target. Where the Electron
> plan branched on `process.platform`, we compile a single Windows path.

## Testing references

- Run the local `kivi-service` (requires Postgres; `LOAD_TEST_MODE=synthetic` bypasses Sarvam/Gemini but not Postgres). Point the client at `ws://127.0.0.1:8788`. Full run steps in `docs/maps/backend-service-api.md §7`.
- Compare STT/edit output against the SAME local `kivi-service` the Electron app uses, with the fixture WAVs.
- Verify the ported engine against `_reference/sarvam-kivi-electron/test/golden-frames/*.json`.

## Working discipline

- Adversarially verify before trusting findings; keep `PROGRESS.md` updated after every meaningful step (it is the resume anchor).
- Follow the Superpowers process (brainstorming → writing-plans → TDD → verification-before-completion). Don't claim something works without running it.
- Clear Kivi cache before any test handoff (`%APPDATA%\Kivi`, full uninstall if testing install) — unasked.
- Do not push or open PRs unless asked. Commit locally with clear messages.
