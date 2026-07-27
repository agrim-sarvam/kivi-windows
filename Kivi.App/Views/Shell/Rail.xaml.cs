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
using Kivi.App.Controls.Shell;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Shell;

public partial class Rail : UserControl
{
    public const double ExpandedWidth = 264;
    public const double CollapsedWidth = 76;

    private AppNavigation? _nav;
    private readonly List<RailRow> _rows = new();

    public Rail()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_nav != null) _nav.PropertyChanged -= NavChanged;
        _nav = DataContext as AppNavigation;
        if (_nav != null)
        {
            _nav.PropertyChanged += NavChanged;
            BuildGroups();
            ApplyCollapse(_nav.RailCollapsed, animate: false);
            UpdateSelection();
        }
    }

    private void NavChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppNavigation.Section)) UpdateSelection();
        else if (e.PropertyName == nameof(AppNavigation.RailCollapsed)) ApplyCollapse(_nav!.RailCollapsed, animate: true);
    }

    private void BuildGroups()
    {
        GroupsHost.Children.Clear();
        _rows.Clear();
        foreach (var group in AppNavigation.Groups)
        {
            var panel = new StackPanel();

            if (group.Id == "capture")
                panel.Children.Add(new Border
                {
                    Height = 1,
                    Background = (Brush)FindResource("Hairline"),
                    Margin = new Thickness(14, 6, 14, 0),
                });

            if (group.Title != null)
            {
                var titleGrid = new Grid { Height = 25, Margin = new Thickness(12, 4, 12, 8), HorizontalAlignment = HorizontalAlignment.Left };
                var sweep = new HighlightSweep
                {
                    Fill = (Brush)FindResource("Accent"),
                    Opacity = 0.32,
                    Width = 34,
                    Height = 7,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                var titleText = new TextBlock
                {
                    Text = group.Title,
                    FontFamily = (FontFamily)FindResource("FontBody"),
                    FontWeight = FontWeights.Medium,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("InkSecondary"),
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                // letter-spacing 0.14em approximation via typography not available; leave default.
                titleGrid.Children.Add(sweep);
                titleGrid.Children.Add(titleText);
                titleGrid.Tag = "collapsible-label";
                panel.Children.Add(titleGrid);
            }

            var items = new StackPanel { Margin = new Thickness(8, 10, 8, 10) };
            foreach (var item in group.Items)
            {
                var row = new RailRow(item, () => _nav?.Navigate(item.Section));
                _rows.Add(row);
                items.Children.Add(row);
            }
            panel.Children.Add(items);
            GroupsHost.Children.Add(panel);
        }
    }

    private void UpdateSelection()
    {
        if (_nav == null) return;
        foreach (var r in _rows) r.SetSelected(r.Section == _nav.Section);
    }

    private void ApplyCollapse(bool collapsed, bool animate)
    {
        double target = collapsed ? CollapsedWidth : ExpandedWidth;
        if (animate && !SystemParametersHelper.ReduceMotion)
        {
            var anim = new DoubleAnimation(target, TimeSpan.FromSeconds(0.24))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            BeginAnimation(WidthProperty, anim);
        }
        else
        {
            BeginAnimation(WidthProperty, null);
            Width = target;
        }

        // Label fade/collapse: wordmark, group titles, rail-item labels, account meta.
        var vis = collapsed ? Visibility.Collapsed : Visibility.Visible;
        Wordmark.Visibility = vis;
        AccountMeta.Visibility = vis;
        BrandStack.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        foreach (var child in GroupsHost.Children)
            if (child is StackPanel sp)
                foreach (var c in sp.Children)
                    if (c is Grid g && (g.Tag as string) == "collapsible-label")
                        g.Visibility = vis;
        foreach (var r in _rows) r.SetCollapsed(collapsed);
    }

    private void Gear_Click(object sender, RoutedEventArgs e) => _nav?.Navigate(AppSection.Settings);
}

/// <summary>A single 38px rail row: 40px icon slot + label, accent-wash hover/selected.</summary>
internal sealed class RailRow : Button
{
    public AppSection Section { get; }
    private readonly RailIcon _icon;
    private readonly TextBlock _label;
    private readonly Border _bg;
    private bool _selected;

    public RailRow(RailItemSpec spec, Action onClick)
    {
        Section = spec.Section;
        Height = 38;
        Cursor = System.Windows.Input.Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Margin = new Thickness(0, 1, 0, 1);
        ToolTip = spec.Label;
        Click += (_, _) => onClick();

        _bg = new Border { CornerRadius = new CornerRadius(8) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40), Name = "IconCol" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _icon = new RailIcon { Icon = spec.Icon, Size = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_icon, 0);
        _label = new TextBlock
        {
            Text = spec.Label,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 16,
        };
        Grid.SetColumn(_label, 1);
        grid.Children.Add(_icon);
        grid.Children.Add(_label);
        _bg.Child = grid;

        Template = WrapTemplate(_bg);
        UpdateColors();
        MouseEnter += (_, _) => UpdateColors();
        MouseLeave += (_, _) => UpdateColors();
    }

    private ControlTemplate WrapTemplate(UIElement content)
    {
        var t = new ControlTemplate(typeof(RailRow));
        var host = new FrameworkElementFactory(typeof(ContentPresenter));
        t.VisualTree = host;
        Content = content;
        return t;
    }

    public void SetSelected(bool sel) { _selected = sel; UpdateColors(); }

    public void SetCollapsed(bool collapsed)
    {
        _label.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateColors()
    {
        Brush iconBrush, labelBrush, bg;
        if (_selected)
        {
            bg = (Brush)FindResource("AccentWash");
            iconBrush = (Brush)FindResource("Accent");
            labelBrush = (Brush)FindResource("Accent");
            _label.FontWeight = FontWeights.Medium;
        }
        else if (IsMouseOver)
        {
            bg = (Brush)FindResource("AccentWash");
            iconBrush = (Brush)FindResource("InkSecondary");
            labelBrush = (Brush)FindResource("InkPrimary");
            _label.FontWeight = FontWeights.Normal;
        }
        else
        {
            bg = Brushes.Transparent;
            iconBrush = (Brush)FindResource("InkTertiary");
            labelBrush = (Brush)FindResource("InkPrimary");
            _label.FontWeight = FontWeights.Normal;
        }
        _bg.Background = bg;
        _icon.Foreground = iconBrush;
        _label.Foreground = labelBrush;
    }
}
