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
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

public partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        InitializeComponent();
        Rebuild(string.Empty);
    }

    private void Finder_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = string.IsNullOrEmpty(Finder.Text) ? Visibility.Visible : Visibility.Collapsed;
        Rebuild(Finder.Text);
    }

    private void Rebuild(string query)
    {
        ListHost.Children.Clear();
        string q = query.Trim().ToLowerInvariant();
        int total = 0;
        foreach (var group in PageData.History)
        {
            var rows = string.IsNullOrEmpty(q)
                ? group.Rows
                : group.Rows.Where(r => r.Text.ToLowerInvariant().Contains(q) || r.App.Contains(q)).ToArray();
            if (rows.Length == 0) continue;
            total += rows.Length;

            ListHost.Children.Add(BuildDaySep(group.Title, rows.Length));
            foreach (var r in rows) ListHost.Children.Add(BuildRow(r));
        }
        EmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildDaySep(string title, int count)
    {
        var grid = new Grid { Height = 40, Margin = new Thickness(0, 16, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var t = new TextBlock { Text = title, FontFamily = (FontFamily)FindResource("FontBody"), FontWeight = FontWeights.Medium, FontSize = 13, Foreground = (Brush)FindResource("Accent"), VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(t, 0);
        var rule = new Border { Height = 1, Background = MakeAccent(0.46), Margin = new Thickness(8, 0, 8, 3), VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(rule, 1);
        var c = new TextBlock { Text = count.ToString(), FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 13, Foreground = (Brush)FindResource("InkTertiary"), VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(c, 2);
        grid.Children.Add(t);
        grid.Children.Add(rule);
        grid.Children.Add(c);
        return grid;
    }

    private UIElement BuildRow(PageData.HistRow r)
    {
        var btn = new Button { Height = 52, Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var markColor = (Color)ColorConverter.ConvertFromString(r.AppColor);
        var mark = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(markColor), Margin = new Thickness(0, 0, 13, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = r.App.Substring(0, 1), Foreground = Brushes.White, FontFamily = (FontFamily)FindResource("FontBody"), FontWeight = FontWeights.Medium, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Grid.SetColumn(mark, 0);
        var text = new TextBlock { Text = r.Text, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 16, Foreground = (Brush)FindResource("InkPrimary"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);
        var time = new TextBlock { Text = r.Time, FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 13, Foreground = (Brush)FindResource("InkTertiary"), MinWidth = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(time, 2);
        grid.Children.Add(mark);
        grid.Children.Add(text);
        grid.Children.Add(time);

        var bd = new Border { Padding = new Thickness(6, 0, 6, 0), BorderBrush = (Brush)FindResource("Hairline"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        btn.Content = bd;
        btn.Template = TransparentButtonTemplate();
        return btn;
    }

    private static ControlTemplate TransparentButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new System.Windows.FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.Name = "bd";
        var cp = new System.Windows.FrameworkElementFactory(typeof(ContentPresenter));
        bd.AppendChild(cp);
        t.VisualTree = bd;
        var trig = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        t.Triggers.Add(trig);
        return t;
    }

    private Brush MakeAccent(double op)
    {
        var c = (Color)FindResource("AccentColor");
        var b = new SolidColorBrush(c) { Opacity = op };
        b.Freeze();
        return b;
    }
}
