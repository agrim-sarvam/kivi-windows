// KiwiMarkEngine (PURE portion) — ported from
// src/renderer/src/orb/kiwi/KiwiMarkEngine.ts (Orb/Core/KiwiMarkEngine.swift).
//
// SCOPE (per core-porter charter): this file holds the PURE data + state math only —
// the dark/light state tables, the eye colours, the verbatim geometry constants, and the
// `Step(dt, target, inverted)` per-tick colour/walk lerp (dt-corrected exactly like the
// engine). The CANVAS DRAW (buildPart / composeFrame / gaitRaster / coverage / draw) needs
// a 2D raster surface and belongs to Kivi.App/Drawing in P4 — it is intentionally NOT here.
// The 48×8 gait-raster bucket cache is a draw-time optimisation over that canvas, so it too
// lives with the P4 draw layer; the pure bucket-index math is kept here for reuse.
using System;

namespace Kivi.Core.KiwiMark;

using Kivi.Core.Orb; // KiwiMarkState

public readonly record struct MarkStateSpec(
    double[] Col, double Walk, double Listen, double Alpha, double Dot, double[]? Bd, double[]? Bl);

public sealed class KiwiMarkEngine
{
    // KiwiMarkEngine.swift darkTable / lightTable — verbatim.
    public static readonly System.Collections.Generic.Dictionary<KiwiMarkState, MarkStateSpec> DarkTable = new()
    {
        [KiwiMarkState.Idle] = new(new double[] { 250, 252, 246 }, 0, 0, 1.0, 1.0, null, null),
        [KiwiMarkState.Listening] = new(new double[] { 248, 168, 108 }, 1, 1, 0.98, 1.1, new double[] { 236, 128, 52 }, new double[] { 255, 196, 118 }),
        [KiwiMarkState.Processing] = new(new double[] { 120, 140, 255 }, 0, 0, 0.96, 1.4, null, null),
        [KiwiMarkState.Editing] = new(new double[] { 242, 200, 104 }, 1, 0, 0.96, 1.4, null, null),
        [KiwiMarkState.Speaking] = new(new double[] { 156, 206, 108 }, 1, 1, 0.98, 1.1, new double[] { 120, 178, 56 }, new double[] { 198, 236, 140 }),
        [KiwiMarkState.Done] = new(new double[] { 166, 214, 118 }, 0, 0, 0.98, 1.4, null, null),
        [KiwiMarkState.Error] = new(new double[] { 184, 21, 20 }, 0, 0, 0.98, 1.4, null, null),
        [KiwiMarkState.Waiting] = new(new double[] { 210, 150, 45 }, 0, 1, 0.98, 1.1, null, null),
        [KiwiMarkState.Acting] = new(new double[] { 66, 80, 213 }, 1, 0, 0.96, 1.4, null, null),
        [KiwiMarkState.Confirming] = new(new double[] { 210, 150, 45 }, 0, 0, 0.98, 1.4, null, null),
    };

    public static readonly System.Collections.Generic.Dictionary<KiwiMarkState, MarkStateSpec> LightTable = new()
    {
        [KiwiMarkState.Idle] = new(new double[] { 70, 78, 62 }, 0, 0, 0.92, 1.0, null, null),
        [KiwiMarkState.Listening] = new(new double[] { 208, 92, 30 }, 1, 1, 0.96, 1.1, new double[] { 150, 58, 14 }, new double[] { 232, 128, 52 }),
        [KiwiMarkState.Processing] = new(new double[] { 48, 60, 200 }, 0, 0, 0.94, 1.4, null, null),
        [KiwiMarkState.Editing] = new(new double[] { 162, 114, 36 }, 1, 0, 0.94, 1.4, null, null),
        [KiwiMarkState.Speaking] = new(new double[] { 56, 112, 36 }, 1, 1, 0.96, 1.1, new double[] { 30, 74, 22 }, new double[] { 96, 160, 56 }),
        [KiwiMarkState.Done] = new(new double[] { 64, 124, 40 }, 0, 0, 0.96, 1.4, null, null),
        [KiwiMarkState.Error] = new(new double[] { 184, 21, 20 }, 0, 0, 0.96, 1.4, null, null),
        [KiwiMarkState.Waiting] = new(new double[] { 210, 150, 45 }, 0, 1, 0.96, 1.1, null, null),
        [KiwiMarkState.Acting] = new(new double[] { 66, 80, 213 }, 1, 0, 0.94, 1.4, null, null),
        [KiwiMarkState.Confirming] = new(new double[] { 210, 150, 45 }, 0, 0, 0.96, 1.4, null, null),
    };

    public static readonly double[] EyeDark = { 0x16, 0x21, 0x0e };  // (22, 33, 14)
    public static readonly double[] EyeLight = { 0xf1, 0xf4, 0xec }; // (241, 244, 236)

