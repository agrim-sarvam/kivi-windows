// WPF/WinForms type disambiguation (project enables both UseWPF and UseWindowsForms).
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Application = System.Windows.Application;
using Orientation = System.Windows.Controls.Orientation;
using ComboBox = System.Windows.Controls.ComboBox;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using System;
using System.Windows;
using System.Windows.Media;
using Kivi.Core.KiwiMark;

namespace Kivi.App.Controls.Shell;

/// <summary>
/// The Kivi brand mark for the rail header — a dotted kiwi, ported VERBATIM from
/// main-window/KiwiMark.tsx: a fine dot grid over the 120x162 KiwiData silhouette,
/// each dot a per-position volumetric green (deep forest body + belly/neck bloom +
/// bottom under-shadow, leg ramp, one light eye). Dark mode uses a LIFTED palette.
/// Static (no animation).
/// </summary>
public sealed class KiwiMark : FrameworkElement
{
    public static readonly DependencyProperty HeightPxProperty = DependencyProperty.Register(
        nameof(HeightPx), typeof(double), typeof(KiwiMark),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ColsProperty = DependencyProperty.Register(
        nameof(Cols), typeof(int), typeof(KiwiMark),
        new FrameworkPropertyMetadata(20, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DarkProperty = DependencyProperty.Register(
        nameof(Dark), typeof(bool), typeof(KiwiMark),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public double HeightPx { get => (double)GetValue(HeightPxProperty); set => SetValue(HeightPxProperty, value); }
    public int Cols { get => (int)GetValue(ColsProperty); set => SetValue(ColsProperty, value); }
    public bool Dark { get => (bool)GetValue(DarkProperty); set => SetValue(DarkProperty, value); }

    private const double BX0 = 2.0, BY0 = 2.0, BY1 = 159.0, BW = 116.0, BH = 157.0, LEG_BAND = 0.22, GAP = 0.18;

    private int Rows => Math.Max(1, (int)Math.Round(Cols * (double)KiwiData.GH / KiwiData.GW));
    private double WidthPx => HeightPx * ((double)Cols / Rows);

    protected override Size MeasureOverride(Size availableSize) => new(WidthPx, HeightPx);

    private static double[] Mix(double[] a, double[] b, double t) => new[]
    {
        Math.Round(a[0] + (b[0] - a[0]) * t),
        Math.Round(a[1] + (b[1] - a[1]) * t),
        Math.Round(a[2] + (b[2] - a[2]) * t),
    };

    private static double ClampD(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));

    private static double[] BodyColor(double mcx, double mcy, double[] baseC, double[] hi, double[] shadow)
    {
        double nx = (mcx - BX0) / BW, ny = (mcy - BY0) / BH;
        double G(double cx, double cy, double rx, double ry)
        {
            double dx = (nx - cx) / rx, dy = (ny - cy) / ry;
            return Math.Exp(-(dx * dx + dy * dy));
        }
        double belly = G(0.45, 0.56, 0.5, 0.56);
        double neck = G(0.57, 0.2, 0.28, 0.24);
        double hl = Math.Min(1, Math.Max(belly, 0.88 * neck));
        hl = Math.Pow(hl, 0.72);
        double loF = ClampD((ny - 0.6) / 0.4, 0, 1);
        var c = Mix(baseC, hi, hl);
        c = Mix(c, shadow, loF * 0.55);
        return c;
    }

    protected override void OnRender(DrawingContext dc)
    {
        bool dark = Dark;
        int cols = Cols, rows = Rows;
        double cssW = WidthPx, cssH = HeightPx;

        double[] bodyInk = dark ? new double[] { 110, 163, 53 } : new double[] { 22, 33, 14 };
        double[] volumeHi = dark ? new double[] { 196, 230, 154 } : new double[] { 124, 170, 74 };
        double[] volumeLo = dark ? new double[] { 58, 90, 34 } : new double[] { 10, 15, 5 };
        double[] legInk = dark ? new double[] { 110, 163, 53 } : new double[] { 65, 105, 30 };
        double[] eyeInk = dark ? new double[] { 22, 33, 14 } : new double[] { 241, 244, 236 };
        double[] legDark = dark ? new double[] { 46, 72, 28 } : Mix(legInk, new double[] { 0, 0, 0 }, 0.6);

        double cellW = cssW / cols, cellH = cssH / rows;
        double r = (Math.Min(cellW, cellH) / 2) * (1 - GAP);
        double legCut = BY0 + (1 - LEG_BAND) * BH;

        double ex = BX0 + 0.42 * BW, ey = BY0 + 0.095 * BH;
        int ecx = (int)Math.Floor((ex / KiwiData.GW) * cols);
        int ecy = (int)Math.Floor((ey / KiwiData.GH) * rows);

        for (int cy = 0; cy < rows; cy++)
        {
            double centerMy = ((cy + 0.5) / rows) * KiwiData.GH;
            bool inLegs = centerMy > legCut;
            double bar = inLegs ? 0.26 : 0.42;
            for (int cx = 0; cx < cols; cx++)
            {
                int mx0 = (int)Math.Floor((cx / (double)cols) * KiwiData.GW);
                int mx1 = Math.Max(mx0 + 1, (int)Math.Floor(((cx + 1) / (double)cols) * KiwiData.GW));
                int my0 = (int)Math.Floor((cy / (double)rows) * KiwiData.GH);
                int my1 = Math.Max(my0 + 1, (int)Math.Floor(((cy + 1) / (double)rows) * KiwiData.GH));
                int on = 0, tot = 0;
                for (int my = my0; my < my1; my++)
                    for (int mx = mx0; mx < mx1; mx++)
                    {
                        tot++;
                        if (KiwiData.MaskOn(mx, my)) on++;
                    }
                if (tot == 0 || (double)on / tot < bar) continue;

                double centerMx = ((cx + 0.5) / cols) * KiwiData.GW;
                double cxV = (cx + 0.5) * cellW, cyV = (cy + 0.5) * cellH;

                double[] ink;
                double radius = r;
                if (!inLegs && cx == ecx && cy == ecy) { ink = eyeInk; radius = r * 0.82; }
                else if (inLegs)
                {
                    double t = ClampD((centerMy - legCut) / (BY1 - legCut), 0, 1);
                    ink = Mix(legInk, legDark, t);
                }
                else ink = BodyColor(centerMx, centerMy, bodyInk, volumeHi, volumeLo);

                var brush = new SolidColorBrush(Color.FromRgb((byte)ink[0], (byte)ink[1], (byte)ink[2]));
                brush.Freeze();
                dc.DrawEllipse(brush, null, new Point(cxV, cyV), radius, radius);
            }
        }
    }
}
