# Kivi Onboarding Flow + Persistent Orb Overlay

> **Scope.** This corrects course from `docs/superpowers/specs/2026-07-20-kivi-ui-figma-retrofit-design.md`,
> whose Tasks 3-4 (tray icon, always-visible orb tied to a 7-item settings nav with
> 5 unbuilt "coming soon" pages) were reverted (commit `8d590b8`) after not
> matching the actual product ask. This spec replaces that scope with the real
> one: a first-run onboarding flow (Login → Permissions → Config), after which
> a persistent orb overlay runs — closer to Wispr Flow's UX — using the
> existing `Kivi.Core`/`Kivi.Platform` dictation engine underneath.
>
> **Kept from the reverted work:** the WinUI3 project conversion, real Figma
> design tokens (`Kivi.App/Themes/Tokens.xaml`), and the `App.xaml.cs` DI
> composition root (commits `fe81ccb`/`a3cdaee`/`05beff4`/`0adf155`) — genuine
> foundation, not scope creep.
> **Recovered, not rebuilt:** `KiviOrbControl`, `OverlayWindow`, and the Win32
> click-through interop from the reverted commits (`63f81f7`/`468a5a7`) — sound
> work, cherry-picked and fixed rather than rewritten from scratch.
>
> **Reference inputs:** `ui/04 - mockups.png` (login screen, "two permissions,
> then you talk" screen, "kivi on the desktop" orb postures, settings screen),
> `ui/02 - brand.png` (orb state marks/postures), `ui/components/fig-tokens.css`.

---

## 1. Onboarding gate and persistence

A new `AppConfig.OnboardingCompleted` (bool, default `false`) field, persisted via
the existing `IAppConfigStore`/`JsonAppConfigStore` (`%APPDATA%\Kivi\settings.json`)
— no new storage mechanism needed.

**Startup logic in `App.xaml.cs`:**

```
OnLaunched:
    load AppConfig (existing JsonAppConfigStore.Load())
    if !AppConfig.OnboardingCompleted:
        show OnboardingWindow, starting at the Login page
    else:
        real-check current OS microphone permission
        if denied:
            show OnboardingWindow, starting directly at the Permissions page
            (Login and Config are skipped — they already completed)
        else:
            proceed straight to the orb, no window shown
    create OrbOverlayWindow (always, once the above resolves)
```

Onboarding only runs end-to-end once. A later mic-permission revocation
re-triggers only the Permissions page, not the whole flow — Login and Config
stay marked complete.

`OnboardingCompleted` is set to `true` only after the Config page's "Done"
action, via `IAppConfigStore.Save(AppConfig)` — same persistence path already
used for every other setting.

---

## 2. The three onboarding pages

Hosted in one `OnboardingWindow.xaml(.cs)` (a normal chromed window, not
borderless/click-through) with a `Frame` that navigates between three pages,
mirroring the `Frame.Navigate`/`ContentFrame.Content =` pattern already used
correctly in the reverted Task 4's `SettingsWindow` (kept as a reference
pattern, not reused code — that file itself was reverted).

### 2.1 Login page
- "Continue with Google" button — **pure UI stub**: clicking it immediately
  navigates to the Permissions page. No OAuth, no browser popup, nothing sent
  or stored. This is deliberately non-functional per this pass's "UI flow
  only, no backend" scope.
- "Use work email instead" — a link/button with the same stub behavior.
- **No "Continue with Apple" button** — dropped entirely; Apple ID sign-in has
  no meaning on Windows and the design's macOS-only option isn't ported.

### 2.2 Permissions page
- **Microphone** — a real check against Windows' actual microphone-permission
  API. Shows genuine granted/denied state, not a stub. If denied, the page
  should offer a way to open Windows' privacy settings (the standard
  `ms-settings:privacy-microphone` URI) and a way to re-check without
  restarting the app.
- **Accessibility** — informational only, always shown as granted. Windows'
  UI Automation (what Kivi actually uses for screen-context capture) requires
  no separate OS-level permission grant the way macOS's Accessibility API
  does. Copy is rewritten for Windows: something like "Kivi reads the focused
  app and window title to clean up your text better" rather than the design's
  macOS-oriented "Accessibility" framing.
