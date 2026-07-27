# MAP: personalization-subsystem

Windows-only .NET port of `_reference/sarvam-kivi-electron/docs/maps/personalization-subsystem.md`.
The domain model + resolver + all copy/example strings port **verbatim** (a content-parity
contract); the only native cost is Windows app-icon/running-app discovery and the app-identity
convention. Target: `Kivi.App/Views/Personas`, `Kivi.App/ViewModels` (StylesVM etc.),
`Kivi.Core/Rest` (persona/style REST), `Kivi.Core` `StyleCatalog` cache.

---

## 0. Orientation & routing

The personalization surface is **one page** — `PersonasPage` — the live destination for BOTH the
`.styles` and legacy `.presets` routes. The reference `StylesPage`/`PresetsPage` are **DORMANT** —
never routed. **Do NOT port them**; the pre-convergence design. All live behavior is under
`Personas/*`.

The page composition root is `PersonasStore` (an observable in `Kivi.App/ViewModels`), which owns
selection/presentation/transient feedback and delegates to four sub-reducers: `StylesViewModel`,
`PresetsViewModel`, `PreviewViewModel`, `ActiveStylePoliciesStore`.

---

## 1. Domain model — four distinct concepts

| Concept | UI name | What it is | Identity | Scope |
|---|---|---|---|---|
| **Style scope / persona** | "voice" / "style" | A named group of apps that share writing settings. Server calls it a "persona". | `slug`: `"global"` or `custom_<name>` | Two visible kinds: `global` + `custom_*`. Legacy `dev`/`work`/`personal` slugs filtered out. |
| **Cosmetic style** | "writing style" (Standard / Formal / Custom) | The active formatting preset WITHIN a scope. | `"standard"｜"formal"｜"custom"` | One active per scope. `custom` has editable controls. |
| **Custom rules** | "custom rules" / "note to kivi" | Free-text instructions compiled to bullets by the server. | text blob (raw + compiled) | Belongs to the **scope/persona**, not one cosmetic card. |
| **App-scope override** | per-app voice | An app overrides its group's cosmetic style + controls + rules. | keyed by **appKey** (Windows: exe path / AppUserModelID) | `Target.App(personaSlug, appKey)`. Inherits from its persona unless `hasOverride`. |
| **Preset / recipe** | "recipe" | A saved, reusable transform. | `presetID` | Largely **wired but dormant**. |

### The five seeded personas (fixed release set)
`Developer, Work Messaging, Personal Messaging, Email, Other Apps`. `"Other Apps"` == the `global`
scope. Each seeded persona owns a curated **appKey → persona** mapping. **On Windows the reference
`PersonaSeedRegistry` bundle-ID tables must be re-mapped to Windows app identifiers** (exe path /
AppUserModelID) — a cross-team dependency (`FEATURE-PARITY.md`). User-created custom personas are
**dormant** (`.newStyle` intent no-ops); the release surface is the fixed five + per-app overrides.

### Cosmetic style resolution (`CosmeticStyleState`)
- `normalizeID`: `"formal"→formal`, `"custom"/"free_flowing"→custom`, else `standard`.
- Default resolved levels (1–4) per style: **standard** 2/2/2 romanize true; **formal** 4/4/4 true; **custom** 2/2/2 true (only `custom` has `controlsEditable`).
- `romanize` is **global runtime state** (mirrored across all scopes/cards on save) — NOT per-card.
- The three knobs (`StyleLevels`): `noiseCleanup`, `formality`, `punctuationCaps`, each 1–4. In the shipped detail pane these are NOT rendered (they live in the dormant `StylesPage`).

### Legacy `StylePreset` enum
`casual, formal, custom, free_flowing, verbatim`. Raw value == server `base_preset` slug. Cosmetic→legacy:
`standard→casual`, `formal→formal`, `custom→casual`. Gallery `[casual, formal, custom, free_flowing]` — dormant.

---

## 2. `PersonasStore` — page state machine

Key published state (all observable, `Kivi.App/ViewModels`):
- `selectedTarget: Target?` — `.Persona(slug)` or `.App(personaSlug, appKey)`
- `isDetailPresented`, `activeSheet: Sheet?`, `showCreateVoice`, `showManageVoice`
- `noteCompileState`: `.Draft / .Compiling / .Reviewed / .Saving / .Active / .Failed(string)`
- `appApplyOffer: AppApplyOffer?` — after saving an app override, offers "apply across the persona".
- `quickAddPresetIDs`, `quickAddFailure`.

