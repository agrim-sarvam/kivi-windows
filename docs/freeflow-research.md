# FreeFlow Research → Kivi-for-Windows Port Plan

Research across the three FreeFlow repos, focused on one question: **what can we call/reuse
as-is, and what must we reimplement in .NET?** Bottom line first, then the step-wise build plan.

---

## TL;DR — The most important finding

**There is no "FreeFlow backend" to run. FreeFlow is a thin client that calls a hosted,
OpenAI-compatible REST API directly with a Bearer API key.** Default provider is **Groq** (free tier).

That means the thing you wanted — *"understand what backend I can just call and use the whole
thing as a wrapper"* — is simply:

```
POST {baseURL}/audio/transcriptions   ← Whisper STT (multipart WAV)
POST {baseURL}/chat/completions        ← LLM cleanup (standard chat completion)
Authorization: Bearer {apiKey}
baseURL default = https://api.groq.com/openai/v1
```

Both endpoints are the **standard OpenAI API shape**. Any OpenAI-compatible provider works by
changing the base URL (Groq, OpenAI, Ollama, LM Studio, or — later — Kivi's own backend if it
exposes an OpenAI-compatible surface). **Nothing is self-hosted; there is no server component in
any FreeFlow repo.**

So the port is: **reuse the API contract + prompts + cleanup logic verbatim (just translated to
C#), and reimplement only the OS glue** (hotkey, mic, paste, context capture, key storage, UI).

---

## What each repo gave us

### 1. `zachlatta/freeflow` (original, macOS/Swift) — the source of truth
- **Backend:** Groq OpenAI-compatible REST. STT `whisper-large-v3` (verbose_json), cleanup
  `openai/gpt-oss-20b` (fallback `qwen/qwen3.6-27b`), `temperature 0`.
- **The prompts are load-bearing and portable** — 3 system prompts (cleanup / command-edit /
  verbatim-translate) + a context-synthesis prompt, all plain strings. Copy verbatim into C#.
- **Robustness logic worth porting:** hallucination filter (`no_speech_prob >= 0.1` + known
  phrases like "thank you for watching"), model fallback on 429/empty, rate-limit cooldown
  circuit breaker, and a **prompt-injection guard** (detects when the LLM *answered* the
  transcript instead of cleaning it, falls back to raw text).
- **Config keys / model IDs / defaults** — direct C# translation.
- macOS-specific (reimplement): CGEventTap hotkey (**default Fn key — won't map on Windows,
  pick a new default**), AVFoundation mic, NSPasteboard+CGEvent Cmd+V paste, AX context,
  `.settings` JSON chmod-600 key storage.

### 2. `stha-hardik/freeflow-windows` (community port, Python/PyQt6) — the Windows how-to
This is the single most useful reference because it already solved every Windows problem. Same
Groq backend (raw `httpx`, not the Groq SDK). Gives a clean 1:1 mapping:

| Concern | Python port did | .NET equivalent to use |
|---|---|---|
| Hold-to-talk hotkey | `keyboard` lib, **non-suppressing** low-level hook, default **right ctrl** | P/Invoke `SetWindowsHookEx(WH_KEYBOARD_LL)` returning `CallNextHookEx`. **Avoid `RegisterHotKey`** — fires once & consumes the key, wrong for hold-to-talk. |
| Mic capture | `sounddevice` 16k mono float32 → WAV | **NAudio** `WaveInEvent`/`WasapiCapture`, `WaveFormat(16000,16,1)`, `WaveFileWriter`→`MemoryStream` (16-bit directly, skip the float32→int16 step). |
| Device change/disconnect | **not handled** | **We improve here** (POA #3): `MMDeviceEnumerator` + `IMMNotificationClient`, re-enumerate at session start. |
| Paste | `win32clipboard` + `SendInput` Ctrl+V | `SendInput` P/Invoke (`INPUT`/`KEYBDINPUT` structs) + clipboard. |
| Context capture | `comtypes` UIA → Win32 fallback → Ctrl+C clipboard trick; PIL screenshot→base64 | `System.Windows.Automation` + `TextPattern`, `GetForegroundWindow`/`GetWindowText`, `Graphics.CopyFromScreen`→JPEG→base64. **.NET is stronger here.** |
| Config/secrets | `platformdirs` JSON + `keyring` | `%APPDATA%` JSON + **DPAPI** (`ProtectedData`) or Credential Manager. |

**Four hard-won paste safeguards to copy verbatim (subtle Windows bugs already fixed):**
1. **Wait for modifier release** before Ctrl+V (`GetAsyncKeyState` on Shift/Ctrl/Alt/Win) — else
   the still-held hotkey modifiers corrupt the paste into Ctrl+Shift+V. 40ms poll, 1s timeout.
2. **Verify clipboard** after write; rewrite once if another process clobbered it.
3. **400ms delay** after Ctrl+V before restoring the old clipboard (slow Electron apps).
4. Optional **press Enter** (VK 0x0D) for "enter to send".

### 3. `mrinalwadhwa/freeflow` (advanced fork, macOS/Swift) — best cleanup logic + local option
- **Adds a large deterministic `PolishPipeline`** that cleans text *before/instead of* the LLM:
  spoken-punctuation substitution, filler stripping, known-term capitalization, list formatting,
  `<keep>…</keep>` tags, and **`sanitizeContextField`** (strips ChatML delimiters / role
  prefixes from window titles — prompt-injection defense). **Pure regex/string logic → port to
  `System.Text.RegularExpressions`. This is genuinely worth porting** and directly supports the
  perf goal (less LLM dependence).
- **Local/offline mode** exists but is Apple-only (Parakeet CoreML + Qwen3 via MLX, or Apple
  SpeechAnalyzer + Foundation Models). **Does not port to Windows.** If we ever want local STT on
  Windows the slot is filled by whisper.cpp / Whisper ONNX + a small local LLM behind an
  OpenAI-compatible endpoint — at which point we reuse the *same* HTTP client and prompts unchanged.
- **Caveat — the "skip LLM polish" heuristic (`isClean`) is documented but NOT in the code.** The
  4 conditions are known (starts capitalized, ends with sentence punctuation, no filler words, no
  repeated phrases) — implement it ourselves if we want it. Cheap win for latency/cost/perf budget.
- **Design patterns to mirror as C# interfaces:** `PolishChatClient.complete(model, systemPrompt,
  userPrompt)`, `LocalSTTEngine.transcribe(audio)->string`, streaming provider — so cloud/Groq/
  local backends are swappable behind one seam. This is exactly the abstraction that lets us later
  point at Kivi's real backend without touching the pipeline.

---

## The reuse boundary (answers "what code do I reuse, just in a different language")

**REUSE verbatim (translate to C#, no behavior change) — the portable core:**
- API contract: both endpoint shapes, multipart body, request/response JSON, model IDs, base URLs.
- All system prompts + user-message templates (from original + fork).
- `PolishPipeline` deterministic cleanup (regex tables) from the fork.
- Hallucination filter, model-fallback, cooldown circuit breaker, prompt-injection guard.
- Voice-macro exact-match logic, custom-vocabulary append, "press enter" command parsing.
- Config schema / defaults / timeout keys.

**REIMPLEMENT (OS glue — Windows-native, ~all the actual new code):**
- Global hold-to-talk hotkey (low-level keyboard hook).
- Mic capture → 16kHz mono PCM16 WAV (NAudio/WASAPI) + device-change resilience.
- Clipboard + Ctrl+V paste with the 4 safeguards.
- Screen-context capture (UI Automation + Win32 + screenshot), **skip password/secure fields**.
- Secret storage (DPAPI / Credential Manager).
- UI shell (tray + recording overlay + settings) — Kivi UI skin.

---

## Suggested step-wise build order (maps to the POA in overview.md)

**Step 0 — Decide two things first** (see Open Questions): backend (Groq stopgap vs Kivi) and
WinUI 3 vs WPF. Recommendation below.

**Step 1 — Portable core library (`Kivi.Core`, no UI, no OS glue).** Pure C#. This is where the
reuse lives, and it's testable without any device:
- `OpenAiCompatibleClient` — `HttpClient` wrapper: `TranscribeAsync(wav)` (multipart) +
  `CompleteAsync(system, user)` (chat). Configurable dual base URLs + keys.
- Port the prompts (constants), `PolishPipeline` regex cleanup, hallucination filter,
  fallback/cooldown/injection-guard, voice-macro + vocab logic, config model.
- Define the swap seams as interfaces (`ISttEngine`, `IPolishClient`) so Groq today / Kivi later.
- *Unit-testable end to end with a fake HTTP handler — no mic, no hotkey needed.*

**Step 2 — OS-glue services (Windows-native), each behind an interface:**
`IHotkeyService`, `IAudioCapture` (+ device-change), `IPasteService` (4 safeguards),
`IContextService` (UIA + password-field skip), `ISecretStore` (DPAPI). Build/verify each in
isolation before wiring.

**Step 3 — Orchestrator** (port of `AppState`/`app.py`): hold-start → snapshot context (concurrent
with recording) → hold-end → transcribe + context in parallel → macro/vocab/cleanup → paste.

**Step 4 — Kivi UI skin** (tray + recording overlay + settings) on the chosen framework.

**Step 5 — Perf pass** (<100MB RSS): native controls, streaming buffers, implement the `isClean`
LLM-skip gate, `dotnet-counters` profiling.

**Step 6 — Installer:** WiX MSI → single signed `installer.exe`.

---

## Decisions (Step 0 — RESOLVED)

- **Backend: Groq** (OpenAI-compatible), behind `ISttEngine`/`IPolishClient` interfaces from day
  one. When `sarvam-kivi` access lands, swapping to Kivi's backend is a config/adapter change, not
  a rewrite. Defaults: STT `whisper-large-v3`, cleanup `openai/gpt-oss-20b`, base
  `https://api.groq.com/openai/v1`.
- **UI framework: WinUI 3** (Windows App SDK) — chosen for the modern Fluent look that best matches
  the Kivi aesthetic (this is a skinning project, so aesthetics matter).

### Hard requirements (must use these Microsoft SDK surfaces)

**Screen context capture — Windows UI Automation (UIA).** Verified against Microsoft Learn.
- Use the **COM UIA3 client** (`IUIAutomation` / `IUIAutomationTextPattern` / `IUIAutomationValuePattern`)
  via **CsWin32** source-gen interop (preferred for WinUI 3) — not the legacy WPF-era managed
  `System.Windows.Automation` (still works, but UIA3 COM is the current recommended surface).
- Flow: `GetFocusedElement()` → `TextPattern.GetSelection()` / `DocumentRange.GetText(n)` for
  surrounding text; `ValuePattern` for simple field content. Win32 fallback:
  `GetForegroundWindow` + `GetWindowText` + `QueryFullProcessImageName` for app/window identity.
- **Parity requirement (mirror Kivi macOS AX behavior): never read password/secure fields.** Check
  `IsPassword` (UIA `IsPasswordAttribute` / control-type + password style) and bail to empty context.
- Perf note (from docs): `TextPattern` is cross-process, no caching — retrieve **moderate blocks**
  in one `GetText` call, don't char-by-char. Snapshot context at hold-*start*, run concurrent with STT.
- Docs: UIA Text pattern `IUIAutomationTextPattern`, `IUIAutomationTextRange::GetText`.

**Weekly driver-update resilience — NAudio (WASAPI) + device-change handling.** Verified against
Microsoft Learn Core Audio (MMDevice/WASAPI stream-routing).
- Capture via NAudio **`WasapiCapture`** (WASAPI wrapper), format `WaveFormat(16000,16,1)` mono PCM16.
- Register **`IMMNotificationClient`** via `MMDeviceEnumerator.RegisterEndpointNotificationCallback`;
  handle `OnDefaultDeviceChanged`, `OnDeviceStateChanged` (`DEVICE_STATE_UNPLUGGED`/`NOTPRESENT`),
  `OnDeviceRemoved` → tear down and **re-open capture on the new default endpoint**.
- **Doc rules to obey:** callbacks must be **non-blocking**; never call
  Register/UnregisterEndpointNotificationCallback *inside* a callback; never release the final
  MMDevice ref in a callback. → Marshal reinit to a worker thread.
- **Retry/backoff specifically around mic init** (device busy right after a driver update is
  transient): exponential backoff (e.g. 100ms→200→400…, cap ~2s, N tries) before surfacing an error.
- **Re-enumerate devices at session start** (each dictation) rather than caching a device handle,
  so a driver swap between sessions can't leave us pointing at a dead endpoint.

### WinUI 3 stack + interop notes to carry forward
- **Framework:** Windows App SDK / WinUI 3, `Microsoft.UI.Xaml.*`, XAML + MVVM. Target .NET 8/9.
- **Tray icon:** `H.NotifyIcon` (has a WinUI 3 build) — WinUI 3 has no built-in `NotifyIcon`.
- **Recording overlay:** borderless always-on-top window via `AppWindow` — set
  `OverlappedPresenter` (`IsAlwaysOnTop = true`, no titlebar/border). Click-through needs
  `WS_EX_TRANSPARENT`/`WS_EX_LAYERED` via P/Invoke `SetWindowLongPtr` on the `HWND`
  (`WindowNative.GetWindowHandle`).
- **Global hotkey / mic / paste / context:** unchanged from the plan — `SetWindowsHookEx`
  low-level hook, NAudio/WASAPI, `SendInput`, UI Automation. These are OS-level and framework-
  independent; WinUI 3 just needs the `HWND` for window-tied calls.
- **Packaging watch-outs (WinUI 3 friction):** decide **packaged (MSIX) vs unpackaged** early —
  unpackaged is simpler for a WiX/`installer.exe` distribution (POA #6) and avoids MSIX identity
  requirements, but needs the Windows App SDK bootstrapper. Self-contained publish + trimming to
  stay under the <100MB budget.

---

## Key source URLs (for implementation reference)

Original (zachlatta):
- Pipeline: `Sources/AppState.swift` · STT: `Sources/TranscriptionService.swift`
- Cleanup + prompts: `Sources/PostProcessingService.swift` · Context: `Sources/AppContextService.swift`
- Model params: `Sources/ModelConfiguration.swift`
- (raw base: `https://raw.githubusercontent.com/zachlatta/freeflow/main/<path>`)

Windows port (stha-hardik):
- `src/freeflow/{hotkey_service,audio_service,paste_service,context_service,groq_client,app,settings}.py`
- (raw base: `https://raw.githubusercontent.com/stha-hardik/freeflow-windows/main/<path>`)

Advanced fork (mrinalwadhwa):
- `FreeFlowKit/Sources/FreeFlowKit/Services/PolishPipeline.swift` (deterministic cleanup — port this)
- `FreeFlowKit/Sources/FreeFlowKit/Prompts/PolishPrompt*.swift` (prompts)
- Providers: `Services/OpenAI{Chat,Dictation,Realtime}*.swift`
- (raw base: `https://raw.githubusercontent.com/mrinalwadhwa/freeflow/main/<path>`)
