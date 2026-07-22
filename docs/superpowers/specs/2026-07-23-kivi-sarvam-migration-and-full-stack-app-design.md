# Kivi: Sarvam Migration + Full-Stack App Design

**Date:** 2026-07-23
**Status:** Approved design, pending implementation planning

## Context

Kivi is a Windows dictation app (`Kivi.Core`/`Kivi.Platform`/`Kivi.App`, WinUI3) currently using Groq for STT (Whisper) and text polish (LLM cleanup/rewrite). It has a rough onboarding flow (Google login is a UI stub, no real OAuth), a rough main app window (sidebar with only Record/History wired, History shows hardcoded sample rows), no tray icon (dependency referenced but unused), and no installer.

This spec covers a five-part expansion, in build order:

1. Swap Groq for Sarvam across STT and text-polish
2. Rebuild onboarding: real Google identity capture, preferences, an interactive walkthrough
3. Make Kivi tray-resident (survives main window close)
4. Flesh out the main app window to match the design mockups (6 sidebar sections)
5. Ship a modern, single-screen installer

**Standing note:** the mockups in `ui/` (`01-04*.png`, `kivi design.png`, `ios walkthrough.mov`) were designed for Kivi's **iOS** app. Treat them as design-intent references (color, layout, tone, information architecture) for the Windows app, not literal specs — platform-specific details (hotkeys, gestures, OS chrome) are adapted to Windows conventions already proven in this codebase, not copied. Concretely: the mockups show **fn** as the dictate hotkey; Windows Kivi keeps its existing **Right Ctrl** hold-to-talk + double-tap-for-hands-free hotkey, unchanged.

---

## Part 1: Sarvam Migration

### Model choice

- **STT: `saaras:v3`, `mode=codemix`.** Sarvam's current recommended ASR model (Saarika v2.5 is legacy/being phased out). `codemix` mode outputs "English words in English, Indic words in native script" — the correct behavior for Hinglish dictation. Supports 23 languages including `hi-IN`/`en-IN`. Kivi does push-to-talk record-then-send (not continuous streaming), so the synchronous REST endpoint is the right fit — no WebSocket streaming complexity needed.
- **Polish/rewrite: `sarvam-30b` primary.** Genuinely OpenAI-compatible chat completions (`choices[0].message.content`), 64K context (ample for cleanup/rewrite), and roughly 3x the rate-limit headroom of `sarvam-105b` at each tier. `sarvam-105b` (128K context) is the fallback-model slot, mirroring the existing primary/fallback chain — escalate to it only if `sarvam-30b` cleanup quality proves insufficient on Hinglish in practice.

### API shape (confirmed against Sarvam docs)

- STT: `POST https://api.sarvam.ai/speech-to-text`, multipart (`file`, `model=saaras:v3`, `mode=codemix`, `language_code`), auth header `api-subscription-key: <key>` (not Bearer). Response: `{request_id, transcript, language_code, language_probability, timestamps, diarized_transcript}`.
- Chat: `POST https://api.sarvam.ai/v1/chat/completions`, standard OpenAI request/response shape, `Authorization: Bearer <token>` (or `api-subscription-key`), supports `temperature`/`max_tokens`/`top_p`/`reasoning_effort`/streaming SSE.

### Implementation

- New `SarvamSttEngine : ISttEngine` replaces `GroqSttEngine`. Because the auth header and multipart fields differ from Groq's shape, this needs a genuine new implementation, not a base-URL swap — `OpenAiCompatibleClient` gains a Sarvam-compatible transcription method (or a small sibling client) rather than reusing Groq's method signature as-is. The existing hallucination filter (`no_speech_prob` from Whisper segments) has no direct Sarvam equivalent; either drop it or reimplement a low-confidence filter against Sarvam's flatter `language_probability` field — decide during implementation based on whether false transcriptions show up in testing.
- New `SarvamPolishClient : IPolishClient` replaces `GroqPolishClient`. Keeps the existing `CleanupAsync`/`RewriteAsync`/`EnteringCooldown` contract, the primary/fallback model chain, the per-model 429 cooldown, and the prompt-injection guard (`AppearsToHaveExecutedInstruction`) — these are provider-agnostic behaviors that carry over unchanged. Reuses `OpenAiCompatibleClient.PostChatCompletionAsync` since Sarvam's chat completions are OpenAI-shaped.
- `AppConfig.TranscriptionBaseUrl`/`ChatBaseUrl` defaults change to `api.sarvam.ai`; model name fields become Sarvam model names.
- `.env.example` and `ISecretStore` usage: `GROQ_API_KEY` → `SARVAM_API_KEY`.
- Tests: port `GroqSttEngineTests.cs`/`GroqPolishClientTests.cs` to `SarvamSttEngineTests.cs`/`SarvamPolishClientTests.cs`, reusing the existing `FakeHttpMessageHandler`/`SequencedFakeHttpMessageHandler` infrastructure, asserting against Sarvam's response shape.
- Old Groq classes are deleted outright, not kept behind a flag.

