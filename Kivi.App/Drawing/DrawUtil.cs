using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Kivi.App.Drawing;

internal static class DrawUtil
{
    public static Color Argb(double a, int r, int g, int b) =>
        Color.FromArgb(Clamp8((int)Math.Round(a * 255)), Clamp8(r), Clamp8(g), Clamp8(b));

    public static Color Rgb(int r, int g, int b) => Color.FromArgb(255, Clamp8(r), Clamp8(g), Clamp8(b));

    public static int Clamp8(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    public static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return Color.FromArgb(255, r, g, b);
    }

    /// A rounded-rectangle path (plain circular corners), clamped radius.
    public static GraphicsPath RoundedRect(double x, double y, double w, double h, double r)
    {
        var p = new GraphicsPath();
        r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2));
        float d = (float)(r * 2);
        var rf = new RectangleF((float)x, (float)y, (float)w, (float)h);
        if (r <= 0.01) { p.AddRectangle(rf); p.CloseFigure(); return p; }
        p.AddArc(rf.X, rf.Y, d, d, 180, 90);
        p.AddArc(rf.Right - d, rf.Y, d, d, 270, 90);
        p.AddArc(rf.Right - d, rf.Bottom - d, d, d, 0, 90);
        p.AddArc(rf.X, rf.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
