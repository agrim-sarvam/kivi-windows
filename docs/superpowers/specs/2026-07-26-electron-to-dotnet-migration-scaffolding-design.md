# Electron → .NET Migration Scaffolding — Design Spec

**Date:** 2026-07-26
**Status:** Approved (brainstorming complete; ready for implementation-plan phase)
**Scope:** The *scaffolding and process* for porting Kivi from Electron to .NET/Windows — the `CLAUDE.md`, the agent roster and each agent's charter, the `docs/` structure, and the parity-gate process. **Not** the per-milestone port itself (each milestone gets its own plan later).

---

## 1. Context & the single most important fact

Kivi is entering a new phase: port the Electron codebase to .NET/Windows, then finish the remaining backend wiring for the first MVP. The Electron codebase lives at `_reference/sarvam-kivi-electron`.

**The reference is not a docs dump — it is a ~60%-complete, tested implementation with a lead-architect-grade doc set.** This reframes the whole migration:

- **Lineage is three layers deep:** original macOS Swift app → Electron/TypeScript clone (in `_reference`) → .NET/Windows (this project). For nearly every feature there are *two* reference implementations; the **TypeScript is the closer analog** to what we build (already de-Swift-ified, injected-time, DOM-separated).
- **The pure logic is already ported once from Swift.** `packages/orb-core` (~3400 LOC) is the 4218-line Swift `FlowEngine` as clean, DOM-free, injected-time TypeScript verified at **100% golden-frame parity**. `FlowFrame` (~120 fields), `TranscriptModel`, `CueBus`, `SpeechPace`, constants, the 120×162 `KiwiData` mask, and the `KiwiMarkEngine` Canvas algorithm are all pure and directly portable to C#.
- **The docs are exceptional and authoritative:** `docs/MASTER-PLAN.md` (M0–M9 roadmap + 28-item risk register), `docs/FEATURE-PARITY.md` (131 features, P0 shortlist of 15), `docs/GOAL.md`, `docs/PROGRESS.md`, and 12 architecture/behavior maps under `docs/maps/` with every parity constant (endpoints, budgets, easing curves, color hexes, geometry).
- **The current `Kivi.*` .NET code is throwaway/reference-only.** It is read to understand what was tried, but is *not* the base and is *not* cherry-picked. Even the platform seams are rebuilt from scratch (see §4 decisions).

## 2. Approved decisions (the invariants the scaffolding must encode)

These were settled during brainstorming and are non-negotiable inputs to the scaffolding:

1. **Source of truth = the Electron app** (`_reference/sarvam-kivi-electron`). The current `Kivi.*` .NET code is read-only reference, not the base, not cherry-picked.
2. **Porting rule:** mirror the Electron structure by default; **diverge only on the four tripwires** —
   - **T1 — platform-native seams:** windowing, tray, global hotkey, layered orb, audio capture, clipboard/paste injection, secrets, frontmost-app. Electron does these in JS via Chromium/Node; .NET does them natively. Mirror the *feature*, not the file.
   - **T2 — async/threading model:** Electron IPC + EventEmitter → `async`/`await`, `Task`, events. No fake renderer↔main IPC bus in a single-process .NET app.
   - **T3 — UI layer:** React components → XAML + MVVM. Mirror screens and view logic, not the component tree.
   - **T4 — DI/lifetime:** proper .NET DI container + interface-based services. Don't replicate Electron module-singleton patterns that fight DI.
   Everything else — wire client, request/response models, feature flags, config schemas, business logic, state shapes, domain model — mirrors Electron as closely as possible.
3. **Docs-first .NET rewrite.** A `docs-porter` phase converts the maps + MASTER-PLAN + FEATURE-PARITY into clean **Windows-only .NET** docs under our `docs/` before code porting. macOS/Linux/Electron-isms stripped; parity constants preserved.
4. **Native seams rebuilt from scratch** to the Electron/OpenWhispr patterns (terminal-detection paste, release-held-modifiers-before-paste, dedicated-thread low-level hook, WASAPI capture with continuous resampler state). The current `Kivi.Platform` code is *reference to read, not code to lift*.
5. **UI fidelity is a hard acceptance gate.** The UI is almost entirely code-drawn (only one real image asset, `build/icon.png`): inline SVG + CSS + Canvas driven by design tokens. Fidelity therefore means porting **token values, SVG paths, and Canvas algorithms exactly**, then verifying **side-by-side Electron-vs-.NET per screen** before a screen is "done." Every animation is **spec-extracted** (exact duration / easing curve / delay / property / from→to) and reproduced in XAML Composition/Storyboards with matching values.
6. **Fonts:** Space Grotesk (3 woff2, OFL) is shippable. Matter, Matter Mono, Season Mix (11 woff2, proprietary/uncleared) are **dev-only for parity**; ship the documented fallback stacks until licensing clears. This is a tracked cross-team gate.
7. **Milestone sequence** follows the Electron `MASTER-PLAN.md` M0–M9; the **P0 shortlist (15 features) is the MVP bar**.

