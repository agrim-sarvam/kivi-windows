# MAP: platform-coupling-audit

Windows-only .NET port of `_reference/sarvam-kivi-electron/docs/maps/platform-coupling-audit.md`.
**This is the definitive macOS → Windows/.NET capability mapping** — the Linux column is dropped
(Windows-only repo), and this table is consistent with `CLAUDE.md §4` and `MASTER-PLAN.md §3`. Every
constant is byte-exact. Native seams live in `Kivi.Platform.*`; pure logic in `Kivi.Core`.

---

## 0. Executive framing — what the app actually is at runtime

Kivi is a **background agent + transparent floating overlay**, not a normal windowed app. The
dictation loop:

1. A **global key gesture** (default **Right-Ctrl hold** — no `fn` on Windows; timing-classified into tap/hold/double/long-hold) starts/stops a take.
2. On press, mic capture begins (**press-time early capture**), audio is downsampled to **16 kHz Int16 mono LE PCM** and streamed as **binary WebSocket frames** to `kivi-service` at `/v1/dictate/stream`.
3. Server streams back `interim` + a `final` (raw + formatted text).
4. Final text is **synthesized into the frontmost app** at its caret (clipboard + Ctrl+V), *without Kivi ever taking keyboard focus*.
5. A transparent always-on-top **native layered window** renders the orb/transcript UI, click-through except over the editable box.

On macOS the app's power came from **TCC Accessibility + Microphone** permissions. **On Windows the
picture simplifies dramatically: there is NO Accessibility trust gate** — `SetWindowsHookEx`,
`SendInput`, and UI Automation just work. The only user-facing permission is **Microphone**. This is
the single most important porting fact.

---

## 1. THE CRUX TABLE — macOS capability → Windows/.NET

