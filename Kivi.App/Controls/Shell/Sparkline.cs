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
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Kivi.App.Controls.Shell;

/// <summary>Hand-drawn sparkline — ported from analytics.ts sparkline() (last <=12 values,
/// polyline + end dot). Fixed 120x20 design box scaled to the element.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double[]? Values { get => (double[]?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    private const double W = 120, H = 20;

    protected override Size MeasureOverride(Size availableSize) => new(W, H);

    protected override void OnRender(DrawingContext dc)
    {
        if (Values == null || Values.Length == 0) return;
        var pts = Values.Length > 12 ? Values.Skip(Values.Length - 12).ToArray() : Values;
        double max = pts.Max();
        if (max <= 0) return;
        double min = pts.Min();
        double span = Math.Max(max - min, 1);
        double step = pts.Length > 1 ? W / (pts.Length - 1) : 0;

        Point At(int i, double v)
        {
            double x = pts.Length > 1 ? i * step : W;
            double norm = (v - min) / span;
            double y = H - norm * (H - 2) - 1;
            return new Point(x, y);
        }

        var pen = new Pen(Stroke, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();

        if (pts.Length > 1)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(At(0, pts[0]), false, false);
                for (int i = 1; i < pts.Length; i++) ctx.LineTo(At(i, pts[i]), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
        var dot = At(pts.Length - 1, pts[^1]);
        dc.DrawEllipse(Stroke, null, dot, 2.2, 2.2);
    }
}