### Key distribution (single shared key, own-your-risk model)

The Sarvam API key ships **embedded with the app, owned by the developer** — not entered by each end user. This means:

- **No API-key onboarding step.** Anyone who installs the exe gets working dictation immediately after granting mic/accessibility permissions — no Sarvam account, no key of their own.
- **All installs draw on one account.** Every person who runs the exe is spending the developer's Sarvam credits; there is no per-user attribution or usage cap unless one is added later.
- **The key is not truly secret once shipped.** A reasonably technical user can extract it from the installed app (decompiling the .NET assembly, or self-MITM'ing the app's own HTTPS traffic on their own machine, since they control both ends). Once extracted, it can be used entirely outside Kivi, indistinguishably from Kivi's own traffic on Sarvam's side.
- **Practical guardrails for this phase:**
  - Ship the key in a separate local config/appsettings file next to the exe (loaded at startup and seeded into `DpapiSecretStore`), not as a hardcoded C# string literal — allows rotating the key without a rebuild if it leaks.
  - Treat the key as revocable/rotatable on short notice; watch Sarvam's usage dashboard for anomalous spikes.
  - This model is appropriate for a small, trusted circle (friends/beta testers). It is explicitly **not** a plan for public/wide distribution — if the exe is ever shared beyond people the developer trusts and can reach, revisit this in favor of a backend-proxy model (app calls a server the developer controls; the server holds the key and can meter/cap usage; the raw key never reaches any client machine). That tradeoff is documented here so the choice is deliberate, not accidental.

---

## Part 2: Onboarding

Rebuilds the existing `OnboardingWindow` → Frame-navigated page flow (`Kivi.App/Views/Onboarding/`):

