# Kivi UI — Figma Design Retrofit

> **Scope.** `docs/impl-03-winui3-kivi-ui.md` already specifies a complete WinUI 3
> architecture (MVVM, borderless click-through overlay window, H.NotifyIcon tray,
> NavigationView settings shell, token-swap theming, unpackaged deployment) built
> against a **placeholder** token set, explicitly designed so a real design could be
> dropped in later by editing `Themes/` only. That design has now arrived: a Figma
> design system (`ui/` folder — exported PNGs of foundations/brand/components/mockups
> pages, plus a generated React component reference in `ui/components/`).
>
> This spec is a **retrofit**, not a rewrite. impl-03's architecture stays. This
> document covers exactly what changes to bring the real Kivi design in, plus the
> handful of structural decisions that go beyond a pure token swap (state model
> expansion, the orb replacing the pill, nav surface growing to match the design).
>
> **Reference inputs:** `ui/01 - foundation.png`, `ui/02 - brand.png`,
> `ui/03 - components.png`, `ui/04 - mockups.png`, `ui/kivi design.png`,
> `ui/components/*.jsx` + `fig-tokens.css` + `fig-typography.css`.
> **Prereqs read:** `docs/impl-03-winui3-kivi-ui.md` (full architecture, kept as-is
> except where noted below), `docs/overview.md`.

---

## 1. State model expansion (Kivi.Core — real engine change)

`Kivi.Core/Orchestration/RecordingState.cs` currently has 5 values. The Figma design's
"state marks" (brand page) defines 7. This is not cosmetic — it's an orchestrator
contract change.

```csharp
// Kivi.Core/Orchestration/RecordingState.cs
public enum RecordingState { Idle, Listening, Processing, Speaking, Waiting, Done, Error }
```

| Design state | Maps to | Nature of change |
|---|---|---|
| `Idle` | unchanged | — |
| `Listening` | unchanged (hold-to-talk, mic capturing) | — |
| `Processing` | replaces `Transcribing` (STT + cleanup in flight) | rename |
| `Speaking` | replaces `Pasting` (SendInput injection) | rename only — **no TTS/voice-output feature**; "speaking" is the design's label for the paste-injection stage |
| `Waiting` | **new** — surfaces the Groq 429 rate-limit cooldown that `GroqPolishClient` already tracks internally (`_cooldownUntil` dictionary) but never exposes | `GroqPolishClient` needs to signal "entering cooldown" outward (e.g. via a callback/event `IPolishClient` exposes, or the orchestrator polling `InCooldown` before calling) so the orchestrator can transition to `Waiting` instead of the state jump being invisible |
| `Done` | **new** — transient ~1-2s success-flash state shown after a successful paste, then auto-returns to `Idle` | `DictationOrchestrator.RunPipelineAsync` sets `Done` instead of `Idle` immediately after `_paste.InjectTextAsync` succeeds, then a short delay (`Task.Delay` or a timer) transitions to `Idle` |
| `Error` | unchanged | — |

**Renaming impact:** any code that pattern-matches on `RecordingState.Transcribing` or
`RecordingState.Pasting` needs updating to `Processing`/`Speaking`. This includes
`DictationOrchestrator.cs`'s own `SetState()` calls and the console logger's
`orchestrator.StateChanged += s => logger.LogInformation(...)` line in `Program.cs`
(no logic change there, just compiles against the renamed enum values).

**Kivi.Core.Tests impact:** any existing orchestrator tests asserting on
`RecordingState.Transcribing`/`.Pasting` need the same rename. The `Waiting`/`Done`
transitions get new test coverage (TDD, per the existing project convention): a test
that a 429 response drives the orchestrator through `Waiting` before retrying, and a
test that a successful pipeline run passes through `Done` before settling on `Idle`.

---

## 2. Design tokens (Kivi.App/Themes/Tokens.xaml — real values replace placeholders)

Layer 1 (primitives) and Layer 2 (semantic `ThemeDictionaries`, `Light`/`Dark`/
`HighContrast`) structure from impl-03 §2 is unchanged. Only the *values* change,
sourced directly from `ui/components/fig-tokens.css`:

- **Colors** — direct port of the `--color-*`/`--brand-*` CSS custom properties. The
  CSS file already has both light (`:root`) and dark (`:root[data-theme="dark"]`)
  blocks with exact `rgb()` triples for every semantic role (`paper`, `fg1-4`,
  `border1-3`, `brandInk`, all 6 `state*` colors + their `*bg` variants, `positive`,
  `warning`, `danger`). This maps close to mechanically onto the existing
  `Light`/`Dark` `ResourceDictionary` blocks.