| # | Capability | macOS native (API) | Windows/.NET replacement | Risk / caveats |
|---|---|---|---|---|
| 1 | **Global hotkey** (chord, push-to-talk hold, optional consume) | `CGEvent.tapCreate` on a dedicated thread, `.defaultTap` (consumes). AX-gated. | **`SetWindowsHookEx(WH_KEYBOARD_LL)`** on a **dedicated native thread with its own message pump** (`GetMessage` loop); sees key-down/up; can consume (return `1`). No AX gate. | `fn`/Globe key **does not exist off Apple hardware** → default **Right-Ctrl hold** (rebindable). `globalShortcut`/`RegisterHotKey` is combo-only, no key-up, no hold — insufficient. A busy pump makes the OS drop the hook (R5). |
| 2 | **Frontmost/active app** (name + id + title) | `NSWorkspace.frontmostApplication` + notification | **`GetForegroundWindow` + `GetWindowThreadProcessId` + `QueryFullProcessImageName`** (exe path). Capture at key-down; memo last non-Kivi. | Windows gives exe path, not a bundle-id → need an app-key convention (exe path / AppUserModelID). |
| 3 | **Insert text into active app** (caret-level) | Synthetic Unicode `CGEvent.keyboardSetUnicodeString`, posted to session tap | **`SendInput`** with `KEYEVENTF_UNICODE` (16-unit chunks) for the typed path; clipboard + Ctrl+V is the **primary** path. | Newline handling must stay "type a literal line break, never synth Return-as-submit" (§3). |
| 4 | **AX-level text replace** (edit mode: set selected range/value) | `AXUIElementSetAttributeValue(kAXSelectedTextRangeAttribute)`, Ctrl+A+paste fallback | **UI Automation** `ValuePattern.SetValue` / `TextPattern` (native, no clean managed wrapper — use `System.Windows.Automation` or CsWin32). | **Deferred to M9.** MVP uses Ctrl+A select-all + paste-whole-field only. |
| 5 | **Synthetic paste** (⌘V / Ctrl+V) | `CGEvent` V-key `.maskCommand`, clean `CGEventSource(.privateState)` | **`SendInput` Ctrl+V**; **release held modifiers first** (PTT means Ctrl may be down); terminal → Ctrl+Shift+V; paste without re-foregrounding. | Restore prior clipboard after confirmed paste (§5). |
| 6 | **Clipboard read/write** (multi-type, snapshot/restore, transient tagging) | `NSPasteboard.general` + `changeCount`, custom UTIs, `org.nspasteboard.TransientType` | **`System.Windows.Forms.Clipboard` / WinRT `Clipboard`**; snapshot → write → paste → restore. | Windows clipboard has **no `changeCount`** and **no transient UTI** → replace with an **in-process "we-just-wrote-this" guard** keyed on the exact string; rich custom types (Slack Quill delta) degrade to plain text unless a native clipboard helper is added (M9). |
| 7 | **Screen/selection context** (AX tree, focused field) | `AXUIElementCreateSystemWide`, `kAXFocusedUIElementAttribute`, `AXTreeDumper` | **UI Automation** tree walk + `EnumWindows`/`GetWindowRect` | Very heavy. **Defer entirely for MVP/v1** (M9); server tolerates absence exactly as macOS did when AX was ungranted. Preserve secure-field redaction when built. |
| 8 | **Secure credential storage** (JWT, refresh, per-install AES key) | Keychain `SecItem*`, data-protection, access group | **DPAPI** (`System.Security.Cryptography.ProtectedData`, `CurrentUser`) to a file, or **Windows Credential Manager** | Clean swap. No cross-app "access group" concept — not needed for a standalone app. Keep AES-GCM per-install key (DPAPI-protected) for retained audio. |
| 9 | **Transparent, click-through, always-on-top overlay** (the orb) | `NSPanel` `[.borderless,.nonactivatingPanel]`, `level=.statusBar`, `ignoresMouseEvents`, `canJoinAllSpaces` | **Native Win32 layered window** (invisible WPF host for lifetime): `WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW`, drawn via `UpdateLayeredWindow` (premultiplied ARGB); click-through via `WS_EX_TRANSPARENT` toggled per-tick | `WS_EX_NOACTIVATE` gives true non-activation (the overlay never steals host keyboard focus — the crux). A WPF transparent window can't be truly non-activating → native Win32 layered window with an invisible WPF host (see the `orb-is-a-chip` memo); WPF↔Win32 interop is seamless. DWM composition (transparency) is always on for modern Windows. |
| 10 | **Audio capture + echo cancellation** | CoreAudio AUHAL / VPIO (`setVoiceProcessingEnabled`) | **WASAPI** (`IAudioClient`, via NAudio/CsWin32) + resample to 16 kHz Int16 mono LE. Enable the WASAPI voice-communication capture category for mic-path AEC/NS where the device supports it. | Mic-path only — **NOT** system-audio AEC parity (R2); full system-audio AEC (WASAPI-loopback → APM) deferred M9. Must resample native→16 k and pack Int16 mono LE (continuous resampler state, R10). |
| 11 | **Mic permission** | `AVCaptureDevice.requestAccess(.audio)` + usage string + entitlement | Windows mic-privacy model (Settings ▸ Privacy ▸ Microphone). Detect denial via a capture-open failure; deep-link `ms-settings:privacy-microphone`. | No in-app grant prompt API like macOS; the capture attempt surfaces it. If packaged (MSIX), declare the microphone capability. |
| 12 | **Accessibility permission** (post events / read AX) | TCC via `AXIsProcessTrusted()` | **❌ NO EQUIVALENT GATE.** `SendInput`/`WH_KEYBOARD_LL`/UI Automation work without any trust gate (UAC only matters for injecting into elevated targets). | The whole permission-probe/self-heal UX largely **disappears** on Windows. Drop it. |
| 13 | **Suppress OS `fn` behavior** (stop Emoji viewer) | `com.apple.HIToolbox/AppleFnUsageType=0` | **N/A** (no fn key) | **Drop entirely.** |
| 14 | **Launch at login** | `SMAppService.mainApp` | **Registry `Run` key** (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) / a Startup-folder shortcut / MSIX `StartupTask` | Straightforward. |
| 15 | **Menu-bar / tray** | `NSStatusItem` | **Windows notification-area icon** (`NotifyIcon` / Shell_NotifyIcon interop) + a frameless popover window | Pre-render discrete per-state icon frames; avoid high-frequency tray updates. |
| 16 | **Auto-update** | **Sparkle** (appcast, EdDSA) | **MSIX auto-update** (`.appinstaller`) or **Squirrel/Velopack**-style feed | Different signing/feed; see `RELEASE.md`. EV-sign early (R11). |
| 17 | **OAuth callback** | `kivi://` URL scheme | **Loopback `http://127.0.0.1:<port>/callback`** (`HttpListener`) — handles `?code=` + `#fragment` uniformly | Custom scheme (`kivi`) is a registry-registered fallback; loopback preferred. |

