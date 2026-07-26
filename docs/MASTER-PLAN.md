# Kivi → .NET/Windows Port — Master Implementation Plan (Authoritative)

**Lead architect's final call.** This is the Windows-only .NET port of the Electron
reference. It supersedes the three historical drafts in `docs/plans/` (carried over only
to mirror the reference structure). It takes mvp-velocity's *prove-the-loop-first* spine,
fidelity-first's *numeric-golden-frame + generated-token* verification machinery, and
platform-risk-first's *platform-seam + engine-in-view-layer + wire-trap discipline* —
and resolves every blocker/high objection from the six critiques.

Three decisions frame everything below:

1. **Loop-first, Windows-native.** The mission's first deliverable is a *tangible transcription MVP*. We prove the loop on Windows in M0 against the real local `kivi-service`, then build up the visual clone and the pages. (There is **no cross-platform inversion** and **no Linux tier** — this is a Windows-only repo.)
2. **The Electron reference is the single source of truth for logic and pixels.** We port `packages/orb-core` (the FlowEngine, 100% golden-parity in the reference) and `packages/design-tokens` verbatim to C#. `_reference/*` is immutable; we read it constantly and never edit it. Every parity constant transfers byte-exact (see §3).
3. **Verification is numeric first, pixels second, both against a SHA-pinned oracle.** The engine is a pure function of `(events, now)`; we assert `FlowFrame` field equality against the golden frames the reference already exports (`_reference/.../test/golden-frames/*.json`), then pixel-diff against the running Electron app, with empirically-calibrated per-region budgets.

### The .NET project / namespace mapping (the mirror — used across every doc)

We mirror the Electron module boundaries so someone who knows the reference layout
recognizes the .NET layout. **This mapping is authoritative for all later phases:**

| Electron reference | .NET project / namespace | Contents |
|---|---|---|
| `packages/orb-core` | **`Kivi.Core/Orb/*`** (`Kivi.Core.Orb`) | `FlowEngine`, `FlowFrame`, `Transcript`, `CueBus`, `CueCatalog`, `SpeechPace`, `GestureClassifier`, phase/mark maps, engine constants |
| `packages/kiwi-mark` | **`Kivi.Core/KiwiMark/*`** (`Kivi.Core.KiwiMark`) | `KiwiData` 120×162 mask, state color tables, 48×8 gait cache (pure); Win2D draw lives in `Kivi.App` |
| `packages/design-tokens` | **`Kivi.Core/DesignTokens/*`** (`Kivi.Core.DesignTokens`) | generated token values; surfaced as XAML theme dictionaries in `Kivi.App/Themes/*` |
| `packages/planner` | **`Kivi.Core/Planner/*`** (`Kivi.Core.Planner`) | `PasteBoundaryPlanner`, `DictationInsertionPlanner`, `DictationJoinRewritePlanner` |
| `src/main/wire` | **`Kivi.Core/Wire/*`** (`Kivi.Core.Wire`) | `KiviServiceClient` (`ClientWebSocket`), `WireModels`, `Endpoints`, `DictationBudgets` |
| REST client | **`Kivi.Core/Rest/*`** (`Kivi.Core.Rest`) | `KiviRestClient` (`HttpClient`) |
| `src/preload` + IPC contracts (`src/shared`) | **`Kivi.Core/Contracts/*`** (`Kivi.Core.Contracts`) | in-process interfaces/DTOs — **no IPC bus** (one process, `async`/`await` + events) |
| `src/main` OS-coupled (`platform/*`) | **`Kivi.Platform/*`** (`Kivi.Platform.Hotkey`, `.Paste`, `.Frontmost`, `.Overlay`, `.Tray`, `.Audio`, `.Secrets`, `.Auth`) | Windows-native seams, rebuilt from scratch |
| `src/main` lifecycle/window orchestration | **`Kivi.App`** (host) | DI composition root, app lifetime, window orchestration, `DictationOrchestrator` |
| `src/renderer` (React/Canvas) | **`Kivi.App/Views/*` + `Kivi.App/ViewModels/*`** | XAML + MVVM; orb/box drawn with Win2D (`Microsoft.Graphics.Canvas`) / Composition |
| `test/golden-frames` | **`Kivi.Core.Tests`** | golden-frame oracle consumers |

Sub-namespace names may be refined during implementation, but the module boundaries above are fixed.

---

## 1. NORTH-STAR GOAL & DEFINITION OF DONE

**Goal:** a **Windows-native .NET** application that is a **visually exact** clone of the
Electron Kivi UI and wired to the **same local `kivi-service`** backend, delivering
functional parity starting from a trimmed OpenWhispr-style dictation MVP and growing
toward full parity.

**Definition of done — measurable gates:**

| Dimension | Criterion | Method |
|---|---|---|
| **Functional loop** | Global hotkey → WASAPI mic capture → 16 kHz Int16 mono LE PCM stream → local `kivi-service` `/v1/dictate/stream` → `final.formatted_text` pasted into the frontmost app's caret, on Windows. | OS-level integration harness (§6.5) spawns a target text field, injects the hotkey via the native hook, asserts inserted text + clipboard restored + host focus retained. |
| **Service-output parity** | For a fixed WAV fixture set, the .NET client's `final.formatted_text` and `raw_transcript` are byte-identical to the Electron client's against the *same* local service. | Golden-transcript harness (§6.3), `LOAD_TEST_MODE=synthetic` in CI, nightly real-STT job. |
| **Engine/motion parity** | C# `FlowEngine.Step()` reproduces the reference `FlowFrame` fields within a per-field tolerance policy (§6.1) across scripted event timelines at fixed `now`. | Golden-frame numeric gate against the JSON in `_reference/.../test/golden-frames/`. |
| **Visual parity (orb)** | Per-region screenshot delta under calibrated budgets vs the running Electron app for states `rest / listening / processing / done / edit` in forest+mist × light+dark, at matched logical size and DPI, over an identical composite background. Soft-effect regions (glow halo, sphere gloss, paper grain, backdrop) carry looser measured budgets; hard geometry/color regions carry tight budgets. Desktop-behind-window blur is **excluded** (physically unreproducible — see Risk R1). | Per-region masks (§6.4), image-diff. |
| **Visual parity (pages)** | Per-page delta under a calibrated budget (light+dark) vs the Electron app, with font-rasterization treated as an explicit allowance. | Same harness, per-page masks. |
| **App-identity contract** | A Windows app-identity scheme (exe path / AppUserModelID) is agreed with the backend and drives `app_context.bundle_id`, telemetry `paste_target`, and per-app personas. | Signed-off convention doc + wire tests. |

