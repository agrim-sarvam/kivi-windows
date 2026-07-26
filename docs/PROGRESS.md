# Kivi .NET/Windows — Progress Log

> Newest entries at the top. This is the resume anchor for the .NET port.
> After each meaningful step, append what was done + what's next.
>
> This is a **fresh log for the .NET/Windows port** — it does NOT carry over the Electron
> reference's build history (that lives, immutable, under `_reference/sarvam-kivi-electron/`).
> The Electron app's own progress is not our progress; we start from Phase 0.

---

## Phase 0 — docs port (in progress)

### Porting the Electron reference docs → Windows-only .NET docs
- Mirroring `_reference/sarvam-kivi-electron/docs/` structure exactly into `docs/`: the 5
  top-level docs (`GOAL`, `MASTER-PLAN`, `FEATURE-PARITY`, `PROGRESS`, `RELEASE`), all 12
  maps under `docs/maps/`, and the historical `docs/plans/` (3) + `docs/critiques/` (6) as
  superseded-planning stubs.
- Every macOS primitive → its Windows/.NET equivalent (Keychain→DPAPI, CGEventTap→`WH_KEYBOARD_LL`,
  NSPanel→native layered window, AUHAL/VPIO→WASAPI, ⌘V→Ctrl+V via `SendInput`, `NSWorkspace`
  bundle-id→`GetForegroundWindow`+exe path, Swift Charts→XAML). Every Electron/Node primitive →
  .NET (BrowserWindow→native/Composition window, IPC/preload→in-process `async`/`await`+events,
  `ws`→`ClientWebSocket`, `safeStorage`→DPAPI, `getUserMedia`/AudioWorklet→WASAPI,
  electron-vite/builder/updater→the .NET build/publish + installer). Every Linux/Wayland/X11
  concern **removed** (Windows-only repo).
- **All parity constants transferred byte-exact** (ack 4000 / ping 20000 / finalTimeout 20000 /
  maxPendingAudioFrames 50 / JWT 900 s / idle 180 s / context window 30 s; audio 16 k Int16 mono
  LE, 1600 samples = 3200 bytes/frame; gestures holdMs 420 / doubleTapMs 450 / longHoldMs 600;
  `ease60(k)=1−pow(1−k,dtFrames)` with `dtFrames=clamp((now−prev)/16,0..3)`; all color hexes,
  type scale, spacing, radii, motion durations/easings; paper-grain LCG seed `0x4B49564950415045`;
  endpoint `/v1/dictate/stream`, `/v1/edit` camelCase response).
- **Value-conflict resolutions applied** (prefer the shipped-client value): wake lerp **0.30**
  (not 0.20); `transcription_mode` default **`codemix`** (not `transcribe`); orb baseline = the
  **maxi mini-app** design in `map:orb-visual`.
- **.NET namespace/project mapping settled** (see `MASTER-PLAN §5` header) and used consistently
  across all docs: `packages/orb-core`→`Kivi.Core/Orb`, `packages/design-tokens`→`Kivi.Core/DesignTokens`
  (+ XAML themes in `Kivi.App`), `packages/kiwi-mark`→`Kivi.Core/KiwiMark`,
  `packages/planner`→`Kivi.Core/Planner`, `src/main/wire`→`Kivi.Core/Wire`, REST→`Kivi.Core/Rest`,
  shared contracts→`Kivi.Core/Contracts`, OS-coupled `src/main/platform`→`Kivi.Platform.*`,
  app lifecycle/window orchestration→`Kivi.App`, `src/renderer`→`Kivi.App/Views`+`ViewModels`+`Drawing`,
  `test/golden-frames`→`Kivi.Core.Tests`.

**NEXT (toward M0 — the tangible transcription MVP; see `MASTER-PLAN §5 M0` + `§8`):**
1. Stand up the local `kivi-service` (Postgres 16 + `.env.local`, `DICTATE_AUTH_MODE=none`, `PORT=8788`); confirm `/health`. (Steps: `docs/maps/backend-service-api.md §7`.)
2. Headless wire spike (console app): `ClientWebSocket` → `ack` → the exact MVP `context` → stream a 16 k WAV fixture as 3200-byte frames → `end_of_speech` → print `final.formatted_text`. Save as the **golden-transcript baseline**.
3. Create `Kivi.sln` (`Kivi.Core` / `Kivi.Platform` / `Kivi.App` / `Kivi.Core.Tests`) per `MASTER-PLAN §4`; display-only native layered orb + hidden main window.
4. Build `KiviServiceClient` (`Kivi.Core.Wire`, `ClientWebSocket`) with wire-trap guards + `DictationBudgets`.
5. Wire WASAPI audio → 16 k Int16 mono LE (continuous resampler state) → 100 ms frames → in-process `Channel` → binary WS frames.
6. Add `WH_KEYBOARD_LL` hotkey (dedicated thread) + pure `GestureClassifier` + `SendInput` Ctrl+V paste (modifier-release, clipboard restore, secure-field gate). EV-sign hook/inject binaries early.
7. Close the M0 loop: hold Right-Ctrl → speak into Notepad → release → pasted text; assert `final.formatted_text` == the M0 golden.

**Parallel (Track B, gates M2–M3):** font license go/no-go; token pipeline (`Kivi.Core.DesignTokens`
+ XAML themes, Canon-over-KDS dark override); consume the reference golden frames
(`_reference/.../test/golden-frames/*.json`) in `Kivi.Core.Tests`; capture the orb visual baseline
from the running Electron app.
