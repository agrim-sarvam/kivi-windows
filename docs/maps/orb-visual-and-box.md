# MAP: orb-visual-and-box

Windows-only .NET port of `_reference/sarvam-kivi-electron/docs/maps/orb-visual-and-box.md`.
Every geometry value, color hex, lerp coefficient, and motion duration below is **byte-exact**.
The orb is drawn via a **native layered / Composition window** (`Kivi.Platform.Overlay`) + Win2D
(`Kivi.App/Drawing`); the render model is a pure function of `FlowFrame`.

**Scope note (from the reference):** the **maxi mini-app** is the design to clone — it is the
documented visual baseline. This map describes the maxi design.

---

# KIVI ORB + ORB-BOX MINI-APP — VISUAL/GEOMETRY MAP

## 0. Render model (read this first — it governs everything)

- The entire orb + box is a **pure function of one value type**, `FlowFrame`. Views NEVER animate; they read `FlowFrame` fields and draw. All motion is produced by `FlowEngine.Step(now)` (see `orb-engine-behavior.md`), called per render-loop tick, returning a fresh `FlowFrame`.
- **Easing is per-tick lerp, dt-corrected.** In `Step()`: `dtFrames = clamp((now-prev)/16, 0, 3)`, `ease60(k) = 1 - pow(1-k, dtFrames)`. Every coefficient below (0.30, 0.22, 0.18…) was tuned at a 16 ms cadence. A field snaps to target when `|target-value| < ~0.0005–0.0008` (kills sub-pixel re-render jitter). `reduceMotion` snaps instantly.
- **For the .NET clone:** replicate as a `CompositionTarget.Rendering` loop computing a `FlowFrame`, with `k_eff = 1 - (1-k)^(dt/16)`. Views are frame-driven Win2D/XAML. **Do NOT use XAML Storyboards/transitions for the morphs** — the values are already eased per frame; drive geometry directly each frame.
- Frame-rate tiers: rest 24 fps (20–30), steady 30 (20–40), morph 60 (30–60).

---

## 1. Hosting window & the envelope swap

`Kivi.Platform.Overlay` (was `OrbHostKit.swift` `FloatingBarPanel: NSPanel` / a transparent
`BrowserWindow`).

- The orb lives in a **transparent, borderless, non-activating, always-on-top native window**:
  - Windows: a layered/Composition window with `WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW`, drawn via `UpdateLayeredWindow` (or DirectComposition), no taskbar button.
  - Click-through by default (`WS_EX_TRANSPARENT`), toggled per-tick by the hit-test.
  - The window **never takes keyboard focus** (`WS_EX_NOACTIVATE`) — keeps the host app's text caret during dictation. (The editable-box case briefly makes it activatable — M4, see `orb-engine-behavior.md §2.1 focus contradiction`.)
- **Envelope** (`PanelEnvelope`), swapped live:
  - `.base`  = **1480×720**, `flowTop = 300`
  - `.maxi`  = **1880×1760**, `flowTop = 880`
  - Swap (not a permanent big canvas) keeps the resident window buffer small. The flow anchor stays `flowTop` below the window top in either envelope.
