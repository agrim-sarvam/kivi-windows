# MAP: main-window-shell-pages

Windows-only .NET port of `_reference/sarvam-kivi-electron/docs/maps/main-window-shell-pages.md`.
The main window is a **custom-drawn design system** (almost no stock controls), so a WinUI/XAML
clone reproduces it exactly with `PathGeometry` + theme brushes. Target: `Kivi.App/Views` (XAML) +
`Kivi.App/ViewModels` (MVVM). All token values are byte-exact (see `design-tokens.md`).

---

# Kivi — Main Window Shell + Pages Map (for the .NET port)

WinUI 3 + Composition. Everything is custom-drawn (no native controls except a few
`Toggle`/`Slider`/`TextBox`), so the HTML→XAML translation is 1:1 with brushes + inline
`PathGeometry`.

---

## 0. App shell & window

- **One main window (single-instance)**, custom title bar (`AppWindowTitleBar` with a drag region + own window controls top-right). No macOS traffic lights.
- **Default size 1180×760**; **min content size 980×640** (per-destination min from the auth gate — shell 980×640, onboarding 360×480, splash/signIn 480×560).
- Closing the window drops the process to a **resident agent** (orb + tray stay alive); does not terminate. (The window is disposable — see `menubar-onboarding-auth.md §0`.)
- **Fonts loaded before first render**: Matter, Matter SemiMono, Space Grotesk, Season Mix (see §2.5, `design-tokens.md §1`).
- **Theme**: resolved to a concrete light/dark from an `AppAppearance` (`system`/`light`/`dark`, persisted under `kiviAppearance`). System reads the live Windows app theme (`UISettings`). The whole app (orb/tray) matches in lockstep. Apply a **240 ms crossfade** on theme change.
- **RootView**: wraps the main window in an auth gate (see `menubar-onboarding-auth.md §3.1`); for the transcription MVP this renders the shell directly (anonymous local dev). A permissions gate sits between auth and shell when mic isn't granted.

---

## 1. Shell layout

Custom two-column layout (**NOT** a `NavigationView`) so the rail keeps exact DS styling:

```
┌─────────────┬─┬───────────────────────────────────┐
│  Rail       │1│  detail                            │
│  264px      │p│  ┌ PersistenceBanner (cond)        │
│  (or 76px   │x│  ├ ConnectivityBanner (cond)        │
│  collapsed) │ │  ├ Topbar (only for .clipboard)     │
│             │ │  ├ InvitationBanner (cond)          │
│             │ │  └ page(for section)  ← swap        │
└─────────────┴─┴───────────────────────────────────┘
```