- Hotkey badge shown at the bottom of this screen reads "Right Ctrl" (not the
  design's `fn`), consistent with the hotkey correction already made
  elsewhere in this project.
- "Continue" advances to the Config page only once mic permission is granted
  (button disabled/greyed while denied).

### 2.3 Config page
- **Hotkey** — a real hotkey-capture control. Clicking it and pressing a key
  combination rebinds the dictation hotkey at runtime (see §4 — this requires
  a genuine `Kivi.Core`/`Kivi.Platform` capability addition, not just UI).
  Defaults to Right Ctrl.
- **Language** — a picker bound to `AppConfig.TranscriptionLanguage` (the
  existing STT input-language field — "speak any of these" per the design
  copy). Not `OutputLanguage`; that field is untouched by this page.
- **Orb accent color** — a color picker that sets a new `AppConfig.OrbAccentColor`
  (string, hex) field, independent of the app's light/dark theme. This
  recolors the orb's active-state dots (see §3).
- **Launch at login** — a real toggle. Backed by a standard Windows Startup
  mechanism (registry `Run` key or Startup-folder shortcut — implementation
  detail for the plan), not a new `Kivi.Core` concept.
- **Screen context** — a real toggle bound to a new `AppConfig.ScreenContextEnabled`
  (bool, default `true`) field. When off, `DictationOrchestrator` skips calling
  `IScreenContextProvider.CaptureContextAsync` entirely (passes an empty
  string as context instead) — a small, contained conditional in already-
  working code, not new capability.
- **Explicitly excluded from this page** (per this pass's scope): sound-on-paste,
  memory, incognito dictation, clear-history. The first is a nice-to-have net-new
  feature deferred to a later pass; the latter three depend on the
  transcript-history storage system that doesn't exist yet (the same gap that
  made the reverted Settings nav's History/Personas/Memory stubs premature).
- "Done" button: persists all of the above via `IAppConfigStore.Save`, sets
  `OnboardingCompleted = true`, closes `OnboardingWindow`, and hands off to
  the orb.

---

## 3. The persistent orb overlay

Recovered from the reverted commits (`63f81f7` `KiviOrbControl`, `468a5a7`
`OverlayWindow`, plus the Win32 click-through interop), with two real bugs
fixed and the tray dependency removed entirely:

**Bug fixes required during recovery:**
1. **Fullscreen black-window bug** — the reverted `OverlayWindow` presented as
   a fullscreen black window disrupting the taskbar instead of a small
   bottom-center pill. Root cause to be confirmed during implementation (most
   likely the `AppWindow.Resize()`/positioning calls not running before first
   paint, or a presenter/backdrop configuration issue specific to a
   borderless click-through window) — this must be root-caused and fixed, not
   worked around.
2. **No tray window in this design at all** — the reverted `TrayWindow` never
   configured a hidden/off-screen presenter, so `Activate()`-ing it showed a
   default-titled "WinUI Desktop" window. This entire class of bug is
   eliminated by this spec's decision to have **no tray icon** — the orb is
   the only UI surface once onboarding completes. (If a way to reopen the
   Config page later is wanted, that's a future addition — out of scope here,
   consistent with "just the orb, like Wispr Flow.")

**Behavior (unchanged from the reverted design, since it was sound):**
- Persistent, bottom-center anchored, borderless, always-on-top, click-through
  (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW`).
- Full posture system: rest pill (39×15), woken (61×61), satellites (23×23),
  box (322×108), sized via `AppWindow.Resize()` per `RecordingState`.
- Full 7-state color mapping (Idle/Listening/Processing/Speaking/Waiting/Done/Error)
  via the existing semantic `Overlay*Brush` tokens in `Tokens.xaml`.
- Dot-matrix kiwi silhouette, procedurally sampled from the recovered
  `kivi-mask.png` trace onto a 24-column grid (the sampling algorithm's
  aspect-ratio bug found and fixed during the reverted Task 3 is kept fixed).

**New in this pass:** the orb's accent color is tinted by
`AppConfig.OrbAccentColor` (from the Config page) rather than always the
fixed `KiviColorLegGreen` primitive. Precisely: the **Listening, Speaking, and
Done** states resolve their orb color through the user-configurable accent
color. **Idle, Processing, Waiting, and Error** keep their fixed design-token
colors (`OverlayIdleBrush`, `OverlayProcessingBrush`, `OverlayWaitingBrush`,
`OverlayErrorBrush`) — these communicate distinct system/pipeline states
(neutral-at-rest, working, rate-limited, failed) whose meaning depends on
their specific colors, so they must not be overridden by a user's brand-color
choice. Only the "your voice is being heard / delivered successfully" states
carry the personal accent.

---

## 4. New Kivi.Core/Kivi.Platform capability: runtime hotkey rebinding

The only genuine new engine capability in this pass. Today `IHotkeyService`
(`Kivi.Core/Abstractions/IHotkeyService.cs`) has no way to change which key
triggers hold-to-talk — `LowLevelKeyboardHookService` hardcodes Right Ctrl.

**Interface addition:**
```csharp
public interface IHotkeyService
{
    event Action? HoldStarted;
    event Action? HoldEnded;
    void Start();
    void Stop();
    void SetHotkey(int virtualKeyCode); // NEW
}
```

`LowLevelKeyboardHookService` stores the bound virtual-key code as mutable
state (default: Right Ctrl's VK code) and checks against it in the low-level
keyboard hook callback instead of a hardcoded constant. `SetHotkey` can be
called at runtime (while the hook is already running) to change the bound key
without restarting the hook.

The Config page's hotkey-capture control listens for the next keypress the
user makes while the control has focus, resolves it to a virtual-key code,
calls `IHotkeyService.SetHotkey(...)`, and persists the choice to `AppConfig`
(new field: `AppConfig.HotkeyVirtualKeyCode`, int, default = Right Ctrl's VK
code) so it survives restarts (re-applied via `SetHotkey` on next launch).

---

## 5. What does NOT change

- `Kivi.Core`'s Groq HTTP clients, prompts, polish pipeline, orchestrator
  state machine (`RecordingState`'s 7 values, `Waiting`/`Done` transitions)
  from the prior plan — all unchanged and still correct.
- `Kivi.Platform`'s audio capture, screen-context provider (UIA), paste
  service, DPAPI secret store — unchanged, aside from the new
  `IHotkeyService.SetHotkey` addition.
- The WinUI3 project conversion, design tokens, DI composition root
  (`App.xaml.cs`) — kept as-is from the prior (approved, not reverted) work.
- No tray icon, no settings-nav shell, no history/personas/presets/memory/
  analytics pages — all correctly out of scope, not deferred features to
  revisit in this pass.

---

## 6. Open items deliberately deferred

- Sound-on-paste toggle — small, self-contained, not in this pass.
- Orb position configurability (design mentioned "location of the pill") —
  bottom-center fixed for now; a position picker is future work.
- A way to reopen the Config page after onboarding (no tray icon in this
  design) — deferred; if wanted, a minimal future addition, not a gap in this
  spec's own scope.
- Memory/incognito dictation/clear-history — blocked on transcript-history
  storage, which doesn't exist yet; same gap noted in the previous spec.
