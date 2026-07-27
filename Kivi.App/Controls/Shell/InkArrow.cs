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
using System.Windows;
using System.Windows.Media;

namespace Kivi.App.Controls.Shell;

/// <summary>
/// The hand-drawn 2.1px accent ink arrow — ported VERBATIM from glyphs.tsx InkArrow
/// (viewBox 72x32: dipping shaft + open chevron head). Stroke = Foreground.
/// </summary>
public sealed class InkArrow : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(InkArrow),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WidthPxProperty = DependencyProperty.Register(
        nameof(WidthPx), typeof(double), typeof(InkArrow),
        new FrameworkPropertyMetadata(72.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double WidthPx { get => (double)GetValue(WidthPxProperty); set => SetValue(WidthPxProperty, value); }

    private static readonly Geometry Shaft = Geometry.Parse("M3 15 C20 22 40 22 62 16");
    private static readonly Geometry Head = Geometry.Parse("M54 10 L63 16 L54 22");

    protected override Size MeasureOverride(Size availableSize) => new(WidthPx, WidthPx * 32.0 / 72.0);

    protected override void OnRender(DrawingContext dc)
    {
        double scale = WidthPx / 72.0;
        var pen = new Pen(Foreground, 2.1 / scale)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, pen, Shaft);
        dc.DrawGeometry(null, pen, Head);
        dc.Pop();
    }
}
