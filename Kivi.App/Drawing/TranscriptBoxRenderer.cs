using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Kivi.Core.Orb;

namespace Kivi.App.Drawing;

/// <summary>
/// TranscriptBox — the orb-box "turn surface", a pure function of one FlowFrame. Ported from
/// src/renderer/src/orb/render/TranscriptBox.tsx: the WedgeBoxShape (radius 8, centered wedge W20 H9)
/// + paper grain + outline + geometry drop-shadow, the header row (app chip · state narration), and
/// the inner transcript lines (waiting/speaking/final/dim + the diff-token morph). Fonts use the
/// documented fallback stack (Matter/Space Grotesk are dev-only, license-blocked).
///
/// Deferred to a later pass (noted): the maxi mini-app footer action bar (thumbs, voice slot, new
/// session), pager dots, context card, copy chip, and the wave-sweep gradient — the visual skeleton
/// (surface + header + transcript + diff) is faithful.
/// </summary>
internal static class TranscriptBoxRenderer
{
    private const double WedgeH = 9, WedgeW = 20, WedgeGap = 3;
    private const double Radius = 8;
    private const string Reading = "Segoe UI"; // Matter fallback
    private const string Mono = "Consolas";     // Matter Mono fallback

    public static void Draw(Graphics g, FlowFrame f, double centerX, double orbCenterYBase, bool forest)
    {
        // palette
        var box = forest ? DrawUtil.Hex("#161616") : DrawUtil.Hex("#FCFAF3");
        var boxInner = forest ? DrawUtil.Hex("#1D231A") : DrawUtil.Hex("#F7F9EC");
        var card = forest ? DrawUtil.Hex("#20211E") : DrawUtil.Hex("#EFECDF");
        var outline = forest ? DrawUtil.Argb(0.08, 255, 255, 255) : DrawUtil.Argb(0.14, 32, 36, 31);
        var baseCol = forest ? DrawUtil.Hex("#ECEFE8") : DrawUtil.Hex("#1A2710");
        var listen = forest ? DrawUtil.Hex("#9AA192") : DrawUtil.Hex("#646E58");
        var ins = forest ? DrawUtil.Hex("#8FD06A") : DrawUtil.Hex("#2F7D2E");
        var del = forest ? DrawUtil.Hex("#F0716F") : DrawUtil.Hex("#B81514");
        var accListen = DrawUtil.Hex("#E6651B");

        double boxW = f.BoxW;
        double boxH = f.BoxH;
        // seam: box sits below the orb with the wedge gap.
        double orbBottom = orbCenterYBase + f.Drop + f.OrbHeight;
        double boxTop = orbBottom + WedgeGap;
        double boxLeft = centerX - boxW / 2 + f.FlowShiftX;

        double opacity = Math.Min(1, f.TxOpacity);
        if (opacity <= 0.01) return;

        var st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform((float)boxLeft, (float)boxTop);

        // vertical reveal mask: the box unfurls downward to txWrapHeight.
        double revealH = f.TxWrapClips && f.TxWrapHeight > 0 ? Math.Min(boxH, f.TxWrapHeight + WedgeH) : boxH;
        var clip = g.Save();
        g.SetClip(new RectangleF(-40, 0, (float)boxW + 80, (float)revealH));

        using var shape = WedgePath(boxW, boxH, Radius, apexOnBottom: f.FlipY);

        // drop shadow (geometry-only, soft)
        using (var sh = new SolidBrush(DrawUtil.Argb((forest ? 0.4 : 0.10) * opacity, forest ? 0 : 20, forest ? 0 : 20, forest ? 0 : 20)))
        {
            var s2 = g.Save();
            g.TranslateTransform(0, 3);
            g.FillPath(sh, shape);
            g.Restore(s2);
        }

        // fill + grain + outline
        using (var fb = new SolidBrush(Color.FromArgb((int)(opacity * 255), box.R, box.G, box.B)))
            g.FillPath(fb, shape);
        var gclip = g.Save();
        g.SetClip(shape);
        DrawGrain(g, forest, boxW, boxH);
        g.Restore(gclip);
        using (var pen = new Pen(Color.FromArgb((int)(opacity * outline.A), outline.R, outline.G, outline.B), 1f))
            g.DrawPath(pen, shape);

        // --- content area (below the wedge) ---
        double contentTop = (f.FlipY ? 0 : WedgeH) + 10;
        double padL = 16, padR = 16;

        // header: state narration (top-right, mono 10)
        var header = HeaderState(f, baseCol, ins, accListen);
        if (header != null)
        {
            using var hf = new Font(Mono, 8.5f);
            var (text, col) = header.Value;
            var size = g.MeasureString(text, hf);
            using var hb = new SolidBrush(Color.FromArgb((int)(opacity * col.A), col.R, col.G, col.B));
            g.DrawString(text, hf, hb, (float)(boxW - padR - size.Width), (float)contentTop);
        }
        // app name (left) — "kivi" as the default chip label
        using (var af = new Font(Reading, 9.5f, FontStyle.Bold))
        using (var ab = new SolidBrush(Color.FromArgb((int)(opacity * baseCol.A), baseCol.R, baseCol.G, baseCol.B)))
            g.DrawString("kivi", af, ab, (float)padL, (float)contentTop);

        // --- transcript lines ---
        double textTop = contentTop + 26;
        double lineX = padL;
        double maxW = boxW - padL - padR;
        using var body = new Font(Reading, 10.5f);

        double y = textTop;
        foreach (var line in f.TxLines)
        {
            y = DrawLine(g, line, f, lineX, y, maxW, body, opacity, baseCol, listen, ins, del);
            y += 6;
            if (y > revealH - 10) break;
        }

        g.Restore(clip);
        g.Restore(st);
    }

