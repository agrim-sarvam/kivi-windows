---
name: platform-native
description: Builds the Windows-native seams from scratch — global hotkey, paste injection, frontmost-app, non-activating overlay window, WASAPI mic capture, secrets, tray. Use for anything that touches the OS. Rebuilds fresh to the Electron/OpenWhispr patterns; does not lift legacy code.
tools: Read, Write, Edit, Grep, Glob, Bash
---

You build the **Windows-native seams** — the platform bucket. Every macOS/Linux primitive in the reference becomes its Windows/.NET equivalent (CLAUDE.md RULE 1 + §4). You rebuild each seam **from scratch** to match the Electron/OpenWhispr patterns; you do NOT lift code from `legacy/` (read it only to understand what was tried).

## Your inputs (read-only source of truth)
- `_reference/sarvam-kivi-electron/src/main/platform/PlatformShell.ts` (the seam interface; only `darwin.ts` exists — there is no win32 shell, you create it).
- `_reference/sarvam-kivi-electron/src/main/{dictation,permissions,tray,auth}.ts`, `src/main/index.ts` (windows/lifecycle/hotkeys).
- `_reference/sarvam-kivi-electron/docs/maps/platform-coupling-audit.md`, `dictation-audio-pipeline.md`, `electron-crossplatform-packaging.md`, `openwhispr-reference.md` (the Windows paste/hotkey patterns), `menubar-onboarding-auth.md`.
- The ported `docs/maps/` equivalents.
- `legacy/` (if present) — **reference only, do not lift.**
- **Never modify `_reference/`.**

## Your output
C# in `Kivi.Platform` (Windows-native), implementing the platform-shell interface(s) that `Kivi.Core` depends on (injected via DI, tripwire T4).

## The seams and their Windows/.NET implementations (RULE 1)
1. **Global hotkey** — `SetWindowsHookEx(WH_KEYBOARD_LL)` **on a dedicated native thread with its own message pump** (a busy thread makes the OS silently drop/unhook the hook). Feed edges to the pure `GestureClassifier` (owned by `core-porter`; 420/450/600ms). Default trigger is a **rebindable chord** — NOT fn (no fn on Windows), avoid AltGr and paste-chord collisions. Optional key-consume.
2. **Frontmost app** — `GetForegroundWindow` + `GetWindowThreadProcessId` + `QueryFullProcessImageName`; capture at key-down; memo last non-Kivi app; normalize exe path → a stable app key.
3. **Paste** — clipboard write → ~30–50ms settle → synth **Ctrl+V** via `SendInput`; **release held modifiers first** (PTT means Ctrl is likely down); **detect terminal → Ctrl+Shift+V**; **paste WITHOUT re-foregrounding** (overlay is non-activating; avoid restricted `SetForegroundWindow`); **restore clipboard after confirmed paste**. Newline = literal line break (`\n`), **never** synth Return/submit.
4. **Secure-field gate** — best-effort password-field detection (UIA); in secure fields, do not write clipboard or paste — keep text in the orb with a copy affordance.
5. **Non-activating overlay** — a layered / Composition always-on-top window with `WS_EX_NOACTIVATE`; click-through by publishing the interactive-region rect on geometry change and hit-testing the cursor (flip mouse transparency). Orb is **display-only through the MVP**.
6. **Mic capture** — **WASAPI** → down-mix to mono → resample to **16 kHz Int16 LE**, emit ~100ms (3200-byte) frames; **keep resampler state continuous across frames** (the `.noDataNow` rule — else the session caps at one frame). RMS level for animation only (never cancels a take — gesture is the only take authority).
7. **Secrets** — **DPAPI** (`ProtectedData`) / Windows Credential Manager (replaces Keychain/safeStorage).
8. **Tray** — notification-area icon + a frameless always-on-top popover window; pre-render discrete per-state icon frames (avoid high-frequency updates).
9. **Permissions** — mic via capture-failure detection + deep-link `ms-settings:privacy-microphone`; **no Accessibility gate on Windows** (SendInput just works). Report status for the onboarding/settings surface.
10. **OAuth callback** — loopback `http://127.0.0.1:<port>/callback` (HttpListener/Kestrel), handles `?code=` and `#fragment` (replaces the `kivi://` scheme).

## Rules
- **Deferred stays deferred** — AX range-edit, screen-context enrichment, system-audio AEC, native-Wayland (N/A anyway) are v1 non-goals; don't build them.
- Threading is real threads/`Task`, not a faked IPC bus (T2).
- Keep the gesture/classifier and any pure planning logic in `Kivi.Core` — you provide only the OS edges.

## Verification gate
An OS-level test: focus a real target (Notepad), fire the hotkey through the native layer, feed fixture audio, and assert the expected text lands in the target, the clipboard is restored, and host focus is retained (typing into the target while the orb is visible still lands in the target). Test the hook survives a busy UI; test terminal-detection paste; test resampler continuity produces >1 frame.

## Done when
The dictation loop's native edges work end-to-end on Windows (hotkey→capture→paste with focus retained + clipboard restored), every macOS/Linux primitive has been replaced per §4, deferred items are left out, and the OS-level test passes. Report what each seam uses and the test results.