**Explicit non-goals for v1 (documented, not silently dropped):** UI-Automation range-level
edit (v1 uses select-all + paste-whole-field); system-audio echo cancellation (WASAPI
capture with the OS voice-communication signal processing is adequate for dictate-into-text
and is **not** full system-audio AEC — see R2); screen-context enrichment
(`screen_nodes`/`cursor_context` via UI Automation — server degrades gracefully); rich-format
clipboard paste (degrades to plain text). **Removed entirely (not deferred):** all
Linux/Wayland/X11 concerns — there is no such target.

---

## 2. TARGET ARCHITECTURE

### 2.1 Process / window topology

**One .NET process.** There is no renderer↔main split and no IPC bus (T2): the Electron
main/renderer boundary collapses to in-process `async`/`await`, `Task`, C# events, and
`IObservable`. The app owns all OS-native concerns and the STT socket directly. Three
window surfaces, one tray.

| Surface | Window model | Owns | Electron analog |
|---|---|---|---|
| **Orb overlay** | **Native layered / Composition window**: always-on-top, `WS_EX_NOACTIVATE` + `WS_EX_TOPMOST` + `WS_EX_TOOLWINDOW`, click-through toggled by hit-testing the cursor against the published interactive-region rect. Drawn via `UpdateLayeredWindow` (or DirectComposition) — WinUI cannot host a truly transparent, non-activating window. | Renders `FlowFrame`; hosts the `FlowEngine` (see 2.2) | transparent `BrowserWindow` (`focusable:false`, `setIgnoreMouseEvents`), `syncCursorState` |
| **Main window** | Normal WinUI 3 window, custom title bar (drag region + own window controls), 1180×760 default, min 980×640, hidden until ready | Settings + pages | normal `BrowserWindow` (`titleBarStyle` custom) |
| **Tray popover** | Windows notification-area (`NotifyIcon`) + a frameless always-on-top popover window positioned near the tray, hide-on-deactivate | quick dictate / history / settings | `Tray` + frameless `BrowserWindow` |

Resident-agent model: the process has **no taskbar button for the orb** (tool-window
style) and stays alive with all windows hidden; closing the main window does not terminate
the process (the orb + tray stay resident).

**Orb is display-only through M3.** The overlay must never take keyboard focus
(`WS_EX_NOACTIVATE` is load-bearing — it is the contract that keeps host keyboard focus so
dictated text lands in the target app). This conflicts with an editable in-orb transcript
box needing keyboard focus. Therefore the MVP orb renders transcripts but accepts no typed
input; in-box editing is a scoped M4 feature that briefly makes the window activatable on an
explicit gesture, accepts that the target loses foreground during editing, and does **not**
attempt `SetForegroundWindow` restoration afterward (paste resolves the frontmost app fresh
at the next take). (Resolves the focus contradiction, R13.)

### 2.2 Where the engine lives, and why

The C# `FlowEngine` runs **in-process on the UI/render thread** that drives the orb. It is a
per-frame consumer of `FlowFrame` at up to 120 fps and must render "in the same pass as the
input edge" (the `nudge()` latency contract). Because there is no process boundary, there is
no per-frame IPC cost at all — a strict improvement over the Electron reference, which paid
`webContents.send` per frame. The orchestrator feeds the engine three normalized streams —
`DictationEvent`s from the STT client, hotkey gesture edges, frontmost-app changes — and
receives back `SetInteractive(rect)` and PCM frames.

### 2.3 Data path

```
[key down]  HotkeyService (WH_KEYBOARD_LL on a dedicated native thread w/ its own message pump)
              ──C# event──▶ DictationOrchestrator ──▶ FlowEngine.FnDown()
AudioCapture (WASAPI, dedicated thread) ── Int16 100ms frames ──▶ orchestrator
KiviServiceClient (ClientWebSocket) ── binary frame ──▶ ws://127.0.0.1:8788/v1/dictate/stream
service ── interim/final ──▶ orchestrator ──▶ FlowEngine.EnqueueServiceEvent (generation-tagged)
[key up]  ──▶ FlowEngine.FnUp() → drain audio queue → send end_of_speech (AFTER queue drains)
final ──▶ PasteService (clipboard + synth Ctrl+V into frontmost captured at key-down)
```

