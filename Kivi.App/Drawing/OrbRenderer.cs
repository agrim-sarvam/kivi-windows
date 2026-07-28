using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Kivi.Core.Orb;

namespace Kivi.App.Drawing;

/// <summary>
/// Draws the pill⇄orb surface — a pure function of one FlowFrame — into a premultiplied-ARGB
/// bitmap sized for the layered window. Ported from src/renderer/src/orb/render/OrbView.tsx:
/// 4-layer glow (approximated with soft concentric layers; see NOTE), themed fill @ fillAlpha,
/// paper grain, breathing/sleeping eyes, the living kiwi mark, and the cursor-driven sphere gloss.
///
/// NOTE (divergence): CSS box-shadow blur has no GDI+ equivalent. The four glow layers are
/// approximated with stacked translucent expanded rounded-rects with a soft alpha falloff. The
/// color/spread/alpha values come straight from the engine's ShadowSpec fields, so intensity and
/// hue match; only the exact blur kernel differs. The desktop-behind backdrop blur is excluded (R1).
/// </summary>
internal sealed class OrbRenderer
{
    private readonly KiwiMarkRenderer _mark;

    // The orb is drawn centered horizontally in a fixed logical zone; the layered window is this
    // size. Generous margin for glow + the box that unfurls below the orb. Orb center at (CenterX, OrbCenterY).
    public const int CanvasW = 700;   // logical px — wide enough for the maxi box + side satellites
    public const int CanvasH = 620;
    public const double CenterX = CanvasW / 2.0;
    public const double OrbCenterY = 120; // room below for the transcript box

    public OrbRenderer(KiwiMarkRenderer mark) => _mark = mark;

    /// Render into a NEW premultiplied ARGB bitmap. Caller disposes.
    public Bitmap Render(FlowFrame f, double dpiScale)
    {
        int pw = (int)Math.Ceiling(CanvasW * dpiScale);
        int ph = (int)Math.Ceiling(CanvasH * dpiScale);
        var bmp = new Bitmap(pw, ph, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(0, 0, 0, 0));
            g.ScaleTransform((float)dpiScale, (float)dpiScale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            DrawOrb(g, f);
        }
        Premultiply(bmp);
        return bmp;
    }

    private void DrawOrb(Graphics g, FlowFrame f)
    {
        bool forest = f.Settings.Orb == OrbStyle.Forest;
        int[] fillRGB = forest ? new[] { 13, 30, 9 } : new[] { 223, 234, 209 };
        bool invert = forest;           // forest resolves to Canon.dark
        bool glossy = !forest;          // mist is glossy
        var eyeColor = forest ? DrawUtil.Hex("#EAF0E2") : DrawUtil.Hex("#1B330F");

        double w = f.OrbWidth, h = f.OrbHeight, r = f.OrbRadius;
        double press = f.Press;

        // --- transcript box (unfurls below the orb) — drawn first, behind the orb ---
        if (f.TxOpacity > 0.01 && f.Exp > 0.01)
            TranscriptBoxRenderer.Draw(g, f, CenterX, OrbCenterY, forest);

        // --- satellites (drawn on top of the box, behind/around the orb) ---
        SatellitesRenderer.Draw(g, f, CenterX, OrbCenterY, forest);

        // Orb top-left, centered horizontally, with the vertical `drop` offset. press scales about center.
        double cx = CenterX;
        double cy = OrbCenterY + f.Drop + h / 2.0;

        var state = g.Save();
        g.TranslateTransform((float)cx, (float)cy);
        g.ScaleTransform((float)press, (float)press);
        g.TranslateTransform((float)(-w / 2.0), (float)(-h / 2.0));
        // now origin is orb top-left in local space

        // --- 4-layer glow (approximation) behind the orb ---
        DrawGlow(g, f, w, h, r);

        // --- clip to rounded shape for fill/grain/gloss ---
        using (var shape = DrawUtil.RoundedRect(0, 0, w, h, r))
        {
            var clipState = g.Save();
            g.SetClip(shape);

            // fill
            using (var fb = new SolidBrush(DrawUtil.Argb(f.FillAlpha, fillRGB[0], fillRGB[1], fillRGB[2])))
                g.FillRectangle(fb, 0, 0, (float)w, (float)h);

            // paper grain
            if (!f.Settings.ReduceMotion) DrawGrain(g, invert, w, h);

            g.Restore(clipState);
        }

        // --- eyes (collapsed-pill face) ---
        DrawEyes(g, f, w, h, eyeColor, forest);

        // --- pill-take face (dictating pill, §6): mic bars while listening/speaking, morphing to
        // glowing eyes while processing. Cross-fades in over the normal rest-eyes as f.PillPop rises
        // (both can be simultaneously visible mid-morph; PillPop is the engine's own eased 0..1
        // blend fraction for the whole pill-take geometry, so reusing it here keeps the face in sync
        // with the pill shape it's drawn on).
        if (f.PillPop > 0.001)
            DrawPillFace(g, f, w, h);

        // --- living kiwi mark ---
        if (f.MarkOpacity > 0.001)
            DrawMark(g, f, w, h);

        // --- sphere gloss overlay (clipped) ---
        if (f.SphereOpacity > 0.001)
        {
            using var shape = DrawUtil.RoundedRect(0, 0, w, h, r);
            var clipState = g.Save();
            g.SetClip(shape);
            DrawSphere(g, f, w, h, glossy);
            g.Restore(clipState);
        }

        g.Restore(state);
    }