- **Spacing** — `--space-s1` through `--space-s32` (2px base unit) replaces the
  placeholder `KiviSpace4/8/12/16/24` scale.
- **Radii** — `--radius-xs/sm/md/lg/xl/full` replaces `KiviRadiusPill`/`KiviRadiusCard`.
  Per the components page ("buttons — pills, never rounded-rects"), buttons
  specifically use `--radius-full` (9999) regardless of size.
- **Typography** — see §3 (fonts) below for the family decision. Type roles (display/
  heading/body scale) come from the foundations page's "type roles" section — sizes
  and line-heights to be read precisely from the Figma file directly (Dev Mode
  inspect) during implementation, since the PNG export of that section renders
  low-contrast and isn't reliably legible from the exported image alone.
- **HighContrast** dictionary — unchanged from impl-03 (still maps to `SystemColor*`,
  the design system has no explicit high-contrast spec to port).

**`Themes/Icons.xaml`** — new: path-geometry keys for the orb dot-matrix mark
construction parameters (silhouette mask reference, 24-column grid, dot size/gap) live
here rather than as literal values in the control, per impl-03's existing pattern of
keeping visual constants in `Themes/`.

---

## 3. Fonts

Two families:

- **Space Grotesk** (wordmark only — weight 500, -4% tracking, green dotted "i"s) —
  free, OFL-licensed (Google Fonts). Bundle as `Assets/Fonts/SpaceGrotesk-Medium.ttf`,
  register as `KiviWordmarkFontFamily`. Used **only** for the literal "kivi" logotype,
  nowhere else.
- **Body/heading text** — the design specifies **Matter** (Regular/Medium/SemiBold)
  and **Season Mix Medium** (serif numerals), both commercial fonts. The Figma file
  itself flags them as missing fonts (fell back to Inter) — meaning even the design
  file doesn't have them loaded. **Decision: use Inter** (free, OFL, metrically close)
  as the real typeface for `KiviFontFamily` now. This is not a placeholder — it's the
  actual shipping choice unless/until Matter/Season Mix licensing is separately
  confirmed and swapped in later (a pure `Tokens.xaml` edit if that happens, per
  impl-03's existing swap workflow).
- Space Mono (seen in component `fontFamily` strings for hotkey badges / metadata
  labels like "LIVE", "hi-IN · auto") — also free (Google Fonts), bundle alongside
  the above.

---

## 4. The orb replaces the pill

This is the one genuinely structural change to impl-03's view layer.

**What impl-03 has today:** `OverlayWindow` hosts a `Border` (the "pill") containing a
`StackPanel` with a status-glyph `Grid` (`Ellipse` for listening-pulse, `ProgressRing`
for processing, `Ellipse` for error) plus a `TextBlock` status label.

**What replaces it:** a new custom control, **`KiviOrbControl`**
(`Kivi.App/Controls/KiviOrbControl.cs`), that procedurally renders the dot-matrix kiwi
silhouette described on the brand page:
- Traced 120×162 alpha mask (bundled as a bitmap resource) sampled onto a 24-column
  dot grid — circular dots, size/gap driven by tokens, opacity/color per dot derived
  from mask coverage at that grid cell.
- Dot fill color is bound to the current `RecordingState`'s semantic color token
  (`stateIdle`/`stateListening`/`stateProcessing`/`stateSpeaking`/`stateWaiting`/
  `positive` for Done/`stateError`) — the orb **is** the status light, per the brand
  page's own framing ("the mark is a status light").
- Renders via `Win2D`/`CompositionDrawingSurface` or a `Canvas` of `Ellipse`s
  (implementation detail to resolve during planning — the design doesn't require a
  specific rendering technique, only the visual output and its motion behavior).

**Postures** (rest pill 39×15 / woken 61 / satellites 23 / box 322×108, from the
mockups page) are **not** separate controls or windows — they're `AppWindow.Resize()`
calls keyed off `RecordingState`, using impl-03's existing borderless/topmost/
click-through window mechanics (§3.1-3.3) unchanged. Only the target size passed to
`Resize()` changes from the placeholder `240×64` to these real per-state dimensions.

**Motion:** a Composition-based breathing animation (opacity/scale pulse) runs only
while `Listening`/`Processing`/`Waiting` — consistent with impl-03's existing idle-CPU
budget rule (§8: "idle ≈ 0 CPU... animation only runs while Listening"). `Done` and
`Error` are static (single frame), matching the brand page's own motion note
("reduced motion: one still frame").

**Click-through stays universal.** Even in the "box" posture (which visually shows a
transcript box, matching the `TranscriptBoxListening`/`TranscriptBoxHeyKivi`
components already in `ui/components/`), the box is **display-only** — no buttons,
no clickable affordances. `WS_EX_TRANSPARENT` stays on across all postures; impl-03's
already-noted fallback (toggle transparency on hover) is explicitly **not** built in
this pass, since Kivi's actual UX (hold hotkey, speak, release) never requires
clicking the overlay.

**Tray icons** — regenerated from the orb's dot-matrix mark at small fixed size for
the three states (idle/active/error), keeping impl-03's existing filename contract
(`Assets/Tray/idle.ico`, `active.ico`, `error.ico`) so `TrayViewModel`'s existing
icon-switch logic needs no code change, only new asset files.

---

## 5. Settings shell + navigation surface

`SettingsWindow`'s `NavigationView` (impl-03 §5) grows from 5 items to 7, matching the
Figma sidebar order: **record, history, personas, presets, memory, analytics,
settings.**

**Fully built this pass** (backed by real `Kivi.Core` state — no new storage needed):
- **Record** — not a settings page; this nav item focuses/shows the orb overlay
  (or a compact live view of current state). A shortcut, not new page content.
- **Settings** — impl-03's existing 5 sub-groups (Account/Models/Input/Text/
  Appearance), restyled with real tokens from §2. No structural change:
  - `Input`'s hotkey field already documents "default Right Ctrl (Fn doesn't map on
    Windows)" in impl-03's own settings table — already correct, no change needed.
  - `Text` page's custom vocabulary / macros lists map directly to
    `AppConfig.CustomVocabulary` / `AppConfig.Macros` — already modeled, no new
    backend.

