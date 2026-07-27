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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

public partial class MemoryPage : UserControl
{
    private int _limit = PageData.MemoryPageSize;

    public MemoryPage()
    {
        InitializeComponent();
        Rebuild();
    }

    private void Rebuild()
    {
        ListHost.Children.Clear();
        var terms = PageData.Terms;
        int shown = Math.Min(_limit, terms.Length);
        for (int i = 0; i < shown; i++)
            ListHost.Children.Add(BuildRow(terms[i], i == shown - 1));

        if (terms.Length > PageData.MemoryPageSize)
        {
            ShowMore.Visibility = Visibility.Visible;
            var tb = (TextBlock)(ShowMore.Content as TextBlock ?? new TextBlock());
            ShowMore.Content = _limit < terms.Length ? "show more" : "show less";
        }
        else ShowMore.Visibility = Visibility.Collapsed;
    }

    private UIElement BuildRow(PageData.MemTerm t, bool last)
    {
        var outer = new StackPanel();
        var grid = new Grid { MinHeight = 56, Margin = new Thickness(8, 11, 8, 11) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var main = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var termRow = new StackPanel { Orientation = Orientation.Horizontal };
        termRow.Children.Add(new TextBlock
        {
            Text = t.Term, FontFamily = (FontFamily)FindResource("FontBody"), FontWeight = FontWeights.Medium,
            FontSize = 15, Foreground = (Brush)FindResource("InkPrimary"),
        });
        if (t.Imported)
            termRow.Children.Add(new TextBlock
            {
                Text = " ↓", FontSize = 12, Foreground = (Brush)FindResource("InkTertiary"),
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "imported",
            });
        main.Children.Add(termRow);
        if (!string.IsNullOrEmpty(t.Note))
            main.Children.Add(new TextBlock
            {
                Text = t.Note, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 13,
                Foreground = (Brush)FindResource("InkTertiary"), Margin = new Thickness(0, 2, 0, 0),
            });
        grid.Children.Add(main);

        if (!last)
            outer.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Hairline") });
        outer.Children.Add(grid);
        return outer;
    }

    private void ShowMore_Click(object sender, RoutedEventArgs e)
    {
        _limit = _limit < PageData.Terms.Length ? PageData.Terms.Length : PageData.MemoryPageSize;
        Rebuild();
    }
}