## 3. The natural layering (drives the agent roster)

The Electron code sorts cleanly into three buckets with sharply different skills, tools, and verification methods. Crucially, these buckets cut **across** every feature — which is why the roster is organized **by discipline/layer, not by feature vertical** (a feature-vertical agent would need wire-protocol *and* XAML-Composition *and* Win32-interop expertise at once).

| Bucket | What (examples) | .NET treatment | Verification | Difficulty |
|---|---|---|---|---|
| **Pure logic/data** | `orb-core` engine, `FlowFrame`, transcript, design tokens, `KiwiData` mask, wire message shapes, budgets, IPC/auth contracts, personas domain model + resolver | Port ~1:1 to C# (mirror structure) | Ported golden-frame JSON oracles; unit tests | Low — mechanical |
| **OS-coupled** | STT WebSocket client (→ `ClientWebSocket`), platform seams (hotkey/paste/frontmost/overlay), mic capture (WASAPI), tray, auth/OAuth, lifecycle | Reimplement natively from scratch | Live local `kivi-service`; OS-level integration harness | Med — well-specified |
| **View layer** | orb render (Canvas → Win2D/Composition), main-window pages (React → XAML/MVVM), all styling | Rebuild in XAML reading the same `FlowFrame` contract | Side-by-side visual + motion parity per screen | High — the bulk + the fidelity gate |

## 4. Deliverables of the scaffolding phase

The scaffolding phase (post-branch, post-clean, done by us) produces exactly these artifacts. This spec defines *what each contains*; the implementation plan sequences building them.

### 4.1 `CLAUDE.md` (repo root)
Encodes the invariants every agent obeys regardless of task:
- **Source-of-truth rule** and the read-only status of legacy `Kivi.*` code.
- **The four divergence tripwires** (T1–T4) as the decision rule for "mirror vs. diverge."
- **Parity-constants pointer:** link to the ported parity docs (§4.3) and the rule that wire/audio/engine constants are byte-exact (see `kivi-wire-parity-constants` memory).
- **UI-fidelity gate:** assets copied 1:1 (there is essentially one), tokens/SVG/Canvas ported exactly, motion spec-extracted + reproduced + **side-by-side verified per screen before "done."**
- **Font-license constraint** and the fallback-stack rule.
- **P0 shortlist = MVP bar**; milestone order M0→M9.
- **Where things live:** `_reference/sarvam-kivi-electron` (Electron truth, never modified), our `docs/` (ported .NET docs), the `Kivi.*` solution (target), and how to run the local `kivi-service` for parity tests.
- **Build/test discipline:** how to build the solution, run tests, and which parity gate applies to which bucket.

### 4.2 Agent roster (`.claude/agents/*.md` or SDK agent defs)
Discipline/layer agents, each with a focused charter, tool set, and verification method:

1. **`docs-porter`** — converts each Electron doc/map into a .NET/Windows-only equivalent under our `docs/`, stripping macOS/Linux/Electron-isms, preserving every parity constant. **Runs first.** Output feeds all other agents.
2. **`core-porter`** — ports the pure-logic bucket to C# (`Kivi.Core`): `FlowEngine`, `FlowFrame`, transcript, tokens, wire models, domain models. Verifies against ported golden-frame JSON oracles.
3. **`wire-backend`** — the STT `ClientWebSocket` client, REST surface, auth/JWT mint, budgets. **Byte-exact wire parity**; tests against the live local `kivi-service`. Owns the "A3 trap" guards and drain-before-EOS ordering.
4. **`platform-native`** — the Windows seams rebuilt from scratch (hotkey with dedicated-thread hook, paste with terminal-detection + modifier-release, frontmost-app, non-activating overlay, WASAPI mic with continuous resampler state, DPAPI secrets, tray). Reads the current `Kivi.Platform` and the OpenWhispr reference as *references*, not sources to lift.
5. **`ui-fidelity`** — the XAML/Composition view layer + the hard motion/pixel gate. Owns side-by-side Electron-vs-.NET verification per screen. Largest and most specialized.
6. **Lead orchestration** (human + primary Claude) — DI wiring, milestone sequencing, running parity gates, integration. Not a subagent.