---

## 2. Global hotkey — the "kivi key" (highest-effort port)

**Reference behavior not reproducible with `globalShortcut`/`RegisterHotKey`:**
- **Consumes** the key so the host never sees it: `CGEvent.tapCreate(..., .defaultTap)`, callback returns nil (mac). On Windows: `WH_KEYBOARD_LL` hook proc returns `1` to swallow.
- Runs on a **dedicated `.userInteractive` thread + runloop** (mac) so a busy main thread can't disable the tap. On Windows: a **dedicated native thread with its own `GetMessage` message pump** — never the UI thread (a busy pump makes the OS silently drop/unhook the LL hook, R5).
- Needs **key-up** (push-to-talk release) and **flagsChanged** (bare-modifier down/up), neither of which the built-in `globalShortcut`/`RegisterHotKey` delivers.
- **Timing gestures** (`GestureClassifier`): `holdMs = 420`, `doubleTapMs = 450`, `longHoldMs = 600`; default binding `hold → dictate`, `doublePress → edit`, `longHold → act`, `tap → home`, `Esc → cancel`.

**Port strategy (`Kivi.Platform.Hotkey`):**
- `SetWindowsHookEx(WH_KEYBOARD_LL)` on a dedicated thread with its own message pump. To consume, return `1` from the proc.
- **Re-use the pure logic verbatim:** `GestureClassifier` (thresholds 420/450/600) is injectable/time-driven — port to C# 1:1. The one big code-reuse win in the hotkey layer.
- **New default key** (no `fn`): **Right-Ctrl hold**, rebindable via a `HotkeyCaptureField`.
- Esc-to-cancel (listen-only) → a plain low-level keydown listener; no consume needed.
- **EV-sign the hook binary early** — a low-level keyboard hook trips keylogger AV/SmartScreen heuristics (R11).

---

## 3. Frontmost app + text insertion

**Frontmost** (`Kivi.Platform.Frontmost`): track the foreground window, updating on a poll + on our
own deactivation, and **remember the last NON-Kivi app** so a take is still attributed to (and pasted
into) the real target when the orb is frontmost. Wire shape (`AppContextWire`): `{app_name,
bundle_id, window_title}`.
- Port: `GetForegroundWindow` + `GetWindowThreadProcessId` + `QueryFullProcessImageName` (exe path → stable app key) + `GetWindowText` (title); memo last external app. `bundle_id` uses the agreed app-key convention.

