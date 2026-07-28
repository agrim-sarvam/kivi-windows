using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Kivi.App.Drawing;

/// <summary>
/// Stroke icons — the SVG `d` paths ported verbatim from src/renderer/src/orb/render/Icons.tsx
/// (Orb/Views/Icons.swift): 24×24 design space, stroke 2, round caps/joins, fill none. Each path is
/// parsed into a GDI+ GraphicsPath (a minimal SVG parser supporting M/L/A/Q/h/v/Z absolute+relative).
/// </summary>
internal static class OrbIcons
{
    private static string Star(double cx, double cy, double r)
    {
        double i = r * 0.28;
        return $"M{cx},{cy - r} L{cx + i},{cy - i} L{cx + r},{cy} L{cx + i},{cy + i} " +
               $"L{cx},{cy + r} L{cx - i},{cy + i} L{cx - r},{cy} L{cx - i},{cy - i} Z";
    }

    public static readonly Dictionary<string, string> Paths = new()
    {
        ["pencil"] = "M4,20 L8.2,19 L19.2,8 C20.4,6.6 17.4,3.6 16,4.8 L5,15.8 L4,20 Z",
        ["gear"] =
            "M15.1,12 A3.1,3.1 0 1 1 8.9,12 A3.1,3.1 0 1 1 15.1,12 " +
            "M12,2.4 L12,5.4 M12,18.6 L12,21.6 M21.6,12 L18.6,12 M5.4,12 L2.4,12 " +
            "M18.5,5.5 L16.4,7.6 M7.6,16.4 L5.5,18.5 M18.5,18.5 L16.4,16.4 M7.6,7.6 L5.5,5.5",
        ["expand"] = "M9,4 L4,4 L4,9 M20,9 L20,4 L15,4 M15,20 L20,20 L20,15 M4,15 L4,20 L9,20",
        ["cross"] = "M6,6 L18,18 M18,6 L6,18",
        ["copy"] =
            "M11,9 h7 a2,2 0 0 1 2,2 v7 a2,2 0 0 1 -2,2 h-7 a2,2 0 0 1 -2,-2 v-7 a2,2 0 0 1 2,-2 Z " +
            "M5,15 L5,5 Q5,3 7,3 L17,3",
        ["check"] = "M5,12.5 L10,17.5 L19,6.5",
        ["newSession"] = "M12,5 L12,19 M5,12 L19,12",
        ["maximize"] = "M13.5,10.5 L20,4 M20,9.5 L20,4 L14.5,4 M10.5,13.5 L4,20 M4,14.5 L4,20 L9.5,20",
        ["restore"] = "M20,4 L13.5,10.5 M13.5,5 L13.5,10.5 L19,10.5 M4,20 L10.5,13.5 M10.5,19 L10.5,13.5 L5,13.5",
        ["sparkles"] = Star(9.5, 9.5, 6.0) + " " + Star(18, 18, 3.3),
        ["chevronLeft"] = "M15,6 L9,12 L15,18",
        ["chevronRight"] = "M9,6 L15,12 L9,18",
        ["playback"] =
            "M4.29,14.80 A8.2,8.2 0 1 1 5.72,17.27 " +
            "M3.8,3.8 L3.8,8.6 L8.6,8.6 M12,7.8 L12,12 L14.9,13.9",
        ["thumbUp"] =
            "M4,11 L7,11 L7,20 L4,20 Z " +
            "M7,12 L11,4 Q14,3.4 14,6 L13,11 L18,11 Q20.4,11.4 20,13.4 L18.5,18.6 Q18,20 16.5,20 L7,20",
        ["thumbDown"] =
            "M20,4 L20,13 L17,13 L17,4 Z " +
            "M17,12 L13,20 Q10,20.6 10,18 L11,13 L6,13 Q3.6,12.6 4,10.6 L5.5,5.4 Q6,4 7.5,4 L17,4",
    };