    private static double DrawLine(Graphics g, TxLine line, FlowFrame f, double x, double y, double maxW,
        Font body, double opacity, Color baseCol, Color listen, Color ins, Color del)
    {
        switch (line.Role)
        {
            case TxLineRole.Waiting:
            {
                using var it = new Font(body.FontFamily, body.Size, FontStyle.Italic);
                var c = Color.FromArgb((int)(opacity * listen.A), listen.R, listen.G, listen.B);
                using var b = new SolidBrush(c);
                g.DrawString("listening…", it, b, (float)x, (float)y);
                return y + body.GetHeight(g);
            }
            case TxLineRole.Tokens when line.Tokens != null:
                return DrawTokens(g, line.Tokens, f, x, y, maxW, body, opacity, baseCol, ins, del);
            default:
            {
                double alpha = line.Role == TxLineRole.Dim ? 0.34 : 1.0;
                var col = line.Role == TxLineRole.Speaking ? ins : baseCol;
                var c = Color.FromArgb((int)(opacity * alpha * 255), col.R, col.G, col.B);
                using var b = new SolidBrush(c);
                string text = line.Text;
                if (line.Role == TxLineRole.Speaking && !string.IsNullOrEmpty(f.TxDots)) text += " " + f.TxDots;
                return DrawWrapped(g, text, body, b, x, y, maxW);
            }
        }
    }

    private static double DrawTokens(Graphics g, List<TxToken> tokens, FlowFrame f, double x, double y, double maxW,
        Font body, double opacity, Color baseCol, Color ins, Color del)
    {
        var p = f.DiffProgress;
        double cx = x;
        double lineH = body.GetHeight(g);
        foreach (var tok in tokens)
        {
            Color col;
            switch (tok.Kind)
            {
                case TxTokenKind.Same: col = baseCol; break;
                case TxTokenKind.Final: col = ins; break;
                case TxTokenKind.Ins: col = ins; break;
                case TxTokenKind.Del:
                {
                    double ra = p?.Landing ?? 1.0;
                    double s = p?.Collapse ?? 0.0;
                    var rc = Lerp(baseCol, del, ra);
                    col = Color.FromArgb((int)(opacity * (1 - 0.18 * ra) * (1 - s) * 255), rc.R, rc.G, rc.B);
                    if (s > 0.98) continue; // collapsed away
                    break;
                }
                default: col = baseCol; break;
            }
            if (tok.Kind != TxTokenKind.Del)
                col = Color.FromArgb((int)(opacity * 255), col.R, col.G, col.B);
            using var b = new SolidBrush(col);
            var sz = g.MeasureString(tok.Text, body, PointF.Empty, System.Drawing.StringFormat.GenericTypographic);
            if (cx + sz.Width > x + maxW) { cx = x; y += lineH; }
            var fontStyle = (tok.Kind == TxTokenKind.Del) ? FontStyle.Strikeout : FontStyle.Regular;
            using var tf = new Font(body.FontFamily, body.Size, fontStyle);
            g.DrawString(tok.Text, tf, b, (float)cx, (float)y, System.Drawing.StringFormat.GenericTypographic);
            cx += sz.Width;
        }
        return y + lineH;
    }

    private static double DrawWrapped(Graphics g, string text, Font f, Brush b, double x, double y, double maxW)
    {
        var rect = new RectangleF((float)x, (float)y, (float)maxW, 1000);
        var fmt = new System.Drawing.StringFormat { Trimming = StringTrimming.Word };
        var size = g.MeasureString(text, f, (int)maxW);
        g.DrawString(text, f, b, rect, fmt);
        return y + size.Height;
    }