Derived accessors merge an in-memory **app-style draft** over the selected scope's active cosmetic
style: `selectedCosmeticStyleID`, `selectedResolvedFormat`, `selectedNoteRaw`, `selectedNoteCompiled`,
`selectedRules`, `isDirty`, `hasSavedRules`, `appHasOverride`.

**Cache-first paint:** `HydrateNow()` → `styles.HydrateFromCache()` paints the grid synchronously
from the warm `StyleCatalog` cache on the first frame; `Load()` revalidates over the network in
place. `hasCachedContent` gates the cold spinner.

**App override save flow (`SaveAppOverride`)**: coalescing loop (`appSaveRequested`/`appSaveInFlight`)
so rapid edits queue rather than race; on success builds `appApplyOffer`. `ClearAppOverride()`
reverts to the inherited draft.

---

## 3. UI structure & flows

### 3.1 Page shell (`PersonasPage`)
A `Grid`/overlay (trailing-aligned):
1. **Scroll** → `PersonasOverview`, framed to content column **980px**, hpad **26**, bottom 150, centered. Content is blurred (radius **5**) + disabled when any overlay is open.
2. **Detail pane** — a right-anchored `PersonasDetailPane`, fixed **width 700**, full height, `surface2` background, 1px `hairline` leading edge, slide-in transition. A dimming button (black 0.18 dark / 0.08 light) closes it.
3. **Modal sheets** (marketplace / presetLibrary / createVoice / manageVoice) — centered, dim scrim (black 0.30 dark / 0.16 light), rounded rect **radius 20**, 1px hairline, shadow `black .16 / radius 32 / y 10`. Sizes: marketplace `min(760, W-64) × min(700, H-64)`; presetLibrary `min(1080,…) × min(780,…)`; create/manage `min(500, W-64)`.
- Animation: settle (300ms) gated on reduced-motion. `Esc` closes the topmost overlay.