    private static void DrawGlow(Graphics g, FlowFrame f, double w, double h, double r)
    {
        // drop shadow (directional, black) — draw first (furthest back)
        var dr = f.Settings.Page == PageStyle.Dark ? new[] { 0, 0, 0 } : new[] { 20, 20, 20 };
        SoftLayer(g, w, h, r, f.DropShadow.Spread, f.DropShadow.Blur, 0, f.DropShadow.YOffset,
            dr[0], dr[1], dr[2], f.DropShadow.Alpha);

        // white halo (wide)
        SoftLayer(g, w, h, r, f.GlowHalo.Spread, f.GlowHalo.Blur, 0, 0, 255, 255, 255, f.GlowHalo.Alpha);

        // colored core glow (state color, engine-eased)
        var gc = f.GlowColor;
        SoftLayer(g, w, h, r, f.GlowCore.Spread, f.GlowCore.Blur, 0, 0,
            (int)gc.R, (int)gc.G, (int)gc.B, f.GlowCore.Alpha);
    }

    // Approximate a blurred shadow with N concentric expanded rounded-rects fading out.
    private static void SoftLayer(Graphics g, double w, double h, double r,
        double spread, double blur, double xoff, double yoff, int cr, int cg, int cb, double alpha)
    {
        if (alpha <= 0.001) return;
        const int steps = 6;
        double maxGrow = spread + blur; // total falloff reach
        for (int i = steps; i >= 1; i--)
        {
            double t = (double)i / steps;         // 1 = outermost
            double grow = maxGrow * t;
            double a = alpha * (1 - t) * 0.9 / steps * 2.2; // taper toward edge
            if (a <= 0.001) continue;
            using var brush = new SolidBrush(DrawUtil.Argb(a, cr, cg, cb));
            using var p = DrawUtil.RoundedRect(-grow + xoff, -grow + yoff, w + grow * 2, h + grow * 2, r + grow);
            g.FillPath(brush, p);
        }
        // solid core at spread to keep the near-glow crisp
        if (spread > 0)
        {
            using var brush = new SolidBrush(DrawUtil.Argb(alpha * 0.55, cr, cg, cb));
            using var p = DrawUtil.RoundedRect(-spread + xoff, -spread + yoff, w + spread * 2, h + spread * 2, r + spread);
            g.FillPath(brush, p);
        }
    }

