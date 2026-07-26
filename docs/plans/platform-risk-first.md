# PLAN DRAFT: platform-risk-first (SUPERSEDED — historical)

> **Status: superseded historical planning draft.** This file exists only to mirror the
> Electron reference `docs/` structure 1:1. It is **not** an active plan and was **not**
> rewritten for Windows/.NET. The authoritative plan is `docs/MASTER-PLAN.md`, which
> explicitly supersedes all three drafts.
>
> **Original:** `_reference/sarvam-kivi-electron/docs/plans/platform-risk-first.md`

**What that draft argued (2–3 line summary):** De-risk the hard platform parts first — the
port lives or dies on four capabilities (global hold-to-talk hotkey, text insertion, a
transparent non-activating click-through overlay, mic capture), so prove all four before
writing any visual-clone code. `MASTER-PLAN.md` adopts its correct calls — the platform seam,
the engine living in the render layer, the wire-trap discipline, and paste-without-refocus —
but rejects its Windows-first inversion of the loop-first ordering (moot here anyway, since
this is a Windows-only repo with no cross-platform sequencing to invert).
