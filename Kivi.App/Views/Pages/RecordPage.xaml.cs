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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Kivi.App.Services;
using Kivi.App.Themes;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

public partial class RecordPage : UserControl
{
    private static readonly Regex Marker = new(
        "(bird|words?|morning(?:'s)?|afternoon|evening|home|listening)", RegexOptions.IgnoreCase);
    private static readonly Regex Word = new("[a-z']+", RegexOptions.IgnoreCase);
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

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

        AppServices.History.Changed += OnHistoryChanged;
        Unloaded += (_, _) => AppServices.History.Changed -= OnHistoryChanged;

        RefreshData();
    }

    private void OnHistoryChanged()
    {
        // Add() raises Changed on the dictation thread — marshal back to the UI thread.
        Dispatcher.BeginInvoke(new Action(RefreshData));
    }

    /// <summary>Recomputes every real-data surface (latest take, recent list, today's word count +
    /// app spread) from the shared history store. Greeting is left untouched.</summary>
    private void RefreshData()
    {
        var all = AppServices.History.All();

        // Latest take.
        if (all.Count > 0)
        {
            var latest = all[0];
            LatestText.Text = latest.Text;
            LatestApp.Text = latest.AppName ?? "";
            LatestTime.Text = FormatTime(latest.TimestampUtc);
        }
        else
        {
            LatestText.Text = "your last take shows up here";
            LatestApp.Text = "";
            LatestTime.Text = "";
        }

        // Recent takes list (3 most recent).
        TakesList.Children.Clear();
        var recent = all.Take(3).ToArray();
        for (int i = 0; i < recent.Length; i++)
            TakesList.Children.Add(BuildTakeRow(recent[i].Text, recent[i].AppName ?? "", i > 0));

        // Today (local) rollups: word count + distinct app spread.
        var today = DateTime.Now.Date;
        var todays = all.Where(e => e.TimestampUtc.ToLocalTime().Date == today).ToArray();

        long words = todays.Sum(e => WordCount(e.Text));
        BirdCount.Text = PageData.FormatCount(words);

        var apps = todays
            .Select(e => e.AppName)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!.ToLowerInvariant())
            .Distinct()
            .Take(3)
            .ToArray();

        if (todays.Length == 0)
        {
            TakesSummary.Text = "no takes yet today";
            AppSpread.Text = "no takes yet today";
        }
        else
        {
            string spread = apps.Length > 0 ? "from " + string.Join(", ", apps) : "";
            string takeWord = todays.Length == 1 ? "take" : "takes";
            TakesSummary.Text = apps.Length > 0
                ? $"{todays.Length} {takeWord} today · {spread}"
                : $"{todays.Length} {takeWord} today";
            AppSpread.Text = apps.Length > 0 ? spread : "";
        }
    }

    private static int WordCount(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string FormatTime(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var date = local.Date;
        bool recent = date == DateTime.Now.Date || date == DateTime.Now.Date.AddDays(-1);
        string fmt = recent ? "h:mm tt" : "ddd h:mm tt";
        return local.ToString(fmt, EnUs).ToLowerInvariant();
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

    private UIElement BuildTakeRow(string takeText, string app, bool hasRule)
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
        var text = new TextBlock { Text = takeText, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 15, Foreground = (Brush)FindResource("InkSecondary"), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);
        var appText = new TextBlock { Text = app, FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 12, Foreground = (Brush)FindResource("InkTertiary"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(appText, 2);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        grid.Children.Add(appText);
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
