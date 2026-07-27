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
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Kivi.App.Themes;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

public partial class RecordPage : UserControl
{
    private static readonly Regex Marker = new(
        "(bird|words?|morning(?:'s)?|afternoon|evening|home|listening)", RegexOptions.IgnoreCase);
    private static readonly Regex Word = new("[a-z']+", RegexOptions.IgnoreCase);

    private string[] _lines = Array.Empty<string>();
    private int _index;

    public bool IsDarkBird => ThemeManager.Instance.IsDark;

    public RecordPage()
    {
        InitializeComponent();

        int hour = DateTime.Now.Hour;
        var key = PageData.GreetingPoolKey(hour);
        _lines = PageData.GreetingPools.TryGetValue(key, out var pool) ? pool : new[] { "ready when you are" };
        _index = new Random().Next(_lines.Length);
        RenderGreeting();

        var latest = PageData.RecentTakes[0];
        LatestText.Text = latest.Text;
        LatestApp.Text = latest.App;
        LatestTime.Text = DateTime.Now.ToString("h:mmtt", CultureInfo.GetCultureInfo("en-US"))
            .ToLowerInvariant().Replace(" ", "");
        TakesSummary.Text = PageData.TakesSummary;
        BirdCount.Text = PageData.FormatCount(PageData.TodayWordCount);

        for (int i = 0; i < PageData.RecentTakes.Length; i++)
            TakesList.Children.Add(BuildTakeRow(PageData.RecentTakes[i], i > 0));
    }

    private void RenderGreeting()
    {
        string line = _lines[((_index % _lines.Length) + _lines.Length) % _lines.Length];
        var m = Marker.Match(line);
        if (!m.Success) m = Word.Match(line);

        Greeting.Inlines.Clear();
        if (m.Success)
        {
            string pre = line.Substring(0, m.Index);
            string word = m.Value;
            string post = line.Substring(m.Index + m.Length);
            if (pre.Length > 0) Greeting.Inlines.Add(new Run(pre));
            // highlight: accent-wash background + accent underline
            var container = new InlineUIContainer(BuildMark(word)) { BaselineAlignment = BaselineAlignment.TextBottom };
            Greeting.Inlines.Add(container);
            if (post.Length > 0) Greeting.Inlines.Add(new Run(post));
        }
        else Greeting.Inlines.Add(new Run(line));
    }

    private FrameworkElement BuildMark(string word)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("AccentWash"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2, 0, 2, 0),
            BorderBrush = (Brush)FindResource("Accent"),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Child = new TextBlock
            {
                Text = word,
                FontFamily = (FontFamily)FindResource("FontSerif"),
                FontSize = 52,
                Foreground = (Brush)FindResource("InkPrimary"),
            },
        };
        return border;
    }

    private UIElement BuildTakeRow(PageData.Take t, bool hasRule)
    {
        var outer = new StackPanel();
        if (hasRule)
            outer.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Hairline"), Margin = new Thickness(8, 0, 8, 0) });
        var grid = new Grid { Margin = new Thickness(8, 11, 8, 11) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Rectangle { Width = 4, Height = 4, Fill = (Brush)FindResource("InkPrimary"), Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(dot, 0);
        var text = new TextBlock { Text = t.Text, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 15, Foreground = (Brush)FindResource("InkSecondary"), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);
        var app = new TextBlock { Text = t.App, FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 12, Foreground = (Brush)FindResource("InkTertiary"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(app, 2);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        grid.Children.Add(app);
        outer.Children.Add(grid);
        return outer;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) { _index++; RenderGreeting(); }

    private void AllTakes_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mw)
            mw.Nav.Navigate(AppSection.History);
    }
}
