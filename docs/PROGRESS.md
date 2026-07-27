# Kivi .NET/Windows — Progress Log

> Newest entries at the top. This is the resume anchor for the .NET port.
> After each meaningful step, append what was done + what's next.
>
> This is a **fresh log for the .NET/Windows port** — it does NOT carry over the Electron
> reference's build history (that lives, immutable, under `_reference/sarvam-kivi-electron/`).
> The Electron app's own progress is not our progress; we start from Phase 0.

---

## Phase 4 — orb visuals ✅ (renders + animates in demo mode)

### The living orb is drawn in WPF from the FlowFrame, in a native layered window
- **`Kivi.Platform/Overlay/LayeredOrbHost.cs`** (was a stub): real Win32 layered window
  (`WS_EX_LAYERED|TOPMOST|NOACTIVATE|TOOLWINDOW`) painted per tick via `UpdateLayeredWindow` with a
  premultiplied-ARGB DIB (true per-pixel alpha, never activates, no taskbar button). `SetClickThrough`
  toggles `WS_EX_TRANSPARENT`; `WM_MOUSEACTIVATE→MA_NOACTIVATE`.
- **`Kivi.App/Drawing/`** — the FlowFrame renderer (GDI+, pure functions of one frame):
  `FlowRuntime` (the FlowRuntimeWeb port — 3-tier fps band 24/30/60, rest-park + 1 Hz heartbeat,
  nudge; steps the engine, positions bottom-center DPI-aware), `OrbRenderer` (pill⇄orb morph,
  fill@alpha, paper grain, 4-layer glow per-state, eyes, sphere gloss), `KiwiMarkRenderer` (dotted
  walking kiwi: mask + 48×8 gait cache + coverage + wave glow + breathing, from the frozen
  Kivi.Core.KiwiMark math), `PaperGrain` (128×128 LCG, seed 0x4B49564950415045 byte-exact),
  `TranscriptBoxRenderer` (wedge box, header state narration, transcript lines, red-strike diff morph,
  reveal mask), `SatellitesRenderer` + `OrbIcons` (SVG-path→GraphicsPath), `DemoDriver`, `DrawUtil`.
- **Wiring:** `App.xaml.cs` shows the orb at launch on both paths; demo mode (`KIVI_ORB_DEMO=1` /
  `--demo`) drives a standalone engine through rest→listening→processing→done with no socket/mic.
  The M0 live loop is untouched (orchestrator exposes `Engine`; runtime renders it in the live path).
- **Verified:** `dotnet build` green (0/0); 92/2 tests pass; demo launches, layered window + render
  loop run, no crash; agent captured screenshots of forest orb + kiwi + per-state glow easing
  (orange→blue→green) + wedge box + diff morph.

### Rendered vs deferred (this phase = orb + box + satellites; box additions are incremental)
- Rendered: orb morph/fill/grain/glow/eyes/gloss, kiwi mark (wave+gait), states rest/listening/
  processing/done, wedge box + header + transcript + diff morph, left/right/below satellites + icons.
- Deferred (additive, noted in code): maxi footer action bar, pager dots, context card, copy chip,
  wave-sweep gradient, pill-take 7-bar mic face, hint pills, drag handle, edit pane.
- Divergences: GDI+ has no gaussian blur → glow = stacked translucent expanded rects (hue/spread/alpha
  from the engine, only the blur kernel differs; Win2D GaussianBlur is the pixel-exact upgrade path);
  desktop-behind backdrop blur excluded (R1); fonts use the fallback stack (Matter/Space Grotesk
  license-blocked/dev-only).

### How to watch it
```
$env:KIVI_ORB_DEMO=1; dotnet run --project Kivi.App -c Debug
```

**NEXT:** P5 — main window shell + pages (Record/History/Settings/Personas), then tray (P6 start).
Live M0 round-trip still pending the NetBird VPN approval.

---

## Phase 3 — M0 dictation loop ✅ CODE-COMPLETE (awaiting live-service test)

### The loop is wired end to end: hotkey → capture → STT → paste
- **`Kivi.Core/Wire`** (wire-backend): `KiviServiceClient` over `ClientWebSocket`, `WireModels`
  (deterministic sorted-key snake_case JSON), `Endpoints`, `DictationBudgets`, `Kivi.Core/Rest`
  `KiviRestClient`. All wire invariants implemented + unit-tested: always-emit `formatting_enabled`,
  closed-enum guard (`verbatim|casual|transliteration|formal`), `transcription_mode=codemix`,
  drain-before-EOS, pre-connect buffer flush-in-order, backpressure drop-oldest@50, app-level
  ping/pong, 3200-byte frames, budgets byte-exact, `/v1/edit` camelCase, anon-omits-Authorization.
- **`Kivi.Platform`** (platform-native, rebuilt from scratch): `LowLevelKeyboardHookService`
  (WH_KEYBOARD_LL on a dedicated message-pump thread, default Right-Ctrl, auto-repeat debounced),
  `WasapiCaptureService` (+ `ContinuousResampler` → 16k Int16 mono LE 3200-byte frames, state
  continuous across callbacks), `SendInputPasteService` (clipboard + release-held-modifiers +
  Ctrl+V / terminal→Ctrl+Shift+V + no-refocus + clipboard-restore + secure-field gate),
  `ForegroundAppResolver` (GetForegroundWindow→exe path, last-non-Kivi memo), `DpapiSecretStore`.