Each agent `.md` charter states: its bucket, its inputs (which ported docs / which `_reference` paths), its output location in the `Kivi.*` tree, its divergence tripwires, and its verification gate.

### 4.3 `docs/` structure (our ported, Windows-only docs)
Proposed layout (final structure decided in the implementation plan):
- `docs/architecture/` — the .NET target architecture (project layout, DI, threading model, window topology) — the T1–T4 divergences made concrete for Windows.
- `docs/maps/` — Windows-only ports of the 12 Electron maps (wire, audio, engine, tokens, orb-visual, main-window, personalization, auth/tray, platform, packaging).
- `docs/parity/` — the parity-constant reference (wire/audio/engine/tokens/geometry) and the per-screen parity-gate checklists.
- `docs/roadmap/` — the .NET milestone roadmap derived from MASTER-PLAN M0–M9 + the P0 shortlist as the MVP bar + a port-status tracker (the analog of `PROGRESS.md`).
- `docs/superpowers/specs/` — this spec and future per-milestone specs.

### 4.4 Parity-gate process
- **Pure-logic gate:** port the golden-frame JSON oracles from `_reference/test/golden-frames/` and assert the C# engine reproduces `FlowFrame` fields under the per-field tolerance policy (exact on discrete/quantized; drift-budget on eased scalars).
- **Wire gate:** stream fixture WAVs through the C# client against the live local `kivi-service`; assert `final.formatted_text` matches the Electron/Swift golden for the same audio; assert wire invariants (3200-byte frames, drain-before-EOS, `formatting_enabled` present, closed-enum guard).
- **Visual/motion gate:** per screen, run the Electron reference and the .NET app side-by-side; verify assets, layout, colors (against ported token values), and every animation's timing/easing. Screen is not "done" until this passes.

## 5. Explicit non-goals of the scaffolding phase

- Not writing any production .NET port code (that's the per-milestone plans).
- Not creating the branch or cleaning the tree (the user does this).
- Not modifying anything under `_reference/` (it is immutable truth).
- Not re-specifying the M0–M9 migration end-to-end (that already exists in the Electron MASTER-PLAN; we port it, not rewrite it).
- Not resolving the font-licensing or backend cross-team dependencies (tracked, not solved here).

## 6. Open items to resolve in the implementation plan

- Exact `docs/` folder names and which of the 12 maps merge/split for Windows.
- Whether agents are `.claude/agents/*.md` files or SDK agent definitions, and their precise tool allowlists.
- The `Kivi.*` project/solution shape for the rebuilt tree (how closely the C# namespaces mirror the Electron `packages/` + `src/` split under tripwire T4).
- The concrete visual-parity tooling for the side-by-side gate on Windows (manual side-by-side vs. an automated screenshot-diff harness).

---

### Appendix: reference doc index (Electron, immutable)
- `_reference/sarvam-kivi-electron/docs/GOAL.md`
- `_reference/sarvam-kivi-electron/docs/MASTER-PLAN.md` — authoritative M0–M9 + 28-item risk register
- `_reference/sarvam-kivi-electron/docs/FEATURE-PARITY.md` — 131 features, P0 shortlist
- `_reference/sarvam-kivi-electron/docs/PROGRESS.md` — build state (what's already implemented)
- `_reference/sarvam-kivi-electron/docs/maps/` — 12 maps: `backend-service-api`, `service-client-wire`, `dictation-audio-pipeline`, `design-tokens`, `orb-engine-behavior`, `orb-visual-and-box`, `main-window-shell-pages`, `menubar-onboarding-auth`, `personalization-subsystem`, `platform-coupling-audit`, `electron-crossplatform-packaging`, `openwhispr-reference`
- Pure-logic source to port: `_reference/sarvam-kivi-electron/packages/orb-core/`, `packages/design-tokens/`
- Wire source to port: `_reference/sarvam-kivi-electron/src/main/wire/`
- View source to reproduce: `_reference/sarvam-kivi-electron/src/renderer/src/`
