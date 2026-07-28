# Kivi .NET/Windows — Progress Log

> Newest entries at the top. This is the resume anchor for the .NET port.
> After each meaningful step, append what was done + what's next.
>
> This is a **fresh log for the .NET/Windows port** — it does NOT carry over the Electron
> reference's build history (that lives, immutable, under `_reference/sarvam-kivi-electron/`).
> The Electron app's own progress is not our progress; we start from Phase 0.

---

## Orb mouse interaction + free-drag ✅ (bug fix: hover/click/drag were completely dead)

### Root cause
The engine's geometric hit-test (`FlowFrame.InteractiveTarget(x,y) -> HoverTarget?`) was never
ported in P2 — only the `HoveredTarget` FIELD existed, not the function that computes it. Per
`docs/maps/orb-engine-behavior.md` §12.6 this hit-test is **"fully portable and load-bearing (no
`.OnHover` fallback)"** — it's how the layered window's click-through gets toggled every tick.
With nothing computing it, the orb window stayed permanently `WS_EX_TRANSPARENT`, so Windows never
delivered mouse messages to it at all: hover, satellite clicks, and dragging all silently no-op'd.

### Fixed
- **`Kivi.Core/Orb/FlowFrame.cs`** — ported the hit-test: `InteractiveTarget`, `IsInteractive`,
  `OrbShapeContains`, satellite geometry + **1.5× visible-radius hit margin**, **opacity≤0.08
  invisible-satellite skip**, z-order (pane → satellites → drag handle → orb → hint → box → field).
  Measured against THIS repo's actual renderer math (SatellitesRenderer/OrbRenderer/
  TranscriptBoxRenderer), not fixed offsets — can't drift from what's drawn.
- **`Kivi.Core/Orb/FlowEngine.cs`** — `SetPointer(flowX, flowY, frame)` + `IsInteractiveAt(...)`;
  wired the previously-dead hover fields (`_orbNear`, `_groupHover`, per-satellite hover) for real.
- **`Kivi.Platform/Overlay/LayeredOrbHost.cs`** — real `WM_LBUTTONDOWN/MOUSEMOVE/LBUTTONUP` handling
  with `SetCapture`; a 4px move threshold distinguishes click from drag.
- **`Kivi.App/Drawing/FlowRuntime.cs`** — polls `GetCursorPos` every render tick (24–60Hz, whichever
  fps tier is active), converts to flow-space, calls `Engine.SetPointer` + `IsInteractiveAt`, toggles
  `SetClickThrough` only on change. Satellite clicks routed: SatCancel→CancelClick/CopyClick,
  SatEdit→EditClick, SatExpand→Expand/CollapseClick toggle. **Free-drag** (user's explicit ask — grab
  anywhere on the orb body, no handle, no double-click): `WM_LBUTTONDOWN` on the orb body captures
  the cursor; dragging moves the window live via `SetWindowPos`; releasing sets `_userPositioned=true`
  **permanently for the session**, after which `ScreenTopLeft()` returns the last dragged-to spot
  instead of auto bottom-center. Drag never touches `FnDown`/`FnUp` — fully separate from the M0
  hotkey path.
- **9 new tests** (`Kivi.Core.Tests/HitTestTests.cs`): orb-center hit, far-away miss, shape margin,
  invisible-satellite skip, visible-satellite hit, z-order (pane>satellite, satellite>orb),
  `IsInteractive`, rest-pill shape. **101/2 total, build green** (re-verified independently).

### Manual verification (needs a real interactive session)
1. `KIVI_ORB_DEMO=1 dotnet run --project Kivi.App` — orb appears bottom-center.
2. Hover near it — it should wake (2px enter / 10px leave hysteresis on the visible bounds).
3. During a take, hover/click a satellite (cancel ✕, expand) — should respond.
4. Press+hold directly on the orb body, drag, release — orb follows the cursor and STAYS at the
   drop point on every subsequent frame (never snaps back).
5. Confirm the hotkey-driven M0 loop is unaffected by dragging.

Also fixed same session: the orb was rendering near screen-CENTER instead of bottom-center
(`ScreenTopLeft()` offset bug, see commit `1269c72`) — same root area, separate bug, already fixed.

---

## Orb fidelity completion pass ✅ (closes the known P4 gaps before P6)

Closed all 8 items flagged as deferred after the mouse-interaction fix:
1. **Plain orb-body click** — confirmed no-op by design (dictation is hotkey-only, mice never drive
   PTT in the reference); comment now cites the investigation, not a guess.