- **`Kivi.App`** (orchestrator, this step): `WireDictationService` bridges the FlowEngine's
  `IDictationService` seam to `KiviServiceClient` (maps wire events → engine `DictationEvent`s,
  generation-guarded; raises PasteRequested on final). `DictationOrchestrator` owns the FlowEngine
  + the seams: hotkey Down → capture frontmost target + start mic + `engine.FnDown()`; frames route
  mic → wire client (buffered pre-handshake); hotkey Up → stop mic + `engine.FnUp()` (drain → EOS);
  on final → paste formatted text into the captured target (terminal-detected `PasteMeta`).
- **Verified:** `dotnet build` green (0/0); `dotnet test` 92 passed / 2 skipped (skips = live-service
  integration test + mic smoke, both gated); `Kivi.App.exe` launches, DI resolves, orchestrator
  starts, WH_KEYBOARD_LL hook installs, no crash.
- **The ONE remaining hop to prove M0 live:** the full mic→STT→paste round-trip needs (a) the
  NetBird VPN + local `kivi-service` reachable at `ws://127.0.0.1:8788`, and (b) mic permission.
  Every seam AROUND it is tested. The live integration test auto-runs (un-skips) once `/ready` is
  reachable — set `KIVI_WS_URL` / `KIVI_FIXTURE_PCM` to point at the service + a fixture WAV.

**NEXT:** user connects NetBird VPN → run the live test + hold Right-Ctrl into Notepad and speak →
confirm text pastes. Then P4 — orb visuals (the FlowFrame renderer + native layered window).

---

## Phase 1 — solution skeleton ✅ (build green, app launches)

### `Kivi.sln` scaffolded per MASTER-PLAN §4
- Four projects on **.NET 8 / WPF** (`net8.0-windows`): `Kivi.Core` (pure, UI/OS-free),
  `Kivi.Platform` (Windows-native seams), `Kivi.App` (WPF host + DI root), `Kivi.Core.Tests` (xUnit).
- **Kivi.Core** folders per §4: `Orb/ KiwiMark/ DesignTokens/ Planner/ Wire/ Rest/ Contracts/`.
  `Contracts/PlatformContracts.cs` defines the in-process seam interfaces (`IHotkeyService`,
  `IPasteService`, `IOverlayHost`, `IFrontmostApp`, `IAudioCapture`, `ISecretStore`, `ITrayHost`)
  + DTOs (`GestureEdge`, `AppTarget`, `PasteMeta`, `PasteOutcome`). All other folders hold a
  placeholder noting which Electron source ports into them and in which phase.
- **Kivi.Platform** folders per §4: `Hotkey/ Paste/ Frontmost/ Overlay/ Audio/ Secrets/ Tray/ Auth/`,
  each with a **stub** implementing its contract (no-op), documented with the real P3+ approach.
  `PlatformServiceCollectionExtensions.AddKiviPlatform()` wires them for DI.
- **Kivi.App**: WPF, DI composition root in `App.xaml.cs` (no StartupUri — DI creates windows),
  `DictationOrchestrator` skeleton (holds injected seams), stub `MainWindow` (Canon canvas bg,
  "kivi" wordmark), per-monitor-V2 DPI manifest.
- **Verified:** `dotnet build` green (0 errors, 2 expected stub-event warnings); `dotnet test`
  2/2 pass; `Kivi.App.exe` launches, DI resolves, MainWindow shows and stays alive.
- Decision recorded: **WPF over WinUI 3** (latency/fidelity/native-layered-window/interop — see
  MASTER-PLAN §2.5 note). Orb remains a native Win32 layered window (P4).

**NEXT:** P2 — core port (`core-porter`): FlowEngine + FlowFrame + transcript + tokens + KiwiData
mask → C# in `Kivi.Core`, verified against the golden-frame JSON oracles.

---

## Phase 0 — docs port ✅ (committed)

### Porting the Electron reference docs → Windows-only .NET docs
- Mirroring `_reference/sarvam-kivi-electron/docs/` structure exactly into `docs/`: the 5
  top-level docs (`GOAL`, `MASTER-PLAN`, `FEATURE-PARITY`, `PROGRESS`, `RELEASE`), all 12
  maps under `docs/maps/`, and the historical `docs/plans/` (3) + `docs/critiques/` (6) as
  superseded-planning stubs.
- Every macOS primitive → its Windows/.NET equivalent (Keychain→DPAPI, CGEventTap→`WH_KEYBOARD_LL`,
  NSPanel→native layered window, AUHAL/VPIO→WASAPI, ⌘V→Ctrl+V via `SendInput`, `NSWorkspace`
  bundle-id→`GetForegroundWindow`+exe path, Swift Charts→XAML). Every Electron/Node primitive →
  .NET (BrowserWindow→WPF window / native Win32 layered orb, IPC/preload→in-process `async`/`await`+events,
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
