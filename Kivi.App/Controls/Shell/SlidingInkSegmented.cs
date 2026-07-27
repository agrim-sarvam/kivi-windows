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
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Kivi.App.Controls.Shell;

public sealed class SegmentedOption
{
    public string Value { get; init; } = "";
    public string Label { get; init; } = "";
}

/// <summary>
/// SlidingInkSegmented / Segmented (pages.css .mw-seg): a row of tab buttons with a 2px
/// accent underline that SLIDES to the selected option in <=200ms with the Canon EaseOut.
/// </summary>
public sealed class SlidingInkSegmented : Control
{
    private StackPanel? _panel;
    private Border? _underline;
    private readonly List<Button> _buttons = new();

    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(IReadOnlyList<SegmentedOption>), typeof(SlidingInkSegmented),
        new PropertyMetadata(null, (d, _) => ((SlidingInkSegmented)d).Rebuild()));

    public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
        nameof(SelectedValue), typeof(string), typeof(SlidingInkSegmented),
        new PropertyMetadata(null, (d, _) => ((SlidingInkSegmented)d).UpdateSelection()));

    public IReadOnlyList<SegmentedOption>? Options
    {
        get => (IReadOnlyList<SegmentedOption>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }
    public string? SelectedValue
    {
        get => (string?)GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public event EventHandler<string>? SelectionChanged;

    static SlidingInkSegmented()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SlidingInkSegmented),
            new FrameworkPropertyMetadata(typeof(SlidingInkSegmented)));
    }

    public SlidingInkSegmented()
    {
        Template = BuildTemplate();
        Loaded += (_, _) => { ApplyTemplate(); Rebuild(); };
    }

    private static ControlTemplate BuildTemplate()
    {
        var t = new ControlTemplate(typeof(SlidingInkSegmented));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var sp = new FrameworkElementFactory(typeof(StackPanel));
        sp.Name = "PART_Panel";
        sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var underline = new FrameworkElementFactory(typeof(Border));
        underline.Name = "PART_Underline";
        underline.SetValue(Border.HeightProperty, 2.0);
        underline.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        underline.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        underline.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
        underline.SetResourceReference(Border.BackgroundProperty, "Accent");
        underline.SetValue(Border.RenderTransformProperty, new TranslateTransform());
        grid.AppendChild(sp);
        grid.AppendChild(underline);
        t.VisualTree = grid;
        return t;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _panel = GetTemplateChild("PART_Panel") as StackPanel;
        _underline = GetTemplateChild("PART_Underline") as Border;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_panel == null) return;
        _panel.Children.Clear();
        _buttons.Clear();
        if (Options == null) return;

        foreach (var opt in Options)
        {
            var btn = new Button
            {
                Content = new TextBlock
                {
                    Text = opt.Label,
                    FontSize = 13,
                    FontFamily = (FontFamily)TryFindResource("FontBody"),
                },
                Tag = opt.Value,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 18, 0),
                Padding = new Thickness(0, 4, 0, 8),
                Template = TabButtonTemplate(),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            btn.Click += (_, _) =>
            {
                var v = (string)btn.Tag;
                if (v != SelectedValue) { SelectedValue = v; SelectionChanged?.Invoke(this, v); }
            };
            _buttons.Add(btn);
            _panel.Children.Add(btn);
        }
        Dispatcher.BeginInvoke(new Action(UpdateSelection), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static ControlTemplate TabButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.MarginProperty, new Thickness(0));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetValue(Border.PaddingProperty, new Thickness(0, 4, 0, 8));
        bd.AppendChild(cp);
        t.VisualTree = bd;
        return t;
    }

    private void UpdateSelection()
    {
        if (_panel == null || _underline == null || _buttons.Count == 0) return;
        int idx = -1;
        for (int i = 0; i < _buttons.Count; i++)
        {
            bool sel = (string)_buttons[i].Tag == SelectedValue;
            if (_buttons[i].Content is TextBlock tb)
            {
                tb.Foreground = (Brush)(sel
                    ? TryFindResource("Accent") : TryFindResource("InkTertiary"));
                tb.FontWeight = sel ? FontWeights.Medium : FontWeights.Normal;
            }
            if (sel) idx = i;
        }
        if (idx < 0) return;
        var target = _buttons[idx];
        target.UpdateLayout();
        double x = target.TranslatePoint(new Point(0, 0), _panel).X;
        double w = target.ActualWidth;
        _underline.Width = w;
        var tt = (TranslateTransform)_underline.RenderTransform;
        var anim = new DoubleAnimation(x, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        tt.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