**Text insertion** (`Kivi.Platform.Paste`) — two ordered strategies:
1. **Clipboard + Ctrl+V** (primary everywhere): write payload → ~30 ms settle → `SendInput` Ctrl+V; **release held modifiers first** (`cleanSource` analog — don't merge with a still-held hotkey modifier); terminal detect → Ctrl+Shift+V; paste **without re-foregrounding** (orb non-activating).
2. **Unicode keystrokes** (typed fallback, no clipboard): `SendInput` with `KEYEVENTF_UNICODE`, chunked (mirror the 16-unit chunking + a small inter-char delay for input pipelines like Cursor). **Deferred** — clipboard+paste is primary.

Newlines: **type each line, a literal line break between lines** — Return is only ever a line break,
**never** a submit ("NEVER synthesised as Return key presses which would send a chat message").

---

## 4. AX text replacement (edit mode) — defer for MVP

macOS reads `kAXSelectedTextAttribute`, sets `kAXSelectedTextRangeAttribute`, falls back to Ctrl+A +
paste-whole-field. Windows equivalent = **UI Automation** `TextPattern`/`ValuePattern`. **MVP and v1
use only the Ctrl+A-select-all + paste path** (no per-span replace). Full range-level edit is a later
native effort (M9).

---

## 5. Clipboard

Key behaviors to reproduce:
- **Snapshot → write → paste → restore** so the user's clipboard survives a paste.
- **Own-write suppression**: the history poller must skip frames Kivi itself wrote.

**Port (`Kivi.Platform.Paste`/clipboard service):** WinRT `Clipboard` / WinForms `Clipboard`. Gaps:
- No `changeCount` → poll `GetText()`/available formats and diff, or use a clipboard-change listener (`AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE`).
- No native transient UTI → an **in-process flag** ("the next change is ours") keyed on the exact string written.
- Multi-format restore: text/html/rtf/image cover the common cases; arbitrary custom types are painful → rich paste degrades to plain text unless a native clipboard helper is added (M9).

---

## 6. Screen/selection context — defer

macOS system-wide AX (`AXUIElementCreateSystemWide`, focused-field role gate, `AXSecureTextField` →
redacted, value capped **2000 chars**, tree dump over running apps). Feeds optional
`screen_nodes`/`focused_field`. **Port plan: omit for MVP/v1; Windows via UI Automation later (M9).**
The secure-field redaction rule must be reproduced when eventually built. No pixel screenshots
anywhere — it's a pure UIA-text + window-list extractor.

---

## 7. Keychain / secure storage

macOS: `SecItem*` data-protection keychain, shared access group, per-install `retainedAudioEncryptionKey`
(AES-GCM). **Port (`Kivi.Platform.Secrets`):** **DPAPI** (`ProtectedData.Protect/Unprotect`,
`DataProtectionScope.CurrentUser`) persisted to a file under `%APPDATA%\Kivi`, or Windows Credential
Manager. Drop the access-group + legacy-carryover logic. Keep the AES-GCM per-install key pattern for
retained audio (the key itself DPAPI-protected). Stores Supabase/Kratos tokens, the 15-min org JWT,
Kratos identity, and the retained-audio key.

---

## 8. The orb overlay window

macOS `FloatingBarPanel: NSPanel`:
```
styleMask = [.borderless, .nonactivatingPanel]; level = .statusBar
collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary]
isOpaque = false; backgroundColor = .clear; hasShadow = false
ignoresMouseEvents = true (toggled per-frame); canBecomeKey = keyArmed (only over editable box)
panelSize = 1480×720; orbEdgeInset = 24
```
This **non-activating, focus-preserving** behavior is what lets dictated text land in the host app.

**Port (`Kivi.Platform.Overlay`):** a **native Win32 layered window** (invisible WPF host for lifetime):
```
WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW  (+ WS_POPUP)
drawn via UpdateLayeredWindow (premultiplied ARGB)
click-through: WS_EX_TRANSPARENT toggled per-tick from the geometric hit-test
```
- **`WS_EX_NOACTIVATE`** gives true non-activation — clicking the orb never steals focus (the `.nonactivatingPanel` contract). A WPF transparent window **cannot** be truly non-activating — hence the native Win32 layered window (an invisible WPF host window holds lifetime, and WPF↔Win32 interop is seamless; see the `orb-is-a-chip` memo). (R20.)
- Always-on-top via `WS_EX_TOPMOST` + `SetWindowPos(HWND_TOPMOST)`.
- No cross-Space concept; the window floats on the current desktop.
- Per-frame `WS_EX_TRANSPARENT` toggling from the hover-classifier hit-test (`FlowFrame.InteractiveTarget`) — the port of `syncCursorState` (poll `GetCursorPos`).
- For the editable-box case, briefly make the window activatable (clear `WS_EX_NOACTIVATE` / focus it) then revert (M4).

---

## 9. Audio capture + AEC

Native path (mac): AUHAL → `AVAudioConverter` → 16 kHz Int16 mono LE, ~100 ms frames; VPIO for AEC.
**Port (`Kivi.Platform.Audio`):** **WASAPI** (`IAudioClient`/`IAudioCaptureClient`, via NAudio or
CsWin32) capturing float32 at native rate, mix to mono, **resample to 16 kHz Int16 mono LE**
(continuous resampler state), chunk to ~100 ms (3200-byte) frames, send as binary WS frames. Enable
the **WASAPI voice-communication capture category** for mic-path AEC/NS. Device enumeration via WASAPI;
persist a stable device id string. Keep **press-time early capture** (`earlyPrefix`, M1/M2). No AEC
warm-up cost applies. Full system-audio AEC deferred M9 (R2).

Match target format exactly: **16000 Hz, mono, signed 16-bit little-endian PCM**, ~100 ms chunks
(1600 samples = 3200 bytes). (Details: `dictation-audio-pipeline.md §3`.)

---

## 10. Backend wire contract (unchanged — reuse `kivi-service` as-is)

The .NET app talks to the **same** Rust service. `Kivi.Core/Wire/*`.
- **Endpoint (local dev):** `ws://127.0.0.1:8788/v1/dictate/stream`, `DICTATE_AUTH_MODE=none` → anonymous, no bearer. Prod: `wss://kivi.sarvam.ai/...` with `Bearer <jwt>`.
- **Handshake:** connect → `ack` (`{type:"ack", session_id}`) within **4 s** → send **`context`** → stream audio.
- **`context`** (snake_case): `{type:"context", transcription_mode:"codemix", formatting_enabled:true, session_id, client_take_id?, trace_id?, frontmost_app?, app_context:{app_name,bundle_id,window_title}?, auto_persona_resolution:true, selected_persona_slug?, supports_formatting_progress:true, idle_timeout_secs?}`. Screen-context fields optional — omit for MVP.
- **Audio:** binary WS frames, the 16 kHz Int16 mono PCM blocks verbatim. Backpressure cap ~5 s (50 frames).
- **End:** `end_of_speech`, then consume until `final`/`error`. Cancel = `{type:"cancel"}` then close. **Ping every 20 s.**
- **Server → client:** `ack`, `interim{segment_idx,text,is_final}`, `speech_start`, `route_hint`, `final(FinalPayload{raw_transcript, formatted_text, ...})`, `eos_ack`, `formatting_progress`, `error{code,message}`, `pong`.

For the MVP: connect → ack → context → binary audio → end_of_speech → read `final.formatted_text` →
insert. A direct mechanical reimplementation with `ClientWebSocket`. (Full: `service-client-wire.md`,
`backend-service-api.md`.)

---

## 11. Design primitives (for the visually-exact clone)

All tokens are extracted 1:1 from the reference and are byte-exact — **reuse the numbers verbatim**.
Primary source: the reference `packages/design-tokens` (orb + KDS/Canon). Concrete values:
- **Colors** — light: paper `#F1F4EC`, paper2 `#FFFFFF`, fg1 `rgb(20,20,20)`, fg2 `rgb(102,102,102)`, warm-tint `#E7EEDD`. Dark: paper `#121512`, paper2 `#1B1F1A`, fg1 `#ECEFE8`. Orb "forest": fill `rgb(13,30,9)`, glow `rgb(120,184,72)`, eye `#EAF0E2`, restA `0.72`; "mist": fill `rgb(223,234,209)`, glow `rgb(176,212,132)`. Accents: idle `#41691E`, listen `#E6651B`, del `#B81514`, ins `#2F7D2E`.
- **Geometry (px)** — rest pill `39×15` r`7.5`; woken orb `61×61` r`30.5`; take pill `57×18`; transcript box `322×108` r`8` (min 322×108, max 640×360); edit pane `212` wide r`20`; panel `1480×720`; mark `65`.
- **Type** — Matter (300–700), Matter SemiMono, Space Grotesk (static 400/500/600/700). Embed the woff2/otf. Role sizes: body `13`, hint `11`, hint2 10.5, keycap `11`, tx-key `9.5`.
- **Motion** — **wake lerp `0.30`** *(RESOLUTION: this quick-reference historically said `0.20`; the current engine uses `0.30` — trust `0.30`)*, expand `0.18`, breath period `2.6 s`, dots `600 ms`, processing `2000 ms`, diff `520/1050/620 ms`, hover in/out `44/54 px`. Gesture timing: `holdMs 420`, `doubleTapMs 450`, `longHoldMs 600`.

A WPF-hosted 2D surface (Win2D or `WriteableBitmap`/`DrawingContext`) with blur effects, `PathGeometry`, and embedded fonts reproduces all of this exactly; the paper
grain is a tiled deterministic noise texture (`orb-visual-and-box.md §3`). (Full: `design-tokens.md`,
`orb-visual-and-box.md`.)

---

## CROSS-PLATFORM NOTES (what is macOS-specific and how Windows handles it)

1. **`fn`/Globe key does not exist** off Apple hardware → default **Right-Ctrl hold** (rebindable); delete the `fn`-suppression guard. Port the **pure** `GestureClassifier` (420/450/600, chord windows) unchanged.
2. **CGEvent tap consume-and-forward** → `WH_KEYBOARD_LL` (can eat, return `1`) on a dedicated thread + message pump.
3. **TCC Accessibility** — **no equivalent gate on Windows** (`SendInput`/hooks/UIA just work). The self-healing permissions UX collapses; the only permission is Microphone.
4. **AX tree + focused-field capture** — deeply macOS-specific, powers only *optional* screen context → **defer** (M9, UI Automation). Server tolerates absence.
5. **AX range-level text replacement** (edit mode) → UI Automation `TextPattern`/`ValuePattern`, deferred M9. MVP + v1 = select-all + paste.
6. **Non-activating overlay window** (`.nonactivatingPanel`, `.statusBar`, click-through, focus-preserving) → a native Win32 layered window with `WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TOOLWINDOW` + per-frame `WS_EX_TRANSPARENT` toggle. A WPF transparent window can't be truly non-activating → native Win32 layered window with an invisible WPF host (`orb-is-a-chip` memo).
7. **Transparency** — DWM composition is always on for modern Windows; the transparent layered orb renders correctly (no compositor detection needed, unlike Linux X11).
8. **Keychain access-group + legacy carryover** → unnecessary; **DPAPI** (`ProtectedData`) or Credential Manager.
9. **Audio/AEC** — WASAPI capture + resample to 16 kHz Int16 mono LE (continuous state); voice-communication capture category for mic-path AEC. System-audio AEC deferred M9.
10. **Backend is fully reusable** — same `ws://127.0.0.1:8788/v1/dictate/stream`, anonymous local (`DICTATE_AUTH_MODE=none`), same binary-audio + JSON-context protocol. Just a `ClientWebSocket` reimplementation.
11. **Frontmost app id** differs: macOS bundle-id vs Windows exe-path/AppUserModelID. Normalize to a stable app key; let it be null (server renders "kivi"/untrusted gracefully).

**Deferred / v1 non-goals (this map):** UI-Automation range edit + screen context (M9); system-audio
AEC (M9); rich-clipboard custom types (M9); Unicode-typing fallback (M1). The permission-probe UX is
**dropped** (mic-only on Windows).

> **Not applicable — Windows-only.** The reference's entire Linux column (XTest, ydotool, uinput,
> AT-SPI2, Wayland portals, X11 compositor requirement, `.desktop` handlers, "two hard Wayland
> blockers") is **removed**. There is no Linux/Wayland target, no compositor-detection, no
> degraded-Wayland tier. The macOS `IsSecureEventInputEnabled` secure-field gate → best-effort UIA
> password-field detect; `AppleFnUsageType` guard → dropped.
