using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Kivi.Core.Orb;

namespace Kivi.App.Drawing;

/// <summary>
/// TranscriptBox — the orb-box "turn surface", a pure function of one FlowFrame. Ported from
/// src/renderer/src/orb/render/TranscriptBox.tsx: the WedgeBoxShape (radius 8, centered wedge W20 H9)
/// + paper grain + outline + geometry drop-shadow, the header row (app chip · state narration ·
/// copy/maximize chip · pager dots), the inner transcript lines (waiting/speaking/final/dim + the
/// diff-token morph + the wave-sweep overlay while processing/editing), and the footer action bar
/// (voice slot · word count · thumbs · new-session pill). Fonts use the documented fallback stack
/// (Matter/Space Grotesk are dev-only, license-blocked).
///
/// Still deferred (out of this pass's scope, noted so it isn't mistaken for an oversight): the
/// context card (§8b hey-kivi callout) and the maxi in-box resize handles.
/// </summary>
internal static class TranscriptBoxRenderer
{
    private const double WedgeH = 9, WedgeW = 20, WedgeGap = 3;
    private const double Radius = 8;
    private const string Reading = "Segoe UI"; // Matter fallback
    private const string Mono = "Consolas";     // Matter Mono fallback
    private const string Display = "Segoe UI Semibold"; // Space Grotesk fallback

    // Footer + copy-chip geometry — MUST mirror FlowFrame.cs's private CopyChipSize/HeaderPadR/
    // FooterH/ThumbSize/ThumbGap/NewSessionW/H/Pad constants verbatim so the drawn chips and their
    // hit regions never drift apart.
    private const double CopyChipSize = 26, HeaderPadR = 16;
    private const double FooterH = 30;
    private const double ThumbSize = 28, ThumbGap = 6;
    private const double NewSessionW = 92, NewSessionH = 27, NewSessionPad = 12;

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
        double headerTextRight = boxW - padR;
        bool copyChipVisible = f.TxWordCount > 0 &&
            (f.TxStage == TxStage.Done || f.TxStage == TxStage.Typed || f.TxStage == TxStage.Pasted);
        if (copyChipVisible) headerTextRight -= CopyChipSize + 6;
        if (header != null)
        {
            using var hf = new Font(Mono, 8.5f);
            var (text, col) = header.Value;
            var size = g.MeasureString(text, hf);
            using var hb = new SolidBrush(Color.FromArgb((int)(opacity * col.A), col.R, col.G, col.B));
            g.DrawString(text, hf, hb, (float)(headerTextRight - size.Width), (float)contentTop);
        }
        // app name — "kivi" as the default chip label, centered in the header row's available
        // width (between the left pad and wherever the state-narration/copy-chip start on the
        // right) rather than pinned flush-left, per explicit user request.
        using (var af = new Font(Reading, 9.5f, FontStyle.Bold))
        using (var ab = new SolidBrush(Color.FromArgb((int)(opacity * baseCol.A), baseCol.R, baseCol.G, baseCol.B)))
        {
            var nameSize = g.MeasureString("kivi", af);
            double nameLeft = padL + (headerTextRight - padL - nameSize.Width) / 2.0;
            g.DrawString("kivi", af, ab, (float)nameLeft, (float)contentTop);
        }

        // copy chip (§8b/§8c, header-row top-right, 26x26) — matched to the actual
        // _reference/TranscriptBox.tsx header markup, not the map's inner-card wording (RULE 2: the
        // running Electron code wins on placement when the two disagree). Washes to ins·0.16 with a
        // check glyph on copyFlash.
        if (copyChipVisible)
        {
            double chipLeft = boxW - padR - CopyChipSize;
            double chipTop = contentTop;
            double washAlpha = f.CopyFlash ? 0.16 : 0.10;
            using var wb = new SolidBrush(DrawUtil.Argb(opacity * washAlpha, ins.R, ins.G, ins.B));
            using var wp = DrawUtil.RoundedRect(chipLeft, chipTop, CopyChipSize, CopyChipSize, 7);
            g.FillPath(wb, wp);
            using var wpen = new Pen(DrawUtil.Argb(opacity * (f.CopyFlash ? 0.45 : 0.28), ins.R, ins.G, ins.B), 1f);
            g.DrawPath(wpen, wp);
            var chipIconColor = Color.FromArgb((int)(opacity * 255), ins.R, ins.G, ins.B);
            OrbIcons.Draw(g, f.CopyFlash ? "check" : "copy", chipLeft + CopyChipSize / 2, chipTop + CopyChipSize / 2, 13, chipIconColor);
        }