1. **Login page** (replaces today's UI-only stub). "Continue with Google" launches the system default browser to Google's OAuth consent screen; a localhost loopback listener catches the redirect, exchanges the code, and decodes the id_token client-side for name/email/picture. **No backend** — this is identity capture for personalization only, not account creation or sync. Stored as local profile fields on `AppConfig`. "Use work email instead" remains as the existing fallback path.
2. **Preferences page** (new). Language selection (multi-select chips, matching the Settings mockup's language list) and "what do you primarily use typing for" (enum: Emails, Messaging, Notes, Code/Technical, Social, Other) — stored for display/analytics only, not wired into the polish prompt.
3. **Permissions page** (exists — `PermissionsPage`). Kept as-is: mic + accessibility grants.
4. **Interactive walkthrough** (new, replaces any placeholder walkthrough content). Real hands-on steps, not animated/scripted:
   - "Hold Right Ctrl and say something" against a practice text field — real orb, real STT round-trip via the new Sarvam engine, real paste into the field. Advances on successful completion; includes a Skip escape hatch.
   - "Double-tap Right Ctrl for hands-free" — waits for the actual gesture.
   - This validates the real mic/hotkey/STT pipeline works end-to-end before the user reaches the main app, catching permission or connectivity issues early.
5. **Kivi preferences page** (extends existing `ConfigPage` content shown during onboarding). Orb color and screen position.

Completion sets `AppConfig.OnboardingCompleted = true`, same gate as today in `App.xaml.cs`. `OnboardingWindow.ForSettingsReentry()` continues to work as the re-entry point for changing these later from Settings.

---

## Part 3: Tray-Resident Background App

- Wire up the already-referenced-but-unused `H.NotifyIcon.WinUI` package: a `TaskbarIcon` with Kivi's icon, composed in `App.xaml.cs` alongside existing DI registrations.
- Tray context menu: **Open Kivi** (show/focus `MainAppWindow`), **Pause dictation** (toggles hotkey listening off/on without closing anything), **Settings** (opens `MainAppWindow` directly to the Settings page), **Quit Kivi** (real exit: stops orb, hotkey listener, tray icon, process). Left-click tray icon = same as "Open Kivi".
- `MainAppWindow`'s close (X) is intercepted: `Hide()` instead of destroying the window, unless the close was triggered via the tray's "Quit" command (tracked with a flag so Quit still results in a real exit).
- Orb (`OverlayWindow`) and hotkey listener lifetime must be independent of `MainAppWindow` lifetime — owned by the app-level composition root. Verify during implementation planning how `App.xaml.cs` currently manages window lifetimes, since this determines how much restructuring "close to tray" actually requires.

---

## Part 4: Main App Window

Sidebar (from `04 - mockups.png`): **Record, History**, then a **Workspace** group — **Personas, Presets, Memory, Analytics** — then **Settings** pinned at the bottom. All six are real, navigable sidebar items — none hidden or marked "coming soon."

### Real, end-to-end

- **Record** — live dictation view: hero text ("Your voice, polished"), live transcript box streaming partial recognized text (reuses existing `OverlayViewModel` partial-transcript plumbing), dictate/edit hotkey pills reflecting the actual configured hotkeys.
- **History** — real persisted transcript storage. New `Kivi.Core` store (schema: transcript text, timestamp, target app, language, duration) replacing `HistoryPage`'s hardcoded sample rows. Search bar, per-app/language/time display matching the mockup.
- **Settings** — expand `ConfigPage`/`ConfigViewModel` to the mockup's full layout: hotkeys (dictate + hey-kivi capture, press-and-hold delay), language chips, behavior toggles (launch at login, screen context, incognito dictation, sound on paste), privacy (clear all history — now meaningful since History persists real data).
- **Analytics** — derived from the real History store (no separate persistence needed): total words, words/min, time spoken, dictation count, words-over-time chart, top-apps breakdown, dictation-type breakdown (dictation vs. hey-kivi rewrite).

### UI-only (mock/in-memory data, no persistence, no backend wiring)

- **Personas** — persona list (e.g. work messaging, email, developer, casual + "new persona"), assigned-apps display, tone-rule list, attached-presets display. Seeded from an in-memory mock dataset at launch; add/edit/delete mutate the in-memory list so the UI feels functional, but nothing persists across restarts and nothing affects actual dictation/polish behavior.
- **Presets** — reusable instruction list, same in-memory-only treatment.
- **Memory** — corrections/vocabulary list view, same treatment.

This split means Personas/Presets/Memory get a complete, demoable UI now, while the real backend work (per-app persona auto-detection via `IScreenContextProvider`, persona/preset persistence, memory-informed prompt assembly) is deferred to a future spec once the UI/UX is validated.

---

## Part 5: Installer

- **Framework: Velopack** (successor to Squirrel.Windows) — modern, single-EXE installer with a minimal branded progress UI and built-in future auto-update support.
- **Validation needed first:** Velopack's compatibility with Kivi.App's current unpackaged `WindowsAppSDKSelfContained=true` configuration is not confirmed from documentation alone. The implementation plan must start with a small spike — package a minimal build and confirm it installs/launches correctly — before committing further installer work.
- **Installer UI**: single fixed-size branded window (Kivi logo, "Setting up Kivi..." text, progress bar), no license page, no install-location picker, no component checkboxes. Auto-launches Kivi on completion.
- **Install behavior**: per-user install to `%LocalAppData%\Kivi` (no admin elevation), Start Menu shortcut, registers for future auto-updates.
- **Signing**: unsigned for now. SmartScreen will show a warning on first run regardless of installer framework — this is expected and out of scope for this spec (to be handled separately once a code-signing certificate is obtained).
- First launch after install flows directly into onboarding via the existing `App.xaml.cs` gate on `AppConfig.OnboardingCompleted`.

---

## Open Items Carried Into Planning

- Confirm `App.xaml.cs`'s current window-lifetime management before scoping the tray "close to hide" change.
- Decide whether to keep/replace the STT hallucination filter now that `no_speech_prob` has no Sarvam equivalent.
- Spike Velopack packaging against the unpackaged Windows App SDK build before committing to it as the installer framework.
- Decide on Record page theming (mockups show Record in a dark "ink forest" theme distinct from the rest of the app) — confirm whether this is a deliberate per-page theme or should be unified.
- Revisit the shared-key distribution model before any distribution beyond a small trusted circle — see "Key distribution" under Part 1.