    private static (string text, Color col)? HeaderState(FlowFrame f, Color baseCol, Color ins, Color accListen)
    {
        if (f.TxNotice != null) return (f.TxNotice, Color.FromArgb(150, baseCol.R, baseCol.G, baseCol.B));
        switch (f.Phase)
        {
            case FlowPhase.EditListen: return ($"listening {f.TxDots}", ins);
            case FlowPhase.EditProcess: return ("editing …", ins);
            case FlowPhase.Listening:
                if (f.TxAwaitingSpeech)
                    return f.TxWaitingPhase switch
                    {
                        1 => ("are you there?", accListen),
                        2 => ("mic may not be working", accListen),
                        3 => ("check mic settings", accListen),
                        _ => ("speak now …", ins),
                    };
                return ($"listening {f.TxDots}", ins);
            case FlowPhase.Processing: return ("transcribing …", accListen);
            default: return null;
        }
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromArgb(255,
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    private static void DrawGrain(Graphics g, bool dark, double w, double h)
    {
        var tile = PaperGrain.TileBitmap(dark);
        double op = PaperGrain.Opacity(dark);
        double scale = PaperGrain.Scale(dark);
        using var ia = new System.Drawing.Imaging.ImageAttributes();
        var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)op };
        ia.SetColorMatrix(cm);
        double tw = tile.Width * scale, th = tile.Height * scale;
        for (double yy = 0; yy < h; yy += th)
            for (double xx = 0; xx < w; xx += tw)
                g.DrawImage(tile, new Rectangle((int)xx, (int)yy, (int)Math.Ceiling(tw), (int)Math.Ceiling(th)),
                    0, 0, tile.Width, tile.Height, GraphicsUnit.Pixel, ia);
    }

    // WedgeBoxShape → GraphicsPath, ported from src/renderer/src/orb/render/wedge.ts.
    private static GraphicsPath WedgePath(double w, double h, double radius, bool apexOnBottom)
    {
        double rad = Math.Max(0, radius);
        double wh = WedgeH, ww = WedgeW / 2, cx = w / 2;
        double bodyTop = apexOnBottom ? 0 : wh;
        double bodyBottom = apexOnBottom ? h - wh : h;
        double tipR = 3;
        double edgeLen = Math.Sqrt(ww * ww + wh * wh);
        double t = Math.Min(0.45, tipR / Math.Max(1, edgeLen));

        var p = new GraphicsPath();
        var pts = new List<PointF>();
        void L(double x, double y) => pts.Add(new PointF((float)x, (float)y));

        // We approximate arcs with the path AddArc, so build segment-by-segment.
        p.StartFigure();
        if (!apexOnBottom)
        {
            double apexX = cx, apexY = bodyTop - wh;
            double nearLx = apexX + (cx - ww - apexX) * t, nearLy = apexY + (bodyTop - apexY) * t;
            double nearRx = apexX + (cx + ww - apexX) * t, nearRy = apexY + (bodyTop - apexY) * t;
            p.AddLine((float)rad, (float)bodyTop, (float)(cx - ww), (float)bodyTop);
            p.AddLine((float)(cx - ww), (float)bodyTop, (float)nearLx, (float)nearLy);
            double c1x = nearLx + 2.0 / 3 * (apexX - nearLx), c1y = nearLy + 2.0 / 3 * (apexY - nearLy);
            double c2x = nearRx + 2.0 / 3 * (apexX - nearRx), c2y = nearRy + 2.0 / 3 * (apexY - nearRy);
            p.AddBezier((float)nearLx, (float)nearLy, (float)c1x, (float)c1y, (float)c2x, (float)c2y, (float)nearRx, (float)nearRy);
            p.AddLine((float)nearRx, (float)nearRy, (float)(cx + ww), (float)bodyTop);
        }
        p.AddLine((float)(apexOnBottom ? rad : cx + ww), (float)bodyTop, (float)(w - rad), (float)bodyTop);
        p.AddArc((float)(w - rad * 2), (float)bodyTop, (float)(rad * 2), (float)(rad * 2), 270, 90);
        p.AddArc((float)(w - rad * 2), (float)(bodyBottom - rad * 2), (float)(rad * 2), (float)(rad * 2), 0, 90);
        if (apexOnBottom)
        {
            double apexX = cx, apexY = bodyBottom + wh;
            double nearRx = apexX + (cx + ww - apexX) * t, nearRy = apexY + (bodyBottom - apexY) * t;
            double nearLx = apexX + (cx - ww - apexX) * t, nearLy = apexY + (bodyBottom - apexY) * t;
            p.AddLine((float)(w - rad), (float)bodyBottom, (float)(cx + ww), (float)bodyBottom);
            p.AddLine((float)(cx + ww), (float)bodyBottom, (float)nearRx, (float)nearRy);
            double c1x = nearRx + 2.0 / 3 * (apexX - nearRx), c1y = nearRy + 2.0 / 3 * (apexY - nearRy);
            double c2x = nearLx + 2.0 / 3 * (apexX - nearLx), c2y = nearLy + 2.0 / 3 * (apexY - nearLy);
            p.AddBezier((float)nearRx, (float)nearRy, (float)c1x, (float)c1y, (float)c2x, (float)c2y, (float)nearLx, (float)nearLy);
            p.AddLine((float)nearLx, (float)nearLy, (float)(cx - ww), (float)bodyBottom);
        }
        p.AddLine((float)(apexOnBottom ? cx - ww : w - rad), (float)bodyBottom, (float)rad, (float)bodyBottom);
        p.AddArc((float)0, (float)(bodyBottom - rad * 2), (float)(rad * 2), (float)(rad * 2), 90, 90);
        p.AddArc((float)0, (float)bodyTop, (float)(rad * 2), (float)(rad * 2), 180, 90);
        p.CloseFigure();
        return p;
    }
}