- `flowTopInPanel = 300`, `orbEdgeInset = 24` (orb centre's distance from screen edge).
- Orb sits at window horizontal centre; box is centred under it. Screen-edge overflow handled by `flowShiftX` (§7).

**Windows/.NET:** the two-size envelope swap = resize the layered window between 1480×720 and
1880×1760, keeping the orb anchor point fixed. Feed **logical (DIP) sizes** — the 14″ reference
figures are logical, not device px (R26).

---

## 2. Orb geometry — the pill ⇄ orb ⇄ mini ⇄ take morph

Constants in `FlowEngine` + `DS.Geometry`. Applied in the frame build and the Win2D draw.

| Form | W×H | radius | source |
|---|---|---|---|
| **rest pill** | 39 × 15 | 7.5 | `restSize` |
| **woken orb** (normal) | 61 × 61 | 30.5 | `wakeSize` |
| **mini orb** (setting / expanded tuck) | 42.7 × 42.7 | 21.35 | `wakeSizeMini` (0.7×) |
| **pill-take** (dictating pill) | 57 × 18 | 9 | `pillTakeW/H` |
| orb zone | 62 × 76 | — | `orbZoneW/H` |
| kiwi mark canvas | 65 | — | `markSize` |

Morph math per frame (`open` ∈ 0..1 wake amount, `exp` ∈ 0..1 box-expand, `pillPop` ∈ 0..1):
```
orbWidth  = 39 + (wokenW - 39)*open        // wokenW = 61 normal / 42.7 mini / 39 pill
orbHeight = 15 + (wokenH - 15)*open
orbRadius = 7.5 + (wokenR - 7.5)*open
// EXPANDED hinge-top shrink toward mini:
if exp>0: orbW += (42.7 - orbW)*exp ; orbH += (42.7 - orbH)*exp ; orbR += (21.35 - orbR)*exp
// PILL-take pop:
if pillPop>0: orbW += (57-orbW)*pillPop ; orbH += (18-orbH)*pillPop ; orbR += (9-orbR)*pillPop
drop = -6*(1-open) - 1.5*pillPop           // vertical offset; TOP EDGE fixed on expand (hinge)
press = pressed ? 0.95 : 1                 // click scale
```
Morph lerps (`ease60`): **wake 0.30** *(RESOLUTION: current engine uses `0.30`; the
platform-coupling quick-reference and the design-tokens §7d older note say `0.20` — trust
`0.30`)*, collapse `0.16+(1-open)·0.24`, expand 0.24, box-size 0.22, pillPop 0.18. Phase flips at
`open>0.86` (→idle) and `open<0.04` (→rest).

The **hinge-top expand** is the signature: `drop` is untouched by `exp`, so the orb's top edge
stays pixel-fixed while its body shrinks to mini — hinged at the top, box unfurls downward.
Bottom-docked (`flipY`) mirrors everything about the pill centre y=1.5 (orb grows UP, box above).

The rounded shape is a `RoundedRectangle` (`.circular`, i.e. plain rounded corners). Selection-pill
state morphs body toward 39→`selectionPillWidth`, H→22, R→11.

---

## 3. Orb surface layers (z-stacked, drawn back-to-front, clipped to the rounded shape)

1. **Backdrop glass** (`backdropBlur = 10*(1-open)` px — blur fades as the orb fills; hidden when `open>0.92`). macOS `NSVisualEffectView` / Electron `backdrop-filter` → Windows: an `AcrylicBrush` / Composition `GaussianBlur` on the pill element for the *local* frost. **The desktop-behind-window blur is physically unreproducible (R1)** — faked with a static frosted approximation, excluded from the pixel gate.
2. **Fill**: `orb.fillRGB` at `fillAlpha = restA + (1-restA)*open`. Forest orb fill `rgb(13,30,9)` restA 0.72; mist orb fill `rgb(223,234,209)` restA 0.66.
3. **Paper grain**: a **128×128 deterministic noise tile** (seed `0x4B49564950415045`, LCG `seed*6364136223846793005+1442695040888963407`, alpha = high byte), template-tinted `inkPrimary`, tiled, nearest-neighbor. Opacity **0.035 light / 0.02 dark**; dark scaled 1.5×. Removed under reduced-transparency. Used on orb, box, satellites. **Port the LCG verbatim** to a Win2D-generated tile or a pre-baked PNG.
4. **Selection chip** (word-count text mono 10.5 + app-icon glyph 14×14) when a host selection is captured.
5. **Rest eyes** (collapsed pill face): two capsules, open-diameter `eyeD = 0.36·15 = 5.4`, closed line `1.8`, spacing `0.62·15 = 9.3`. `eyeH = 1.8 + (5.4-1.8)*eyeOpen`. Breath scale `1+(eyeScale-1)*eyeOpen` where `eyeScale = 0.90+0.20·breath`. Eyes wear theme eye color (`orb.eye`: forest `#EAF0E2`, mist `#1B330F`) except in pill mode where they take the live state glow color. **eyeOpen** eases 0→1 (`ease60 0.18`): idle/rest → shut flat line; any active mark → open breathing dots.
6. **Pill-take face** (when `pillFace>0.001`): 7 vertical mic **bars** (width 2.6, spacing 3.4, height `max(3.4,(H-7)·energy)`, energy driven by `sin` seeds `[0.55,0.9,0.4,1.0,0.65,0.85,0.5]`, phases `[0,1.7,3.1,4.4,0.9,2.4,5.2]` + live mic level) while listening/speaking; morphs to two glowing **eyes** (Ø6.4, pulse `1+0.10·sin`, shadow radius 5.5) while processing. Colored by `f.glowColor`.
7. **Living kiwi mark** (`KiwiMarkEngine.Draw` → Win2D `CanvasControl`/`CanvasVirtualControl`): dotted walking-kiwi per tick, scaled `min(1, bodyWidth/61)`, opacity `markOpacity`. A full dot-render engine (see `orb-engine-behavior.md §10`) — the orb's "face" when awake.
8. **Sphere overlay**: specular highlight radial gradient at (`0.5+lightX·0.5`, `0.5+lightY·0.5`), rim shadow at antipode, edge vignette. Glossy (mist): white 0.65→0.18→0, warm rim. Non-glossy (forest): white 0.18→0.05→0 + black rim 0.55, vignette black 0.34. Light target follows cursor via `OrbLightTarget(nx,ny)` (`sphereLightLerp 0.16`) — driven by `GetCursorPos` relative to the orb. Use the farthest-corner radial extent.

### 3a. 4-layer glow (behind the orb)
Each layer = the orb silhouette grown by a "spread", blurred by `blur/2` (σ≈blur/2). Per frame:
```
dropShadow: yOffset 6+8·open, blur 18+12·open, spread -4, alpha dropBase+dropAdd·open   (color: page.glow.dropRGB — black)
DS ambient l2: spread 0, blur page.shadowL2.blur/2                                        (color page.shadowL2)
white halo: spread glowSpread·open·breathS, blur glowBlur·1.15·open, alpha glowA·0.7·open·breathA   (white)
core glow: spread glowSpread·0.5·open·breathS, blur glowBlur·0.55·open, alpha glowA·open·breathA     (f.glowColor)
```
Page constants: **dark** glowA 0.40, glowBlur 60, glowSpread 9, dropBase 0.42, dropAdd 0.16;
**light** glowA 0.24, glowBlur 40, glowSpread 4, dropBase 0.28, dropAdd 0.12. Breath swell:
`breathA = 0.91+0.09·bq`, `breathS = 0.95+0.10·bq`, `bq` = breath quantized to **12 steps** (perf:
avoids re-blur every tick). `glowColor` **eases** (`ease60 0.09`) toward the per-state color and is
rounded to integer RGB (quantized so blur layers are reused between frames). Win2D: render the
silhouette + a `GaussianBlurEffect` per layer; the 12-step breath quantization keeps blur reuse.

### 3b. Breath
`b = 0.5 + 0.5·sin(now/1000 · 2π/2.6)` — global 2.6 s brand breath. `f.breath = b`.

### 3c. State-color table (`KiwiMarkEngine.StateColor`, exact RGB)
Drives glow, pill face, eyes-in-pill.

| state | dark orb RGB | light orb RGB | motion |
|---|---|---|---|
| idle | 250,252,246 | 70,78,62 | still |
| **listening** (dictate) | 248,168,108 (orange) | 208,92,30 | WALKS + voice-breath |
| **processing** | 120,140,255 (blue) | 48,60,200 | still |
| **speaking** (hey-kivi) | 156,206,108 (green) | 56,112,36 | WALKS + voice-breath |
| done | 166,214,118 | 64,124,40 | still |
| editing | 242,200,104 | 162,114,36 | walks |
| error | 184,21,20 | 184,21,20 | still |
| waiting | 210,150,45 | 210,150,45 | breathes |
| idle-glow fallback | restGlow 214,220,230 (dark) | 116,126,142 (light) | — |

---

## 4. Satellites / companions

**CROSS layout** around the orb centre (31, 30.5), gap 6, authored on the 61px woken orb:
- **LEFT — hey-kivi** (double 4-point star): `satEditX -33, satEditY 16.75, satEditSize 27.5`. While a take is live it wears the **host app's icon standing alone** (no circle/border, sized `size+5`).
- **RIGHT — open-kivi ⚙ ↔ cancel ✕ ↔ copy** (tri-mode, same slot): `satSettingsX 67.5, Y 16.75, size 27.5`. Morphs to red **✕ cancel** while a take/edit is live (`cancelHover` fill `rgb(150,28,26,.92)` forest / `rgb(216,95,30,.95)` mist, × turns white); morphs to **copy** icon with a yellow breathing tint (`rgb(222,172,46)`, glow 9, alpha 0.30+0.30·breath) when a take parked text with no field to paste (the "manual copy" hot window).
- **BELOW — expand**: `satExpandX 19.5, satExpandY 64, size 23`. **Reveal-on-hover**: invisible until cursor is on the orb cluster, then faded-grey 0.8× until hovered; auto-fades after **1.5 s** dwell. Suppressed while expanded.
- **Side-bubble live sizing** (`satSideLiveSize`): 27.5 normal → **18.5** mini/expanded (blend on `exp`); icon 15 → 12.5. Pill mode: side circles tucked at pill ends (gap 3.5), glide out on expand.

Bubble visual: `Circle().Fill(bg)` + paper grain + `strokeBorder(theme.bd, 1)` + glyph. Shadow
`rgb(20,20,20,.45) r4.5 y3`. Hover/tint animate ease-out 0.15; press scale 0.86. Tooltip flag =
mono 10, fg `#EAF0E2` on bg `#18300F` (grey variant white/0.80 on white/0.22), radius 6. Tooltips
are **geometric-hover driven** (`f.hoveredTarget`), computed in `FlowFrame.InteractiveTarget`
(rounded-shape hit tests, hit radius = 1.5× visible).

**Drag handle** (movable mode only): 2×3 grid of Ø4 dots (spacing 3), grey `#8A8F86` rest →
mist-white `#ECF1E6` active; hit box 28×20 at `dragHandleY -19` above orb; grab/grabbing cursor
(`ProtectedCursor`/`InputCursor`); tooltip "drag to move · double-click to dock".

**Sub-views:** edit pane (width 212, radius 20, pad 7, item font 14.5, opens on the side opposite
the box), hint pills (mono narration, radius 9, gated on the `tooltips` setting), toast (orange
`#E6651B` pill, mono 12.5, radius 9).

---

## 5. Icons

All authored in a **24×24 space, stroke 2 (round cap/join)**, scaled `size/24`. Port each path to
XAML `PathGeometry` (or Win2D `CanvasPathBuilder`) verbatim. Enum: `pencil, gear, expand, cross,
copy, chevronLeft, chevronRight, playback, collapse, polish, lines3, lines3Short, mic, newSession,
sparkles, check, thumbUp, thumbDown, maximize, restore`.

Key maxi icon paths (exact):
- **maximize** (arrows OUTWARD NE+SW): `(13.5,10.5)→(20,4)`; bracket `(20,9.5)→(20,4)→(14.5,4)`; `(10.5,13.5)→(4,20)`; `(4,14.5)→(4,20)→(9.5,20)`.
- **restore** (arrows INWARD): `(20,4)→(13.5,10.5)`; `(13.5,5)→(13.5,10.5)→(19,10.5)`; `(4,20)→(10.5,13.5)`; `(10.5,19)→(10.5,13.5)→(5,13.5)`.
- **sparkles** = big 4-point concave star at (9.5,9.5) r6 + small at (18,18) r3.3 (inner offset r·0.28).
- **copy** = rounded rect (9,9,11,11 r2) + open back-sheet path. **check** = `(5,12.5)→(10,17.5)→(19,6.5)`.

---

## 6. The orb-box: popover shape & the wedge

`WedgeBoxShape`: a rounded rect (radius `txBoxRadius = 8`) with a **centred triangular wedge**
rising from the top edge toward the orb (apex flips to bottom when `flipY`). Wedge: **W 20, H 9,
gap-to-orb 3**, apex softened with a quad curve (tip radius 3). The wedge zone is dead space padded
at the box top (`boxWedgeH 9`). Box fill `pal.box`, 1px `strokeBorder(pal.outline)`, paper grain
clipped to the shape, and a geometry-only drop-shadow (radius **64/2 light, 40/2 dark**; light
`rgb(20,20,20,.08)`, dark `black 0.4`). Build with a Win2D `CanvasPathBuilder` (rect + wedge).

---

## 7. Box sizing — default / max / MAXI plateau curve

| tier | W×H |
|---|---|
| `boxDefault` | 322 × 108 |
| `boxMax` (normal ceiling) | 640 × 360 |
| `boxMaxiCap` (absolute plateau) | **840 × 800** |
| `maxiReferenceScreen` (14″ logical) | 1512 × 982 |

**The plateau/curve** (`maxiBoxTarget(screenW, screenH)`):
```
refW = 1512*0.5  = 756       // half width at the 14" reference
refH =  982*0.75 = 736.5     // three-quarter height at the reference
w = min( min(screenW*0.5, refW + 0.18*max(0, screenW-1512)), 840 )
h = min( min(screenH*0.75, refH + 0.18*max(0, screenH-982)), 800 )
```
Up to the 14″ reference the maxi box is **half the screen width / three-quarters the height**; past
it, growth is **sub-proportional (slope 0.18)** and **plateaus at 840×800** ("a mini-app, not a
takeover"). Read screen size from the .NET `DisplayArea`/work-area in **logical (DIP) px** (not
device px, or the curve mis-scales on HiDPI — R26).

**Size resolution** (`applyBoxTargets`): one resolver used by content-change, maxi-toggle, and
ceiling-change so they can never disagree. `ceiling = boxMaxi ? maxiGrantedSize() : normalCeiling()`.
Maxi **raises the ceiling, never sets a floor** — the box still hugs its content.
`boxWTarget = clamp(fitRequestedW, boxDefault.w, ceiling.w)`;
`boxGrowDownTarget = clamp(fitRequestedH, ..) - boxDefault.h`. Box grows **downward from a fixed
top** (`boxGrowUp=0`).

**maxi toggle** (`maxiClick`): needs `maxiHasScope` (hidden content) to engage; restore always
allowed. Resets on any close.

**Box position** (`FlowFrame.BoxTopOffset`): `seam = drop + orbHeight + boxWedgeGap(3)`;
`baseTop = flipY ? (3-seam-boxH) : seam`; box centred under orb at `x = zoneW/2 - boxW/2`.
**Screen-edge correction only**: `flowShiftX = (max(0, boxW/2+8 - roomLeft) - max(0, boxW/2+8 -
roomRight)) · exp`.

**Hinge-top reveal**: the box is present at full width from frame 1 and revealed via a **vertical
height mask** `Rectangle(height: txWrapHeight)` top-anchored (bottom-anchored flipped) — it unfurls
downward, no sliding. `txOpacity = min(1, exp·2.2)`, `txInteractive = exp>0.6`.

**Box ease**: `boxW += (boxWTarget-boxW)·ease60(0.22)`, `boxGrowDown += (…)·ease60(0.22)`, snap when
`<0.5`. Expand `exp += (…)·ease60(0.24)` (~180 ms to full).

---

## 8. Box internals — the turn surface

Vertical stack inside the wedge-padded surface: **Header row → context card (optional) → inner
textbox → footer bar**. `f.boxMaxi` steps sizes up.

### 8a. Header row (height 26 / **30 maxi**, pad top10 lead16 bot8 trail16)
- **App chip**: app-icon glyph (from the take host app) **18×18 / 20 maxi**, rounded 3.2.
- **App name**: display font medium **13 / 15 maxi**, `pal.base`.
- **State narration** (top-right, mono **10**): `"listening \(dots)"` (green `pal.ins`) while listening; `"transcribing …"` (orange) processing; `"editing …"` edit; escalation copy `"speak now …"`/`"are you there?"`/`"mic may not be working"`/`"check mic settings"`; notices quiet grey; **nothing at idle**.
- **Expand/restore control** (`.maximize`/`.restore`, 12px in a 24×24 tap target, radius 7): green wash when maxi active, else `pal.card`. **Fades to opacity 0.22 & disables when `boxCanMaxi` is false**.
- **Error dot** (replaces expand on banner): Ø22, red `pal.del` fill 0.14/stroke 0.45, "!" bold 12, shadow `pal.del·(0.22+0.18·breath)` r6; header shows "error" jittering (`boxShakeX`); hover unfolds full message card (reading 11, max width 240).
- **Pager dots** (centred overlay): active capsule **16×6** `pal.ins`, inactive **6×6** `pal.base·0.3`, spacing 4, **capped at 10**.

### 8b. Context / reference card (hey-kivi callout)
`"◨ kind"` mono 9 + preview display 12 + italic underlined "More…". Line clamp **2 normal / 7
maxi**. Card radius 9, `pal.card` fill, min-height 40; tap → `PrevClick()`. Inset `txContextInset
10`.

### 8c. The inner textbox — two skins
`boxHasContent` decides:
- **Empty & idle → green invitation card**: `pal.ins` fill 0.09, stroke 0.18, radius 6, height 48, inset `txHintInset 12`. A **rotating one-line hint** floats over the caret spot: messages rotate every **3500 ms** with a 300 ms crossfade — `"tap to talk"`, `"press and hold to talk"`, `"type or paste to edit"`, etc. (Windows: the reference `"fn"` keycap copy → the app's bound hotkey label, e.g. `"right ⌃"` / the user's rebind.) Keycaps: bold 9.5, white on `#41691E`.
- **Has words → crisp card**: fill `pal.boxInner` (**light `#F7F9EC`, dark `#1D231A`**), radius 6, 1px `pal.outline`, **heavy lift shadow** `black·(0.16 light/0.55 dark) r14 y4` + tight contact `black·(0.08/0.35) r3 y1`. Padding top10/bottom12/lead16/trail(44 if copy chip up else 16). **Side inset grows with box**: `innerInset = 16 → 64` (over `boxW 480→760`), `topGap = 4 → 16`, `bottomInset = 16 → 32`.
- **Copy chip** (top-right, offset 8): 28×28 tap target, icon 13, radius 5, `pal.ins` colored, wash `pal.card`→`pal.ins·0.16` + `✓` on `copyFlash`. Only when `txStage ∈ {done,typed,pasted}` and non-empty.

Content font: **reading face = Matter**, size **14 compact / 16 maxi**, lineSpacing `size·0.45`
rounded.

### 8d. Footer action bar (height 30, pad top8 lead12 bot10 trail12, 1px top hairline)
Left **voice slot** (one `slotPill`, radius 9, display medium 12.5): `retry` (orange) / `follow up`
(sparkles, green, glowing while editing) / `last` (playback, while dictating/history) / `follow
up`+keycaps when settled. Word count (mono 10) when settled. **Thumbs** (`f.takeRatable` only): 👍
then 👎, each 28×28 tap target, radius 8, stroke `pal.base·0.25`→`pal.ins·0.4` active, icon 14.
Right **`new session`** ("+"), with a live orange flow-band sweeping the pill while dictating
(period 1300 ms). Fresh pane dims whole bar to 0.72.

### 8e. Scroll behavior (the "glitch fixed 4 ways")
**Hysteresis**: enter "near" within **6 px**, leave only past **18 px** (dead band stops the
elastic end-bounce from flipping fades every frame). Dual progressive fades: 40 px gradient
top+bottom, opacity-animated (ease-out 0.18) on the damped edge flips, always mounted while
`txClipped`. Streaming auto-follow yields to manual scroll (`userScrolledInTake`). `txClipped =
fitRequestedH > granted+0.5`. Port to a WinUI `ScrollViewer` with the same hysteresis + gradient
fade overlays.

### 8f. Content-driven sizing (`BoxContentFit`)
Measures display text with the exact render metrics (line-fragment origin, +4 px for inset/rounding)
at fixed candidate widths → asks `FitBoxToContent(w,h)`. Widths step **322 → 460 (`wideW`) → maxi
620+160 pads** once wider tiers would clip (`widenThresholdH 150`, maxi step at `wideH>300`). Chrome
height `txHeaderBlockH 44 + txFooterBlockH 56 + boxWedgeH 9 + txBoxPadsV 22 + inset 16 + 4`. Empty
pane fits exactly to chrome + 54. Context card adds 62, banner/notice add 44. In .NET, measure with
`CanvasTextLayout` (Win2D) or a `TextBlock` measure pass using the exact font metrics.

---

## 9. Wave sweep & diff morph (in the transcript text)
- **WaveGlow**: 46%-wide gradient band, `blur 3`, position −55%→155% looping; period **2.6 s processing / 2.4 s edit**; color indigo `rgb(74,94,232,.95)` (both processing & edit) or green `rgb(143,206,110,.95)` while hey-kivi listens; blends only over the glyphs (source-atop); text dims to 0.78 during wave.
- **Diff morph** (three beats, `DiffProgress`): `DIFF 520 ms / HOLD 1050 / SETTLE 620` (the engine compresses these to 150+100+250=500ms in the orb — see `orb-engine-behavior.md §9.4`). Deletions tint base→red + strikethrough + bg 0.10 then collapse (font shrinks `size·(1-collapse)`, kern `-0.2·size·s`); insertions grow from 0 + green underline (`tokInsUnderlineAlpha 0.45`); del/ins colors `pal.del`/`pal.ins`.

---

## 10. Fonts (`DS.Fonts`)
- **Matter** (`Matter-Regular/Medium/SemiBold/Bold`) — reading face, keycaps (bold).
- **Matter SemiMono** (`MatterSemiMono-Regular`) — hints, tips, toast, state narration, word count.
- **Space Grotesk** (`SpaceGrotesk-Regular/Medium/SemiBold/Bold`) — classic transcript body, edit-pane items.
Role sizes: hint 11, hint2 10.5, satTip/epHead 10, epItem 14.5, txBody 13 (reading 14/16), txKey
9.5, toast 12.5, keycap 11. Line-height 1.45; intra-segment lineSpacing 3 (classic) or `size·0.45`
(reading); paragraph/chunk spacing 9.

**Windows:** ship these as embedded font files; Space Grotesk (OFL) is free, Matter + Season Mix are
licensed (see `design-tokens.md §1`, R12). Reference the variable Space Grotesk via
`font-variation-settings` analog or ship static weights.

---

## 11. Color reference (light / dark)

**Page**: light paper `#F1F4EC` / dark `#121512`; fg1 `rgb(20,20,20)`/`#ECEFE8`. **Box**: light box
`#FCFAF3`, boxInner `#F7F9EC`, card `#EFECDF`, base `#1A2710`, listen `#646E58`, ins `#2F7D2E`, del
`#B81514`; dark box `#161616`, boxInner `#1D231A`, card `#20211E`, base `#ECEFE8`, listen
`#9AA192`, ins `#8FD06A`, del `#F0716F`. **Accents**: idle `#41691E`, listen(orange) `#E6651B`,
edit `#385418`, tooltipBg `#18300F`, tooltipFg `#EAF0E2`.

---

## 12. Timings & motion table (`DS.Motion`)

| what | value |
|---|---|
| breath period | 2.6 s |
| wake / collapse lerp | **0.30** / `0.16+(1-open)·0.24` |
| expand / box-size lerp | 0.24 / 0.22 |
| pane open lerp | 0.28 |
| glow-color ease | 0.09 |
| listening dots step | 600 ms |
| chunk fade-in | 240 ms |
| processing / done-hold | 2000 / 2000 ms |
| edit apply / edited-hold | 1700 / 1100 ms |
| diff beats | 520 / 1050 / 620 ms |
| hold gesture / double-tap / long-hold | 420 / 450 / 600 ms |
| toast / copied-flash / shake | 1500 / 1100 / 450 ms |
| hover radii in/out | 44 / 54 px |
| press scale / drop | 0.95 / -6 px |
| idle-hint rotation / crossfade | 3500 / 300 ms |
| reveal-on-hover dwell | 1500 ms |
| scroll hysteresis near/leave | 6 / 18 px |

---

## Windows/.NET notes (macOS/Electron → Windows/.NET)

1. **Transparent non-activating window** (`NSPanel .nonactivatingPanel` / transparent `BrowserWindow` `focusable:false`): the whole point is dictated text lands in the *host* app, not Kivi. Windows: a **native layered/Composition window** with `WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TOOLWINDOW`, drawn via `UpdateLayeredWindow`/DirectComposition, click-through via `WS_EX_TRANSPARENT` toggled per-frame from the JS-equivalent port of `FlowFrame.InteractiveTarget`. `WS_EX_NOACTIVATE` gives true non-activation (R20). (See the `orb-is-a-chip` memo: WinUI cannot host a truly transparent non-activating window — hence the native layered window with an invisible WinUI anchor for lifetime.)
2. **Backdrop blur / cross-space** → `AcrylicBrush` / Composition `GaussianBlur` for the *local* frost. There is no cross-Space concept on Windows — the window floats on the current desktop, always-on-top. The desktop-behind blur is excluded (R1).
3. **First-mouse + per-tick click-through** → reproduce the hit-test in C# and toggle click-through each tick. Load-bearing — clickable/hoverable/tooltip regions are ONE function (`InteractiveTarget`); keep unified.
4. **App icon + display name for the header chip** (`NSWorkspace.icon(forFile:)`) → Windows `SHGetFileInfo` / PE-resource extraction keyed by exe path; cache; fall back to the kivi logo. (See `personalization-subsystem.md`.)
5. **Screen geometry** for the maxi plateau curve → feed the .NET `DisplayArea`/work-area size in **logical (DIP) px** (not device px). The 14″ reference (1512×982 logical) is a logical figure.
6. **Fonts** — embed Matter / Matter Mono / Space Grotesk; Space Grotesk needs static cuts or the variation-settings analog.
7. **Cursor management** (open/closed hand on the drag handle) → WinUI `ProtectedCursor` / `InputCursor` (grab/grabbing).
8. **Reduce Motion / Reduce Transparency** → read `UISettings.AnimationsEnabled` / the transparency-effects setting; `reduceMotion` snaps all eases; `reduceTransparency` zeroes paper grain.
9. **PaperGrain** — port the LCG verbatim to a Win2D-generated data tile or a static PNG, tiled nearest-neighbor, tinted, at 0.035/0.02 opacity.
10. **The kiwi mark** — a large standalone Win2D engine (dotted walking-bird), the single biggest port item for visual fidelity; draws per-tick into a 65px canvas, scaled with the live orb. Its own module (`Kivi.Core.KiwiMark` + `Kivi.App/Drawing`).

**Deferred / v1 non-goals:** maxi in-box editing + drag-to-move (M4); the desktop-behind blur (faked,
R1); act/hey-kivi companion tri-mode is post-MVP polish.

> **Not applicable — Windows-only.** The reference's Linux compositor-transparency and Wayland
> placement/focus caveats are dropped; DWM composition is always on for modern Windows.