    private static void DrawGrain(Graphics g, bool dark, double w, double h)
    {
        var tile = PaperGrain.TileBitmap(dark);
        double op = PaperGrain.Opacity(dark);
        double scale = PaperGrain.Scale(dark);
        using var ia = new ImageAttributes();
        var cm = new ColorMatrix { Matrix33 = (float)op };
        ia.SetColorMatrix(cm);
        using var tb = new TextureBrush(tile, System.Drawing.Drawing2D.WrapMode.Tile, new Rectangle(0, 0, tile.Width, tile.Height));
        tb.ScaleTransform((float)scale, (float)scale);
        // apply opacity via a temporary: TextureBrush ignores ImageAttributes opacity, so tint via alpha in tile draw
        // Simpler: draw the tile repeatedly with alpha. Use a low-alpha overlay approximation.
        using var faint = new SolidBrush(Color.FromArgb((int)(op * 255),
            dark ? Color.FromArgb(0xE9, 0xE7, 0xDD) : Color.FromArgb(0x20, 0x24, 0x1F)));
        // Draw the actual noise tile scaled + alpha-blended
        double tw = tile.Width * scale, th = tile.Height * scale;
        for (double yy = 0; yy < h; yy += th)
            for (double xx = 0; xx < w; xx += tw)
                g.DrawImage(tile, new Rectangle((int)xx, (int)yy, (int)Math.Ceiling(tw), (int)Math.Ceiling(th)),
                    0, 0, tile.Width, tile.Height, GraphicsUnit.Pixel, ia);
    }

    private static void DrawEyes(Graphics g, FlowFrame f, double w, double h, Color eyeColor, bool forest)
    {
        double eyeOpen = Math.Max(0, Math.Min(1, f.EyeOpen));
        double eyeD = 0.36 * 15;
        double eyeLineH = 1.8;
        double eyeH = eyeLineH + (eyeD - eyeLineH) * eyeOpen;
        double eyeBreathScale = 1 + (f.EyeScale - 1) * eyeOpen;
        double eyeBreathAlpha = 0.7 + 0.3 * f.Breath;
        double eyeVis = eyeBreathAlpha > 0 ? f.EyeOpacity / eyeBreathAlpha : 0;
        double eyeAlpha = eyeVis * (1 + (eyeBreathAlpha - 1) * eyeOpen);
        eyeAlpha *= (1 - f.SelectionPillOpacity);
        if (eyeAlpha <= 0.001) return;

        // pill mode wears live glow color; else theme eye color
        Color col = f.Settings.OrbSize == OrbSize.Pill
            ? DrawUtil.Rgb((int)f.GlowColor.R, (int)f.GlowColor.G, (int)f.GlowColor.B)
            : eyeColor;

        double gap = 0.62 * 15;
        double totalW = eyeD * 2 + gap;
        var st = g.Save();
        g.TranslateTransform((float)(w / 2), (float)(h / 2));
        g.ScaleTransform((float)eyeBreathScale, (float)eyeBreathScale);
        using var b = new SolidBrush(Color.FromArgb((int)(Math.Min(1, eyeAlpha) * col.A), col.R, col.G, col.B));
        double leftCx = -(gap / 2 + eyeD / 2);
        double rightCx = (gap / 2 + eyeD / 2);
        FillCapsule(g, b, leftCx, eyeD, eyeH);
        FillCapsule(g, b, rightCx, eyeD, eyeH);
        g.Restore(st);
    }

    // Pill-take face (orb-visual-and-box.md §3.6/§6, the "dictating pill" compact face): 7 vertical
    // mic bars while listening/speaking, morphing to two glowing eyes while processing — both
    // colored by f.GlowColor. Not present in the current _reference TS/Electron tree (verified: no
    // `pillFace`/mic-bar code anywhere under _reference — this is Swift-source-only per the map),
    // so the exact numeric spec is taken byte-for-byte from orb-visual-and-box.md §3.6 (widths,
    // spacing, the 7 sin seeds/phases, the eye diameter/pulse/shadow) rather than ported from code.
    private static readonly double[] BarSeeds = { 0.55, 0.9, 0.4, 1.0, 0.65, 0.85, 0.5 };
    private static readonly double[] BarPhases = { 0, 1.7, 3.1, 4.4, 0.9, 2.4, 5.2 };
    private const double BarW = 2.6, BarSpacing = 3.4;
    private const int BarCount = 7;

