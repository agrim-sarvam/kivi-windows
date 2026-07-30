using System;
using System.Drawing;
using System.Drawing.Text;
using System.Text;
using Kivi.Core.Orb;

namespace Kivi.App.Drawing;

/// <summary>
/// Content-driven box sizing — the .NET port of
/// src/renderer/src/orb/render/boxContentFit.ts (itself the web analog of the Swift BoxContentFit).
///
/// The transcript box does NOT have a fixed height: it grows to fit the measured transcript text.
/// In the Electron app a hidden DOM node is measured and handed to <c>engine.fitBoxToContent(w,h)</c>,
/// which clamps + eases the grow. We have no DOM, so we measure the SAME reading font the box
/// renderer draws with (GDI+ MeasureString) at the SAME wrap width the renderer wraps at, and produce
/// the same (w, h) request. Without this the box stayed frozen at BOX_DEFAULT (108 px) and long
/// transcripts spilled past the bottom edge into the footer (the "text going out of the box" bug).
///
/// Chrome constants below MUST mirror <see cref="TranscriptBoxRenderer"/> so the requested height
/// exactly contains header + text + footer as WE draw them (this intentionally diverges from the
/// Electron DOM chrome numbers, which describe THEIR layout — RULE 2 is a pixel-match of the visual
/// RESULT, and the result is "text fits with breathing room").
/// </summary>
internal static class BoxContentFit
{
    private const string Reading = "Segoe UI"; // Matter fallback — must match TranscriptBoxRenderer.Reading

    // --- our renderer's vertical chrome (mirror TranscriptBoxRenderer) ---
    private const double WedgeH = 9;
    private const double ContentTopPad = 10;      // contentTop = WedgeH + 10
    private const double HeaderToTextGap = 34;     // textTop  = contentTop + 34
    private const double FooterH = 48;             // TranscriptBoxRenderer.FooterH (footer strip w/ padding)
    private const double TextToFooterGap = 4;      // small clearance; the footer strip carries its own top padding
    private const double PadL = 16, PadR = 16;     // horizontal insets

    // TextTop from the box's top edge, and the fixed vertical chrome added around the measured text.
    private const double TextTop = WedgeH + ContentTopPad + HeaderToTextGap;   // 53
    private const double ChromeV = TextTop + TextToFooterGap + FooterH;         // 105

    // Candidate widths + the widen threshold (mirror boxContentFit.ts: 322 → 460 when tall).
    private const double DefaultW = 322;
    private const double WideW = 460;
    private const double WidenThresholdH = 150;
    private const double InterLineGap = 6; // TranscriptBoxRenderer's `y += 6` between lines

    // A tiny offscreen Graphics for measuring (GDI+ MeasureString needs one; no window required).
    [ThreadStatic] private static Graphics? _measurer;
    private static Graphics Measurer()
    {
        if (_measurer == null)
        {
            var bmp = new Bitmap(1, 1);
            _measurer = Graphics.FromImage(bmp);
            _measurer.TextRenderingHint = TextRenderingHint.AntiAlias;
        }
        return _measurer;
    }

    /// <summary>A cheap change-key: only when this differs from last frame do we remeasure + refit
    /// (mirrors OrbApp.tsx's fitKey-gated useEffect — no per-frame measuring/feedback churn).</summary>
    public static string FitKey(FlowFrame f)
    {
        var sb = new StringBuilder(64);
        sb.Append(DisplayText(f));
        sb.Append('|').Append((int)f.TxStage);
        sb.Append('|').Append(f.BoxMaxi ? 'M' : 'm');
        sb.Append('|').Append(f.Expanded ? 'e' : 'c');
        sb.Append('|').Append(f.TxNotice ?? "");
        sb.Append('|').Append(f.TxBanner ?? "");
        return sb.ToString();
    }

    /// <summary>The (width, height) to pass to <see cref="FlowEngine.FitBoxToContent"/>.</summary>
    public static (double W, double H) Request(FlowFrame f)
    {
        string text = DisplayText(f);
        if (text.Trim().Length == 0)
            return (DefaultW, Kivi.Core.Orb.Constants.BOX_DEFAULT.H); // empty → stay at default height

        var g = Measurer();
        float size = f.BoxMaxi ? 12f : 10.5f; // 16 px / 14 px — matches renderer body font

        double narrowH = MeasureText(g, text, DefaultW - PadL - PadR, size);
        if (narrowH <= WidenThresholdH)
            return (DefaultW, narrowH + ChromeV);

        double wideH = MeasureText(g, text, WideW - PadL - PadR, size);
        return (WideW, wideH + ChromeV);
    }

    // Sums the wrapped height of each transcript line at the given content width, adding the same
    // inter-line gap the renderer inserts — so the request matches what TranscriptBoxRenderer draws.
    private static double MeasureText(Graphics g, string text, double contentWidth, float size)
    {
        using var body = new Font(Reading, size);
        double total = 0;
        var paras = text.Split('\n');
        int drawn = 0;
        foreach (var para in paras)
        {
            if (para.Length == 0) continue;
            var sz = g.MeasureString(para, body, (int)Math.Max(1, contentWidth));
            total += sz.Height;
            drawn++;
        }
        if (drawn > 1) total += InterLineGap * (drawn - 1);
        return Math.Ceiling(total) + 4;
    }

    // The visible transcript text, joined by newline — mirrors boxContentFit.ts displayText().
    private static string DisplayText(FlowFrame f)
    {
        if (f.TxEditable && !string.IsNullOrEmpty(f.TxEditorSeed))
            return f.TxEditorSeed;

        var sb = new StringBuilder();
        foreach (var line in f.TxLines)
        {
            string s;
            if (line.Role == TxLineRole.Tokens && string.IsNullOrEmpty(line.Text) && line.Tokens != null)
            {
                var tb = new StringBuilder();
                foreach (var t in line.Tokens) tb.Append(t.Text);
                s = tb.ToString();
            }
            else s = line.Text ?? "";

            if (s.Length == 0) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(s);
        }
        return sb.ToString();
    }
}
