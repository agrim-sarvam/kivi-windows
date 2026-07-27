using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Kivi.Core.KiwiMark;
using Kivi.Core.Orb;

namespace Kivi.App.Drawing;

/// <summary>
/// The Canvas-draw layer of the kiwi mark — ported from the draw/compose/gaitRaster/coverage of
/// src/renderer/src/orb/kiwi/KiwiMarkEngine.ts. The PURE state math (color/walk lerp, tables) lives
/// in Kivi.Core.KiwiMark.KiwiMarkEngine; this class owns the raster: the body/leg part masks, the
/// 48×8 gait-raster bucket cache, coverage readback, and the dot compositing into a canvas.
///
/// Coordinate space is top-left origin (like the browser canvas), so no vertical flip is needed.
/// </summary>
internal sealed class KiwiMarkRenderer
{
    private readonly KiwiMarkEngine _engine;

    public double CanvasWidth { get; }
    public double CanvasHeight { get; }

    private readonly int _cols;
    private readonly int _rows;
    private readonly double _cellStep;
    private readonly double _gap;
    private readonly double _volume;

    private const int GW = KiwiMarkEngine.GW;
    private const int GH = KiwiMarkEngine.GH;

    // Part masks: true where mask && part test.
    private readonly bool[] _body;
    private readonly bool[] _legL;
    private readonly bool[] _legR;

    private readonly Dictionary<int, GaitRaster> _gaitCache = new();

    /// The eye color follows the `inverted` flag passed on the most recent Step (dark vs light table).
    public bool Inverted { get; set; } = true;

    private struct GaitRaster { public float[] Cov; public int EyeCX; public int EyeCY; }

    public KiwiMarkRenderer(KiwiMarkEngine engine, double cssWidth = 38, int cols = 24, double gap = 0.18, double volume = 1.0)
    {
        _engine = engine;
        _cols = cols;
        _rows = Math.Max(1, (int)Math.Round((double)(cols * GH) / GW));
        _cellStep = cssWidth / cols;
        _gap = gap;
        _volume = volume;
        CanvasWidth = cssWidth;
        CanvasHeight = _cellStep * _rows;

        double legTop = KiwiMarkEngine.LEG_TOP;
        double legOverlap = KiwiMarkEngine.LEG_OVERLAP;
        double legSplit = KiwiMarkEngine.LEG_SPLIT;
        _body = BuildPart((x, y) => y < legTop);
        _legL = BuildPart((x, y) => y >= legOverlap && x < legSplit);
        _legR = BuildPart((x, y) => y >= legOverlap && x >= legSplit);
    }

    private static bool[] BuildPart(Func<int, int, bool> test)
    {
        var a = new bool[GW * GH];
        for (int y = 0; y < GH; y++)
            for (int x = 0; x < GW; x++)
                if (KiwiData.MaskOn(x, y) && test(x, y))
                    a[y * GW + x] = true;
        return a;
    }

