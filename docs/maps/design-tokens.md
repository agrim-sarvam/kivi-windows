# MAP: design-tokens

Windows-only .NET port of `_reference/sarvam-kivi-electron/docs/maps/design-tokens.md`. Every
color hex, type scale, spacing value, radius, and motion duration/easing below is **byte-exact**
from the reference and transfers unchanged regardless of platform. Target: `Kivi.Core/DesignTokens/*`
(generated values) surfaced as XAML `ResourceDictionary` theme dictionaries in `Kivi.App/Themes/*`.

## 0. Architecture: there are TWO token layers, and you need both

| Layer | Reference source | Drives | Notes |
|---|---|---|---|
| **KDS + Canon** (main window + tray) | `packages/design-tokens` (from the "Tatva" DS CSS + hand-authored Canon) | The 6 main pages (Record/History/Personas/Settings…) and the tray popover | Token-parity tests re-parse and pin every value — port those to xUnit |
| **DS** (the floating orb bar) | `packages/design-tokens` orb tokens (from `floating-bar.css`/`kivi-ds.css`) | The always-on-top orb + transcript box + satellites | Separate, self-contained |

Critical subtlety for the port: **the app's runtime dark theme is NOT the KDS.dark ledger** — a
theme resolver overrides KDS.dark's canvas/surface/accent with **Canon.dark** values while
keeping KDS.dark's text/borders/state accents. Light theme = KDS.light verbatim. Two different
creams coexist: legacy pages use KDS `#F1F4EC`; newer "canon" pages/components use Canon
`#F6F3EA`. Reproduce whichever a given screen actually uses — see §3. (R15.)

---

## 1. Fonts

Bundled faces (originals as woff2 in the reference `fonts/`):

| Family | Weights | PostScript names | Role |
|---|---|---|---|
| **Matter** | 300 Light, 400 Regular, 500 Medium, 600 SemiBold, 700 Bold | `Matter-Light/-Regular/-Medium/-SemiBold/-Bold` | Body, UI labels, controls, keycaps |
| **Matter Mono** | 400 | family **"Matter SemiMono"**, PS `MatterSemiMono-Regular` | Data, latency, params, keys, eyebrows |
| **Space Grotesk** | 400/500/600/700 (+ variable) | `SpaceGrotesk-Regular/-Medium/-SemiBold/-Bold` | Display / headings — **always lowercase** |
| **Season Mix** | 300/400/500/600/700 | `SeasonMix-Light/-Regular/-Medium/-SemiBold/-Bold` | Editorial serif — wordmark + big marque numerals |

- Heading face is **Space Grotesk 500, lowercase, letter-spacing −0.01em, line-height 1.12**.
- **Do not let the framework synthesize weights** — the reference sets `font-synthesis:none`; in WPF/XAML ship every real weight and reference the exact `FontFamily`/`FontWeight`, never a faux-bold.
- The "Matter SemiMono" family-name quirk is a macOS CoreText detail; on Windows just install/embed the woff2/otf and reference the family directly.