- **Rail widths**: expanded **264**, collapsed **76** (`railCollapsedW`). Fold animation ease `cubic-bezier(0.2,0.8,0.2,1)`, duration `0.24 s`, disabled under reduced-motion.
- **Collapse toggle**: a sidebar-leading glyph, 15px, top-leading, top padding 14; keyboard shortcut **Ctrl+\\**. Persisted (`kiviRailCollapsed`).
- **Detail background** (behind every page incl. Record): canon canvas + `PaperGrain` + `ConstellationField` (dot grid). (§2.7)
- **No cross-section transition** — deliberate hard cut (an opacity crossfade caused nav lag). Page switch paints on frame 1.
- **`Topbar` (64px) renders ONLY for `.clipboard`.** Every other section owns its own header at a 44px origin. Topbar carries an ambient state chip mirroring orb state (hidden when idle).
- (The macOS `ScrollPocketDisabler` titlebar hack is **irrelevant** — just don't draw a titlebar separator.)

Page dispatch:
| section | page view |
|---|---|
| `.record` | `RecordPage` |
| `.history` | `HistoryPage` |
| `.clipboard` | `ClipboardPage` |
| `.styles`, `.presets` | `PersonasPage` |
| `.memory` | `MemoryPage` → `MemoryForestPage` |
| `.shortcuts` | `ShortcutsPage` |
| `.analytics` | `AnalyticsPage` |
| `.sharedTerms` | `SharedTermsPage` |
| `.leaderboard` | `LeaderboardPage` |
| `.settings` | `SettingsPage` |

---

## 2. Design tokens — THE reuse primitives

Two parallel palettes. Live pages read **`KDS.Canon`**; legacy pages read **`KDS.Theme`**. For the
clone, **build against Canon** — it's what Record/History/Memory/Personas/Shortcuts/Analytics/Settings/Leaderboard/SharedTerms
use. Full values in `design-tokens.md`; the essentials:

### 2.1 Canon palette — USE THIS
| role | light | dark |
|---|---|---|
| canvas | `#F6F3EA` cream | `#0C0E0C` forest-neutral |
| surface1 | `#FCFAF3` | `#161616` |
| surface2 | `#FFFFFF` | `#1E1E1E` |
| inkPrimary | `#20241F` | `#E9E7DD` |
| inkSecondary | `#5C6454` | `#A7A99E` |
| inkTertiary | `#666D5F` | `#84887D` |
| accent | `#41691E` moss/pine | `#8FCE6E` sage |
| accentWash | `rgba(65,105,30,0.08)` | `rgba(143,206,110,0.08)` |
| hairline | `rgba(32,36,31,0.14)` | `rgba(255,255,255,0.08)` |
| annotation (gold) | `#B98A2E` | `#D8B85C` |
| stateListening | `#D05C1E` | `#F8A86C` |
| stateProcessing | `#303CC8` | `#788CFF` |
| stateSettled | `#5E7A4E` | `#8FCE6E` |
| stateError | `#B81514` | `#B81514` |

Canon rule: **ONE accent per surface, ≤8% coverage**; red is errors only; hover/selection = the
accent wash.

### 2.2 Legacy Theme palette
Used only by `ClipboardPage`, `PillButton`, `StateChip`, `InlineNotice`, some banners. Light paper
`#F1F4EC`, card `#FFFFFF`, warmTint `#E7EEDD`, legGreen `#41691E`; dark app override sets
canvas→`#0C0E0C`, surface→`#161616`. Full conversational-state set + rank metals (gold/silver/bronze)
+ warning strip (`#A27224`/`#FFF8E6`). (Full ledger in `design-tokens.md §3a`.)

### 2.3 Spacing — base 2px
`2, 4, 6, 8, 12, 16, 20, 24, 32, 40, 48, 64`.

### 2.4 Radius & elevation
Radius `xs=4, sm=8, md=12, lg=20, xl=28, full=9999`. Cards use `lg` (20) legacy; most canon
surfaces 8–14. Shadow (only two): `l1 = 0 0 64px rgb(20,20,20)/0.08`; `l2 = same @0.16`. Zero-offset
"lifted, never floating". Layout: `contentColumn=980`, `pageHeaderTopInset=44`. Map the shadows to a
Composition `DropShadow` (Offset 0, BlurRadius 64) or `ThemeShadow`, tuned for alpha.

### 2.5 Typography (see `design-tokens.md §2`)
Faces: Matter, Matter SemiMono, `SpaceGrotesk-Medium` (display/headings), **`SeasonMix-Medium`**
(wordmark/marque serif). All UI copy is **lowercase** by convention. Semantic roles (display 40 /
marque 30 / title1 28 / title2 22 / title3 18 / body 15 / reading 16 / callout 14 / label 13 /
caption 12 / eyebrow 11 +0.08em / monoData 13). Numbers use **Indian-locale grouping** (1,00,000)
via a custom `NumberFormatInfo` (`en-IN` digit grouping).

### 2.6 Motion (see `design-tokens.md §5`)
fast 120 / base 200 / slow 300 / reveal 700 / pulse 1.6 s. Enter `cubic-bezier(0.2,0.8,0.2,1)`,
exit `(0.4,0,1,1)`, dialog overshoot `(0.16,1,0.3,1)`. Press micro: buttons `scale(0.96)`, mic
`0.94`. **No spring/bounce/parallax.** All honors reduced-motion. Reproduce the CSS béziers with
XAML `KeySpline`/Composition `CubicBezierEasingFunction` (exact control points).

### 2.7 Textures/background
- **PaperGrain**: 128×128 noise tile, tinted inkPrimary, opacity light 0.035 / dark 0.02, dark ×1.5. Removed under reduced-transparency. → a tiled `ImageBrush` / Win2D surface at those opacities.
- **ConstellationField**: 24px-pitch 2px dots in inkTertiary, opacity light 0.06 / dark 0.08, optional top fade mask. → a tiled dot brush / `PathGeometry` grid.

### 2.8 SurfaceLevel
`.surface(.canvas|.raised|.overlay, cornerRadius)`: canvas = fill + grain no border; raised =
surface1 + hairline no shadow; overlay = surface2 + hairline + l2 shadow. Group before shadow so
only the silhouette casts it (Composition `DropShadow` on the visual).

---

## 3. Navigation model

A single routing authority (`AppNavigation`, an observable in `Kivi.App/ViewModels`), shared with
the tray for deep-links.

- `AppSection`: `record, history, clipboard, styles, presets, memory, shortcuts, analytics, sharedTerms, leaderboard, settings`. Titles lowercase; `.memory`→"dictionary", `.sharedTerms`→"shared terms". **`.presets` is redirected to `.styles` everywhere.**
- Default section: **`.record`**. Debug env flags jump to a page.
- **Deep-link + one-shot payloads** (park → consume-once on target load): `requestHistory(captureID)`, `requestHistory(search)`, `requestStyles(intent)`, `requestPresets`, `requestClipboard(search)`, `requestShortcut(replacement)`, `surfaceToMainBox(text)` (→ Record composer), `request(section, filter)`. Each `consumePending…()` returns then clears.

### 3.1 Rail taxonomy — PURE DATA
Three groups; Settings is the footer gear:
```
capture       record (micDot)  · history (clock)
your space    dictionary (sparkle) · shortcuts (bolt) · styles (brush) · analytics (bars)
team space    shared terms (layers) · leaderboard (trophy)
```

### 3.2 Rail rendering
- Rail background = canon canvas; trailing 1px hairline divider.
- **Brand header**: kiwi mark (13×18 downsampled pixel bird, blinks 140ms every 8–12s when app active) + "kiv**i**" wordmark in Season Mix 26 (the `i` in accent). Top padding 46.
- **Group eyebrow title**: Matter-Medium at eyebrow size, tracking 1.54, with a highlight-sweep (accent wash, 34×7) behind it.
- **RailItem**: 38px row, icon 17px (selected=accent, hover=inkSecondary, rest=inkTertiary), label Matter/Matter-Medium at 16 tracking -0.08, hover wash bg radius 8. Label fades/offsets on collapse (staggered per-item).
- **Icons** are 24×24-grid **2px monoline `PathGeometry`** (micDot, clock, clipboard, brush, sparkle, grid, layers, bolt, bars, trophy, gear) — NOT stock symbols. **Port each path verbatim to XAML `PathGeometry`/`<Path Data>`.**
- **RailFooter**: account avatar (circle, initial, 2.5px usage-ring — amber `#E09A3A` above 90% quota), name + org/workspace line, settings gear (→ settings). Clicking the avatar opens an org-switcher popover. Usage from `v1/usage` (`billableWordCount / monthlyWordLimit`, `periodEnd`).

### 3.3 Settings sub-navigation
Settings is a two-pane shell (its own left rail + searchable panes) — §5.10.

---

## 4. Shell chrome component inventory

Port each to a WinUI `UserControl` / `Style`. All use `PathGeometry` for the hand-drawn bits.

| component | what it is |
|---|---|
| `WorkspacePageHeader` | THE page header: marque(30) title with an accent-wash highlight-sweep underline behind it, 8px gap to a body subtitle; 44px top inset, 24px bottom. |
| `KiviHighlightSweep` | hand-traced highlighter blob `PathGeometry` (viewBox 100×30, non-uniform stretch). Behind headers, greetings, "never misspells". |
| `KiviSectionHeader` | 13px label + optional 12px tertiary detail + trailing hairline fill. |
| `KiviTeachingDisclosure` | collapsible teaching card: +/× circle button header (annotation-gold 8%-tint), chevron, reveals content below with one annotation border. |
| `KiviCard` | surface2 fill, radius 20, hairline, l1 shadow (group before shadow). |
| `KiviHairline` | the one 1px separator. |
| `PillButton` / `PillPressStyle` | capsule button (primary/secondary/ghost), press scale 0.96, focus ring. (Legacy theme.) |
| `StateChip` | 6px dot + Matter Mono 11 label, state-bg capsule; dot breathes (livepulse) for listening/polishing. |
| `KiviInkArrow` | hand-drawn 2.1px accent arrow (112×50), in "you say → kivi writes" explainers. |
| Mini-app surfaces | `MiniWindowBar` (window dots + app glyph), `MiniMailSurface`, `MiniChatSurface`, `MiniFieldRow`. App-identity colors (mail blue `#3478F6`, slack aubergine `#602861`, imsg green `#34C759`) are **content, not theme accents**. |
| `InlineNotice` | info/success/warning/error row, surface2 55% + accent border 30%. |
| `ConnectivityBanner` | offline strip (wifi-slash + copy). |
| `PersistenceBanner` | strip for degraded/failed persistence. |
| `CueToasts` | pure map cue→toast spec. |
| `KiviEmptyState` | pixel-kiwi dot-field (96×130) that breathes 5× then rests + centered message + optional action. |
| Shared canon controls | `InkButtonStyle` (primary/secondary/ghost/destructive; 34px height, radius 8, press settles 1px + darkens fill 5%, loading spinner), `SlidingInkSegmented` (the selection idiom: 2px capsule underline that slides ≤200ms — reproduce with a Composition/`ConnectedAnimation`-style slide). |

---

## 5. Screen inventory — pages

Every page: a scroll view with content in a `maxWidth 980` column centered, hpad **44**
(History/Shortcuts/Memory) or **26–32** (Personas/Analytics/Leaderboard), + a `WorkspacePageHeader`.
Background = shell canvas+grain+dots (pages are transparent).

### Summary
| Page | Section | Purpose | Data source | Key actions |
|---|---|---|---|---|
| Record | `.record` (default) | in-place dictate/edit workspace + landing greeting | `FlowRuntime`; local Captures | press hotkey to talk, edit take, copy, retry, "all takes" |
| History | `.history` | search + AI-"ask" across dictations | local store + server ask | search, filter, ask (Ctrl+↵), inspector, retry, delete, continue |
| Clipboard | `.clipboard` | opted-in clipboard history | clipboard store | filter, search, click-to-copy, forget, clear-all |
| Dictionary (Memory) | `.memory` | terms kivi never misspells | REST (`v1/memory-forest`) | teach/edit/forget term, import |
| Shortcuts | `.shortcuts` | spoken-phrase → saved text map | REST (`v1/spoken-shortcuts`) | teach/edit/delete, show-more |
| Styles (Personas) | `.styles`/`.presets` | per-app writing voices | REST (`PersonasStore`) | select app/voice→detail, style, rules, code-switching, marketplace |
| Analytics | `.analytics` | usage/speed/apps stats | usage + insights | range 7/30/90/all, hover charts |
| Shared terms | `.sharedTerms` | team dictionary (v1 placeholder) | — | none |
| Leaderboard | `.leaderboard` | voice-race ranking | REST | period/activity toggles, refresh |
| Settings | `.settings` (footer gear) | all preferences | settings + system perms | 8 panes, search, per-pane reset |

> **Legacy/dead:** `StylesPage`/`PresetsPage` are **never instantiated** — routing sends
> `.styles`/`.presets` to `PersonasPage`. **Do not port these**; port Personas/* instead
> (`personalization-subsystem.md §0`).

### 5.1 RecordPage
Split responsive layout: **left working column (min 480) + 48px gap + 300px right rail**, max 980,
breakpoint `480+300+48+60`; below it stacks + scrolls. Record pins its own **64px top inset**.
- **Greeting**: Season Mix **52pt**, time-of-day phrase pools, one word gets accent highlighter + sliding underline + refresh (↻).
- **Workspace sticker**: surface1 card radius 8, dual soft shadow (lifts on hover -2px), 2px accent border when editor focused. Hosts the embedded transcript box. Footer: "`appname` `time`" (click→edit) left; "press `<hotkey>` anywhere to talk" + keycap chip right (the reference `fn` → the app's bound hotkey label). Tap card → copy → green "copied — go paste it" morph (1.5s).
- **Right rail** (300px): animated pixel-bird flight scene + a today stat. Bird hops on processing→done.
- Engine: the main-app `FlowRuntime` (`workspaceSink` = keep-in-box, never pastes elsewhere). **Isolate the streaming leaf** (its own view-model / `x:Bind` node) so per-tick text doesn't re-layout the page. Recent takes (≤4) from the local store.

### 5.2 HistoryPage
Header "history" / "search — or ask — across everything you've dictated." Content 980, hpad 44.
- **Finder field** + **filter popover** (surface1 radius 8, 258px): app facets (real app icons via `SHGetFileInfo`) + time presets, accent check marks, clear-all.
- **Ask**: Ctrl+↵ submits an AI synthesis → answer sheet + results list (citations). Offline → "ask needs a connection — search still works".
- **List**: grouped by day; "earlier ↓" pagination; "your week →" popover. Empty → `KiviEmptyState`.
- **Inspector pane**: fixed **430px** trailing overlay, canon canvas + leading hairline, slides in (`.move(edge:.trailing)`, standard curve). Actions: continue-in-kivi, make-version-current, delete/archive, retry. Selection in an isolated observable (perf). Esc cascades: clear selection → cancel ask → collapse answer → clear search.
- Data: a local store (fetch limit 200), tenant/user scoped; server ask client; separate week VM.

### 5.3 ClipboardPage — legacy theme
The **only page with the 64px `Topbar`**. Max width **720**, hpad 24, vpad 20. Filter chips
(all/kivi/general) + "clear all". Rows: source dot, 2-line text, hover reveals copy + trash; click
copies (1.1s "copied"). Disabled state CTA. Confirmation dialog for clear-all (one of the few
native dialogs — WinUI `ContentDialog`).

### 5.4 MemoryPage → MemoryForestPage
Titled **"dictionary"**. Content 980, hpad 44. Header top-right **import** button →
`DataImportPanel` (trailing slide-in). **Explainer card** (surface1 radius 14): promise + ink-arrow
example. **`KiviTeachingDisclosure` "teach kivi a term"** → term + optional note. **Terms list**:
recency-ordered, `MemoryTermRow` (56px min, hover accent-wash, text selection); hover reveals pencil
(edit) + trash (forget). Forget = inline 2-step. Edit = inline row-swap. Progressive "show more"
(page size 8). No modals. Wiring: REST (create/saveEdits/delete).

### 5.5 ShortcutsPage
"shortcuts". Content 980, hpad 44. **Explainer card** with a mini-doc surface showing the saved
block. **Composer** = `KiviTeachingDisclosure`: "when i say" (limit 120) + "what kivi writes" (limit
16000) + "teach kivi"/"save" + cancel. **List**: rows = trigger (Season Mix italic 16) | replacement
(callout, 3 lines) | edit/delete (delete → inline "delete/keep"). "show more". Empty →
`KiviEmptyState`. Wiring: REST.

### 5.6 Personas (the live Styles page)
See `personalization-subsystem.md` for full detail. Content 980, hpad 26. Overview ("your apps" /
"code switching" / "your styles") + a right detail pane (700px) + centered modal sheets.

### 5.7 AnalyticsPage — legacy theme, charts
Header "analytics". Content 980, hpad 32. Gates: loading / unavailable / **unlock gate** (≥5
captures). **Scorecard strip**: 4 cells (mono 24 value + a hand-drawn sparkline). **"words over
time"**: bar chart + range picker (7d/30d/90d/all). **"speaking pace"**: area+line chart.
**"across apps"**: horizontal bars. **"memory"**: 4 mini-stats. → **Charts are Apple-only in the
reference (Swift Charts).** Reimplement with **hand-drawn XAML `PathGeometry`** (the sparkline is
already hand-drawn — port that) or a WinUI charting lib. The range picker is a `SlidingInkSegmented`
elsewhere; here it was one native segmented — use `SlidingInkSegmented`.

### 5.8 SharedTermsPage
Coming-soon placeholder: header + centered book icon, "team space is on the way", body copy,
raised surface. Content 980.

### 5.9 LeaderboardPage
Header "leaderboard". Content 980, hpad 32. **Top bar**: period `SlidingInkSegmented` +
activity segmented + refresh + mono meta line. **Hero podium**: 3 cards (2 | champion #1 | 3);
champion = surface1 + annotation-gold border, flame + #1, big mono ranked-words, pixel-bird sprite.
**Race list**: rank (+tier symbol ≤3) | username (+"you" accent) | ranked-words. Current user
accent-wash; pinned "your rank". Confetti burst on rank improvement. Data: REST refresh loop.

### 5.10 SettingsPage → SettingsShell
Reached via the **footer gear**. Its own two-pane shell:
- **Left sub-rail 224px**: search field + grouped selectable pane rows (accent-wash selected, hairline hover, radius 8, 34px) + footer. 44px top inset. Pure-data taxonomy + keyword search.
- **1px hairline divider**, then detail scroll max width **640**, hpad 24, 44px top: pane header (title2 + subtitle + optional reset) then pane content.
- **Panes**, two groups:
  - *how kivi behaves* (reset-enabled): **general** (General + Shortcuts + Privacy), **the orb** (Orb + OrbDND), **system settings** (Microphone + EchoCancellation + SystemSettings + KiviClipboard).
  - *you & your team* (no reset): **plan & billing**, **invite friends** (placeholder), **org & workspace** (role-gated), **account**, **advanced** (endpoint, updates, motion, reset-all).
- Search matches title+keywords. `HotkeyCaptureField` records the global hotkey (needed day one so the new default is rebindable). `SystemPermissions` reads mic status (Windows: no Accessibility trust gate — see `menubar-onboarding-auth.md §5`).

---

## 6. Navigation map (summary)

```
Window "kivi" (custom title bar, 1180×760, min 980×640)
└─ AuthGate → [splash|signIn|onboarding] | permissionsGate | MainWindow
   ├─ Rail (264⇄76, Ctrl+\)                       detail
   │   ├ kivi wordmark (brand)                     ├ PersistenceBanner (cond)
   │   ├ capture:  record*, history                ├ ConnectivityBanner (cond)
   │   ├ your space: dictionary, shortcuts,         ├ Topbar (ONLY .clipboard)
   │   │             styles, analytics              ├ InvitationBanner (cond)
   │   ├ team space: shared terms, leaderboard      └ page  (canvas+grain+dots bg)
   │   └ footer: account/org avatar + ⚙ settings
   └─ Deep-links (from tray / orb / rows)
```
`*` default. `.presets` → `.styles`. Settings is footer-only. Trailing overlay panes slide from the
right (History/Styles/Memory); Personas uses centered modal sheets; Settings uses a nested 224px
pane rail.

---

## Windows/.NET notes (macOS/Electron → Windows/.NET)

1. **Titlebar / window chrome** (macOS hidden titlebar + traffic lights) → a WinUI **frameless / custom title bar** (`ExtendsContentIntoTitleBar` + a drag region); put your own window controls top-right. The 44px page-header top-inset re-derives from your chrome height.
2. **Fonts bundled + resolved by PostScript name**: Matter, Matter SemiMono, Space Grotesk, **Season Mix**. Embed as WinUI content, load before first paint. Season Mix is load-bearing (R12).
3. **Two color systems** — port **Canon** (§2.1) as the primary theme dictionaries with light/dark via the Windows app theme + a manual override toggle; port legacy Theme only for Clipboard/Analytics.
4. **All icons are hand-drawn `PathGeometry`** (24×24 2px monoline: RailIcon paths, HistoryGlyph, MemoryPencil/Trash, KiviInkArrow, KiviHighlightSweep, PixelKiwi bead grids). **Translate each path verbatim** to `<Path Data="…">`. A handful of native SF Symbols in the reference → substitute a matching XAML `PathGeometry` / a WinUI icon.
5. **Charts** (AnalyticsPage) — Apple-only in the reference → hand-drawn XAML `PathGeometry` (port the hand-drawn sparkline) or a WinUI charting lib. Native `Picker`/`Toggle`/`Slider`/dialog → styled WinUI (`SlidingInkSegmented`, `ToggleSwitch`, `Slider`, `ContentDialog`, custom popovers) to match the DS everywhere.
6. **App icons** (`PersonasAppMark`, `AppIconView`, `AppDisplayNameCache`): read installed-app icons/names by bundle-id (macOS). On Windows, resolve process/app icons via **`SHGetFileInfo` / PE-resource extraction** keyed by exe path (or ship a curated icon+name map keyed by exe/AppUserModelID; extend the `PersonaSeedRegistry` fallback). See `personalization-subsystem.md`.
7. **Running-app enumeration** (`AppPicker`/`RunningAppsProvider`) → the OS process list (`Process.GetProcesses` / `EnumWindows`).
8. **Reduced-motion & reduced-transparency** → `UISettings.AnimationsEnabled` + the transparency-effects setting; disable PaperGrain + breathing/pulse accordingly.
9. **Per-tick perf discipline** (Record isolates the streaming leaf so the page doesn't re-render at 60fps): isolate the live-transcript node (its own view-model / compiled binding) so streaming STT text doesn't re-layout the whole page — the same architectural constraint applies.
10. **Local history** (SwiftData/`better-sqlite3`) → **SQLite** (`Microsoft.Data.Sqlite`) with the same tenant/user scoping. REST stores (Memory/Shortcuts/Personas/Leaderboard/Usage) hit the same kivi-service and port directly via `HttpClient`.
11. **Indian-locale number grouping** → a custom `NumberFormatInfo` for `en-IN` (digit grouping `3;2`).
12. **Legacy pages** `StylesPage`/`PresetsPage` — do not port; port `Personas/*`.
13. **Global hotkey / paste** (advertised by Record's "press … anywhere to talk" footer) is Windows-native — `WH_KEYBOARD_LL` + `SendInput`; out of scope for this shell map (see `dictation-audio-pipeline.md`, `platform-coupling-audit.md`).

**Deferred / v1 non-goals:** the shell + pages are M5 (after the orb + loop). Analytics/Leaderboard/
SharedTerms/Clipboard are P3; Record/History/Settings are the M5 priority.

> **Not applicable — Windows-only.** The reference's Linux tray/window-control notes are dropped.
