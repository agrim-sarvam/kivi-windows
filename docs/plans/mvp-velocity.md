# PLAN DRAFT: mvp-velocity (SUPERSEDED — historical)

> **Status: superseded historical planning draft.** This file exists only to mirror the
> Electron reference `docs/` structure 1:1. It is **not** an active plan and was **not**
> rewritten for Windows/.NET. The authoritative plan is `docs/MASTER-PLAN.md`, which
> explicitly supersedes all three drafts.
>
> **Original:** `_reference/sarvam-kivi-electron/docs/plans/mvp-velocity.md`

**What that draft argued (2–3 line summary):** "Ship the tangible thing first." Prove the
dictation loop (hotkey → mic → 16 kHz PCM over WS → `final.formatted_text` → paste) as a
headless spike, then wrap it, then make it pretty — front-loading the pure/portable wire +
engine layer and deferring the visual clone. Its headline shortcut was to resurrect the old
HTML/JS orb prototype instead of re-porting the engine; the critiques flagged that this
anchors on a stale, wrong-colored design, and it crammed the hard native seams into one short
milestone. `MASTER-PLAN.md` keeps its loop-first spine but rejects the stale-prototype anchor
(the current reference engine + tokens are the sole source of truth).
