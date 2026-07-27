namespace Kivi.Core.Planner;
// PHASE P3/P4 — DEFERRED (core-porter P2 note): the orb-core TypeScript has NO standalone
// planner package. The only pure spacing/boundary logic present is the word-level LCS diff,
// which lives on FlowEngine (FlowEngine.DiffTokens) exactly as in the TS source — it is ported
// there, not duplicated here. PasteBoundaryPlanner / DictationInsertionPlanner /
// DictationJoinRewritePlanner (as described in the maps) do NOT exist as standalone pure code
// in the reference; they are UIA-caret-dependent join-rewrite features deferred to M4/M9.
// Left empty for P3 to fill once that pure form exists.
internal static class _Placeholder { }