- **STT socket lives in-process from M0** using `System.Net.WebSockets.ClientWebSocket`. Only a native socket can set `Authorization` + `X-Client-*` upgrade headers and read the HTTP upgrade status (401/403 vs network drop) — a WebView `WebSocket` can do neither, which is *why the socket must not live in a WebView*. (This is the .NET analog of "the socket must live in Electron main.")
- **PCM path** is a plain in-process `Channel<T>` / bounded queue of `byte[]` frames from the WASAPI capture thread to the socket pump — zero serialization, ordering-preserved (replaces Electron's `MessageChannelMain` transferables).
- **Control/events**: direct method calls (`async`/`await`) + C# events / `IObservable` (replaces `ipcMain.handle` / `webContents.send`). Every seam is an interface behind DI (T4).

### 2.4 State management

- **View layer:** the `FlowFrame` value type *is* the orb render state — never duplicated in a store. MVVM view-models hold only app-shell state (route, settings, auth, persona cache). The streaming transcript node is isolated (its own view-model / observable) so 60 fps `FlowFrame` updates never re-layout pages — mirroring the reference discipline.
- **App:** an in-memory `Session` per take (generation-tagged, mirroring `takeGeneration`); a DPAPI-backed token cache; a JSON `StyleCatalog` mirror reusing the exact key names (`kiviStyles.appAssignments`, `flowPage`, `flowOrbStyle`, …) for cache-first paint on the dictation hot path.
- **Persistence:** a settings/history-lite store (JSON under `%APPDATA%\Kivi`); **SQLite** (`Microsoft.Data.Sqlite`) for full History parity (the SwiftData/`better-sqlite3` replacement), tenant/user scoped.

### 2.5 View + build stack (decided)

| Choice | Version | Reason |
|---|---|---|
| **.NET** | .NET 8 (LTS) | Current LTS; `ClientWebSocket`, WASAPI interop, DPAPI, `System.Text.Json` all first-class. |
| **UI framework** | **WinUI 3 / Windows App SDK** | Modern XAML, Composition, DPI-correct, matches "our default" for a native Windows app. (The orb overlay itself is a **native layered window**, not a WinUI surface — see §2.1 and the `orb-is-a-chip` memo.) |
| **MVVM** | `CommunityToolkit.Mvvm` | Source-generated observable properties/commands; the pure structs port cleanly. |
| **Orb / canvas drawing** | **Win2D (`Microsoft.Graphics.Canvas`)** + Composition | Port the Canvas *algorithm* (kiwi mark, orb surface layers, record flight) — the math is self-contained; not the Canvas API calls. |
| **STT socket** | `System.Net.WebSockets.ClientWebSocket` | Sets `Authorization` + `X-Client-*` upgrade headers, exposes upgrade status. |
| **REST** | `System.Net.Http.HttpClient` | The REST surface (edit, personas, telemetry). |
| **Audio** | **WASAPI** via `NAudio` (or thin CsWin32 interop) | Capture → resample to 16 k Int16 mono (continuous resampler state). |
| **Secrets** | **DPAPI** (`System.Security.Cryptography.ProtectedData`) or Windows Credential Manager | Token/JWT + per-install AES key. |
| **DI** | `Microsoft.Extensions.DependencyInjection` + `.Hosting` | Interface-based services; composition root in `Kivi.App` (T4). |
| **Persistence** | `Microsoft.Data.Sqlite` + `System.Text.Json` | History store + settings/style cache. |
| **Tokens** | generated C# `DesignTokens` + XAML `ResourceDictionary` | Values from the reference `packages/design-tokens`. |
| **Tests** | **xUnit** + **WinAppDriver**/Appium (UI) + an image-diff lib | unit / golden-frame / e2e / screenshot-diff. |
| **Installer / update** | see `RELEASE.md` (MSIX or WiX/MSI + a Windows updater) | Packaging + auto-update. |

**Not needed / dropped from the Electron template's dependency list:** all `@ai-sdk/*`,
sherpa/onnx/whisper, tiptap, i18next, diarization (we have `kivi-service`); `ffmpeg-static`
(we stream raw PCM); `electron-vite`/`electron-builder`/`electron-updater` (replaced by the
.NET build/publish + installer); `ws`, `safeStorage`, `getUserMedia`/AudioWorklet,
`uiohook-napi` (replaced by their .NET/Win32 equivalents above).

---

## 3. macOS/Electron-PRIMITIVE → WINDOWS/.NET MAPPING

Everything below sits behind a `Kivi.Platform` interface implemented once for Windows. Pure
logic (classifier, buffers, planner, budgets, engine, tokens) is shared C# in `Kivi.Core`,
never platform-specific. (The full macOS→Windows/.NET capability table lives in
`docs/maps/platform-coupling-audit.md`; this is the decision summary.)

| # | Reference primitive (macOS / Electron) | Windows/.NET decision |
|---|---|---|
| 1 | **Global hold-to-talk** — `CGEventTap` fn=63/Globe=179 (mac) / `uiohook-napi` (Electron) | `SetWindowsHookEx(WH_KEYBOARD_LL)` **on a dedicated native thread with its own message pump** (a busy thread makes the OS silently drop the hook, R5). Mask covers key-down + key-up (push-to-talk release). **Default = Right-Ctrl hold** (rebindable; NOT RightAlt=AltGr, NOT Ctrl-double-tap=paste-chord collision, R8). There is no `fn` key off Apple hardware. Port the pure `GestureClassifier` (420/450/600 ms) verbatim. |
| 2 | **Frontmost app** — `NSWorkspace` bundle-id / `get-windows` | `GetForegroundWindow` + `GetWindowThreadProcessId` + `QueryFullProcessImageName` (exe path → app key). Capture at **key-down**; memo last non-Kivi app. |
| 3 | **Paste into app** — clipboard + synth ⌘V / SendInput Ctrl+V | Clipboard + synth **Ctrl+V** via `SendInput`; **release held modifiers first** (PTT means Ctrl may be down); detect terminal → Ctrl+Shift+V (best-effort + manual override); **paste without re-foregrounding** (orb non-activating so target never lost focus — avoids restricted `SetForegroundWindow`, R6). Restore clipboard after confirmed paste. Newline = literal line break, **never** synth Return. Port `PasteBoundaryPlanner` + `DictationJoinRewritePlanner`. |
| 4 | **AX range replace (edit mode)** | UI Automation `ValuePattern`/`TextPattern` — **deferred to M9.** v1 uses Ctrl+A select-all + paste-whole-field only (documented parity gap: corrupts multi-field/partial-selection edits). |
| 5 | **Secure-field gate** — `IsSecureEventInputEnabled` | Best-effort password-field detect via UIA; no clipboard write / no paste in secure fields → keep text in orb with a copy affordance. |
| 6 | **Transparent click-through overlay** — `NSPanel` / transparent `BrowserWindow` | **Native layered / Composition window**, always-on-top, `WS_EX_NOACTIVATE` (true non-activation), click-through toggled by publishing the interactive-region rect on geometry change + hit-testing `GetCursorPos` against it. (Orb display-only through M3.) |
| 7 | **Mic + AEC** — AUHAL/VPIO / `getUserMedia` WebRTC APM | **WASAPI** capture → resample to 16 k Int16 mono (continuous resampler state). Enable the WASAPI voice-communication capture category (OS AEC/NS where the device supports it). Full system-audio AEC has no built-in analog — ship without it for MVP (documented gap, R2). |
| 8 | **Secrets** — Keychain / `safeStorage` | **DPAPI** (`ProtectedData`) or Windows Credential Manager. The mint flow (`POST auth.sarvam.ai/api/v2/auth/jwt`, `X-Session-Token`, `{token,expires_at}`) is pure HTTP and ports unchanged. |
| 9 | **Screen/AX context enrichment** | Windows **UI Automation** — **deferred to M9** (all such wire fields are optional; server degrades). Preserve secure-field redaction when built. |
| 10 | **Tray / menu-bar** — `NSStatusItem`+`NSPopover` / `Tray` | Windows notification-area icon + a frameless always-on-top popover window. Pre-render discrete per-state icon frames (state-tinted breathing pill). |
| 11 | **STT socket** — Node `ws` with upgrade headers | `System.Net.WebSockets.ClientWebSocket` (sets `Authorization` + `X-Client-*` upgrade headers; a WebView socket can't — this is why it must not live in a WebView). |
| 12 | **OAuth callback** — `kivi://` scheme / Electron `second-instance` argv | Loopback `http://127.0.0.1:<port>/callback` (`HttpListener`) — handles Kratos `?code=` and Supabase `#fragment` uniformly; robust vs custom-scheme delivery. |
| 13 | **Launch-at-login** — `SMAppService` / `setLoginItemSettings` | Registry `Run` key (`HKCU\...\Run`) or a Startup shortcut. |
| 14 | **Auto-update** — Sparkle / electron-updater | A Windows updater (MSIX auto-update or Squirrel/Velopack-style) — decided in M8 / `RELEASE.md`. |
| 15 | **Electron IPC / preload / contextBridge** | In-process C# calls, `async`/`await`, events (T2). **No IPC bus.** |
| 16 | **React component / JSX** | XAML + MVVM view (T3). |
| 17 | **Canvas 2D drawing** | Win2D / `Microsoft.Graphics.Canvas` or Composition — port the drawing **algorithm**, not the API calls. |
| 18 | **Fonts: Matter, Matter Mono, Season Mix** | **License-blocked (proprietary, uncleared)** — dev-only for parity; ship the documented fallback stacks. **Space Grotesk** (OFL) is shippable. |

**Anything marked DEFERRED or a v1 non-goal in `FEATURE-PARITY.md` / this doc §1 stays
deferred** — don't build it, don't fake it; the server degrades gracefully without it.

> **Not applicable — Windows-only.** Every Linux/Wayland/X11 row from the Electron plan
> (XTest, ydotool, uinput, AT-SPI, portals, `.desktop`, XWayland fallback, compositor
> detection, the `org.freedesktop.portal.GlobalShortcuts` toggle tier) is removed. There is
> no Linux target and no Wayland-degraded mode; the two "hard Wayland blockers" the Electron
> plan carried simply do not exist here.

---

## 4. SOLUTION & PROJECT LAYOUT

**Decision: one solution, one Windows target, platform code behind interfaces — not
per-platform branches.** The Electron plan's "branch/replica per OS" scheme is irrelevant
here (there is one OS). The mapping in the header defines the projects; the tree:

```
Kivi.sln
├─ Kivi.Core/                         # pure, UI/OS-free, headless-testable
│  ├─ Orb/          FlowEngine, FlowFrame, Transcript, CueBus, CueCatalog, SpeechPace,
│  │                GestureClassifier, phase/mark maps, constants (port of packages/orb-core)
│  ├─ KiwiMark/     KiwiData 120×162 mask, state color tables, 48×8 gait cache
│  ├─ DesignTokens/ generated token values (from packages/design-tokens)
│  ├─ Planner/      PasteBoundaryPlanner, DictationInsertionPlanner, DictationJoinRewritePlanner
│  ├─ Wire/         KiviServiceClient (ClientWebSocket), WireModels, Endpoints, DictationBudgets
│  ├─ Rest/         KiviRestClient (HttpClient)
│  └─ Contracts/    IHotkeyService, IPasteService, IDictationService, DTOs (the in-process seams)
├─ Kivi.Platform/                     # Windows-native seams (implement Kivi.Core.Contracts)
│  ├─ Hotkey/       LowLevelKeyboardHookService (WH_KEYBOARD_LL, dedicated thread + pump)
│  ├─ Paste/        SendInputPasteService (Ctrl+V, modifier-release, terminal detect)
│  ├─ Frontmost/    ForegroundAppResolver (GetForegroundWindow + exe path)
│  ├─ Overlay/      LayeredOrb (UpdateLayeredWindow / Composition), click-through toggle
│  ├─ Audio/        WasapiCapture → 16k Int16 mono resampler
│  ├─ Secrets/      DpapiSecretStore
│  ├─ Tray/         NotifyIcon tray + popover host
│  └─ Auth/         loopback OAuth listener, JWT mint
├─ Kivi.App/                          # WinUI host + composition root + views
│  ├─ App.xaml(.cs)  DI container, app lifetime, window orchestration
│  ├─ DictationOrchestrator.cs        hotkey→connect→capture→final→paste (was OrbHost)
│  ├─ Views/        XAML pages (Record/History/Personas/Settings/…), orb host
│  ├─ ViewModels/   MVVM (CommunityToolkit)
│  ├─ Drawing/      Win2D ports (KiwiMark draw, orb surface, record flight, wedge box)
│  └─ Themes/       XAML ResourceDictionaries generated from DesignTokens
└─ Kivi.Core.Tests/                   # xUnit: golden-frame oracle, wire parity, classifier, planner
   └─ (consumes _reference/.../test/golden-frames/*.json)
```

**The platform seam** confines all OS divergence to `Kivi.Platform`, behind interfaces in
`Kivi.Core/Contracts` (the DI/tripwire T1+T4 boundary):

```csharp
public interface IHotkeyService  { event Action<GestureEdge> Edge; void Start(); void Consume(bool on); }
public interface IPasteService   { Task<PasteOutcome> InsertAsync(string text, PasteMeta meta); }
public interface IOverlayHost    { void ApplyNonActivating(); void SetClickThrough(bool ct); }
public interface IFrontmostApp   { AppContext? Current { get; } }
public interface IAudioCapture   { event Action<byte[]> Frame; void Start(); Task StopAsync(); }
public interface ISecretStore    { string? Read(string key); void Write(string key, string value); }
public interface ITrayHost       { /* icon state, popover, menu */ }
```

---

## 5. MILESTONE ROADMAP (M0…M9)

Each milestone names its exit tests and parity method. Estimates are ranges. "Track B" runs
in parallel and is cheap-to-de-risk; it gates *later* visual milestones, not M0.

### M0 — Foundations + Trimmed Transcription MVP (~4–6 days) — "the tangible thing this week"

**Goal:** hold hotkey → speak into Notepad → release → formatted text pasted, on Windows,
through the real local `kivi-service`.

**Track A (critical path):**
- **Environment (hard prerequisite):** the local `kivi-service` needs **Postgres 16** or it `std::process::exit(78)`; `LOAD_TEST_MODE=synthetic` bypasses Sarvam/Gemini **but not Postgres**. Run per `docs/maps/backend-service-api.md §7`; verify `curl -s localhost:8788/health`.
- **Headless wire spike** (a small console app, hours): `ClientWebSocket` → await `ack` → send `context` (exact MVP payload: `{"type":"context","transcription_mode":"codemix","formatting_enabled":true,"session_id":...,"auto_persona_resolution":true,"client_capabilities":{"spoken_shortcuts_v1":true},"supports_formatting_progress":true}`) → stream a fixture WAV as 3200-byte frames → `{"type":"end_of_speech"}` → print `final.formatted_text`. **This produces the golden-transcript baseline.**
- **.NET scaffold**: `Kivi.sln` with `Kivi.Core` / `Kivi.Platform` / `Kivi.App` / `Kivi.Core.Tests`; a display-only stub orb (native layered window, ~60 px indicator) + hidden main window.
- **`KiviServiceClient`** (`Kivi.Core.Wire`, `ClientWebSocket`): handshake (ack ≤4 s) → context → binary PCM → **drain-before-EOS** → final; budgets (ack 4 s, final 20 s, ping 20 s, pong-miss 2 gated on ever-ponged, maxPending 50). **Wire-trap guards baked in:** always emit `formatting_enabled` (server default false); allowlist-guard the closed `general_app_style_preset` enum (`verbatim|casual|transliteration|formal`); never funnel a base_preset slug there.
- **Audio:** WASAPI capture → resample to **16 kHz Int16 mono LE**, accumulate to 1600-sample (100 ms = 3200-byte) frames → in-process `Channel` to the socket pump. **Keep resampler state continuous across frames** (the `.noDataNow` continuity rule) — validated by the golden-transcript test on real audio (R10).
- **Native hotkey:** `WH_KEYBOARD_LL` on a dedicated thread with its own message pump → pure `GestureClassifier` → key-down starts, key-up (≥420 ms hold) stops. Default **Right-Ctrl hold**, rebindable.
- **Paste:** clipboard write → 30–50 ms settle → synth Ctrl+V (`SendInput`) into frontmost captured at key-down → restore clipboard after confirmed paste. Secure-field gate. Port planners.

**Track B (parallel, non-blocking; gates M2–M3):**
- **Font license go/no-go** (day one): confirm Matter + Season Mix redistribution rights for a shipped .NET installer; map every reference woff2 → family/weight; verify Season Mix present. Define the fallback now (metrics-compatible substitute + documented font-region tolerance) so a "no" degrades the parity claim instead of killing the project (R11). **Space Grotesk (OFL) is shippable.**
- **Token pipeline** (`Kivi.Core.DesignTokens`): generate C# token values + XAML theme dictionaries from the reference `packages/design-tokens`, **encoding the Canon-over-KDS dark override** (dark canvas `#0C0E0C`, accent `#8FCE6E`, warm-tint kept `#404948`; text/borders from KDS.dark) and the two-cream split (`#F1F4EC` legacy vs `#F6F3EA` Canon). Validated by porting the reference token-parity tests to xUnit (R7).
- **Golden-frame oracle:** consume `_reference/.../test/golden-frames/*.json` (already exported by the reference at its pinned SHA) — no re-export needed. Wire the xUnit harness that will gate the M2 engine port.
- **Baseline visual oracle:** the orb baseline is the reference "maxi mini-app" documented in `docs/maps/orb-visual-and-box.md`. Capture the full named-state set from the running **Electron app** (`rest/listening/processing/done/edit × forest/mist × light/dark`) with reduce-motion + reduce-transparency and a fixed capture contract (pinned `now`, breath phase, glow 12-step bucket, cursor-driven light target). Pin the reference commit.
- **Alpha-compositing + calibration spike:** render one transparent orb pose with glow/gloss/backdrop/text via the layered window, composite both baseline and candidate over an identical background, measure achievable per-region deltas → set budgets from measurement (never assert 0.5 % before a render proves it reachable).

**Exit tests:** manual dictate→paste into Notepad/WordPad; e2e (feed fixture PCM → assert
paste fired with expected text, OS paste mocked); xUnit (classifier, pre-connect-buffer
drain-then-flip, budgets, planner spacing, wire encode/decode with closed-enum guard).
**Parity:** .NET `final.formatted_text` == M0 golden for the shared WAV (vs the Electron
client against the same local service).

### M1 — Platform seam hardening (~1–1.5 weeks)

**Goal:** the four native seams are proven robust on real Windows hardware, not just a happy path.
**Deliverables:**
- Harden `WH_KEYBOARD_LL` (dedicated thread + pump; reinstall if the OS drops the hook; never on the UI thread, R9).
- `SendInput` paste: modifier-release-first, terminal detection → Ctrl+Shift+V, paste-without-re-foregrounding (R10), clipboard snapshot/restore with an in-process "we-just-wrote-this" guard (no `changeCount` on Windows clipboard).
- `WS_EX_NOACTIVATE` overlay proven non-activating (type into a target while the orb is visible → keystrokes land in the target).
- Frontmost resolver (`GetForegroundWindow` + `QueryFullProcessImageName`) + last-non-Kivi memo; normalize to the agreed Windows app-key convention.
- **App-identity convention** raised with backend as a cross-team dependency (exe path / AppUserModelID → `app_context.bundle_id`); lands with personas in M6.
- **EV code-sign the hook/inject binaries early** to accrue SmartScreen reputation (a keylogger-shaped low-level hook trips AV/SmartScreen, R11).

**Exit tests:** OS-level integration harness (§6.5) — dictate→paste into Notepad, assert
inserted text + clipboard restored + host focus retained + overlay click-through. Hotkey
exit test = classifier-correctness on synthetic timelines. **Parity:** shared fixture→final
golden matches.

### M2 — Engine port (pure C#) + real orb behavior, display-only (~1.5 weeks)

**Goal:** the real `FlowEngine` drives states; overlay proven.
**Deliverables:** port `packages/orb-core` (`FlowEngine` + `FlowFrame` + `Transcript` +
`CueBus`/`CueCatalog` + `SpeechPace`) to `Kivi.Core.Orb` (injected time, zero UI). A render
runtime: one rendering clock with dt-correction `ease60(k)=1−(1−k)^(dt/16)`, a 3-tier fps
band (24/30/60), 0-fps rest-park + 1 Hz heartbeat, `Nudge()`. Generation-tagged intake
drained at frame top. **Route the very first dictate→paste through `CommitDictationToHost`
from the start** (engine ships with the loop wiring, not after). Earcons via a lightweight
audio player (mid-recording gate, 0.25 s refractory, deferred `.start`); **drop haptics**
(no desktop analog).
**Exit tests:** the **numeric golden-frame gate** — C# engine reproduces the reference
`FlowFrame` fields per the tolerance policy (§6.1) across the exported timelines; ported
transition tests. **Parity:** numeric-then-behavioral.

### M3 — Orb visual clone + screenshot-diff gate (~1.5–2 weeks)

**Goal:** side-by-side pixel parity of orb + box + satellites + mark vs the Electron app.
**Deliverables:** consume `Kivi.Core.DesignTokens` (XAML themes). `Kivi.Core.KiwiMark` +
`Kivi.App/Drawing` KiwiMark port (`KiwiData` 120×162 mask, dark/light state color tables,
48×8 gait cache, `SpeechPace` walk) with a **coverage-readback numeric snapshot (dot-count
per bucket)** in addition to pixels. Orb surface layers (fill+alpha, paper-grain LCG tile
seed `0x4B49564950415045` byte-verified, 4-layer glow, sphere gloss radial, backdrop). Wedge
box, geometry morph (pill 39×15 ⇄ orb 61×61 ⇄ mini 42.7 ⇄ pill-take 57×18, maxi plateau
840×800 fed logical px at the display scale). Fonts loaded, no synthesized weights.
Reduce-motion / reduce-transparency wired (Windows animation + transparency settings).
**Exit tests:** per-region calibrated budgets vs the pinned Electron baselines over an
identical composite background, backdrop-desktop-blur region excluded (faked with a static
frosted approximation, R1). Coverage-readback for the mark. **Parity:** numeric (mark
coverage) + pixel (per region).

### M4 — Orb-box turn surface + in-box editing + edit mode (~2 weeks)

Header row, context card, two textbox skins, scroll hysteresis (6/18 px), thumbs, pager
dots, voice slot, wave sweep, diff morph (500 ms compressed). In-box editing with scoped
activation-gesture handling (2.1). Voice-edit + `POST /v1/edit` (read `text`, response
**camelCase**). Edit = select-all + paste (UIA range deferred). **Mock is replay of recorded
real-service event traces** captured in M0/M1 (one interim per VAD utterance, `is_final:false`
renders nothing, segment-by-index, formatting-progress budget) so locked states match reality.

### M5 — Main window shell + pages (~2–3 weeks)

WinUI main window (custom title-bar drag strip + own window controls), rail (264⇄76, Ctrl+\),
Canon canvas + PaperGrain + ConstellationField. Port hand-drawn SVG icon paths verbatim to
XAML `PathGeometry` (`RailIcon`, `HistoryGlyph`, `KiviInkArrow`, `PixelKiwi`). Pages in
priority order: Record → History (SQLite) → Settings → then
Personas/Memory/Shortcuts/Analytics/Leaderboard/SharedTerms/Clipboard. **Analytics charts =
hand-drawn XAML/`PathGeometry` or a charting lib** (Swift Charts is Apple-only).
`NumberFormatInfo` for `en-IN` grouping. Do **not** port dormant `StylesPage`/`PresetsPage` —
route `.styles`/`.presets` → `PersonasPage`. Per-page calibrated pixel budgets (light+dark),
font rasterization as an explicit allowance.

### M6 — Personalization (~1.5–2 weeks)

Port `PersonasStore`/`StylesViewModel`/`StyleResolver`/`StylesBackend`/`PersonasStyleCatalog`
verbatim (content-parity contract). Wire the full REST surface (`v1/personas*`,
`v1/format-preferences`, cosmetic-styles, app-overrides, compile/preview debounce 500 ms).
Local `StyleCatalog` JSON cache (exact key names) for cache-first paint. **App-identity
convention (raised in M1) lands here.** Create-voice picker uses
`appsAssignedOutsideSelectedScope` (the fixed rule). App-icon resolution via Win32
(`SHGetFileInfo` / PE resources) keyed by exe path; re-map the `PersonaSeedRegistry` table to
Windows app identifiers.

### M7 — Auth + onboarding + tray (~1.5 weeks)

**Loopback OAuth** (`http://127.0.0.1:<port>/callback`, `HttpListener`) for Kratos `?code=` +
Supabase `#fragment`; `X-Session-Token`→15-min JWT mint; whoami arbiter (401-only kills
session); DPAPI token store. Onboarding (permissions → tour → personalization → handoff —
tour/personalization are pure UI ports; the Windows permission model is simpler than macOS —
no Accessibility trust gate). Tray popover with discrete state frames.

### M8 — Packaging, signing, auto-update (~1.5 weeks)

.NET publish → **MSIX (or WiX/MSI)** installer; **EV code-signing** (hook/inject binaries
signed early for SmartScreen reputation); the Windows updater (MSIX auto-update or a
Squirrel/Velopack-style feed) — see `RELEASE.md`. Launch-at-login (registry `Run` key /
Startup shortcut). Windows `.ico` generated from the one image asset.

### M9 — Deferred hard tier

UI-Automation range-level edit (`ValuePattern`/`TextPattern`), screen-context enrichment
(UIA `screen_nodes`/`focused_field`, secure-field redaction), system-audio AEC
(WASAPI-loopback → WebRTC-style APM), rich-format clipboard paste. All optional; server
degrades.

---

## 6. TESTING & PARITY STRATEGY

### 6.1 Numeric engine parity (per-field tolerance policy — NOT flat 1e-4)
Swift/JS↔.NET `Math.Pow`/`Math.Sin` are not bit-identical and per-frame lerps accumulate, so
a flat 1e-4 over a long eased trace flaps for numerics, not logic. Policy: **exact** on
discrete/enum/quantized fields (phase, `markState`, `glowColor` rounded to int RGB, breath
quantized to 12 steps); **drift-budget bound** on continuous eased scalars, derived from
segment length; compare **short scripted segments with periodic re-seeding** from the
golden's intermediate state to cap compounding. Test at simulated 24/30/60/120 Hz; state
explicitly whether goldens are motion-ON (the real test) or motion-OFF. The oracle is the
reference's already-exported `_reference/.../test/golden-frames/*.json` (the reference reports
100 % discrete-exact, eased floats within budget, max Δ 5.0e-7 = the oracle's 6-dp rounding).

### 6.3 Service-output parity (same local backend)
Fixed WAV set → the .NET `KiviServiceClient` and the Electron client both → `ws://127.0.0.1:8788`
→ assert identical `final.formatted_text`/`raw_transcript`. `/v1/edit`: identical `text`
(camelCase). `LOAD_TEST_MODE=synthetic` deterministic in CI; nightly real-STT with
`SARVAM_API_KEY`. Asserts wire invariants: 3200-byte frames, drain-before-EOS ordering,
ack-timeout, `formatting_enabled` present, no illegal preset enum.

### 6.4 Visual parity (screenshot diff)
Baseline = pinned captures from the running **Electron app** at the documented states
(reduce-motion+reduce-transparency, fixed capture contract). Candidate = the .NET orb driven
to the exact `now`, matched viewport + DPI, **composited over the identical background** as
the baseline (alpha contract). Image-diff with **per-region masks**: tight budget on hard
geometry/color, measured looser budgets on glow/gloss/grain/backdrop/text;
desktop-behind-window blur region **excluded** (R1). Emit a diff image per state.

### 6.5 OS-level integration harness (the riskiest code needs a real gate)
UI automation drives the view, not the OS input layer. A scripted harness spawns a
third-party target (Notepad/WordPad), injects the hotkey through the **native hook**, feeds
fixture audio, and asserts: target field contains the expected text, clipboard restored, host
focus retained (type into target while orb visible → keystrokes land in target). Click-through
tested as a pure hit-test function + a view-level interactive-region test.

### 6.6 CI matrix
Windows runners: `build` + `xunit` + numeric golden-frame gate + UI e2e + visual-diff +
integration harness on every PR; nightly real-STT parity. (No macos/ubuntu runners — this is
a Windows-only repo.)

---

## 7. RISK REGISTER (every blocker/high, with the baked-in mitigation)

Renumbered and translated for the Windows/.NET target; all Linux/Wayland risks from the
Electron plan are **removed** (no such target).

| ID | Sev | Risk | Mitigation now in the plan |
|---|---|---|---|
| R1 | BLOCKER | Desktop-behind-window blur (orb's glass backdrop) cannot be reproduced — voids any pixel gate that includes it. | Exclude the backdrop-desktop region from the pixel gate (M0 calibration); fake with a static frosted gradient/blurred-screenshot approximation. Decided before budgets are set. |
| R2 | high | "OS AEC = free system-audio parity" oversold — WASAPI voice-comm processing only cleans the mic path, not other apps' output. | Ship the mic-path AEC for the dictate-into-text MVP (no concurrent audio); explicitly **not** called system-audio parity; system-audio AEC (WASAPI-loopback → APM) descoped to M9. |
| R3 | BLOCKER | (Electron: Wayland global-shortcut blocker.) | **Removed — not applicable.** Windows `WH_KEYBOARD_LL` gives hold-to-talk with key-up unconditionally; there is no Wayland tier. |
| R4 | high | Anchoring visual work on a stale prototype design. | The **current Electron app + `packages/orb-core` + `packages/design-tokens`** are the sole source of truth; baselines captured from the running Electron app; parity constants transferred byte-exact. |
| R5 | high | `WH_KEYBOARD_LL` on a busy thread gets silently dropped/unhooked. | Hook on a **dedicated native thread with its own message pump**; reinstall on drop; never on the UI thread. |
| R6 | high | Synthetic-paste focus/target under-sequenced; `SetForegroundWindow` restricted. | Capture frontmost at key-down; on release release modifiers, paste **without re-foregrounding** (orb non-activating); restore clipboard after confirmed paste; terminal detect best-effort + manual override. |
| R7 | high | Numeric tolerance un-passable (fp non-determinism). | Per-field tolerance policy §6.1 (exact on discrete/quantized, drift-budget on eased, re-seeded short segments). |
| R8 | med | Default hotkey collision (RightAlt=AltGr / Ctrl-double-tap=paste chord). | Default **Right-Ctrl hold**, rebindable UI from day one; consume optional. |
| R9 | high | (folded into R5.) | See R5. |
| R10 | high | 16 kHz resample resets per-frame → the "one-frame-then-dead" bug; first words lost under load. | Continuous resampler state across frames (`.noDataNow` continuity); press-time early capture (`earlyPrefix`); validate the format via the golden-transcript test on real audio. |
| R11 | high | Native hook/inject binaries trip keylogger AV/SmartScreen; signing/distribute unbudgeted. | First-class M1/M8 signing: **EV-sign the hook/inject binaries early** for SmartScreen reputation; MSIX/MSI packaging in M8. |
| R12 | high | Font licensing (Matter + Season Mix proprietary) blocks any pixel gate. | Day-one legal go/no-go; fallback defined now (metrics-compatible substitute + documented font-region tolerance); acceptance criterion decoupled. Space Grotesk (OFL) shippable. |
| R13 | high | Non-activating overlay contradicts an editable in-orb box. | MVP orb display-only; in-box editing scoped to M4 with an explicit activation gesture + no foreground-restore. |
| R14 | high | Transparent layered-window alpha capture mismatches an opaque-composited candidate. | Alpha-compositing contract: composite both sides over an identical background before diff; verify layered-window alpha capture in M0. |
| R15 | high | Dark theme is a Canon-over-KDS override, not a dimmed palette (a naive port renders wrong dark surfaces). | Token generator encodes the Canon-over-KDS dark override + two-cream split; validated by ported token-parity tests. |
| R16 | med | Golden-frame oracle is a moving target. | Consume the reference's already-exported goldens at a pinned reference commit; re-baseline deliberately on bumps. |
| R17 | high | Wire-on-loopback assumptions; upgrade-status handling. | `ClientWebSocket` reads the HTTP upgrade status (401/403 vs drop); local loopback is anonymous (`DICTATE_AUTH_MODE=none`); LAN-anonymous only holds for 127.0.0.1/localhost/::1. |
| R18 | med | Branch-per-platform anti-pattern. | **Removed — not applicable.** One solution, one Windows target, seams behind interfaces. |
| R19 | med | "Fantasy bucket" that collapses weeks into one milestone. | Exploded into M5–M8 (pages / personas / auth+onboarding+tray / packaging) with per-milestone estimates. |
| R20 | med | Non-activating window not truly non-activating on Windows. | `WS_EX_NOACTIVATE` on a native layered window (not WinUI); integration harness asserts host focus retention. |
| R21 | med | Postgres treated as optional. | Hard M0 prerequisite; `LOAD_TEST_MODE=synthetic` bypasses Sarvam/Gemini only, not the DB (service exits 78). |
| R22 | med | Clipboard has no `changeCount`/transient UTI → history poller ingests our own writes; multi-type restore. | In-process "we-just-wrote-this" guard keyed on the exact string; multi-type restore best-effort; rich custom types degrade to plain text (M9). |
| R23 | med | App-identity convention unsequenced cross-team dependency. | Raised with backend in M1; lands in M6 with personas; let it be null where unresolvable (server tolerates). |
| R24 | med | Mock-service-first perfects states against fiction. | Mock = replay of recorded real-service traces captured in M0/M1. |
| R25 | med | KiwiMark under-scoped. | Its own `Kivi.Core.KiwiMark` module (M3) with a coverage-readback dot-count numeric gate + pixel diff. |
| R26 | low | Display-scale / DPI: the 128 px grain tile and 14″ reference (1512×982 logical) are logical points — feed logical px, not device px, or the maxi curve mis-scales on HiDPI. | Feed logical/DIP values to geometry; serve the grain tile DPI-aware. |

---

## 8. IMMEDIATE NEXT ACTIONS (execute now)

1. **Stand up the local backend.** Postgres 16 + `.env.local` (`DATABASE_URL`, `SARVAM_API_KEY`, `DICTATE_AUTH_MODE=none`, `PORT=8788`); run `kivi-service`; confirm `curl -s localhost:8788/health` → `{"status":"ok",...}`. (Full steps: `docs/maps/backend-service-api.md §7`.)
2. **Write the headless wire spike** (a console app): `ClientWebSocket` → `ack` → the exact MVP `context` → stream a 16 k mono WAV fixture as 3200-byte frames → `end_of_speech` → print `final.formatted_text`. Save its output as the **golden-transcript baseline**.
3. **Kick off the font license go/no-go** (parallel): request Matter + Season Mix redistribution rights for a .NET installer; inventory the reference `fonts/*.woff2` → family/weight; confirm Season Mix present. Space Grotesk (OFL) is already shippable.
4. **Create the solution**: `Kivi.sln` with `Kivi.Core` / `Kivi.Platform` / `Kivi.App` / `Kivi.Core.Tests` per §4; a display-only native layered orb + hidden main window launch.
5. **Build `KiviServiceClient`** (`Kivi.Core.Wire`, `ClientWebSocket`) with the wire-trap guards (explicit `formatting_enabled`, closed-enum allowlist, drain-before-EOS) and `DictationBudgets` constants ported.
6. **Wire WASAPI audio** — capture → 16 k Int16 mono LE resampler (continuous state) → 100 ms frames → in-process `Channel` → binary WS frames.
7. **Add the hotkey + paste** — `WH_KEYBOARD_LL` on a dedicated thread → pure `GestureClassifier` → key-down/up edges; clipboard-write + synth Ctrl+V (`SendInput`) into the key-down-captured frontmost app + clipboard restore + secure-field gate. EV-sign the hook/inject binaries early.
8. **Close the M0 loop:** hold Right-Ctrl → speak into Notepad → release → pasted text. Assert `final.formatted_text` equals the M0 golden for the shared WAV (the .NET client and the Electron client against the same local service).
9. **Pin the visual oracle** (parallel): capture the full named-state baseline from the running Electron app with the fixed capture contract, and run the one-pose alpha-compositing + budget-calibration spike.
10. **Wire the golden-frame gate** (parallel): consume `_reference/.../test/golden-frames/*.json` in `Kivi.Core.Tests`, ready to gate the M2 engine port.
