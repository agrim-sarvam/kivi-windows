# MAP: openwhispr-reference

Windows-only .NET port of `_reference/sarvam-kivi-electron/docs/maps/openwhispr-reference.md`.
OpenWhispr is a mature cross-platform Electron dictation app; this digest keeps it as a **reference
for what to borrow for the .NET/Windows implementation** — the patterns, not the code. Every
"borrow this" below is framed for Windows/.NET.

**Repo:** `github.com/OpenWhispr/openwhispr` · MIT. **Verdict:** its hard-won cross-platform layer is
exactly the shape our MVP needs — global push-to-talk on a modifier key, mic capture, pluggable STT,
and the crux: **paste-into-active-app via native per-OS keystroke injection**. We borrow the thin
dictation spine (the **Windows** slice) and drop the rest.

---

## 1. What to borrow, at a glance

| OpenWhispr layer | What to borrow for .NET/Windows |
|---|---|
| Global hotkey via a native listener (NOT the framework's built-in shortcut API) | The **principle**: use `WH_KEYBOARD_LL` for push-to-talk (key-down/up + hold), never a combo-only shortcut API. |
| Mic capture in the view layer | Capture via **WASAPI**; keep the two-mode shape (batch vs streaming). |
| Pluggable STT | Swap all providers for one **`KiviServiceClient`** (`ClientWebSocket`) → local `kivi-service`. |
| ★ **Paste-into-active-app** | Clipboard-write + synthesized paste via **`SendInput`**; terminal detection + modifier-release; capture frontmost at key-down. |
| Permission preflight | Mic-only on Windows (no Accessibility gate); deep-link to Settings on failure. |
| Native-helper bundling | Bundle any tiny native helper via the installer; but with .NET, most of this is in-process P/Invoke — no sidecar needed. |

Drop everything else (agents, notes, diarization, meetings, cloud sync, BYOK providers, i18n, local
whisper/onnx) — Kivi has its own Rust `kivi-service`.

---

## 2. Process architecture → in-process .NET

OpenWhispr splits: a Node **main** process (window/hotkey/paste/tray/secrets managers) + a **preload**
`contextBridge` IPC contract + a **renderer** that owns mic/VAD/STT orchestration.

**Design takeaway for Kivi/.NET:** the same *seam* — OS-native concerns behind interfaces, the view
layer owns audio — but **in one process**. There is no main↔renderer IPC bus: the boundary becomes
`Kivi.Platform.*` interfaces (hotkey, paste, tray, secrets) injected via DI into `Kivi.App`, called
with `async`/`await` + events. Put the orb/overlay + audio in the view layer (`Kivi.App` +
`Kivi.Platform.Audio`); keep OS-native concerns behind `Kivi.Platform`. This mirrors the reference's
natural seam without the IPC tax.

---

## 3. Global hotkey — the first lesson: **do NOT use the built-in shortcut API**

OpenWhispr bypasses Electron `globalShortcut` because it can't bind bare modifiers or do
**push-to-talk** (key-down starts, key-up stops). Each OS has a native listener; the **Windows** one
is `resources/windows-key-listener.c` — a low-level keyboard hook emitting key-down/up.

**Borrow for .NET/Windows (`Kivi.Platform.Hotkey`):** `SetWindowsHookEx(WH_KEYBOARD_LL)` on a
dedicated native thread with its own message pump (see `platform-coupling-audit.md §2`). Feed edges
to the pure `GestureClassifier`. Default trigger = **Right-Ctrl hold** (no `fn` on Windows). The
built-in `RegisterHotKey`/`globalShortcut` is only a fallback for full chords, **never** for
bare-modifier PTT. (OpenWhispr's macOS Swift globe-listener and Linux evdev reader are not relevant —
Windows-only.)

Hotkeys organized in **slots** (`dictation`, `agent`, …) with push-to-talk vs toggle activation
modes, plus fallback events (`onHotkeyRegistrationFailed`/`onHotkeyFallbackUsed`) — worth mirroring
as a small hotkey-registry abstraction.

---

## 4. Microphone capture + audio pipeline → WASAPI

OpenWhispr's renderer `audioManager.js`: `getUserMedia` with **all browser DSP disabled**
(`echoCancellation:false, noiseSuppression:false, autoGainControl:false`), `MediaRecorder` at **250 ms**
timeslices (`audio/webm`/Opus), merged via ffmpeg, with a separate `AudioContext` VAD; two modes
(push-to-talk batch, and realtime streaming over a WS in main).

**Borrow for .NET/Windows (`Kivi.Platform.Audio`):** capture via **WASAPI** (NAudio/CsWin32). Kivi's
service wants **raw 16-kHz Int16 mono LE PCM in ~100 ms frames** — so **skip the WebM/Opus + ffmpeg
transcode entirely** and stream raw PCM (better latency, matches the STT WS). Copy OpenWhispr's
**realtime-streaming** shape (frames pushed over the socket, partial/final events), not its batch
one-shot path. Note: OpenWhispr disables DSP; Kivi instead enables the WASAPI voice-communication
capture category for mic-path AEC (Kivi's own AEC concern; see `platform-coupling-audit.md §10`).

---

## 5. Transcription routing → one `KiviServiceClient`

OpenWhispr routes to local whisper, cloud one-shot, and streaming providers. **This is our swap
point.** Replace all providers with a single **`KiviServiceClient`** (`Kivi.Core.Wire`,
`ClientWebSocket`) → local Rust `kivi-service`. Keep OpenWhispr's warmup + one-shot + streaming
trichotomy shape (the interface — `bytes → text`, plus a WS partial/final event pair — is reusable),
but Kivi only needs the **streaming** path (`interim`/`final`).

---

## 6. ★ Paste-into-active-app — the single most important pattern to steal

OpenWhispr's universal approach: **write text to the clipboard, then simulate the paste keystroke**
(never per-character typing — too slow/unreliable). A native "fast-paste" helper injects the
keystroke; a clipboard manager orchestrates with a **`PASTE_DELAY_MS = 50`** settle delay. The
frontmost app's PID is captured at hotkey-down so focus is restored before pasting.

**Windows — `resources/windows-fast-paste.c` (the one to mirror):** pure **`SendInput`**. Detects if
the foreground window is a **terminal emulator**; sends **Ctrl+V** for normal apps or **Ctrl+Shift+V**
for terminals. Crucially it first **releases any modifier keys the user is still holding**
(`ReleaseModifiers`) — because PTT means Ctrl/Win may be down — pastes, then restores them.

**Borrow for .NET/Windows (`Kivi.Platform.Paste`):**
- Adopt the **clipboard-write + synthesized-paste** model wholesale (`SendInput` Ctrl+V). It's the only reliable cross-app method.
- **Must-copy details**: terminal detection → Ctrl+Shift+V; **release held modifiers first**; capture the target at key-down; **paste without re-foregrounding** (the orb is non-activating so the target never lost focus — avoids the restricted `SetForegroundWindow`); preserve/restore the user's clipboard around the paste; `PASTE_DELAY_MS ≈ 50` (Kivi uses ~30 ms settle — tune).
- **No Accessibility grant needed** on Windows (unlike the macOS fast-paste path, which is gated by `AXIsProcessTrusted()`).
- **In .NET this is in-process P/Invoke** (`SendInput`, clipboard APIs) — no separate signed helper `.exe` needed (though the low-level *hook* binary still wants EV signing, R11).

(OpenWhispr's macOS `macos-fast-paste.swift` and the three Linux backends — XTest/portal/uinput/ydotool
— are **not applicable**; Windows-only. Note: the reference's auto-summary once mis-labeled ⌘V — verify
keycodes when reading their source.)

---

## 7. Packaging & native-helper bundling → .NET installer

OpenWhispr ships native helpers via `extraResources`, `asarUnpack`s native node modules, and
compiles/downloads per-OS helper binaries at build time. **For .NET/Windows this mostly evaporates:**
the hotkey hook + paste are **in-process P/Invoke**, not sidecar binaries, so there's no
`extraResources`/`asarUnpack` analog to manage. Packaging is `dotnet publish` → MSIX/MSI (see
`RELEASE.md`). Any genuinely native helper (rare) is bundled as installer content and **EV-signed**
alongside the app (keylogger AV heuristics, R11). Kivi's Rust `kivi-service` is a separate
process/binary (the backend), not bundled into the app.

---

## 8. Permissions surface → mic-only on Windows

OpenWhispr's preload exposes mic + accessibility + paste-tool checks with surfaced failures. **Borrow
the preflight + self-heal UX shape, trimmed for Windows:**
- **Microphone**: detect via a capture-open failure; deep-link `ms-settings:privacy-microphone`.
- **Accessibility**: **not applicable** — Windows has no such gate (`SendInput`/hooks work without it). Drop it.
- The `onAccessibilityMissing`/`onLinuxPttPermissionDenied` surfaces have **no Windows analog** — drop.

---

## 9. Concrete borrow-list for Kivi M0 (trimmed transcription MVP)

1. **Scaffold:** `Kivi.sln` (`Kivi.Core` / `Kivi.Platform` / `Kivi.App` / `Kivi.Core.Tests`) per `MASTER-PLAN §4`; OS-native seams behind `Kivi.Core/Contracts` interfaces, injected via DI. (The .NET analog of "one clean contextBridge object".)
2. **Hotkey:** `WH_KEYBOARD_LL` on a dedicated thread + pure `GestureClassifier`. **Skip the built-in shortcut API** for PTT.
3. **Capture:** **WASAPI** → 16 kHz Int16 mono LE, ~100 ms frames (skip WebM/Opus/ffmpeg — stream raw PCM).
4. **STT:** `KiviServiceClient` (`ClientWebSocket`) → local Rust `kivi-service` (streaming: `interim`/`final`).
5. **★ Paste:** clipboard-write + `SendInput` Ctrl+V; terminal → Ctrl+Shift+V; **release held modifiers first**; capture frontmost at key-down; paste without re-foregrounding; restore clipboard; ~30–50 ms settle.
6. **Permissions:** mic preflight only (deep-link Settings on failure). No Accessibility gate.
7. **Packaging:** `dotnet publish` → MSIX/MSI; EV-sign the hook binary early (R11). (`RELEASE.md`.)

**Top risks flagged (Windows slice):** (a) the low-level hook trips keylogger AV/SmartScreen →
EV-sign early (R11). (b) audio-format contract with `kivi-service` — stream raw PCM, don't transcode
(decided). (c) OpenWhispr disables all DSP; Kivi's AEC (WASAPI voice-communication category) is its
own concern.

**Not in scope to borrow:** agents, notes, diarization, meeting capture, cloud sync, BYOK/enterprise
providers, i18n — all removable without touching the dictation spine.

> **Not applicable — Windows-only.** OpenWhispr's macOS globe-listener/fast-paste, its Linux
> evdev/XTest/portal/uinput/ydotool backends, and its Wayland/Hyprland notes are dropped — we borrow
> only the Windows slice + the cross-app *patterns*.