**Stubbed this pass** (visible in nav, disabled/placeholder content) — **History,
Personas, Presets, Memory, Analytics.** Each renders a minimal "coming soon" page
(icon + label, styled with real tokens so it reads as intentional rather than
broken) rather than functional content. None of these have backing storage in
`Kivi.Core` today:
- **History** needs a transcript-history store (doesn't exist — nothing today
  persists past transcripts; `JsonAppConfigStore` only persists settings).
- **Personas / Presets** need a new data model entirely (grouping apps, tone rules,
  attached presets) — not represented anywhere in the current engine.
- **Memory** needs a name/phrasing-preference store — also new.
- **Analytics** could theoretically read `KiviMetrics`/OTel data, but that's
  console-only today (no persistence) — would need its own lightweight local store.

Each stub is an explicit candidate for its own future spec once its backend is
scoped — this pass intentionally does not improvise storage design for them.

**Unchanged from impl-03:** MVVM shape (§1), theming layering and runtime application
(§6), deployment strategy — unpackaged + self-contained WinAppSDK bootstrapper (§7),
performance budget and idle-CPU rules (§8). None of these depend on the specific
design values and remain correct as originally specified.

---

## 6. What does NOT change

- `Kivi.Platform` — no changes. Hotkey/audio/context/paste services are unaffected by
  any of the above.
- `Kivi.Core`'s Groq HTTP clients, prompts, polish pipeline logic — unchanged, aside
  from `GroqPolishClient` needing to surface its existing cooldown state outward (§1).
- impl-03's windowing mechanics (§3.1-3.3: `OverlappedPresenter`, click-through via
  `WS_EX_LAYERED`/`WS_EX_TRANSPARENT`, DPI-aware positioning) — reused as-is, only the
  resize target values and the control rendered inside the window change.
- Deployment/packaging decisions (unpackaged, self-contained, WiX-feeding publish
  output) — unchanged.

---

## 7. Open items deliberately deferred (not blocking this pass)

- Matter/Season Mix font licensing — revisit if/when confirmed; Inter is the real
  shipping font until then, not a temporary placeholder.
- History/Personas/Presets/Memory/Analytics backends — each its own future spec.
- Exact type-scale point sizes/line-heights from the foundations page — to be read
  directly from Figma (Dev Mode inspect) during implementation, since the exported
  PNG's "type roles" section is not reliably legible (dark-on-dark rendering issue in
  the export).