### 3.2 Overview (`PersonasOverview`) — the scroll body
Sections led by `PersonasSectionHeader` (Season Mix at title2 = 22pt + a 1px hairline rule):
1. **Header** — `WorkspacePageHeader` "how you sound, app by app" with trailing "app" **italic** in the "cloth" red (`PersonasPalette.cloth`).
2. **"your apps"** — ranked `PersonasFavoriteAppRow` (app icon 27 + lowercased name + chevron, min-height 52), from server-ranked usage (excludes Kivi's own bundle IDs, useCount>0, deduped). 5 with "show more" (+5). Tapping → the app's detail target.
3. **"code switching"** — one row: "non-english words" + a live example + right-aligned `SlidingInkSegmented` toggling `globalRomanize` (transliteration ⇄ indic characters). Example: `"let's meet kal at 8"` vs `"let's meet कल at 8"`.
4. **"your styles"** — `PersonasVoiceCard` per ordered scope (Season Mix marque name lowercased + a voice statement line w/ italic cloth-red accent word + up to 4 app marks + arrow). Card: surface1, radius **14**, 1px hairline, padding h22/v17. Voice statements hardcoded per canonical persona.

`orderedScopes` filters to global + canonical seeded personas, sorted by `orderedPersonaNames`.

### 3.3 Detail pane (`PersonasDetailPane`, 700px)
Header + scrollable body (`PersonasConfiguration` + `PersonasVoiceNotes` + `PersonasVoiceApps` when persona-target).
- **Header**: 46×46 icon (app icon for app-target; a person badge for persona-target) + lowercased display name (marque 30) + identity line + revert/save (when dirty) + close ×.
- **`PersonasConfiguration` → "writing style"**: for an app target, an inherited-cosmetics notice ("uses <persona>" / "custom for this app" + "use group voice") + an apply-to-persona offer card (accent-wash bg + accent 0.5 stroke, radius 12).
  - **The 3-card cosmetic picker** — the centerpiece. A "you say" source line (italic 15) above **3 `PersonasPreviewChoice` cards** (`HStack` gap 12). Each: title (label) + selected accent-circle check (19px), a 2-line help caption, and a **live mini-app preview** (`PersonasCategoryPreview`, minHeight 138) rendering that style's example (`MiniMailSurface`/`MiniChatSurface`/dev/doc). Selected: accent-wash bg + accent 1.5px border; unselected: surface1 + hairline 1px, radius 12. Choosing = `SelectCosmeticStyle(styleID)`.
  - Titles/examples come from `PersonasStyleCatalog` (per canonical persona). **Content-parity contract — reproduce these strings verbatim.**
- **`PersonasVoiceNotes` → "custom rules"**: state machine on `noteCompileState`:
  - `.Draft/.Reviewed/.Failed`: multi-line text field (4–8 lines, min 118px, radius 8) + save/remove pill; placeholder verbatim.
  - `.Compiling/.Saving`: spinner + "kivi is reading your note…".
  - `.Active`: "active" dot + rules list (each removable via ✕) + "edit".
  - Compile → `rest.CompileCustomInstructions(raw, instructionKind:"cosmetic_style")`; 0 bullets → **persist raw note as freeform** (never lose it).
- **`PersonasVoiceApps` → "apps"** (persona-target): assigned apps (icon 20 + name + chevron) + "add app" via a picker; assigning an app owned by another scope → inline **move-confirmation** ("<app> already uses <persona>. cancel / move"). 409 → annotation caption.

### 3.4 Create-voice sheet (`PersonasCreateVoiceSheet`, 500px)
"new voice" (Season Mix 28) + name field (radius 8, min 44) + apps list + "add app" + cancel/"create
voice". **Critical picker rule (the fixed rule):** the create picker passes `unavailable:
appsAssignedOutsideSelectedScope` — which excludes `global` and the selected scope, so apps the user
has only *dictated in* (globally owned) stay pickable. On create: `CreateVoice(name, appKeys)`.

### 3.5 Manage-voice sheet (`PersonasManageVoiceSheet`, 500px)
Rename (custom only) + delete (custom only, confirmation) + app add/remove.

### 3.6 Marketplace sheet
Grouped-by-category grid (2 cols, min 220). Each card: name + description (3 lines) + a from→to
before/after + "for <personas>" + "add to <name>". Data from `presets.marketplacePresets`. Add
routes through `AddMarketplacePreset`.

### 3.7 Preset library / "my recipes"
Split view (300px rail + editor). Rail: recipe list + "new recipe" + suggestions. Editor: name /
what it does / when to use / when not to use / instructions + compile + save + duplicate + delete.
Backed by `PresetsViewModel`.

---

## 4. View-model reducers

### `StylesViewModel` — the core reducer
- Pure reducer over `[StyleScopeState]`; never touches network directly — talks to `StylesBackend`.
- State: `scopes`, `appUsage`, `selectedSlug`, `isLoading/isOnline`, `saveState`, `isMutatingStyles`, `suggestions`, `assignmentConflict`, `presetControlConflicts`, `lastError`.
- `StyleScopeState` holds `activeCosmeticStyleID` + `cosmeticStyles: Dictionary<string, CosmeticStyleState>` (seeded standard/formal/custom) + per-control overrides (null=inherit) + `globalOverrides` + `appKeys` + raw/compiled rules.
- `StyleResolver` — **pure, testable** resolution folding preset floor → global overrides → scope overrides, plus romanize + preset-stack conflict handling. **Port verbatim.**
- Key ops: `SelectCosmeticStyle` (carries custom-rules + romanize across the switch; resets overrides), `SetNoiseCleanup/Formality/PunctuationCaps` (custom-only), `SaveGlobalRomanize` (mirrors everywhere), `CompileRules`, `RemoveBullet`, `AssignApp/UnassignApp/MoveApp` (409 handling), `CreateCustomStyle/RenameStyle/DeleteStyle`, `ReconcileSeededPersonas`.
- `appsAssignedOutsideSelectedScope`: union of `appKeys` from *custom* scopes ≠ selected (global excluded).
- Save request builder `MakePutRequest` → `PutFormatPreferencesRequest`.

### `PreviewViewModel`
Shows a **bundled instant example** on any change, then **debounces 500ms** a `POST /v1/format-preview`
round-trip and crossfades server truth over the bundled text. Offline → bundled only. Single debounce
window (cancels prior task — a `CancellationTokenSource` per keystroke).

### `PresetsViewModel` + `PresetsBackend`
Recipes/marketplace/suggestions CRUD; `CompileRules` with `instructionKind:"preset_transform"`.

### `StylesBackend` — the network seam
`IStylesBackend`. `LiveStylesBackend` (real `KiviRestClient` + `StyleCatalog`) vs
`OfflineStylesBackend` (cache-only; mutations throw). `Load()` does ONE concurrent fetch pass (cap 4)
shared with the cache write; every mutation refreshes the `StyleCatalog` cache. 409 bodies parsed
into `StyleConflict` / `PresetControlConflicts`.

---

## 5. Backend contract (what the .NET clone wires to, via `HttpClient`)

Base path prefix `v1/`.

| Endpoint | Method | Purpose | Response |
|---|---|---|---|
| `v1/personas` | GET | List style scopes | `{personas:[{persona_slug, base_preset, selected_preset_id(s), display_name, app_bundle_ids, is_custom, edited}]}` |
| `v1/personas/apps` | GET | Ranked observed app usage | `{apps:[{app_bundle_id, app_name, persona_slug, category_slug, use_count, last_seen_at}]}` |
| `v1/personas/apps/assignment` | PUT | Move app between personas (optimistic `expected_persona_slug`) | `{app_bundle_id, previous_persona_slug, persona_slug}`; 409 = conflict |
| `v1/format-preferences?slug=` | GET | One scope's resolved row | `FormatPreferencesRow` |
| `v1/format-preferences` | PUT | Save a scope | `PutFormatPreferencesRequest`; 409 |
| `v1/format-preferences?slug=` | DELETE | Delete custom scope | — |
| `v1/persona-cosmetic-styles?persona_slug=` | GET/PUT | Cosmetic rows | `{cosmetic_styles:[…]}` / `{cosmetic_style:{…}}` |
| `v1/persona-app-style-overrides?app_bundle_id=` | GET/PUT/DELETE | App override | `{override:…?}` |
| `v1/persona-app-style-overrides/apply-to-persona` | POST | Promote app override to persona | `{persona_style:{…}, cleared_app_override_count}` |
| `v1/compile-custom-instructions` | POST | Compile plain-English → bullets | `CompiledInstructions`; `instruction_kind` = `cosmetic_style｜preset_transform` |
| `v1/format-preview` | POST | Stateless rewrite preview | `{formatted}` (debounced 500ms) |
| `v1/preset-marketplace` | GET | Curated transform presets | `{presets:[…]}` |
| `v1/transform-presets` | GET/POST/PUT/DELETE | User recipes | |
| `v1/style-presets` | — | **RETIRED** (local defaults; writes → 410) |

> **Wire is snake_case** (`persona_slug`, `noise_cleanup`, `punctuation_caps`,
> `active_cosmetic_style_id`, `custom_instructions_raw/compiled`, `app_bundle_ids`, `is_custom`).
> Levels are integers **1–4**; `romanize` bool. Map explicitly via `[JsonPropertyName]`.
> `app_bundle_ids` on the wire carries the **Windows app-key** (exe path / AppUserModelID) — the
> agreed cross-platform app-identity convention; the field name stays `app_bundle_id` for the
> backend contract.

**`PutFormatPreferencesRequest`** custom-encodes: only non-empty `active_cosmetic_style_id`;
`selected_preset_id` + `selected_preset_ids` from a normalized stack; conditional
`preset_conflict_choices`; `display_name`/`app_bundle_ids`/`is_custom` only when present.

**Client-side cache (`StyleCatalog`, `Kivi.Core`)** — mirrors server-resolved prefs to a
**JSON store under `%APPDATA%\Kivi`** so the dictation hot path reads synchronously. **Reuse the
exact cache keys**: `kiviStyles.appAssignments`, `.presetByPersona`, `.resolvedByPersona`,
`.cosmeticStylesByPersona`, `.configuredPresets` (optionally namespaced by org-context scope).
`ClientFormatPrefs()` assembles the WS dictation context (always seeds a `global` casual 2/2/2 row).
`maxPaneStyles = 4` (global + ≤3 customs). **The .NET clone needs this local cache** for cache-first
paint + attaching per-app style context without a round-trip. It was an `actor` on macOS — in .NET,
keep all cache access single-writer (one owning service / a lock).

---

## 6. Design tokens (reproduce exactly)

Personalization uses the **Canon** palette (see `design-tokens.md §3b`). Read via theme dictionaries.
Personas-specific accents (NOT in canon): `cloth` (the italic heading accent) light `rgb(166,64,46)` /
dark `rgb(224,138,118)`; `ochre` (marketplace eyebrow) light `rgb(156,117,38)` / dark
`rgb(216,169,87)`.

Typography roles as in `design-tokens.md §2` (marque 30 = pane/card titles, title2 22 = section
headers). Content-case rule: display/titles/eyebrows are **lowercased** (app/persona names
`.ToLowerInvariant()` at render).

Geometry/motion: content column **980px**, hpad 26. Radii: voice cards **14**, style-choice
cards/notices **12**, sheets **20**, small fields/pills **8**, buttons `sm`(8), inherited notice
**10**. Buttons — `InkButtonStyle` (primary/secondary/ghost/destructive; height 34, hpad 16, radius 8,
press offset y+1 + darken 5%, disabled 0.4, loading spinner). `PersonasInkButtonStyle` — a distinct
"offset shadow" button (radius 11, height 36, a duplicated stroke offset x/y 2 collapsing on press).
Motion — fast 120 / standard 200 / settle 300, ease-out, no springs; gated on reduced-motion. App
icons: `PersonasAppMark` renders the real Windows app icon (`SHGetFileInfo`), else a fallback chosen
by app-key substring.

---

## 7. Windows/.NET notes (macOS/Electron → Windows/.NET)

1. **App icons + running-app discovery** — the biggest native dependency. macOS uses `NSImage` +
   `NSWorkspace.runningApplications` + an `/Applications` walk reading each `Info.plist`
   `CFBundleIdentifier`. **On Windows there is no bundle-ID** — use an app-identity scheme (**exe
   path / AppUserModelID / process name**). Icons come from **`SHGetFileInfo` / PE-resource
   extraction** keyed by exe path (or a curated icon+name map). **The entire `PersonaSeedRegistry`
   bundle-ID table must be re-mapped to Windows identifiers**, and the server's
   `persona`/`app_bundle_id` contract needs the cross-platform app-key convention agreed with
   backend (`FEATURE-PARITY.md` cross-team dep #1). The picker's `browseForApp` (macOS `NSOpenPanel`
   `.application`) → a Windows file dialog over installed apps / a process enumeration.
2. **Active-app detection driving per-app overrides** — resolving "which persona applies now"
   depends on the frontmost app's key (`StyleCatalog.PersonaSlug(forAppKey)` on the dictation hot
   path). Windows: `GetForegroundWindow` + `GetWindowThreadProcessId` + `QueryFullProcessImageName`.
3. **UI primitives** map to XAML: `RoundedRectangle(.continuous)` = a superellipse — plain
   `CornerRadius` is a close approximation (SVG/`PathGeometry` squircle only if a large-radius diff
   fails). `.blur(5)` = a WPF `BlurEffect` (or a Win2D `GaussianBlur` on the 2D surface). Custom flow-wrap layout =
   a `WrapPanel`. `SlidingInkSegmented` = a custom animated segmented control.
4. **Fonts** — Matter, Space Grotesk, Season Mix, Matter SemiMono — embedded font files (see
   `design-tokens.md §1`, R12).
5. **Theme** — resolve from the Windows app theme + a manual override; port Canon light/dark as theme
   dictionaries.
6. **`StyleCatalog` cache** — a **JSON store under `%APPDATA%\Kivi`** preserving the exact cache
   keys/shape for hot-path parity + cache-first paint. Single-writer (one owning service / lock).
7. **Stock controls** (`ProgressView`, `Slider`, `Toggle`, `.confirmationDialog`, `.popover`,
   tooltip, text-selection) → WPF equivalents (`ProgressBar`, `Slider`, a templated `ToggleButton`
   switch, a modal `Window` dialog, `Popup`, `ToolTip`, `TextBox`/`TextBlock` selection).
8. **Everything else is portable** — the domain model, resolution logic (`StyleResolver`,
   `CosmeticStyleState`, `PersonasStore` state machine), the REST contract, the compile/preview
   debounce, and all copy/example strings (`PersonasStyleCatalog`, voice statements) are pure
   logic/data — port **verbatim** to preserve behavioral + visual + content parity.

Port order (priority): `PersonasStore`, `StylesViewModel`, `StylesBackend`, `StyleCatalog`,
`KiviRestClient` (persona/style section), `PersonasStyleCatalog`, then the views + tokens +
`PersonasVisuals` (icon fallbacks). **Deferred / v1 non-goals:** the whole personalization surface is
**M6**; marketplace/recipes are P3 (mostly dormant).

> **Not applicable — Windows-only.** The reference's Linux app-identity (`.desktop`/WM_CLASS) and
> Wayland "cannot read foreground window" caveats are dropped.