    /// Draw an icon centered at (cx,cy) at the given pixel size (24-space scaled to size).
    public static void Draw(Graphics g, string name, double cx, double cy, double size, Color color, double strokeWidth = 2)
    {
        if (!Paths.TryGetValue(name, out var d)) return;
        var st = g.Save();
        double scale = size / 24.0;
        g.TranslateTransform((float)(cx - size / 2), (float)(cy - size / 2));
        g.ScaleTransform((float)scale, (float)scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, (float)strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        foreach (var sub in ParsePath(d))
            g.DrawPath(pen, sub);
        g.Restore(st);
    }

    // --- minimal SVG path parser → list of subpaths (each an open GraphicsPath) ---
    private static IEnumerable<GraphicsPath> ParsePath(string d)
    {
        var results = new List<GraphicsPath>();
        int i = 0;
        double curX = 0, curY = 0, startX = 0, startY = 0;
        GraphicsPath? path = null;
        char cmd = '\0';

        double ReadNum()
        {
            while (i < d.Length && (d[i] == ' ' || d[i] == ',')) i++;
            int s = i;
            if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
            while (i < d.Length && (char.IsDigit(d[i]) || d[i] == '.' || d[i] == 'e' || d[i] == 'E' ||
                   ((d[i] == '-' || d[i] == '+') && i > s && (d[i - 1] == 'e' || d[i - 1] == 'E')))) i++;
            return double.Parse(d.Substring(s, i - s), CultureInfo.InvariantCulture);
        }
        void Skip() { while (i < d.Length && (d[i] == ' ' || d[i] == ',')) i++; }

        while (i < d.Length)
        {
            Skip();
            if (i >= d.Length) break;
            if (char.IsLetter(d[i])) { cmd = d[i]; i++; }
            switch (cmd)
            {
                case 'M':
                {
                    if (path != null) results.Add(path);
                    path = new GraphicsPath();
                    curX = ReadNum(); curY = ReadNum();
                    startX = curX; startY = curY;
                    cmd = 'L';
                    break;
                }
                case 'L':
                {
                    double nx = ReadNum(), ny = ReadNum();
                    path?.AddLine((float)curX, (float)curY, (float)nx, (float)ny);
                    curX = nx; curY = ny;
                    break;
                }
                case 'h':
                {
                    double nx = curX + ReadNum();
                    path?.AddLine((float)curX, (float)curY, (float)nx, (float)curY);
                    curX = nx;
                    break;
                }
                case 'v':
                {
                    double ny = curY + ReadNum();
                    path?.AddLine((float)curX, (float)curY, (float)curX, (float)ny);
                    curY = ny;
                    break;
                }
                case 'Q':
                {
                    double qx = ReadNum(), qy = ReadNum(), nx = ReadNum(), ny = ReadNum();
                    // quad → cubic bezier
                    double c1x = curX + 2.0 / 3 * (qx - curX), c1y = curY + 2.0 / 3 * (qy - curY);
                    double c2x = nx + 2.0 / 3 * (qx - nx), c2y = ny + 2.0 / 3 * (qy - ny);
                    path?.AddBezier((float)curX, (float)curY, (float)c1x, (float)c1y, (float)c2x, (float)c2y, (float)nx, (float)ny);
                    curX = nx; curY = ny;
                    break;
                }
                case 'C':
                {
                    double c1x = ReadNum(), c1y = ReadNum(), c2x = ReadNum(), c2y = ReadNum(), nx = ReadNum(), ny = ReadNum();
                    path?.AddBezier((float)curX, (float)curY, (float)c1x, (float)c1y, (float)c2x, (float)c2y, (float)nx, (float)ny);
                    curX = nx; curY = ny;
                    break;
                }
                case 'A':
                case 'a':
                {
                    double rx = ReadNum(), ry = ReadNum(); ReadNum(); /*rot*/ ReadNum(); /*large*/ double sweep = ReadNum();
                    double nx = ReadNum(), ny = ReadNum();
                    if (cmd == 'a') { nx += curX; ny += curY; }
                    AddArc(path, curX, curY, rx, ry, sweep >= 0.5, nx, ny);
                    curX = nx; curY = ny;
                    break;
                }
                case 'Z':
                case 'z':
                    path?.CloseFigure();
                    curX = startX; curY = startY;
                    break;
                default:
                    i++; // skip unknown
                    break;
            }
        }
        if (path != null) results.Add(path);
        return results;
    }

    // Approximate an SVG elliptical arc from (curX,curY) to (nx,ny) with radius rx,ry.
    private static void AddArc(GraphicsPath? path, double x0, double y0, double rx, double ry, bool sweep, double x1, double y1)
    {
        if (path == null) return;
        // Center via midpoint + perpendicular offset (assumes rx=ry for these icons).
        double r = rx;
        double mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
        double dx = x1 - x0, dy = y1 - y0;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double h = Math.Sqrt(Math.Max(0, r * r - dist * dist / 4));
        double ux = -dy / (dist == 0 ? 1 : dist), uy = dx / (dist == 0 ? 1 : dist);
        // choose center side by sweep
        double sign = sweep ? 1 : -1;
        double cx = mx + sign * h * ux, cy = my + sign * h * uy;
        double a0 = Math.Atan2(y0 - cy, x0 - cx);
        double a1 = Math.Atan2(y1 - cy, x1 - cx);
        double sweepAng = a1 - a0;
        if (sweep && sweepAng < 0) sweepAng += 2 * Math.PI;
        if (!sweep && sweepAng > 0) sweepAng -= 2 * Math.PI;
        int steps = Math.Max(2, (int)(Math.Abs(sweepAng) / (Math.PI / 16)));
        double px = x0, py = y0;
        for (int s = 1; s <= steps; s++)
        {
            double a = a0 + sweepAng * s / steps;
            double qx = cx + r * Math.Cos(a), qy = cy + r * Math.Sin(a);
            path.AddLine((float)px, (float)py, (float)qx, (float)qy);
            px = qx; py = qy;
        }
    }
}