    private static void DrawPillFace(Graphics g, FlowFrame f, double w, double h)
    {
        bool processing = f.MarkState == KiwiMarkState.Processing;
        var col = DrawUtil.Rgb((int)f.GlowColor.R, (int)f.GlowColor.G, (int)f.GlowColor.B);
        double alpha = Math.Min(1, f.PillPop);
        if (alpha <= 0.001) return;

        var st = g.Save();
        g.TranslateTransform((float)(w / 2), (float)(h / 2));

        if (processing)
        {
            // two glowing eyes, Ø6.4, pulse 1+0.10*sin, shadow radius 5.5.
            double t = f.Now / 1000.0;
            double pulse = 1 + 0.10 * Math.Sin(t * 2 * Math.PI / 1.4);
            double d = 6.4 * pulse;
            double gap = 0.62 * 15;
            double leftCx = -(gap / 2 + d / 2), rightCx = (gap / 2 + d / 2);
            using var glow = new SolidBrush(DrawUtil.Argb(0.35 * alpha, col.R, col.G, col.B));
            using var eye = new SolidBrush(DrawUtil.Argb(alpha, col.R, col.G, col.B));
            foreach (var cx in new[] { leftCx, rightCx })
            {
                g.FillEllipse(glow, (float)(cx - d / 2 - 5.5), (float)(-d / 2 - 5.5), (float)(d + 11), (float)(d + 11));
                g.FillEllipse(eye, (float)(cx - d / 2), (float)(-d / 2), (float)d, (float)d);
            }
        }
        else
        {
            // 7 vertical mic bars, width 2.6, spacing 3.4, height driven by per-bar sin energy +
            // live mic level (energy in KiwiMarkEngine terms; approximate directly from now+seed).
            double t = f.Now / 1000.0;
            double totalW = BarCount * BarW + (BarCount - 1) * (BarSpacing - BarW);
            double startX = -totalW / 2.0;
            using var bar = new SolidBrush(DrawUtil.Argb(alpha, col.R, col.G, col.B));
            for (int i = 0; i < BarCount; i++)
            {
                double energy = 0.5 + 0.5 * Math.Sin(t * BarSeeds[i] * 2 * Math.PI + BarPhases[i]);
                double barH = Math.Max(3.4, (h - 7) * energy);
                double bx = startX + i * BarSpacing;
                g.FillRectangle(bar, (float)bx, (float)(-barH / 2), (float)BarW, (float)barH);
            }
        }
        g.Restore(st);
    }

    private static void FillCapsule(Graphics g, Brush b, double cx, double d, double eyeH)
    {
        using var p = DrawUtil.RoundedRect(cx - d / 2, -eyeH / 2, d, eyeH, Math.Min(d, eyeH) / 2);
        g.FillPath(b, p);
    }

    private void DrawMark(Graphics g, FlowFrame f, double w, double h)
    {
        double markScale = Math.Min(1, w / 61.0);
        double cw = _mark.CanvasWidth, ch = _mark.CanvasHeight;
        var st = g.Save();
        // apply mark opacity via a layered bitmap
        int mw = (int)Math.Ceiling(cw), mh = (int)Math.Ceiling(ch);
        using (var mbmp = new Bitmap(Math.Max(1, mw), Math.Max(1, mh), PixelFormat.Format32bppArgb))
        {
            using (var mg = Graphics.FromImage(mbmp))
            {
                mg.Clear(Color.Transparent);
                mg.SmoothingMode = SmoothingMode.AntiAlias;
                _mark.Draw(mg, f.Now / 1000.0);
            }
            double opacity = f.MarkOpacity * (1 - f.SelectionPillOpacity);
            using var ia = new ImageAttributes();
            var cm = new ColorMatrix { Matrix33 = (float)Math.Max(0, Math.Min(1, opacity)) };
            ia.SetColorMatrix(cm);
            g.TranslateTransform((float)(w / 2), (float)(h / 2));
            g.ScaleTransform((float)markScale, (float)markScale);
            var dst = new Rectangle((int)(-cw / 2), (int)(-ch / 2), mw, mh);
            g.DrawImage(mbmp, dst, 0, 0, mw, mh, GraphicsUnit.Pixel, ia);
        }
        g.Restore(st);
    }