    private static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));

    // --- rasterize one composed gait frame into a GW×GH alpha buffer (0/255) ---
    private void RasterCompose(double clock, double amt, byte[] outBuf, out double ang, out double hop)
    {
        Array.Clear(outBuf, 0, outBuf.Length);
        ang = Math.Sin(clock) * KiwiMarkEngine.ROCK * amt;
        hop = Math.Abs(Math.Sin(clock)) * KiwiMarkEngine.HOP * amt;
        double aL = Math.Sin(clock) * KiwiMarkEngine.SWING_ANG * amt;
        double aR = Math.Sin(clock + Math.PI) * KiwiMarkEngine.SWING_ANG * amt;

        var pivot = KiwiMarkEngine.PIVOT;
        var hipL = KiwiMarkEngine.HIP_L;
        var hipR = KiwiMarkEngine.HIP_R;

        // legs (rotated about their hip) THEN whole-body rock about pivot, all composited.
        StampLeg(_legR, hipR.x, hipR.y, aR, pivot.x, pivot.y, ang, hop, outBuf);
        StampLeg(_legL, hipL.x, hipL.y, aL, pivot.x, pivot.y, ang, hop, outBuf);
        StampBody(_body, pivot.x, pivot.y, ang, hop, outBuf);
    }

    // Forward-map each set pixel through leg-swing (about hip) then body rock (about pivot).
    private static void StampLeg(bool[] part, double hx, double hy, double legAng,
        double px, double py, double bodyAng, double hop, byte[] outBuf)
    {
        double lc = Math.Cos(legAng), ls = Math.Sin(legAng);
        double bc = Math.Cos(bodyAng), bs = Math.Sin(bodyAng);
        for (int y = 0; y < GH; y++)
            for (int x = 0; x < GW; x++)
            {
                if (!part[y * GW + x]) continue;
                // leg rotation about hip
                double dx = x - hx, dy = y - hy;
                double lx = hx + dx * lc - dy * ls;
                double ly = hy + dx * ls + dy * lc;
                // body rock about pivot (+ hop up)
                double ex = lx - px, ey = ly - py;
                int fx = (int)Math.Round(px + ex * bc - ey * bs);
                int fy = (int)Math.Round(py + ex * bs + ey * bc - hop);
                if ((uint)fx < GW && (uint)fy < GH) outBuf[fy * GW + fx] = 255;
            }
    }

    private static void StampBody(bool[] part, double px, double py, double bodyAng, double hop, byte[] outBuf)
    {
        double bc = Math.Cos(bodyAng), bs = Math.Sin(bodyAng);
        for (int y = 0; y < GH; y++)
            for (int x = 0; x < GW; x++)
            {
                if (!part[y * GW + x]) continue;
                double ex = x - px, ey = y - py;
                int fx = (int)Math.Round(px + ex * bc - ey * bs);
                int fy = (int)Math.Round(py + ex * bs + ey * bc - hop);
                if ((uint)fx < GW && (uint)fy < GH) outBuf[fy * GW + fx] = 255;
            }
    }

    private static (double x, double y) Xform(double pxIn, double pyIn, double ang, double hop)
    {
        var pivot = KiwiMarkEngine.PIVOT;
        double dx = pxIn - pivot.x, dy = pyIn - pivot.y;
        double c = Math.Cos(ang), s = Math.Sin(ang);
        return (pivot.x + dx * c - dy * s, pivot.y + dx * s + dy * c - hop);
    }

    private readonly byte[] _rasterScratch = new byte[GW * GH];

    private GaitRaster GaitRasterFor(double clock, double amt)
    {
        int amtB = (int)Math.Round(Clamp(amt, 0, 1) * (KiwiMarkEngine.GAIT_AMT_BUCKETS - 1), MidpointRounding.AwayFromZero);
        int phaseB = 0;
        if (amtB > 0)
        {
            double p = clock % (2 * Math.PI);
            if (p < 0) p += 2 * Math.PI;
            phaseB = (int)Math.Floor((p / (2 * Math.PI)) * KiwiMarkEngine.GAIT_PHASE_BUCKETS) % KiwiMarkEngine.GAIT_PHASE_BUCKETS;
        }
        int key = phaseB * KiwiMarkEngine.GAIT_AMT_BUCKETS + amtB;
        if (_gaitCache.TryGetValue(key, out var hit)) return hit;

        double clockQ = ((phaseB + 0.5) / KiwiMarkEngine.GAIT_PHASE_BUCKETS) * 2 * Math.PI;
        double amtQ = (double)amtB / (KiwiMarkEngine.GAIT_AMT_BUCKETS - 1);
        RasterCompose(clockQ, amtQ, _rasterScratch, out double ang, out double hop);

        var cov = new float[_cols * _rows];
        for (int cy = 0; cy < _rows; cy++)
        {
            int my0 = (int)Math.Floor((double)cy / _rows * GH);
            int my1 = Math.Max(my0 + 1, (int)Math.Floor((double)(cy + 1) / _rows * GH));
            for (int cx = 0; cx < _cols; cx++)
            {
                int mx0 = (int)Math.Floor((double)cx / _cols * GW);
                int mx1 = Math.Max(mx0 + 1, (int)Math.Floor((double)(cx + 1) / _cols * GW));
                cov[cy * _cols + cx] = Coverage(_rasterScratch, mx0, mx1, my0, my1);
            }
        }
        double bx0 = KiwiMarkEngine.BX0, by0 = KiwiMarkEngine.BY0, bw = KiwiMarkEngine.BW, bh = KiwiMarkEngine.BH;
        var eye = Xform(bx0 + KiwiMarkEngine.EYE.nx * bw, by0 + KiwiMarkEngine.EYE.ny * bh, ang, hop);
        var raster = new GaitRaster
        {
            Cov = cov,
            EyeCX = (int)Math.Floor(eye.x / GW * _cols),
            EyeCY = (int)Math.Floor(eye.y / GH * _rows),
        };
        _gaitCache[key] = raster;
        return raster;
    }

    private static float Coverage(byte[] data, int mx0, int mx1, int my0, int my1)
    {
        long s = 0; int n = 0;
        for (int my = my0; my < my1; my++)
        {
            int row = my * GW;
            for (int mx = mx0; mx < mx1; mx++) { n++; s += data[row + mx]; }
        }
        return n > 0 ? (float)(s / (double)(n * 255)) : 0f;
    }

    /// <summary>Draw the mark into g, which is already scaled/positioned to CanvasWidth×CanvasHeight.</summary>
    public void Draw(Graphics g, double timeSec)
    {
        var S = _engine.Current;
        double amt = _engine.ReduceMotion ? 0 : Math.Min(1, S.Walk);
        var gait = GaitRasterFor(_engine.WalkClock, amt);

        double stepPx = _cellStep;
        double baseR = (stepPx / 2) * (1 - _gap);
        double[] shadow = { S.Col[0] * 0.45, S.Col[1] * 0.45, S.Col[2] * 0.45 };

        bool bv = _engine.BreathingVolume;
        double breath = bv ? 0.5 + 0.5 * Math.Sin(timeSec * 1.75) : 0;
        double volDepth = bv ? 0.62 + 0.38 * breath : 1.0;
        double volDrift = bv ? 0.05 * Math.Sin(timeSec * 0.9) : 0;

        int ecx = gait.EyeCX, ecy = gait.EyeCY;
        var eyeCol = Inverted ? KiwiMarkEngine.EyeDark : KiwiMarkEngine.EyeLight;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        int dotCount = 0;

        for (int cy = 0; cy < _rows; cy++)
            for (int cx = 0; cx < _cols; cx++)
            {
                float cov = gait.Cov[cy * _cols + cx];
                if (cov < 0.36f) continue;
                double cmx = ((cx + 0.5) / _cols) * GW;
                double cmy = ((cy + 0.5) / _rows) * GH;
                double px = cx * stepPx + stepPx / 2;
                double py = cy * stepPx + stepPx / 2;

                if (cx == ecx && cy == ecy)
                {
                    using var eb = new SolidBrush(Color.FromArgb(255, (int)eyeCol[0], (int)eyeCol[1], (int)eyeCol[2]));
                    double er = baseR * 0.82;
                    FillCircle(g, eb, px, py, er);
                    dotCount++;
                    continue;
                }

                double[] col = { S.Col[0], S.Col[1], S.Col[2] };
                double sizeF = S.Dot;
                double al = S.Alpha;

                if (S.Listen > 0.01)
                {
                    double amp = _engine.ListenLevel;
                    double wave = 0.5 + 0.5 * Math.Sin(timeSec * 2.0 - (cmx + cmy) * 0.02);
                    double glow = Math.Pow(wave, 1.15);
                    for (int i = 0; i < 3; i++) col[i] += (S.Bl[i] - col[i]) * (glow * S.Listen * 0.9 * amp);
                    for (int i = 0; i < 3; i++) col[i] += (S.Bd[i] - col[i]) * ((1 - wave) * 0.18 * S.Listen * amp);
                    sizeF *= 1 + glow * 0.42 * S.Listen * amp;
                    al = Math.Min(1, al * (1 + (glow - 0.2) * 0.35 * S.Listen * amp));
                    if (_engine.SpeechGlow > 0.001)
                    {
                        double lift = Math.Min(1, _engine.SpeechGlow);
                        for (int i = 0; i < 3; i++) col[i] += (S.Bl[i] - col[i]) * (0.55 * lift * S.Listen);
                        sizeF *= 1 + 0.14 * lift;
                        al = Math.Min(1, al * (1 + 0.2 * lift));
                    }
                }
                if (_volume > 0)
                {
                    var (hl, lo) = VolumeFactors(cmx, cmy, volDrift);
                    if (bv)
                    {
                        for (int i = 0; i < 3; i++) col[i] += (255 - col[i]) * (hl * _volume * 0.55 * volDepth);
                        for (int i = 0; i < 3; i++) col[i] += (shadow[i] - col[i]) * ((1 - hl) * _volume * 0.42 * volDepth);
                        for (int i = 0; i < 3; i++) col[i] += (shadow[i] - col[i]) * (lo * _volume * 0.5);
                    }
                    else
                    {
                        for (int i = 0; i < 3; i++) col[i] += (255 - col[i]) * (hl * _volume * 0.6);
                        for (int i = 0; i < 3; i++) col[i] += (shadow[i] - col[i]) * (lo * _volume * 0.5);
                    }
                }
                int a = (int)Math.Round(Clamp(al, 0, 1) * 255);
                using var b = new SolidBrush(Color.FromArgb(a,
                    Clamp8((int)col[0]), Clamp8((int)col[1]), Clamp8((int)col[2])));
                double rr = Math.Min(stepPx * 0.66, baseR * sizeF);
                FillCircle(g, b, px, py, rr);
                dotCount++;
            }
        _engine.LastDotCount = dotCount;
    }

    private static int Clamp8(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    private static void FillCircle(Graphics g, Brush b, double cx, double cy, double r)
    {
        r = Math.Max(0, r);
        g.FillEllipse(b, (float)(cx - r), (float)(cy - r), (float)(r * 2), (float)(r * 2));
    }

    private static (double hl, double lo) VolumeFactors(double mcx, double mcy, double drift)
    {
        double bx0 = KiwiMarkEngine.BX0, by0 = KiwiMarkEngine.BY0, bw = KiwiMarkEngine.BW, bh = KiwiMarkEngine.BH;
        double nx = (mcx - bx0) / bw;
        double ny = (mcy - by0) / bh;
        double G(double cx, double cy, double rx, double ry)
        {
            double dx = (nx - cx) / rx, dy = (ny - cy) / ry;
            return Math.Exp(-(dx * dx + dy * dy));
        }
        double belly = G(0.45 + drift, 0.56, 0.5, 0.56);
        double neck = G(0.57 + drift, 0.2, 0.28, 0.24);
        double hl = Math.Min(1, Math.Max(belly, 0.88 * neck));
        hl = Math.Pow(hl, 0.72);
        double lo = Math.Min(1, Math.Max(0, (ny - 0.6) / 0.4));
        return (hl, lo);
    }
}