    // geometry (verbatim from kiwi-render.js / KiwiMarkEngine.swift)
    public const int GW = KiwiData.GW;
    public const int GH = KiwiData.GH;
    public static readonly int BX0 = KiwiData.BBox.X0;
    public static readonly int BY0 = KiwiData.BBox.Y0;
    public static readonly int BW = KiwiData.BBox.X1 - KiwiData.BBox.X0;
    public static readonly int BH = KiwiData.BBox.Y1 - KiwiData.BBox.Y0;

    public static readonly (double nx, double ny) EYE = (0.42, 0.095);
    public static (double x, double y) PIVOT => (BX0 + 0.66 * BW, BY0 + 0.82 * BH);
    public const double ROCK = 0.05;
    public static double HOP => 0.012 * BH;
    public static double LEG_TOP => BY0 + 0.79 * BH;
    public static double LEG_OVERLAP => BY0 + 0.74 * BH;
    public static double LEG_SPLIT => BX0 + 0.681 * BW;
    public static (double x, double y) HIP_L => (BX0 + 0.603 * BW, BY0 + 0.79 * BH);
    public static (double x, double y) HIP_R => (BX0 + 0.741 * BW, BY0 + 0.79 * BH);
    public const double SWING_ANG = 0.34;

    public const int GAIT_PHASE_BUCKETS = 48;
    public const int GAIT_AMT_BUCKETS = 8;

    private static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));

    // ---- live state (the pure per-tick lerp state, kivi-orb-adapter.js loop) ----
    public double WalkClock = 0;

    public sealed class LivingParams
    {
        public double[] Col = { 236, 242, 230 };
        public double Walk = 0;
        public double Listen = 0;
        public double Alpha = 0.96;
        public double Dot = 1;
        public double[] Bd = { 233, 128, 64 };
        public double[] Bl = { 255, 205, 158 };
    }

    public LivingParams Current = new();
    public int LastDotCount = 0;

    // live per-frame drive (fed by the runtime; see FlowRuntime §10)
    public bool ReduceMotion = false;
    public bool FreezeWalk = false;
    public double ListenLevel = 1.0;
    public double WalkDrive = 1.0;
    public double SpeechGlow = 0;
    public bool BreathingVolume = false;

    /// The kivi-orb-adapter.js lerp loop. PURE: mutates Current + WalkClock only.
    public void Step(double dtSec, KiwiMarkState target, bool inverted)
    {
        var dt = Clamp(dtSec, 0, 0.05);
        var tbl = inverted ? DarkTable : LightTable;
        var s = tbl.TryGetValue(target, out var sp) ? sp : tbl[KiwiMarkState.Idle];

        static double L(double a, double b, double k) => a + (b - a) * k;
        var k = ReduceMotion ? 1.0 : 1 - Math.Pow(1 - 0.12, dt / 0.016);
        // listening color reads "recording" near-instantly (0.30/tick vs 0.12)
        var colK = !ReduceMotion && target == KiwiMarkState.Listening ? 1 - Math.Pow(1 - 0.3, dt / 0.016) : k;
        var c = Current;
        for (int i = 0; i < 3; i++) c.Col[i] += (s.Col[i] - c.Col[i]) * colK;
        var walkTarget = s.Walk * Clamp(WalkDrive, 0, 1.8);
        c.Walk = L(c.Walk, walkTarget, k);
        c.Listen = L(c.Listen, s.Listen, k);
        c.Alpha = L(c.Alpha, s.Alpha, k);
        c.Dot = L(c.Dot, s.Dot, k);
        if (s.Bd != null && s.Bl != null)
        {
            var bk = ReduceMotion ? 1.0 : 1 - Math.Pow(1 - 0.14, dt / 0.016);
            for (int i = 0; i < 3; i++) c.Bd[i] += (s.Bd[i] - c.Bd[i]) * bk;
            for (int i = 0; i < 3; i++) c.Bl[i] += (s.Bl[i] - c.Bl[i]) * bk;
        }
        if (!ReduceMotion && !FreezeWalk) WalkClock += dt * (2.2 + 5.5 * c.Walk);
    }

    /// The exact per-state mark colour the orb lerps toward (for other surfaces).
    public static double[] StateColor(KiwiMarkState state, bool inverted)
    {
        var table = inverted ? DarkTable : LightTable;
        return (table.TryGetValue(state, out var s) ? s : table[KiwiMarkState.Idle]).Col;
    }

    /// Pure gait-cache bucket index math (48 phase × 8 amplitude). The RASTER itself is
    /// built in the P4 canvas draw layer; this maps (clock, amt) → cache key deterministically.
    public static int GaitBucketKey(double clock, double amt)
    {
        var amtB = (int)Math.Round(Clamp(amt, 0, 1) * (GAIT_AMT_BUCKETS - 1), MidpointRounding.AwayFromZero);
        int phaseB = 0;
        if (amtB > 0)
        {
            var p = clock % (2 * Math.PI);
            if (p < 0) p += 2 * Math.PI;
            phaseB = (int)Math.Floor((p / (2 * Math.PI)) * GAIT_PHASE_BUCKETS) % GAIT_PHASE_BUCKETS;
        }
        return phaseB * GAIT_AMT_BUCKETS + amtB;
    }
}
