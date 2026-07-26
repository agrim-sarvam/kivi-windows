# Kivi — Electron → .NET/Windows Migration

You are working on **Kivi**, a Windows voice-dictation app: hold a hotkey → speak → formatted text lands in the app you were already typing in, without Kivi ever stealing focus, wrapped in a hand-drawn per-frame-eased "living orb." This repo is a **port of the Electron implementation of Kivi to native .NET/Windows.**

Read this entire file before doing anything. It overrides default behavior.

---

## 0. The two rules that matter most (never violate these)

**RULE 1 — Every port is a translation to Windows/.NET. Never carry a macOS/Linux/Electron-ism across.**
The reference is a cross-platform Electron app that was itself ported from a macOS Swift app. Its code and docs are full of macOS primitives (Keychain, CGEventTap, NSPanel, NSWorkspace, AX/Accessibility, `⌘`), Linux primitives (XTest, ydotool, uinput, AT-SPI, portals), and Electron/Node primitives (BrowserWindow, IPC, `ws`, preload/contextBridge, `safeStorage`, `getUserMedia`/AudioWorklet). **Whenever you encounter one, stop and find the Windows/.NET-native replacement** — see the mapping table in §4. If a capability is macOS-only with no Windows analog and the docs mark it deferred/non-goal, drop it (don't fake it). If a capability is Linux-only, ignore it — **this repo is Windows-only.**

**RULE 2 — The UI must be pixel-perfect with the Electron app, rebuilt in .NET.**
The Electron renderer (`_reference/sarvam-kivi-electron/src/renderer/`) is the **only** visual source of truth. Every color, spacing value, font, corner radius, SVG icon path, Canvas-drawn shape, and — critically — **every animation's exact timing, easing curve, delay, and from→to values** must be reproduced in XAML/Composition to match. A screen is not "done" until it has been **verified side-by-side against the running Electron app.** The UI is almost entirely code-drawn (there is essentially one image asset), so fidelity = porting token values + SVG paths + Canvas algorithms + motion specs exactly. See §5.

---

## 1. Source of truth & repo layout

- **`_reference/sarvam-kivi-electron/`** — the Electron implementation. **This is the source of truth. NEVER modify anything under `_reference/`.** It is immutable. Read it constantly.
  - `docs/MASTER-PLAN.md` — authoritative product architecture, M0–M9 roadmap, 28-item risk register.
  - `docs/FEATURE-PARITY.md` — 131-feature matrix; the **P0 shortlist (15 features) is the MVP bar.**
  - `docs/GOAL.md`, `docs/PROGRESS.md` — mission + what the Electron app already has built.
  - `docs/maps/*.md` — 12 precise architecture/behavior maps (wire, audio, engine, tokens, orb-visual, main-window, personalization, auth/tray, platform-coupling, packaging, openwhispr). **Every parity constant lives here.**
  - `packages/orb-core/` — the pure engine (`FlowEngine`, `FlowFrame`, transcript, cue, speech-pace, constants) — ~3400 LOC of pure logic, verified at 100% golden parity. **The crown jewel to port.**
  - `packages/design-tokens/` — the exact color/type/spacing/motion token values.
  - `src/main/wire/` — the STT WebSocket client + budgets.
  - `src/main/platform/` — the platform seam (only `darwin.ts` exists; **no win32 shell** — you build it).
  - `src/renderer/src/` — the React/Canvas view layer to reproduce in XAML.
  - `test/golden-frames/` — JSON `FlowFrame` oracles for verifying the ported engine.
- **`docs/`** — OUR ported, **Windows-only .NET** docs (produced by the `docs-porter` agent). macOS/Linux/Electron-isms stripped; parity constants preserved.
- **`Kivi.*`** — the target .NET solution we are building.
- **`legacy/`** (if present) — an earlier throwaway .NET attempt. **Read-only reference to understand what was tried. Do NOT lift code from it. Do NOT let it constrain the design.** Every seam is rebuilt from scratch to match the Electron/OpenWhispr patterns.

---

## 2. The porting rule: mirror Electron, diverge only on four tripwires

**Default: mirror the Electron structure closely** — namespaces, module boundaries, service names, request/response models, feature flags, config schemas, business logic, state shapes, the domain model. Someone who knows the Electron code should be able to navigate the .NET code. The detailed Electron docs pay off only if we keep the structure aligned.

**Diverge ONLY when mirroring would violate core .NET/Windows principles. The four tripwires:**

- **T1 — Platform-native seams.** Windowing, tray, global hotkey, the layered orb, audio capture, clipboard/paste injection, secrets, frontmost-app. Mirror the *feature*, not the file. (This is where RULE 1 mappings apply.)
- **T2 — Async/threading.** Electron IPC + EventEmitter → `async`/`await`, `Task`, C# events / `IObservable`. Do **not** build a fake renderer↔main IPC bus inside a single-process .NET app; it's all one process.
- **T3 — UI layer.** React components → XAML + MVVM. Mirror the *screens and view logic*, not the component tree.
- **T4 — DI / lifetime.** Use a proper .NET DI container and interface-based services. Don't replicate Electron module-singleton patterns that fight DI.

When a choice isn't covered by a tripwire, **mirror Electron.** When in doubt about whether a divergence is warranted, note it and ask rather than inventing a new architecture.

---

## 3. Parity is byte-exact for logic, pixel-exact for UI

Some values must be reproduced **exactly** — they are contracts, not preferences. Full list in `docs/parity/` (ported from the maps). The critical ones:

- **Wire:** endpoint `/v1/dictate/stream` (local `ws://127.0.0.1:8788`, anonymous on loopback). JSON `{"type":...}` snake_case both directions; audio is binary frames. `/v1/edit` REST responds **camelCase** (read `text`, not `edited`).
- **Handshake:** connect → `ack` (≤4s) → send `context` immediately → binary PCM + `{"type":"ping"}` every 20s → **drain audio queue** → `{"type":"end_of_speech"}` → await `final`.
- **The "A3 trap":** always emit `formatting_enabled` (server serde default is FALSE). `general_app_style_preset` is a CLOSED enum `verbatim|casual|transliteration|formal` — a bad value fails the whole message.
- **Audio:** 16000 Hz, Int16, mono, little-endian PCM; ~100ms = 1600 samples = 3200 bytes/frame. One frame per binary WS message. Resample on-device; **keep resampler state continuous across frames.**
- **Budgets:** ack 4000ms, ping 20000ms, pongMissLimit 2, finalTimeout 20000ms, maxPendingAudioFrames 50, JWT TTL 900s / refresh lead 180s, idle 180s, context window 30s.
- **Gestures:** holdMs 420, doubleTapMs 450, longHoldMs 600 (invariant 600>450>420).
- **Engine dt-correction:** `ease60(k)=1−pow(1−k, dtFrames)`, `dtFrames=clamp((now−prev)/16, 0..3)`. Replicate exactly or motion runs at the wrong speed on non-60Hz displays.
- **Design tokens:** every color hex (Canon light/dark + orb forest/mist), type scale, spacing, radii, motion durations/easings — verbatim from `packages/design-tokens/`.

Verify the ported engine against `_reference/.../test/golden-frames/*.json` (per-field tolerance: exact on discrete/quantized fields, drift-budget on eased scalars). Verify the wire client against the **live local `kivi-service`** with fixture WAVs.

---

## 4. macOS/Linux/Electron → Windows/.NET mapping (RULE 1 in practice)

When you meet the left column, use the right column. This is the definitive translation table; the ported `docs/maps/` expand each row.

| Reference primitive (macOS / Linux / Electron) | Windows/.NET replacement |
|---|---|
| Global hotkey: CGEventTap fn=63/Globe=179 (mac) / XGrabKey (X11) | `SetWindowsHookEx(WH_KEYBOARD_LL)` **on a dedicated native thread with its own message pump** (a busy thread makes the OS drop the hook). Default trigger is a rebindable chord (NOT fn — no fn on Windows). Port the pure `GestureClassifier` (420/450/600ms) verbatim. |
| Frontmost app: `NSWorkspace` bundle-id | `GetForegroundWindow` + `GetWindowThreadProcessId` + `QueryFullProcessImageName` (exe path → app key). Capture at key-down. |
| Paste: clipboard + synth `⌘V` + AX readback | Clipboard + synth **Ctrl+V** via `SendInput`; **release held modifiers first** (PTT means Ctrl may be down); detect terminal → Ctrl+Shift+V; **paste without re-foregrounding**; restore clipboard after confirmed paste. Newline = literal line break, never synth-submit. |
| AX range replace (edit mode) | UI Automation `ValuePattern`/`TextPattern` — **deferred** per docs; v1 uses select-all + paste-whole-field. |
| Secure-field gate: `IsSecureEventInputEnabled` | Best-effort password-field detect via UIA; no clipboard/paste in secure fields. |
| Transparent click-through overlay: `NSPanel` non-activating | Native layered / Composition window, always-on-top, `WS_EX_NOACTIVATE`, click-through toggled by publishing the interactive-region rect on geometry change + hit-testing the cursor. (Orb is display-only through the MVP.) |
| Mic + AEC: AUHAL / VPIO | **WASAPI** capture → resample to 16k Int16 mono (continuous resampler state). WebRTC-style AEC has no native VPIO analog — ship without system-audio AEC for MVP (documented gap). |
| Secrets: Keychain / libsecret / `safeStorage` | **DPAPI** (`ProtectedData`) or Windows Credential Manager. |
| Screen/AX context enrichment | Windows UI Automation — **deferred** per docs (all such wire fields are optional; server degrades). |
| Tray / menu-bar: `NSStatusItem`+`NSPopover` | Windows notification-area tray icon + a frameless always-on-top popover window. Pre-render discrete per-state icon frames. |
| STT socket: Node `ws` with upgrade headers | `System.Net.WebSockets.ClientWebSocket` (sets `Authorization` + `X-Client-*` upgrade headers; a browser socket can't — this is why it must not live in a WebView). |
| OAuth callback: `kivi://` custom scheme (mac) | Loopback `http://127.0.0.1:<port>/callback` (HttpListener/Kestrel) — handles `?code=` and `#fragment` uniformly. |
| Launch-at-login: `SMAppService` | Registry `Run` key / `setLoginItemSettings` analog. |
| Auto-update: Sparkle / electron-updater | A Windows updater (decided per milestone). |
| Electron IPC / preload / contextBridge | In-process C# calls, `async`/`await`, events (T2). No IPC bus. |
| React component / JSX | XAML + MVVM view (T3). |
| Canvas 2D (kiwi mark, orb surface, record flight) | Win2D / `Microsoft.Graphics.Canvas` or Composition — port the drawing **algorithm** (the math is self-contained), not the Canvas API calls. |
| CSS custom properties / `@font-face` | XAML resources / theme dictionaries; token values from `packages/design-tokens`. |
| Fonts: Matter, Matter Mono, Season Mix | **License-blocked (proprietary, uncleared)** — dev-only for parity; ship the documented fallback stacks. **Space Grotesk** (OFL) is shippable. |

**Anything marked DEFERRED or a v1 non-goal in `FEATURE-PARITY.md` / `MASTER-PLAN.md` §1 stays deferred** — don't build it, don't fake it; the server degrades gracefully without it.

---

## 5. The UI-fidelity gate (RULE 2 in practice)

For **every** screen/component you build:

1. **Assets:** copy the one real image asset (`build/icon.png`) and generate a Windows `.ico`. There are no other image assets — everything else is code-drawn.
2. **Tokens:** pull exact values from `packages/design-tokens/tokens.ts` / `tokens.css` — colors (Canon light/dark, orb forest/mist), type scale, spacing, radii, shadows. Remember dark theme is a **Canon-over-KDS override**, not a dimmed palette.
3. **Icons:** reproduce the inline SVG paths (`RailIcons.tsx`, orb `Icons.tsx`) as XAML `PathGeometry`, verbatim.
4. **Canvas art:** port the algorithm — `KiwiMarkEngine` (120×162 `KiwiData` mask + gait cache + per-state color tables), orb surface layers (fill/paper-grain LCG/4-layer glow/sphere gloss), wedge box, record flight scene.
5. **Motion (the hard part):** for each animation, **spec-extract** from the source — exact duration (ms), easing (the CSS `cubic-bezier` or the engine's per-frame `ease60` lerp coefficient), delay, which property animates, from→to values — then reproduce in XAML Composition/Storyboards with **matching** values. Where a CSS easing has no XAML built-in, match the curve with `KeySpline`/Composition, don't eyeball it. Honor reduce-motion / reduce-transparency.
6. **Verify side-by-side:** run the Electron app and the .NET app next to each other for that screen and confirm layout, color, and motion match. **The screen is not done until this passes.**

The orb and its box are the primary user-named visual gate. Baseline design = the "maxi mini-app" (PR #95) that the orb-visual map documents.

---

## 6. Workflow discipline

- **Read before porting.** For any feature, read the relevant ported `docs/maps/` doc AND the corresponding `_reference` source (both the TS and, where cited, the Swift it came from) before writing C#.
- **Use skills.** Follow the Superpowers process (brainstorming → writing-plans → TDD → verification-before-completion). Don't claim something works without running it.
- **Local backend for parity tests.** The wire/loop tests need the local `kivi-service` running (it requires Postgres; `LOAD_TEST_MODE=synthetic` bypasses Sarvam/Gemini but not Postgres). Point the client at `ws://127.0.0.1:8788`.
- **Milestone order** follows MASTER-PLAN M0–M9; the P0 shortlist is the MVP bar. Prove the dictation loop first.
- **Clear Kivi cache before any test handoff** (`%APPDATA%\Kivi`, full uninstall if testing install) — unasked.
- **Commit/PR only when asked.** If on the default branch, branch first.

---

## 7. Agents

Specialist agents live in `.claude/agents/`. Use them for their bucket:

- **`docs-porter`** — converts Electron docs/maps → Windows-only .NET docs under `docs/`. Runs first.
- **`core-porter`** — ports the pure-logic bucket (engine, frame, transcript, tokens, models) to C#.
- **`wire-backend`** — the STT `ClientWebSocket` client, REST, auth/JWT, budgets. Byte-exact wire parity.
- **`platform-native`** — the Windows seams rebuilt from scratch (hotkey, paste, frontmost, overlay, WASAPI, secrets, tray).
- **`ui-fidelity`** — the XAML/Composition view layer and the side-by-side pixel/motion gate.

Each agent's charter, inputs, outputs, and verification gate are in its `.md` file. Read the relevant charter before delegating.
