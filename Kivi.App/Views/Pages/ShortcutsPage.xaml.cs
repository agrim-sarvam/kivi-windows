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

public partial class ShortcutsPage : UserControl
{
    public ShortcutsPage()
    {
        InitializeComponent();
        var sc = PageData.Shortcuts;
        for (int i = 0; i < sc.Length; i++)
            ListHost.Children.Add(BuildRow(sc[i], i == sc.Length - 1));
    }

    private UIElement BuildRow(PageData.Shortcut s, bool last)
    {
        var outer = new StackPanel();
        var grid = new Grid { Margin = new Thickness(8, 16, 8, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var trig = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        trig.Children.Add(new TextBlock
        {
            Text = $"“{s.Trigger}”", FontFamily = (FontFamily)FindResource("FontSerif"),
            FontStyle = FontStyles.Italic, FontSize = 16, Foreground = (Brush)FindResource("InkPrimary"),
            TextWrapping = TextWrapping.Wrap,
        });
        if (s.Imported)
            trig.Children.Add(new TextBlock { Text = " ↓", FontSize = 12, Foreground = (Brush)FindResource("InkTertiary"), ToolTip = "imported" });
        Grid.SetColumn(trig, 0);

        var repl = new TextBlock
        {
            Text = s.Replacement, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 14,
            LineHeight = 21, Foreground = (Brush)FindResource("InkSecondary"), TextWrapping = TextWrapping.Wrap,
            MaxHeight = 66, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(18, 0, 0, 0),
        };
        Grid.SetColumn(repl, 1);
        grid.Children.Add(trig);
        grid.Children.Add(repl);

        if (!last)
            outer.Children.Add(new Border { Height = 1, Background = MakeHairline(0.55) });
        outer.Children.Add(grid);
        return outer;
    }

    private Brush MakeHairline(double op)
    {
        var c = (Color)FindResource("HairlineColor");
        var b = new SolidColorBrush(c) { Opacity = op };
        b.Freeze();
        return b;
    }
}