**Windows sourcing / licensing:**
- **Space Grotesk** is OFL (free) — shippable.
- **Matter and Season Mix are proprietary/licensed** — must confirm redistribution rights for a shipped .NET installer (`FEATURE-PARITY.md` cross-team dep #3, R12). Until cleared, use them **dev-only** for parity and ship the documented **metrics-compatible fallback stack** with a documented font-region tolerance. Season Mix is load-bearing for the wordmark + every page title.

Font loading in WPF: embed the font files as resources and reference by pack URI
(`pack://application:,,,/Assets/Fonts/#Matter`, or `/Assets/Fonts/Matter-Regular.otf#Matter`). Set
the exact `FontWeight` per role; never faux-synthesize.

---

## 2. Type scale, leading, tracking, semantic roles

Base = 12px; steps are "base + N".

```
--fs-body-xs:12px; --fs-body-sm:14px; --fs-body-md:15px; --fs-body-lg:18px;
--fs-label-sm:14px; --fs-label-md:15px;
--fs-heading-xs:16px; --fs-heading-sm:18px; --fs-heading-md:20px; --fs-heading-lg:24px;
--fs-display-sm:28px; --fs-display-md:40px; --fs-display-lg:64px;
--leading-tight:1.2; --leading-body:1.7; --leading-relaxed:1.9;
```
Display tracking (em): sm `-0.01`, md `-0.02`, lg `-0.03`.

**Semantic roles** — port as reusable XAML styles / a `TextRole` attached property. `line-height`
is a multiple (map to `LineHeight` = size × multiple, `LineStackingStrategy=BlockLineHeight`);
`tracking` is em (map to `CharacterSpacing` in 1/1000 em units — e.g. 0.08em → `CharacterSpacing="80"`).

| Role | Face | Size | line-height | tracking(em) | Notes |
|---|---|---|---|---|---|
| display | Season Mix | 40 | 1.04 | -0.02 | big lowercase serif only |
| marque | Season Mix | 30 | 1.1 | 0 | page title serif |
| title1 | Space Grotesk | 28 | 1.12 | -0.01 | lowercase |
| title2 | Space Grotesk | 22 | 1.16 | -0.005 | |
| title3 | Space Grotesk | 18 | 1.2 | 0 | |
| body | Matter 400 | 15 | 1.5 | 0 | THE body anchor |
| reading | Matter 400 | 16 | 1.6 | 0 | transcripts |
| callout | Matter 400 | 14 | 1.4 | 0 | secondary |
| label | Matter **500** | 13 | 1.25 | 0 | buttons/chips/fields |
| caption | Matter 400 | 12 | 1.35 | 0 | floor |
| eyebrow | Matter Mono | 11 | 1.2 | **0.08** | only sanctioned 11pt |
| monoData | Matter Mono | 13 | 1.3 | 0.01 | tabular figures (`FontFeatures`/`tnum`) |

---

## 3. Colors — theme dictionaries (light default, dark override)

Port each ramp as two XAML `ResourceDictionary` themes (`ThemeDictionaries` keyed `Light`/`Dark`)
plus generated C# constants for code-drawn surfaces (orb/canvas). RGB triples shown as hex.

### 3a. Tatva base ramp + kivi brand layer (KDS ledger)
These back the legacy main pages and the tray.

```
LIGHT:
  fg1 #14180E; fg2 #5C6454; fg3 #929A8A; fg4 #B2B8A8; fg-inverse #FFFFFF;
  border1 #F0F0F0; border2 #E6E6E6; border3 #ACB4A0;
  kivi-paper #F1F4EC; kivi-paper-2 #FFFFFF; kivi-warm-tint #E7EEDD;
  kivi-leg-green #41691E; kivi-leg-green-dark #6EA335; brand-ink #161E10;
  state-idle #929A8A; state-listening #E96C2F; state-processing #4250D5;
  state-speaking #4B7D28; state-waiting #D2962D; state-error #B81514;
  state-idle-bg #E7EEDD; state-listening-bg #FEEDE6; state-processing-bg #E8EFFC;
  state-speaking-bg #E3F1D8; state-waiting-bg #FFF2D2; state-error-bg #FAD7CD;
  positive #6EA335; positive-bg #F2F8EB; warning #A27224; warning-bg #FFF8E6;
  danger #B81514; danger-bg #FDE7E2;
  rank-gold #9A7B35; rank-silver #6E7680; rank-bronze #8A6544;
DARK:
  fg1 #E0E6D8; fg2 #929989; fg3 #626959; fg4 #404737; fg-inverse #14180F;
  border1 #32382B; border2 #424939; border3 #5C6351;
  kivi-paper #14180F; kivi-paper-2 #1C2116; kivi-warm-tint #21271B;
  kivi-leg-green #6EA335; brand-ink #E0E6D8;
  state-idle #626959; state-listening #F59666; state-processing #7C96E6;
  state-speaking #82AF5A; state-waiting #F0B95A; state-error #F85149;
  state-idle-bg #21271B;  /* accent-100 bg tints NOT overridden — same as light: */
  state-listening-bg #FEEDE6; state-processing-bg #E8EFFC; state-speaking-bg #E3F1D8;
  state-waiting-bg #FFF2D2; state-error-bg #FAD7CD;
  positive #3FB981; positive-bg #0E231B; warning #D29922; warning-bg #271F00;
  danger #F85149; danger-bg #2A0C13;
  rank-gold #C9A961; rank-silver #A7AFB8; rank-bronze #B08A66;
```
Selection: `background #14180E, color #FFF`. Links: indigo-700 `#3F42B4` → hover indigo-800 `#3333CC`.

### 3b. Canon palette — the ACTUAL surfaces
This is what new pages and all shared components render against, and — via the theme resolver —
what the whole app's **dark** canvas/surface/accent become. **Use these for surfaces to match the
real app.**

```
LIGHT:
  canvas #F6F3EA;      /* cream paper — the base plane */
  surface1 #FCFAF3;    /* raised card/row */
  surface2 #FFFFFF;    /* overlay/sheet/pill */
  ink-primary #20241F; ink-secondary #5C6454; ink-tertiary #666D5F;
  accent #41691E;                       /* moss/pine — THE single accent */
  accent-wash rgba(65,105,30,0.08);     /* ≤8% coverage fill */
  hairline rgba(32,36,31,0.14);         /* the ONE 1px separator */
  annotation #B98A2E;                   /* quiet gold metadata ink */
  c-listening #D05C1E; c-processing #303CC8; c-settled #5E7A4E; c-error #B81514;
DARK:
  canvas #0C0E0C;      /* "forest at night" — a first-class mood, NOT dimmed paper */
  surface1 #161616; surface2 #1E1E1E;
  ink-primary #E9E7DD; ink-secondary #A7A99E; ink-tertiary #84887D;
  accent #8FCE6E;                       /* sage */
  accent-wash rgba(143,206,110,0.08);
  hairline rgba(255,255,255,0.08);
  annotation #D8B85C;
  c-listening #F8A86C; c-processing #788CFF; c-settled #8FCE6E; c-error #B81514;
```
Canon rules: **one accent per surface at ≤8% coverage**; red is errors-only; borders do
separation, shadow only for true overlays. In the dark override, `kivi-warm-tint` (hover/selection
wash) is intentionally kept at **`#404948`** (rgb 64,73,72), NOT dropped to a near-canvas value.

---

## 4. Spacing, radii, elevation

```
space-1:2  space-2:4  space-3:6  space-4:8  space-6:12  space-8:16  space-10:20
space-12:24  space-16:32  space-20:40  space-24:48  space-32:64
radius-xs:4  radius-sm:8  radius-md:12  radius-lg:20  radius-xl:28  radius-full:9999
/* elevation — zero offset, 64px blur; "lifted", never floating */
shadow-l1: 0 0 64px 0 rgba(20,20,20,0.08);   /* cards, inputs */
shadow-l2: 0 0 64px 0 rgba(20,20,20,0.16);   /* dialogs, toasts, tooltips */
content-column: 980px;   page-header-top-inset: 44px;
```
Radius grammar: **buttons/chips/inline inputs = pill (`radius-full`)**; **cards/panels =
`radius-lg` 20**; the orb/main talk box = `radius-sm` 8 (the documented "box radius 20→8" change).
The reference uses iOS "continuous"/squircle corners; on WPF use `CornerRadius` (a slightly
larger radius, or a `PathGeometry` superellipse only if the large-radius pixel diff fails —
plain rounded corners are close enough for most surfaces).

Surface elevation grammar: **canvas** = fill + paper grain, no border/shadow; **raised** =
surface1 + 1px hairline, no shadow; **overlay** = surface2 + hairline + l2 shadow at half-blur
(radius `32`, y `0`). Map the "0-offset 64px blur" shadow to a WPF `DropShadowEffect`
(BlurRadius 64, ShadowDepth 0), tuned to match the alpha.

---

## 5. Motion

```
dur-fast:120ms  dur-base:200ms  dur-slow:300ms  dur-reveal:700ms  pulse-dur:1.6s
ease-out:  cubic-bezier(0.2,0.8,0.2,1)   /* enter — the default */
ease-in:   cubic-bezier(0.4,0,1,1)       /* exit */
ease-soft: cubic-bezier(0.16,1,0.3,1)    /* single gentle dialog overshoot */
livepulse keyframes: 0%,100% opacity .45 scale 1  |  50% opacity 1 scale 1.3
fade-in: opacity 0→1        zoom-in: opacity 0→1 + scale .96→1
```
Charter: **"exuberant restraint"** — 120–300 ms UI, ease-out enter / ease-in exit,
transform+opacity only, interruptible. **No bounce, no spring, no parallax.** Three interaction
curves = fast(120)/standard(200)/settle(300), all ease-out. One ambient loop per page, ≥4 s,
ease-in-out auto-reverse. Press micro-transforms: buttons `scale(0.96)`, mic `scale(0.94)`.
Everything gated on reduced-motion.

**XAML mapping:** reproduce the CSS `cubic-bezier`es with a WPF `Storyboard` animation using a
`KeySpline` on its `KeyFrame`s (the exact control points above); do not eyeball. Per-frame engine
motion ticks on `CompositionTarget.Rendering` (dt-corrected `ease60`). Reduced-motion = read
`SystemParameters.ClientAreaAnimation` / `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)` and
settle to one still frame.

---

## 6. Effects: paper grain, blur, gradients, glow

- **Paper grain**: a **128×128 deterministic monochrome noise tile** (see the LCG seed in `orb-visual-and-box.md §3`), tiled, tinted `ink-primary`, at **opacity 0.035 light / 0.02 dark**; dark tile scaled ×1.5; only on the `canvas` level; removed under reduced-transparency. Port: pre-bake one PNG (or generate via Win2D / a `WriteableBitmap`) and tile it as a low-opacity overlay `Brush` (a tiled `ImageBrush`), hit-test-invisible.
- **Backdrop blur** (orb chrome): hover-bridge `blur(6px)`; edit-pane `rgba(255,255,255,0.82) blur(8px)`; hint pill `rgba(255,255,255,0.72) blur(4px)`; transcript box `blur(10px) saturate(1.3)`; tray popover `linear-gradient(180deg,rgba(225,232,214,.92),rgba(213,222,200,.86)) blur(18px) saturate(1.5)`; dark tray `linear-gradient(180deg,rgba(30,36,26,.78),rgba(24,30,21,.7))`. Port: a WPF `BlurEffect` (or a Win2D `GaussianBlur` on the 2D surface) over a tinted fill, with matching radii + tints. (The *desktop-behind-window* blur is physically unreproducible for the orb — R1 — faked with a static frosted approximation.)
- **Orb sphere gloss** (radial gradient stack) — dark ("night") orb:
  `radial(circle at hx hy, rgba(255,255,255,.18)0%, .05 16%, 0 40%), radial(circle at sx sy, rgba(0,0,0,.55)0%,0 46%), radial(circle at 50% 50%, transparent 58%, rgba(0,0,0,.34)100%)`.
  Glossy light orb: `radial(…, rgba(255,255,255,.65)0%, .18 22%, 0 46%), radial(circle at sx sy, rgba(20,28,12,.30)0%,0 50%), radial(circle at 50% 50%, transparent 56%, rgba(20,28,12,.20)100%)`. `hx/hy` = highlight, `sx/sy` = shadow, both cursor/light-driven. Port to `RadialGradientBrush`es (or Win2D radial gradients on the 2D surface).
- **Wave "thinking" sweep** — `linear-gradient(90deg, transparent, rgba(74,94,232,0.95) 50%, transparent)`, screen blend + `blur(3px)`, band-size 46%×100%, animates 2.6 s (processing) / 2.4 s (edit). The reference deliberately uses the **same indigo for both** ("one thinking color").

---

## 7. Orb floating-bar tokens

Self-contained; the orb carries its own light/dark ("day"/"night") flag.

### 7a. Orb themes
```
/* forest (default) — dark green orb, light dots */
orb-forest-fill #0D1E09;         /* rgb(13,30,9) — the known forest green */
orb-forest-eye #EAF0E2; orb-forest-glow #78B848; /* restA .72, invert, glossy:false */
orb-forest-sat-bg rgba(13,30,9,.88); orb-forest-sat-hover rgba(20,42,13,.96);
orb-forest-sat-bd rgba(255,255,255,.14); orb-forest-sat-fg #EAF0E2;
orb-forest-sat-edit #E6C24C; orb-forest-sat-accent #41691E;
/* mist ("light") — pale green orb, dark dots */
orb-mist-fill #DFEAD1;           /* rgb(223,234,209) */
orb-mist-eye #1B330F; orb-mist-glow #B0D484; /* restA .66, glossy:true */
orb-mist-sat-bg rgba(223,234,209,.92); orb-mist-sat-hover rgb(210,224,190);
orb-mist-sat-bd rgba(24,48,15,.18); orb-mist-sat-fg #1B330F;
orb-mist-sat-edit #A27224; orb-mist-sat-accent #2F7D2E;
```
Orb accents: `idle #41691E`, `listen #E6651B`, `edit #385418`, `hint2-bg #294614`,
`tooltip-bg #18300F` / `tooltip-fg #EAF0E2`, `hint-close #8C8F88`,
`cancel-hover rgba(150,28,26,.92)`, `cancel-hover-mist rgba(216,95,30,.95)`.

### 7b. Orb page (desktop background) themes
```
/* light */ orb-page-paper #F1F4EC; orb-page-paper2 #FFFFFF;
orb-page-fg1 #141414; orb-page-fg2 #666666; orb-page-fg3 #999999;
orb-page-glow: drop rgb(20,20,20) base .28 add .12; glowA .12 blur 40 spread 4
/* dark */ orb-page-paper #121512; orb-page-paper2 #1B1F1A;
orb-page-fg1 #ECEFE8; orb-page-fg2 #C9CDC0; orb-page-fg3 #7D8278;
orb-page-border1 rgba(255,255,255,.12); orb-page-border2 rgba(255,255,255,.10);
orb-page-glow: drop rgb(0,0,0) base .42 add .16; glowA .40 blur 60 spread 9
```

### 7c. Orb transcript box (lb-tx) palette
```
/* light */ tx-box #FCFAF3; tx-card #EFECDF; tx-outline rgba(32,36,31,.14);
tx-base #1A2710; tx-listen #646E58; tx-wave-text #595E50;
tx-del #B81514; tx-del-bg rgba(184,21,20,.10); tx-ins #2F7D2E;
/* night */ tx-box #161616; tx-card #20211E; tx-outline rgba(255,255,255,.08);
tx-base #ECEFE8; tx-listen #9AA192; tx-wave-text #B3B8AC;
tx-del #F0716F; tx-del-bg rgba(240,113,111,.14); tx-ins #8FD06A;
```
Diff/token styling: body 13px, line-height 1.45, wrapped-line gap 3px, chunk gap 9px, dim opacity
0.34; `tok-del` radius 3 / pad 0 2px / opacity 0.85; `tok-ins` weight 600 + underline `tx-ins`@45%
offset 2px; `tok-final` weight 600.

### 7d. Orb geometry & motion (key values — full set in `orb-visual-and-box.md`)
Rest pill 39×15 r7.5 ⇄ woken orb 61×61 r30.5. Kiwi mark 65px. Satellite base 23px. Transcript box
322×108 (min 322×108, max 640×360), radius **8**, pad `14 / 34 / 14 / 52`. Edit pane 212 wide,
radius 20, pad 7. Toast top 104. Motion: **wake lerp 0.30** *(RESOLUTION: the current engine uses
`0.30`; this file's older orb quick-reference and the platform-coupling map say `0.20` — trust
`0.30`, the shipped source of truth)*, expand 0.18, breath period 2.6s, dots step 600ms,
processing 2000ms, done-hold 2000ms, diff 520/1050/620ms, hover-in 44px / out 54px, holdMs 420,
doubleTap 450, longHold 600, press scale 0.95.

---

## 8. Component recipes

| Component | Geometry | Fill / border | Text | Press |
|---|---|---|---|---|
| **Button** (`InkButton`) | h **34**, radius **8**, pad-x 16 | primary: `ink-primary` fill, `canvas` label; secondary: `surface1` + hairline; ghost: bare ink; destructive: clear + `c-error` stroke/label | `.label` role | **settle 1px + darken fill 5%** on press; disabled opacity 0.4; fast curve |
| **Chip** (`InkChip`) | small-caps label + 2px capsule underline | underline `ink-primary` | `.label` small-caps, tracking 6% | selected = underline scaleX 0.4→1 from leading, fast |
| **Segmented** (`SlidingInkSegmented`) | HStack gap 26, clip radius 8 | sliding 2px ink underline (connected-animation/`matchedGeometry` analog) | `.label` | selection slides ≤200ms; **position, not color** |
| **Row** (`KiviRow`) | leading graphic 20px · min-h **44** (52 w/ subtitle), pad-x 16, gap 12 | container surface sits outside the row | title `.body` `ink-primary`, state `.label` `ink-secondary` | optional 6px `c-settled` status dot trailing |
| **MessagePill** | overlay surface, radius **full**, pad `6 v / 16 l / 8 r`, gap 12 | surface2 + hairline + l2 | text `.callout` `ink-secondary`, 1 line | ✕ dismiss 18×18, `ink-tertiary` |
| **Dial** (`InkedDial`) | 2px hairline track, 3px ticks, 12px ink thumb | track `hairline`, ticks `ink-tertiary`, thumb `ink-primary` | annotation `.caption` | thumb moves fast curve |

---

## 9. Windows/.NET notes (macOS/Electron → Windows/.NET)

- **Fonts.** Space Grotesk = OFL (free). **Matter + Season Mix are licensed** — self-host the woff2/otf and confirm redistribution in a .NET installer; set no faux-bold synthesis (ship every weight, reference the exact `FontWeight`). CoreText PostScript-name quirks (Matter Mono ⇒ "Matter SemiMono", variable-wght statics) are irrelevant on Windows — reference families directly.
- **Continuous (squircle) corners** — plain WPF `CornerRadius` is a close approximation; only add a `PathGeometry` superellipse if a large-radius pixel diff fails.
- **Backdrop blur** — a WPF `BlurEffect` (or a Win2D `GaussianBlur` on the 2D surface) over a tinted fill reproduces the orb-chrome blur; the *desktop-behind* blur is excluded from the pixel gate (R1). Legacy KDS pages rely on borders + `shadow-l*`, not blur, so blur is mostly an orb-chrome concern.
- **Theme resolution.** macOS `.system` / Electron `matchMedia` → on Windows read the system app-theme (registry `AppsUseLightTheme` / `SystemParameters`), plus an explicit app preference (`kiviAppearance`: system/light/dark) that overrides. Swap the merged WPF `ResourceDictionary` theme dictionary at the app root. **Remember the dark override:** dark surfaces come from Canon (`#0C0E0C`/`#161616`, accent `#8FCE6E`, warm-tint kept `#404948`), text/borders/state from KDS.dark — do NOT just dim the light palette.
- **Two creams.** `#F1F4EC` (KDS/legacy pages + the orb light page) vs `#F6F3EA` (Canon canvas, new pages/components). Pick per screen; when in doubt for a fresh clone, standardize on Canon.
- **Orb = a separate native Win32 layered window** (`UpdateLayeredWindow`, invisible WPF host for lifetime) with its own manual light/dark flag, cursor-reactive radial-gradient gloss, and a per-frame lerp loop. The gloss `hx/hy/sx/sy` track the cursor (`GetCursorPos` relative to the orb); replace CSS per-frame lerps with the render runtime (`CompositionTarget.Rendering`) using the documented lerp/duration constants (dt-corrected).
- **Reduced-motion / reduced-transparency** — read `SystemParameters.ClientAreaAnimation` / `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)` and the transparency-effects setting; gate paper grain + heavy blurs and settle animations to one frame.
- **Retina/DPI:** all px values are logical (DIPs); the 128px grain tile is authored at 1× — serve DPI-aware so it doesn't read as a screen door on HiDPI displays.

**Deferred / v1 non-goals:** none for tokens — the full token layer is M0 Track B (gates M3).
Font clearance is the one external gate (R12).

> **Not applicable — Windows-only.** The reference's CSS `@import`/CSP self-hosting notes and any
> Linux font-config concerns are dropped; embed fonts as WPF resource fonts (`pack://` URIs).