2. **Copy chip** — added `HoverTarget.CopyChip` + hit-test + drawing. **Corrected a stale-doc
   discrepancy**: the map said 28×28/r5 in the card; the actual running `TranscriptBox.tsx` puts a
   **26×26/r7 chip in the header row** — followed the code (RULE 2: running code is visual truth),
   verified by reading the source directly. Wired to `CopyClick()` + real clipboard write + the
   engine's existing `CopyFlash` wash/checkmark.
3. **Footer action bar** — voice-slot pill (retry/follow-up/last+keycaps), word count, 28×28 thumbs
   (gated on `TakeRatable`, wired to the existing `RateTake`), new-session pill with the 1300ms
   orange sweep. Added the one genuinely NEW engine method needed: `FlowEngine.NewSessionClick()`
   (per orb-engine-behavior.md §3.4 — void take, clear box, stay expanded+idle).
4. **Pager dots** — active 16×6 / inactive 6×6 capsules, capped at 10, drawn in the header (fields
   already existed from P2; only the drawing was missing). Per-dot click NOT wired — the reference
   itself doesn't wire dot-clicks either (confirmed, not an oversight).
5. **Hint tooltip** — the reference implements exactly ONE satellite tooltip ("cancel", gated on
   `Settings.Tooltips`) — implemented that only; did not invent tooltips the source doesn't have.
6. **Wave sweep** & 7. **Pill-take mic-bar face** — **verified these have ZERO implementation in the
   current Electron/TS reference** (grep-confirmed empty) — implemented from the docs' byte-exact
   spec anyway (46%-band sweep 2.6s/2.4s; 7-bar mic face → glowing eyes), since the map still
   documents them as intended behavior and no richer reference contradicts it.
8. **Drag-handle visual** — deliberately skipped (not rendered): the shipped drag model is
   grab-anywhere-on-the-orb (user's explicit choice); a dot-grid affordance implying a second drag
   entry point would be misleading. Commented in `SatellitesRenderer.cs`.

**Verified independently** (not just agent self-report): no orphaned process after this pass
(`tasklist` clean), `dotnet build` 0/0, **108 passed / 2 skipped** (7 new tests, zero regressions),
app launches and was cleanly killed.

---

## Phase 5 — main window shell + pages ✅ (shell + pages render; data-wiring is P6)

### The Canon shell + all pages ported to WPF (XAML + MVVM)
- **Themes/** — `Base.xaml` (fonts/type/eases), `Light.xaml` + `Dark.xaml` (byte-exact Canon palettes
  from `Tokens.cs`), `Controls.xaml` (KiviCard, InkButton, window controls, finder input…),
  `ThemeManager` (system/light/dark, follows Windows AppsUseLightTheme, HKCU-persisted, 240ms crossfade).
- **Shell** — frameless `WindowChrome` window (caption drag + own min/max/close), `Rail` (264⇄76 fold,
  `cubic-bezier(0.2,0.8,0.2,1)` 0.24s, brand + 3 taxonomy groups + footer gear), Ctrl+\ collapse
  (persisted), `ShellBackground` (paper-grain + 24px constellation), `PageHeader` + highlight sweep,
  hand-drawn rail icons as verbatim `PathGeometry`.
- **ViewModels** — `AppNavigation` (single routing authority; `.presets`→`.styles`; hard-cut page swap),
  `PageData` (seed/stub data ported from model/*.ts, en-IN number format), `SettingsModel` (8 panes).
- **Pages (Views/Pages/)** — Record, History, Settings (8 panes + hotkey-capture field), Memory,
  Shortcuts, Analytics (hand-drawn sparklines/bars/pace-area), Leaderboard (podium), SharedTerms,
  Stub (clipboard). Personas = shell + overview only (detail pane + sheets + REST → P6).
- **Verified:** `dotnet build` green (0/0); 92/2 tests pass; app launches normal path — both the Canon
  shell (1180×760) AND the P4 orb come up together, no XAML-load crash. Navigation, theme swap, and
  rail collapse work. (Interactive screenshots pending — this env's GDI capture returns black; windows
  confirmed present + sized.)

### Deferred to P6 (data wiring) / later
- SQLite history + REST for memory/shortcuts/personas/leaderboard/usage — pages render `PageData` seeds.
- Personas detail pane + create-voice/marketplace/preset-library sheets.
- RecordPage live `recordFlightScene` bird-flight canvas + the streaming-transcript leaf bind.
- Fonts: Segoe UI / Georgia fallbacks (Matter/Season Mix license-blocked/dev-only).

**NEXT:** P6 — tray icon + personalization/data wiring (+ auth), then P7 — packaging (setup.exe).
Live M0 round-trip still pending the NetBird VPN approval.

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
