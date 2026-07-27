using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Kivi.Core.Orb;

namespace Kivi.App.Drawing;

/// <summary>
/// Satellites / companions — the CROSS layout around the orb, ported from
/// src/renderer/src/orb/render/Satellites.tsx: hey-kivi (✦) LEFT, open-kivi ⚙ ⇄ cancel ✕ ⇄ copy
/// RIGHT (tri-mode), expand BELOW. Driven by the FlowFrame opacity/scale/shake/tint fields; side
/// bubbles morph 32.5→21.5px as the orb shrinks toward its mini box-corner size (exp).
/// </summary>
internal static class SatellitesRenderer
{
    private const double Gap = 6;
    private const double SideWoken = 32.5, SideSmall = 21.5;
    private const double SideIconWoken = 17.5, SideIconSmall = 14.5;
    private const double ExpandSize = 23;

    private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));

    public static void Draw(Graphics g, FlowFrame f, double centerX, double orbCenterYBase, bool forest)
    {
        var satBg = forest ? DrawUtil.Argb(0.88, 13, 30, 9) : DrawUtil.Argb(0.92, 223, 234, 209);
        var satBd = forest ? DrawUtil.Argb(0.14, 255, 255, 255) : DrawUtil.Argb(0.18, 24, 48, 15);
        var satFg = forest ? DrawUtil.Hex("#EAF0E2") : DrawUtil.Hex("#1B330F");
        var satEdit = forest ? DrawUtil.Hex("#E6C24C") : DrawUtil.Hex("#A27224");
        bool mist = !forest;

        double blend = Clamp01(f.Exp);
        double sideSize = SideWoken + (SideSmall - SideWoken) * blend;
        double sideIcon = SideIconWoken + (SideIconSmall - SideIconWoken) * blend;

        // orb center in canvas space (orb drawn at OrbCenterY + drop + h/2)
        double orbCenterY = orbCenterYBase + f.Drop + f.OrbHeight / 2.0;
        double sideDX = f.OrbWidth / 2 + Gap + sideSize / 2;
        double belowDY = f.OrbHeight / 2 + Gap + ExpandSize / 2;
        double expandY = f.FlipY ? orbCenterY - belowDY : orbCenterY + belowDY;

        bool cancelMode = f.SatCancelInteractive;
        bool manualCopy = f.SatManualCopy;

        // LEFT — hey-kivi sparkles (host-app icon during a take is deferred; sparkles is faithful default)
        double leftOp = (f.SatEditShown || f.SatEditLocked) ? f.SatEditOpacity : 0;
        Bubble(g, centerX - sideDX + f.SatEditShakeX, orbCenterY, sideSize, sideIcon, leftOp, f.SatEditScale,
            "sparkles", satEdit, satBg, satBd, forest, TintGlow(f.SatEditTint));

        // RIGHT — settings ⚙ / cancel ✕ / copy
        string rightIcon = manualCopy ? "copy" : cancelMode ? "cross" : "gear";
        double rightOp = (cancelMode || manualCopy) ? f.SatCancelOpacity : f.SatSettingsOpacity;
        double rightScale = (cancelMode || manualCopy) ? f.SatCancelScale : f.SatSettingsScale;
        var cancelFill = mist ? DrawUtil.Argb(0.95, 216, 95, 30) : DrawUtil.Argb(0.92, 150, 28, 26);
        var rightBg = (cancelMode && !manualCopy) ? cancelFill : satBg;
        var rightIconColor = (cancelMode && !manualCopy) ? Color.White : satFg;
        Bubble(g, centerX + sideDX, orbCenterY, sideSize, sideIcon, rightOp, rightScale,
            rightIcon, rightIconColor, rightBg, satBd, forest, null);

        // BELOW — expand
        double expOp = f.Expanded ? 0 : f.SatExpandOpacity;
        Bubble(g, centerX, expandY, ExpandSize, ExpandSize * 0.62, expOp, f.SatExpandScale,
            "expand", satFg, satBg, satBd, forest, null);
    }

    private static (int r, int g, int b, double a, double radius)? TintGlow(SatTint t)
    {
        if (t.Type == SatTintType.None) return null;
        return ((int)t.R, (int)t.G, (int)t.B, t.GlowAlpha, t.GlowRadius);
    }

    private static void Bubble(Graphics g, double cx, double cy, double size, double iconSize,
        double opacity, double scale, string icon, Color iconColor, Color bg, Color border,
        bool grainDark, (int r, int g, int b, double a, double radius)? glow)
    {
        if (opacity <= 0.02) return;
        var st = g.Save();
        g.TranslateTransform((float)cx, (float)cy);
        g.ScaleTransform((float)scale, (float)scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        double half = size / 2;
        var rect = new RectangleF((float)-half, (float)-half, (float)size, (float)size);

        // shadow (or tint glow)
        if (glow is { } gl)
        {
            using var gb = new SolidBrush(DrawUtil.Argb(gl.a * opacity, gl.r, gl.g, gl.b));
            double grow = gl.radius;
            g.FillEllipse(gb, (float)(-half - grow), (float)(-half - grow), (float)(size + grow * 2), (float)(size + grow * 2));
        }
        else
        {
            using var sh = new SolidBrush(DrawUtil.Argb(0.45 * opacity, 20, 20, 20));
            g.FillEllipse(sh, rect.X, rect.Y + 3, rect.Width, rect.Height);
        }

        // bubble fill + border
        using (var fb = new SolidBrush(Color.FromArgb((int)(opacity * bg.A), bg.R, bg.G, bg.B)))
            g.FillEllipse(fb, rect);
        // paper grain clipped to the circle
        var clip = g.Save();
        using (var cp = new GraphicsPath()) { cp.AddEllipse(rect); g.SetClip(cp); DrawGrainCircle(g, grainDark, rect); g.Restore(clip); }
        using (var pen = new Pen(Color.FromArgb((int)(opacity * border.A), border.R, border.G, border.B), 1f))
            g.DrawEllipse(pen, rect);

        // icon
        var ic = Color.FromArgb((int)(opacity * 255), iconColor.R, iconColor.G, iconColor.B);
        OrbIcons.Draw(g, icon, 0, 0, iconSize, ic);

        g.Restore(st);
    }

    private static void DrawGrainCircle(Graphics g, bool dark, RectangleF rect)
    {
        var tile = PaperGrain.TileBitmap(dark);
        double op = PaperGrain.Opacity(dark);
        using var ia = new System.Drawing.Imaging.ImageAttributes();
        var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)op };
        ia.SetColorMatrix(cm);
        g.DrawImage(tile, new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height),
            0, 0, tile.Width, tile.Height, GraphicsUnit.Pixel, ia);
    }
}
