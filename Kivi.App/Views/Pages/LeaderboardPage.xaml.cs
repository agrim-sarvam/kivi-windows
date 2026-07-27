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
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

public partial class LeaderboardPage : UserControl
{
    public LeaderboardPage()
    {
        InitializeComponent();
        Meta.Text = $"{PageData.LbRange} · {PageData.LbUpdated} · {PageData.LbDaysLeft}";

        var e = PageData.Leaderboard;
        var champ = e.First(x => x.Rank == 1);
        var second = e.First(x => x.Rank == 2);
        var third = e.First(x => x.Rank == 3);

        var q2 = QuietCard(second);
        Grid.SetColumn(q2, 0);
        var c1 = ChampionCard(champ);
        Grid.SetColumn(c1, 2);
        var q3 = QuietCard(third);
        Grid.SetColumn(q3, 4);
        Podium.Children.Add(q2);
        Podium.Children.Add(c1);
        Podium.Children.Add(q3);

        foreach (var entry in e) ListHost.Children.Add(Row(entry));
    }

    private static string Breakdown(PageData.LbEntry e) =>
        $"{PageData.FormatCount(e.Dictate)} dictate · {PageData.FormatCount(e.Edit)} edit · {PageData.FormatCount(e.DictateEdit)} dictate+edit";

    private UIElement ChampionCard(PageData.LbEntry e)
    {
        var card = new Border { Background = (Brush)FindResource("Surface1"), BorderBrush = (Brush)FindResource("Annotation"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8), MinHeight = 204, Padding = new Thickness(20) };
        var sp = new StackPanel();
        var tag = new StackPanel { Orientation = Orientation.Horizontal };
        tag.Children.Add(new TextBlock { Text = "🔥 ", FontSize = 14 });
        tag.Children.Add(new TextBlock { Text = "#1", FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 13, Foreground = (Brush)FindResource("Annotation"), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(tag);
        sp.Children.Add(new TextBlock { Text = e.Name, FontFamily = (FontFamily)FindResource("FontDisplay"), FontSize = 18, Foreground = (Brush)FindResource("InkPrimary"), Margin = new Thickness(0, 10, 0, 0) });
        sp.Children.Add(new TextBlock { Text = PageData.FormatCount(e.RankedWords), FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 40, Foreground = (Brush)FindResource("InkPrimary"), Margin = new Thickness(0, 6, 0, 0) });
        sp.Children.Add(new TextBlock { Text = "total words", FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 12, Foreground = (Brush)FindResource("InkTertiary") });
        sp.Children.Add(new TextBlock { Text = Breakdown(e), FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 11, Foreground = (Brush)FindResource("InkTertiary"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        card.Child = sp;
        return card;
    }

    private UIElement QuietCard(PageData.LbEntry e)
    {
        var card = new Border { Background = (Brush)FindResource("Surface1"), BorderBrush = (Brush)FindResource("Hairline"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), MinHeight = 156, Padding = new Thickness(14), VerticalAlignment = VerticalAlignment.Bottom };
        var sp = new StackPanel();
        sp.Children.Add(Medal(e.Rank));
        sp.Children.Add(new TextBlock { Text = e.Name, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 15, FontWeight = FontWeights.Medium, Foreground = (Brush)FindResource("InkPrimary"), Margin = new Thickness(0, 8, 0, 0) });
        sp.Children.Add(new TextBlock { Text = PageData.FormatCount(e.RankedWords), FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 22, Foreground = (Brush)FindResource("InkPrimary"), Margin = new Thickness(0, 4, 0, 0) });
        sp.Children.Add(new TextBlock { Text = "total words", FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 12, Foreground = (Brush)FindResource("InkTertiary") });
        card.Child = sp;
        return card;
    }

    private UIElement Medal(int rank)
    {
        var color = rank switch { 1 => (Brush)FindResource("RankGold"), 2 => (Brush)FindResource("RankSilver"), 3 => (Brush)FindResource("RankBronze"), _ => (Brush?)null };
        if (color == null)
            return new TextBlock { Text = $"#{rank}", FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 13, Foreground = (Brush)FindResource("InkTertiary") };
        return new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = color, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = rank.ToString(), Foreground = Brushes.White, FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }

    private UIElement Row(PageData.LbEntry e)
    {
        var bd = new Border { Padding = new Thickness(8, 14, 8, 14), Background = e.You ? (Brush)FindResource("AccentWash") : Brushes.Transparent, CornerRadius = new CornerRadius(8), BorderBrush = (Brush)FindResource("Hairline"), BorderThickness = new Thickness(0, 0, 0, 1) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rank = new ContentControl { Content = Medal(e.Rank), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(rank, 0);

        var main = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock { Text = e.Name, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 15, Foreground = (Brush)FindResource("InkPrimary") });
        if (e.You) nameRow.Children.Add(new TextBlock { Text = " you", FontSize = 12, Foreground = (Brush)FindResource("Accent"), VerticalAlignment = VerticalAlignment.Center });
        main.Children.Add(nameRow);
        main.Children.Add(new TextBlock { Text = Breakdown(e), FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 11, Foreground = (Brush)FindResource("InkTertiary"), Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(main, 1);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        words.Children.Add(new TextBlock { Text = PageData.FormatCount(e.RankedWords), FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 18, Foreground = (Brush)FindResource("InkPrimary"), TextAlignment = TextAlignment.Right });
        words.Children.Add(new TextBlock { Text = "total words", FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 11, Foreground = (Brush)FindResource("InkTertiary"), TextAlignment = TextAlignment.Right });
        Grid.SetColumn(words, 2);

        grid.Children.Add(rank);
        grid.Children.Add(main);
        grid.Children.Add(words);
        bd.Child = grid;
        return bd;
    }
}