        // pager dots (§8a, centered overlay in the header row): active capsule 16x6, inactive 6x6,
        // spacing 4, capped at 10.
        if (f.TxPagerCount > 1)
            DrawPagerDots(g, f, boxW, contentTop + 10, baseCol, ins, opacity);

        // --- transcript lines ---
        // 26px previously left barely a few px of clearance below the 9.5pt-bold "kivi" label
        // (glyph height alone is ~13-14px at that size) before the transcript body started —
        // header and first line read as crowded/touching. 34px gives the header row proper
        // breathing room while staying compact.
        double textTop = contentTop + 34;
        double lineX = padL;
        double maxW = boxW - padL - padR;
        double footerTop = boxH - FooterH;
        using var body = new Font(Reading, 10.5f);

        var textStart = new PointF((float)lineX, (float)textTop);
        double y = textTop;
        foreach (var line in f.TxLines)
        {
            y = DrawLine(g, line, f, lineX, y, maxW, body, opacity, baseCol, listen, ins, del);
            y += 6;
            if (y > Math.Min(revealH, footerTop) - 10) break;
        }

        // wave sweep (§9): a 46%-wide gradient band sweeping -55%->155% across the transcript text
        // while processing/editing, blended only over the glyph area; text dims to 0.78 underneath.
        // NOTE (divergence): the current _reference Electron snapshot only swaps the text to a flat
        // `waveText` muted color during these stages (no literal moving/blurred band — verified, no
        // such CSS/keyframe exists anywhere under _reference/.../orb) — this looks like a
        // Swift-era visual not carried into the Electron port. Implemented here per the map's exact
        // byte-values (orb-visual-and-box.md §9) since that is the documented byte-exact spec and
        // there is no richer reference implementation to contradict it.
        bool waving = f.TxStage == TxStage.Wave || f.TxStage == TxStage.EditWave;
        if (waving && y > textTop)
            DrawWaveSweep(g, f, lineX, textTop, maxW, y - textTop, opacity);

        // footer action bar (§8d, height 30, 1px top hairline)
        DrawFooter(g, f, boxW, boxH, opacity, baseCol, ins, accListen, forest);

