// Numeric constants for the orb engine, ported 1:1 from packages/orb-core/src/constants.ts
// (which re-derives from @kivi/design-tokens). Values are inlined verbatim from the
// token source of truth; NEVER round or "clean up."
using System.Collections.Generic;

namespace Kivi.Core.Orb;

public readonly record struct Size3(double W, double H, double R);
public readonly record struct Size2(double W, double H);

public static class Constants
{
    // FlowEngine.swift:45-51 — rest pill ⇄ woken orb ⇄ mini orb endpoints.
    public static readonly Size3 REST = new(39, 15, 7.5);       // restW, restH, restR
    public static readonly Size3 WAKE = new(61, 61, 30.5);      // wakeW, wakeH, wakeR
    public static readonly Size3 WAKE_MINI = new(42.7, 42.7, 21.35); // not in tokens

    // FlowEngine.swift:79-80 — transcript box size endpoints.
    public static readonly Size2 BOX_DEFAULT = new(322, 108);
    public static readonly Size2 BOX_MAX = new(640, 360);

    // DS.Geometry — the pill take-morph size (used in step()).
    public const double PILL_TAKE_W = 57;
    public const double PILL_TAKE_H = 18;

    // FlowEngine.swift gesture thresholds + DS.Motion.
    public const double HOLD_MS = 420;
    public const double DOUBLE_TAP_MS = 450;
    public const double DOTS_MS = 600;
    public const double BOT_HIDE_MS = 2600;
    public const double BREATH_PERIOD_S = 2.6;

    // Badge rest fill alpha (DS.Orb.forest/mist restA).
    public const double REST_ALPHA_FOREST = 0.72;
    public const double REST_ALPHA_MIST = 0.66;

    // FlowEngine static tuning constants.
    public const double PROCESSING_MIN_DISPLAY_MS = 250; // FlowEngine.swift:218
    public const double EDIT_MIN_DISPLAY_MS = 0;         // :221
    public const double EDIT_REVIEW_HOLD = 5000;         // :296
    public const double SILENCE_REVERT_MS = 8000;        // :2761
    public const int MAX_SEGMENT_BACKFILL = 1024;        // :231
    public const double MAX_DIFF_TOKEN_PRODUCT = 1_000_000; // :1104

    // DictationBudgets.swift
    public const double FINAL_TIMEOUT_MS = 20_000;
    public const double SPOKEN_EDIT_FLUSH_MS = 800;
    public const double PROCESSING_STILL_WORKING_MS = 3_000;
    public const double PROCESSING_LONGER_THAN_USUAL_MS = 9_000;
    public const double FORMATTING_PROGRESS_DELIVERY_MARGIN_MS = 5_000;
    public const double FORMATTING_PROGRESS_ABSOLUTE_CAP_MS = 120_000;

    // FlowEngine.swift:408, 413 — rest light + rest glow colour.
    public static readonly (double x, double y) REST_LIGHT = (-0.42, -0.52);
    public static readonly double[] REST_GLOW = { 214, 220, 230 };
    // step(): glowIdle on the LIGHT page (a slate) — dark page uses REST_GLOW.
    public static readonly double[] GLOW_IDLE_LIGHT = { 116, 126, 142 };

    // KiwiMarkEngine.swift state → RGB colour tables (`col`), used by
    // FlowEngine.step() for the eased glow colour. Only `col` is needed here.
    public static readonly Dictionary<KiwiMarkState, double[]> MARK_DARK_TABLE = new()
    {
        [KiwiMarkState.Idle] = new double[] { 250, 252, 246 },
        [KiwiMarkState.Listening] = new double[] { 248, 168, 108 },
        [KiwiMarkState.Processing] = new double[] { 120, 140, 255 },
        [KiwiMarkState.Editing] = new double[] { 242, 200, 104 },
        [KiwiMarkState.Speaking] = new double[] { 156, 206, 108 },
        [KiwiMarkState.Done] = new double[] { 166, 214, 118 },
        [KiwiMarkState.Error] = new double[] { 184, 21, 20 },
        [KiwiMarkState.Waiting] = new double[] { 210, 150, 45 },
        [KiwiMarkState.Acting] = new double[] { 66, 80, 213 },
        [KiwiMarkState.Confirming] = new double[] { 210, 150, 45 },
    };

    public static readonly Dictionary<KiwiMarkState, double[]> MARK_LIGHT_TABLE = new()
    {
        [KiwiMarkState.Idle] = new double[] { 70, 78, 62 },
        [KiwiMarkState.Listening] = new double[] { 208, 92, 30 },
        [KiwiMarkState.Processing] = new double[] { 48, 60, 200 },
        [KiwiMarkState.Editing] = new double[] { 162, 114, 36 },
        [KiwiMarkState.Speaking] = new double[] { 56, 112, 36 },
        [KiwiMarkState.Done] = new double[] { 64, 124, 40 },
        [KiwiMarkState.Error] = new double[] { 184, 21, 20 },
        [KiwiMarkState.Waiting] = new double[] { 210, 150, 45 },
        [KiwiMarkState.Acting] = new double[] { 66, 80, 213 },
        [KiwiMarkState.Confirming] = new double[] { 210, 150, 45 },
    };

    /// KiwiMarkEngine.stateColor(for:inverted:). inverted == isDark.
    public static double[] MarkStateColor(KiwiMarkState state, bool inverted)
    {
        var table = inverted ? MARK_DARK_TABLE : MARK_LIGHT_TABLE;
        return table.TryGetValue(state, out var c) ? c : table[KiwiMarkState.Idle];
    }
}