    private static void DrawSphere(Graphics g, FlowFrame f, double w, double h, bool glossy)
    {
        double hx = (0.5 + f.LightX * 0.5) * w;
        double hy = (0.5 + f.LightY * 0.5) * h;
        double sx = (0.5 - f.LightX * 0.5) * w;
        double sy = (0.5 - f.LightY * 0.5) * h;
        double op = f.SphereOpacity * (1 - f.SelectionPillOpacity);
        double radius = Math.Max(w, h);

        // specular highlight
        RadialFill(g, hx, hy, radius * 0.7, glossy
            ? new[] { (0.0, DrawUtil.Argb(0.65 * op, 255, 255, 255)), (0.22, DrawUtil.Argb(0.18 * op, 255, 255, 255)), (0.46, DrawUtil.Argb(0, 255, 255, 255)) }
            : new[] { (0.0, DrawUtil.Argb(0.18 * op, 255, 255, 255)), (0.16, DrawUtil.Argb(0.05 * op, 255, 255, 255)), (0.40, DrawUtil.Argb(0, 255, 255, 255)) });

        // rim shadow at antipode
        int[] rim = glossy ? new[] { 20, 28, 12 } : new[] { 0, 0, 0 };
        double rimA = glossy ? 0.30 : 0.55;
        RadialFill(g, sx, sy, radius * 0.6, new[]
        {
            (0.0, DrawUtil.Argb(rimA * op, rim[0], rim[1], rim[2])),
            (0.5, DrawUtil.Argb(0, rim[0], rim[1], rim[2])),
        });

        // edge vignette
        double vigA = glossy ? 0.20 : 0.34;
        int[] vig = glossy ? new[] { 20, 28, 12 } : new[] { 0, 0, 0 };
        RadialFill(g, w / 2, h / 2, radius * 0.75, new[]
        {
            (glossy ? 0.56 : 0.58, DrawUtil.Argb(0, vig[0], vig[1], vig[2])),
            (1.0, DrawUtil.Argb(vigA * op, vig[0], vig[1], vig[2])),
        });
    }

    private static void RadialFill(Graphics g, double cx, double cy, double radius, (double stop, Color col)[] stops)
    {
        if (radius <= 0) return;
        using var path = new GraphicsPath();
        path.AddEllipse((float)(cx - radius), (float)(cy - radius), (float)(radius * 2), (float)(radius * 2));
        using var br = new PathGradientBrush(path)
        {
            CenterPoint = new PointF((float)cx, (float)cy),
            CenterColor = stops[0].col,
        };
        // PathGradientBrush: build interpolation from stops (center→edge).
        int n = stops.Length;
        var colors = new Color[n];
        var positions = new float[n];
        for (int i = 0; i < n; i++)
        {
            positions[i] = (float)Math.Max(0, Math.Min(1, stops[i].stop));
            colors[i] = stops[i].col;
        }
        // PathGradient blend needs positions ascending 0..1 with surround as last.
        br.CenterColor = colors[0];
        br.SurroundColors = new[] { colors[n - 1] };
        try
        {
            var blend = new ColorBlend(n);
            for (int i = 0; i < n; i++)
            {
                // interpolation factors go edge(0)→center(1) for PathGradient
                blend.Positions[i] = 1f - positions[n - 1 - i];
                blend.Colors[i] = colors[n - 1 - i];
            }
            blend.Positions[0] = 0f;
            blend.Positions[n - 1] = 1f;
            br.InterpolationColors = blend;
        }
        catch { /* fall back to center/surround */ }
        g.FillPath(br, path);
    }

    // Convert straight-alpha ARGB to premultiplied ARGB (required by UpdateLayeredWindow).
    private static void Premultiply(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* p = (byte*)data.Scan0;
                int count = bmp.Width * bmp.Height;
                for (int i = 0; i < count; i++)
                {
                    int o = i * 4;
                    byte a = p[o + 3];
                    if (a == 255) continue;
                    p[o + 0] = (byte)(p[o + 0] * a / 255); // B
                    p[o + 1] = (byte)(p[o + 1] * a / 255); // G
                    p[o + 2] = (byte)(p[o + 2] * a / 255); // R
                }
            }
        }
        finally { bmp.UnlockBits(data); }
    }
}
