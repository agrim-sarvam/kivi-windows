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
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Kivi.App.Controls.Shell;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

public partial class AnalyticsPage : UserControl
{
    public AnalyticsPage()
    {
        InitializeComponent();
        var buckets = PageData.AnalyticsBuckets();

        Body.Children.Add(Scorecard(buckets));
        Body.Children.Add(WordsOverTime(buckets));
        Body.Children.Add(SpeakingPace(buckets));
        Body.Children.Add(AcrossApps());
        Body.Children.Add(MemorySection());
    }

    private UIElement Scorecard(PageData.DayBucket[] b)
    {
        long totalWords = b.Sum(x => (long)x.Words);
        long totalSeconds = b.Sum(x => (long)x.Seconds);
        long dictations = b.Sum(x => (long)x.Captures);
        int wpm = totalSeconds > 0 ? (int)Math.Round(totalWords / (totalSeconds / 60.0)) : 0;

        var card = new Border { Style = (Style)FindResource("RaisedCard"), Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(0, 16, 0, 16) };
        var grid = new Grid();
        for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());

        void Cell(int col, string value, string label, double[] series)
        {
            var sp = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
            sp.Children.Add(new TextBlock { Text = value, FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 24, Foreground = (Brush)FindResource("InkPrimary") });
            sp.Children.Add(new TextBlock { Text = label, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 12, Foreground = (Brush)FindResource("InkTertiary"), Margin = new Thickness(0, 2, 0, 6) });
            sp.Children.Add(new Sparkline { Values = series, Stroke = (Brush)FindResource("Accent"), Width = 120, Height = 20, HorizontalAlignment = HorizontalAlignment.Left });
            Grid.SetColumn(sp, col);
            grid.Children.Add(sp);
        }
        Cell(0, PageData.FormatCount(totalWords), "words", b.Select(x => (double)x.Words).ToArray());
        Cell(1, wpm > 0 ? wpm.ToString() : "—", "words / min", b.Select(x => (double)x.Wpm).ToArray());
        Cell(2, TimeSpoken(totalSeconds), "time spoken", b.Select(x => x.Seconds / 60.0).ToArray());
        Cell(3, PageData.FormatCount(dictations), "dictations", b.Select(x => (double)x.Captures).ToArray());
        card.Child = grid;
        return card;
    }

    private static string TimeSpoken(long seconds)
    {
        int minutes = (int)Math.Round(seconds / 60.0);
        if (minutes >= 60) { int h = minutes / 60, m = minutes % 60; return m == 0 ? $"{h}h" : $"{h}h {m}m"; }
        return $"{minutes}m";
    }

    private UIElement WordsOverTime(PageData.DayBucket[] b)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 28, 0, 0) };
        sp.Children.Add(SectionTitle("words over time"));
        int peak = 0, peakIdx = 0;
        for (int i = 0; i < b.Length; i++) if (b[i].Words > peak) { peak = b[i].Words; peakIdx = i; }
        int max = Math.Max(1, b.Max(x => x.Words));

        var bars = new Grid { Height = 150, Margin = new Thickness(0, 12, 0, 0) };
        var col = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Stretch };
        // even distribution
        var uniform = new UniformGrid { Rows = 1, Columns = b.Length, VerticalAlignment = VerticalAlignment.Bottom, Height = 150 };
        for (int i = 0; i < b.Length; i++)
        {
            double frac = Math.Max(0.02, b[i].Words / (double)max);
            var bar = new Border
            {
                Height = frac * 150, MaxWidth = 22, Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(2, 2, 0, 0),
                Background = i == peakIdx ? (Brush)FindResource("Accent") : MakeAccent(0.32),
                ToolTip = $"{PageData.FormatCount(b[i].Words)} {(b[i].Words == 1 ? "word" : "words")} · {b[i].Label}",
            };
            uniform.Children.Add(bar);
        }
        bars.Children.Add(uniform);
        sp.Children.Add(bars);
        return sp;
    }

    private UIElement SpeakingPace(PageData.DayBucket[] b)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 28, 0, 0) };
        sp.Children.Add(SectionTitle("speaking pace"));
        var pace = b.Where(x => x.Captures > 0 && x.Seconds > 0).Select(x => (double)x.Wpm).ToArray();
        if (pace.Length < 2)
        {
            sp.Children.Add(new TextBlock { Text = "not enough timed dictations yet.", Margin = new Thickness(0, 12, 0, 0), Foreground = (Brush)FindResource("InkTertiary"), FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 14 });
            return sp;
        }
        double w = 300, h = 100;
        var d = SmoothPath(pace, w, h, 4);
        var host = new Viewbox { Height = 110, Margin = new Thickness(0, 12, 0, 0), Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Stretch };
        var canvas = new Canvas { Width = w, Height = h };

        var accent = (Color)FindResource("AccentColor");
        var grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        grad.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0.22 * 255), accent.R, accent.G, accent.B), 0));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1));

        var area = new Path { Data = Geometry.Parse($"{d} L {w} {h} L 0 {h} Z"), Fill = grad };
        var line = new Path { Data = Geometry.Parse(d), Stroke = (Brush)FindResource("Accent"), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        canvas.Children.Add(area);
        canvas.Children.Add(line);
        host.Child = canvas;
        sp.Children.Add(host);
        return sp;
    }

    private static string SmoothPath(double[] values, double w, double h, double pad)
    {
        if (values.Length < 2) return "";
        double max = Math.Max(values.Max(), 1);
        double step = w / (values.Length - 1);
        var pts = values.Select((v, i) => new Point(i * step, h - pad - (v / max) * (h - pad * 2))).ToArray();
        var s = new System.Text.StringBuilder();
        s.Append($"M {pts[0].X:F2} {pts[0].Y:F2}");
        for (int i = 0; i < pts.Length - 1; i++)
        {
            var p0 = i > 0 ? pts[i - 1] : pts[i];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i + 2 < pts.Length ? pts[i + 2] : p2;
            double c1x = p1.X + (p2.X - p0.X) / 6, c1y = p1.Y + (p2.Y - p0.Y) / 6;
            double c2x = p2.X - (p3.X - p1.X) / 6, c2y = p2.Y - (p3.Y - p1.Y) / 6;
            s.Append($" C {c1x:F2} {c1y:F2}, {c2x:F2} {c2y:F2}, {p2.X:F2} {p2.Y:F2}");
        }
        return s.ToString();
    }

    private UIElement AcrossApps()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 28, 0, 0) };
        sp.Children.Add(SectionTitle("across apps"));
        long maxWords = Math.Max(1, PageData.AnalyticsApps.Max(a => a.Words));
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var a in PageData.AnalyticsApps)
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            var name = new TextBlock { Text = a.Name, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 14, Foreground = (Brush)FindResource("InkPrimary"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 0);
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = MakeAccent(0.14), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = (Brush)FindResource("Accent"), HorizontalAlignment = HorizontalAlignment.Left };
            track.Child = fill;
            fill.Loaded += (_, _) => fill.Width = track.ActualWidth * (a.Words / (double)maxWords);
            Grid.SetColumn(track, 1);
            var words = new TextBlock { Text = PageData.FormatCount(a.Words), FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 13, Foreground = (Brush)FindResource("InkTertiary"), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(words, 2);
            grid.Children.Add(name);
            grid.Children.Add(track);
            grid.Children.Add(words);
            list.Children.Add(grid);
        }
        sp.Children.Add(list);
        return sp;
    }

    private UIElement MemorySection()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 28, 0, 0) };
        sp.Children.Add(SectionTitle("memory"));
        var grid = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var s in PageData.MemoryStats)
        {
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = s.Value, FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 17, Foreground = (Brush)FindResource("InkPrimary") });
            cell.Children.Add(new TextBlock { Text = s.Label, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 12, Foreground = (Brush)FindResource("InkTertiary"), Margin = new Thickness(0, 2, 0, 0) });
            grid.Children.Add(cell);
        }
        sp.Children.Add(grid);
        return sp;
    }

    private TextBlock SectionTitle(string t) => new()
    {
        Text = t, FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 11, Foreground = (Brush)FindResource("InkTertiary"),
    };

    private Brush MakeAccent(double op)
    {
        var c = (Color)FindResource("AccentColor");
        var b = new SolidColorBrush(c) { Opacity = op };
        b.Freeze();
        return b;
    }
}
