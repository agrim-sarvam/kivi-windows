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
using System.Windows.Controls;
using System.Windows.Media;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

public partial class SharedTermsPage : UserControl
{
    public SharedTermsPage()
    {
        InitializeComponent();
        var terms = PageData.SharedTerms;
        CountText.Text = $"{terms.Length} {(terms.Length == 1 ? "term" : "terms")}";
        for (int i = 0; i < terms.Length; i++)
            ListHost.Children.Add(BuildRow(terms[i], i == 0));
    }

    private UIElement BuildRow(PageData.SharedTerm t, bool first)
    {
        var grid = new Grid { Margin = new Thickness(8, 15, 8, 15) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var term = new TextBlock
        {
            Text = t.Term, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 15,
            Foreground = (Brush)FindResource("InkPrimary"), VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(term, 0);
        var def = new TextBlock
        {
            Text = t.Definition, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 14,
            Foreground = (Brush)FindResource("InkSecondary"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 0, 16, 0),
        };
        Grid.SetColumn(def, 1);

        var by = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var mark = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
            Background = (Brush)FindResource("AccentWash"), Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock
            {
                Text = t.AddedBy.Substring(0, 1), FontSize = 10,
                Foreground = (Brush)FindResource("Accent"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        by.Children.Add(mark);
        by.Children.Add(new TextBlock
        {
            Text = t.AddedBy, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 13,
            Foreground = (Brush)FindResource("InkTertiary"), VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(by, 2);

        grid.Children.Add(term);
        grid.Children.Add(def);
        grid.Children.Add(by);

        var outer = new StackPanel();
        if (!first)
            outer.Children.Add(new Border { Height = 1, Background = MakeHairline55() });
        outer.Children.Add(grid);
        return outer;
    }

    private Brush MakeHairline55()
    {
        var c = (Color)FindResource("HairlineColor");
        var b = new SolidColorBrush(c) { Opacity = 0.55 };
        b.Freeze();
        return b;
    }
}
