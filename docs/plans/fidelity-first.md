# PLAN DRAFT: fidelity-first (SUPERSEDED — historical)

> **Status: superseded historical planning draft.** This file exists only to mirror the
> Electron reference `docs/` structure 1:1. It is **not** an active plan and was **not**
> rewritten for Windows/.NET. The authoritative plan is `docs/MASTER-PLAN.md`, which
> explicitly supersedes all three drafts.
>
> **Original:** `_reference/sarvam-kivi-electron/docs/plans/fidelity-first.md`

**What that draft argued (2–3 line summary):** Pixel-exact clone is the acceptance criterion,
so make the design system milestone zero and gate every later milestone on a numeric + pixel
parity check. Its strongest lever — the orb is a pure function `FlowEngine.step(now)→FlowFrame`,
so port the engine, freeze the clock, and assert `FlowFrame` field-by-field equality against
golden frames before asserting pixels — is adopted wholesale in `MASTER-PLAN.md §6`. Its
weeks-of-pixels-first sequencing (deferring the functional loop behind a mock service) was
rejected in favor of loop-first.