        g.Restore(clip);
        g.Restore(st);
    }

    private static void DrawPagerDots(Graphics g, FlowFrame f, double boxW, double centerY, Color baseCol, Color ins, double opacity)
    {
        int count = Math.Min(f.TxPagerCount, 10);
        int startIdx = f.TxPagerCount - count;
        const double activeW = 16, inactiveW = 6, dotH = 6, spacing = 4;
        double totalW = 0;
        for (int i = 0; i < count; i++) totalW += (startIdx + i == f.TxPagerIndex ? activeW : inactiveW);
        totalW += spacing * (count - 1);
        double x = boxW / 2 - totalW / 2;
        for (int i = 0; i < count; i++)
        {
            bool on = startIdx + i == f.TxPagerIndex;
            double w = on ? activeW : inactiveW;
            var col = on ? ins : DrawUtil.Argb(0.3, baseCol.R, baseCol.G, baseCol.B);
            using var b = new SolidBrush(Color.FromArgb((int)(opacity * col.A), col.R, col.G, col.B));
            using var p = DrawUtil.RoundedRect(x, centerY - dotH / 2, w, dotH, dotH / 2);
            g.FillPath(b, p);
            x += w + spacing;
        }
    }

    private static void DrawWaveSweep(Graphics g, FlowFrame f, double x, double y, double w, double h, double opacity)
    {
        bool editing = f.TxStage == TxStage.EditWave;
        double periodMs = editing ? 2400 : 2600;
        double phase = (f.Now % periodMs) / periodMs; // 0..1
        double bandCenterFrac = -0.55 + phase * (1.55 - (-0.55)); // -55% -> 155%
        double bandWFrac = 0.46;
        // hey-kivi listening uses green; processing/edit uses indigo (map §9).
        bool listeningGreen = f.Phase == FlowPhase.EditListen;
        var waveCol = listeningGreen ? Color.FromArgb(242, 143, 206, 110) : Color.FromArgb(242, 74, 94, 232);

        var clipRect = new RectangleF((float)x, (float)y, (float)w, (float)h);
        var saved = g.Save();
        g.SetClip(clipRect);

        double bandCenterX = x + bandCenterFrac * w;
        double bandW = bandWFrac * w;
        using var path = new GraphicsPath();
        path.AddRectangle(new RectangleF((float)(bandCenterX - bandW / 2), (float)y, (float)bandW, (float)h));
        using var pgb = new PathGradientBrush(path);
        pgb.CenterColor = Color.FromArgb((int)(opacity * waveCol.A), waveCol.R, waveCol.G, waveCol.B);
        pgb.SurroundColors = new[] { Color.FromArgb(0, waveCol.R, waveCol.G, waveCol.B) };
        pgb.CenterPoint = new PointF((float)bandCenterX, (float)(y + h / 2));
        g.FillRectangle(pgb, (float)(bandCenterX - bandW / 2), (float)y, (float)bandW, (float)h);

        g.Restore(saved);
    }

    private static void DrawFooter(Graphics g, FlowFrame f, double boxW, double boxH, double opacity,
        Color baseCol, Color ins, Color accListen, bool forest)
    {
        double footerTop = boxH - FooterH;
        var outline = forest ? DrawUtil.Argb(0.08, 255, 255, 255) : DrawUtil.Argb(0.14, 32, 36, 31);

        bool editingNow = f.Phase == FlowPhase.EditListen || f.Phase == FlowPhase.EditProcess;
        bool dictatingNow = f.Phase == FlowPhase.Listening || f.Phase == FlowPhase.Processing;
        bool hasSettled = (f.TxStage == TxStage.Done || f.TxStage == TxStage.Typed || f.TxStage == TxStage.Pasted) && f.TxWordCount > 0;
        bool freshPane = f.TxStage == TxStage.Idle && !hasSettled && !editingNow && !dictatingNow;
        double barOpacity = opacity * (freshPane ? 0.72 : 1.0);

        // top hairline
        using (var hp = new Pen(Color.FromArgb((int)(barOpacity * outline.A), outline.R, outline.G, outline.B), 1f))
            g.DrawLine(hp, 0, (float)footerTop, (float)boxW, (float)footerTop);

        double slotLeft = 12;
        double slotCenterY = footerTop + FooterH / 2.0;

        // left voice slot pill (retry / follow-up / last / follow-up+keycaps)
        string? slotIcon = null, slotLabel = null;
        Color slotAccent = baseCol; double slotWashA = 0; bool slotFlow = false; string[]? slotKeys = null;
        if (f.RetryOffered) { slotIcon = "playback"; slotLabel = "retry"; slotAccent = accListen; slotWashA = 0.12; }
        else if (editingNow) { slotIcon = "sparkles"; slotLabel = "ask follow up"; slotAccent = ins; slotWashA = 0.18 + 0.14 * f.Breath; }
        else if (dictatingNow) { slotIcon = "playback"; slotLabel = "last"; slotAccent = DrawUtil.Argb(0.7, baseCol.R, baseCol.G, baseCol.B); slotFlow = true; }
        else if (hasSettled) { slotIcon = "sparkles"; slotLabel = "ask follow up"; slotAccent = ins; slotWashA = 0.12; slotKeys = new[] { f.HotkeyLabel, f.EditComboLabel }; }
        else if (f.BandHistOn) { slotIcon = "playback"; slotLabel = "last"; slotAccent = DrawUtil.Argb(0.7, baseCol.R, baseCol.G, baseCol.B); }

        double slotRight = slotLeft;
        if (slotIcon != null)
        {
            slotRight = DrawSlotPill(g, slotLeft, slotCenterY, slotIcon, slotLabel!, slotAccent, slotWashA, slotKeys,
                slotFlow ? f.Now : (double?)null, barOpacity);
        }

        // word count (mono 10) when settled
        if (hasSettled && f.TxWordCount > 0)
        {
            string wc = f.TxWordCount + (f.TxWordCount == 1 ? " word" : " words");
            using var wf = new Font(Mono, 8.5f);
            var wcol = DrawUtil.Argb(0.55, baseCol.R, baseCol.G, baseCol.B);
            using var wb = new SolidBrush(Color.FromArgb((int)(barOpacity * wcol.A), wcol.R, wcol.G, wcol.B));
            var sz = g.MeasureString(wc, wf);
            g.DrawString(wc, wf, wb, (float)(slotRight + 8), (float)(slotCenterY - sz.Height / 2));
        }

        // thumbs (only when ratable), anchored just left of the new-session pill
        double nsLeftAnchor = boxW - NewSessionPad - NewSessionW;
        if (f.TakeRatable)
        {
            double upLeft = nsLeftAnchor - ThumbGap - ThumbSize * 2 - ThumbGap;
            double upTop = footerTop + (FooterH - ThumbSize) / 2.0;
            DrawThumb(g, upLeft, upTop, up: true, active: f.TakeRating == 1, baseCol, ins, barOpacity);
            DrawThumb(g, upLeft + ThumbSize + ThumbGap, upTop, up: false, active: f.TakeRating == -1, baseCol, ins, barOpacity);
        }

        // right "new session" pill, with an orange flow-band sweep while dictating (period 1300ms)
        double nsTop = footerTop + (FooterH - NewSessionH) / 2.0;
        DrawNewSessionPill(g, nsLeftAnchor, nsTop, NewSessionW, NewSessionH, f.HotkeyLabel,
            baseCol, accListen, dictatingNow ? f.Now : (double?)null, barOpacity);
    }

    private static double DrawSlotPill(Graphics g, double left, double centerY, string icon, string label,
        Color accent, double washAlpha, string[]? keys, double? flowNow, double opacity)
    {
        using var df = new Font(Display, 9f);
        using var kf = new Font(Mono, 7f);
        double pad = 9, gap = 6, iconSize = 13;
        var labelSz = g.MeasureString(label, df, PointF.Empty, StringFormat.GenericTypographic);
        double contentW = iconSize + gap + labelSz.Width;
        double keysW = 0;
        var keySizes = new List<SizeF>();
        if (keys != null)
            foreach (var k in keys)
            {
                var ks = g.MeasureString(k, kf, PointF.Empty, StringFormat.GenericTypographic);
                keySizes.Add(ks);
                keysW += gap + ks.Width + 8;
            }
        double w = pad * 2 + contentW + keysW;
        double h = 26;
        double top = centerY - h / 2;

        using var wash = new SolidBrush(DrawUtil.Argb(opacity * washAlpha, accent.R, accent.G, accent.B));
        using var pillPath = DrawUtil.RoundedRect(left, top, w, h, 9);
        if (washAlpha > 0) g.FillPath(wash, pillPath);
        bool flow = flowNow != null;
        var borderCol = flow ? DrawUtil.Hex("#E6651B") : accent;
        using var pen = new Pen(DrawUtil.Argb(opacity * (flow ? 0.45 : 0.30), borderCol.R, borderCol.G, borderCol.B), 1f);
        g.DrawPath(pen, pillPath);

        if (flow)
        {
            var saved = g.Save();
            g.SetClip(pillPath);
            double periodMs = 1300;
            double t = (flowNow!.Value % periodMs) / periodMs;
            double bandCenterFrac = -0.27 + t * (1.28 - (-0.27));
            var flowCol = DrawUtil.Hex("#E6651B");
            double bandCx = left + bandCenterFrac * w, bandW = 0.55 * w;
            using var path = new GraphicsPath();
            path.AddRectangle(new RectangleF((float)(bandCx - bandW / 2), (float)top, (float)bandW, (float)h));
            using var pgb = new PathGradientBrush(path)
            {
                CenterColor = DrawUtil.Argb(opacity * 0.4, flowCol.R, flowCol.G, flowCol.B),
                SurroundColors = new[] { DrawUtil.Argb(0, flowCol.R, flowCol.G, flowCol.B) },
                CenterPoint = new PointF((float)bandCx, (float)(top + h / 2)),
            };
            g.FillRectangle(pgb, (float)(bandCx - bandW / 2), (float)top, (float)bandW, (float)h);
            g.Restore(saved);
        }

        var iconCol = Color.FromArgb((int)(opacity * 255), accent.R, accent.G, accent.B);
        OrbIcons.Draw(g, icon, left + pad + iconSize / 2, centerY, iconSize, iconCol);
        using var lb = new SolidBrush(iconCol);
        g.DrawString(label, df, lb, (float)(left + pad + iconSize + gap), (float)(centerY - labelSz.Height / 2), StringFormat.GenericTypographic);

        double kx = left + pad + contentW + gap;
        if (keys != null)
            for (int i = 0; i < keys.Length; i++)
            {
                var ks = keySizes[i];
                double kw = ks.Width + 8;
                using var kb = new SolidBrush(DrawUtil.Argb(opacity * 0.14, accent.R, accent.G, accent.B));
                using var kp = DrawUtil.RoundedRect(kx, centerY - 8, kw, 16, 4);
                g.FillPath(kb, kp);
                using var kfg = new SolidBrush(DrawUtil.Argb(opacity * 0.85, accent.R, accent.G, accent.B));
                g.DrawString(keys[i], kf, kfg, (float)(kx + 4), (float)(centerY - ks.Height / 2), StringFormat.GenericTypographic);
                kx += kw + gap;
            }

        return left + w;
    }

    private static void DrawThumb(Graphics g, double left, double top, bool up, bool active, Color baseCol, Color ins, double opacity)
    {
        var bg = active ? DrawUtil.Argb(opacity * 0.12, ins.R, ins.G, ins.B) : Color.FromArgb(0, 0, 0, 0);
        var border = active ? DrawUtil.Argb(opacity * 0.4, ins.R, ins.G, ins.B) : DrawUtil.Argb(opacity * 0.25, baseCol.R, baseCol.G, baseCol.B);
        using var bp = new SolidBrush(bg);
        using var path = DrawUtil.RoundedRect(left, top, ThumbSize, ThumbSize, 8);
        g.FillPath(bp, path);
        using var pen = new Pen(border, 1f);
        g.DrawPath(pen, path);
        var iconCol = active ? Color.FromArgb((int)(opacity * 255), ins.R, ins.G, ins.B)
            : DrawUtil.Argb(opacity * 0.5, baseCol.R, baseCol.G, baseCol.B);
        OrbIcons.Draw(g, up ? "thumbUp" : "thumbDown", left + ThumbSize / 2, top + ThumbSize / 2, 14, iconCol);
    }

    private static void DrawNewSessionPill(Graphics g, double left, double top, double w, double h, string hotkeyLabel,
        Color baseCol, Color accListen, double? flowNow, double opacity)
    {
        var accent = DrawUtil.Argb(0.7, baseCol.R, baseCol.G, baseCol.B);
        var card = DrawUtil.Argb(1.0, 239, 236, 223); // pal.card approx (light); good enough tonal wash
        using var path = DrawUtil.RoundedRect(left, top, w, h, 9);
        using var bg = new SolidBrush(DrawUtil.Argb(opacity * 0.5, card.R, card.G, card.B));
        g.FillPath(bg, path);
        bool flow = flowNow != null;
        var borderCol = flow ? DrawUtil.Hex("#E6651B") : accent;
        using var pen = new Pen(DrawUtil.Argb(opacity * (flow ? 0.45 : 0.30), borderCol.R, borderCol.G, borderCol.B), 1f);
        g.DrawPath(pen, path);

        if (flow)
        {
            var saved = g.Save();
            g.SetClip(path);
            double periodMs = 1300;
            double t = (flowNow!.Value % periodMs) / periodMs;
            double bandCenterFrac = -0.27 + t * (1.28 - (-0.27));
            var flowCol = DrawUtil.Hex("#E6651B");
            double bandCx = left + bandCenterFrac * w, bandW = 0.55 * w;
            using var gp = new GraphicsPath();
            gp.AddRectangle(new RectangleF((float)(bandCx - bandW / 2), (float)top, (float)bandW, (float)h));
            using var pgb = new PathGradientBrush(gp)
            {
                CenterColor = DrawUtil.Argb(opacity * 0.4, flowCol.R, flowCol.G, flowCol.B),
                SurroundColors = new[] { DrawUtil.Argb(0, flowCol.R, flowCol.G, flowCol.B) },
                CenterPoint = new PointF((float)bandCx, (float)(top + h / 2)),
            };
            g.FillRectangle(pgb, (float)(bandCx - bandW / 2), (float)top, (float)bandW, (float)h);
            g.Restore(saved);
        }

        using var iconBrush = new SolidBrush(Color.FromArgb((int)(opacity * 255), accent.R, accent.G, accent.B));
        double iconSize = 13, pad = 9, gap = 6;
        OrbIcons.Draw(g, "newSession", left + pad + iconSize / 2, top + h / 2, iconSize, iconBrush.Color);
        using var lf = new Font(Display, 9f);
        var labelSz = g.MeasureString("new session", lf, PointF.Empty, StringFormat.GenericTypographic);
        g.DrawString("new session", lf, iconBrush, (float)(left + pad + iconSize + gap), (float)(top + h / 2 - labelSz.Height / 2), StringFormat.GenericTypographic);
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
