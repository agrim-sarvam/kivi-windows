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
using Image = System.Windows.Controls.Image;
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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kivi.App.Services;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

public partial class HistoryPage : UserControl
{
    // Stable, name-derived accent palette (same accents PageData used for the seed squares), so the
    // same app always gets the same letter-square color across runs.
    private static readonly string[] SquarePalette =
    {
        "#602861", "#B9902E", "#3478F6", "#34C759", "#2C82C9",
    };

    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    public HistoryPage()
    {
        InitializeComponent();
        AppServices.History.Changed += OnHistoryChanged;
        Unloaded += (_, _) => AppServices.History.Changed -= OnHistoryChanged;
        Rebuild(string.Empty);
    }

    private void OnHistoryChanged()
    {
        // Add() raises Changed on the dictation thread — marshal back to the UI thread.
        Dispatcher.BeginInvoke(new Action(() => Rebuild(Finder.Text)));
    }

    private void Finder_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = string.IsNullOrEmpty(Finder.Text) ? Visibility.Visible : Visibility.Collapsed;
        Rebuild(Finder.Text);
    }

    /// <summary>One day-section: a heading + the entries under it, and whether it uses the short
    /// (today/yesterday) time format.</summary>
    private sealed record Section(string Title, List<DictationHistoryEntry> Entries, bool RecentTime);

    private void Rebuild(string query)
    {
        ListHost.Children.Clear();
        string q = query.Trim().ToLowerInvariant();

        var all = AppServices.History.All();
        bool historyEmpty = all.Count == 0;

        var filtered = string.IsNullOrEmpty(q)
            ? all
            : all.Where(e =>
                    (e.Text?.ToLowerInvariant().Contains(q) ?? false) ||
                    (e.AppName?.ToLowerInvariant().Contains(q) ?? false))
                .ToArray();

        var sections = GroupByDay(filtered);
        int total = 0;
        foreach (var section in sections)
        {
            if (section.Entries.Count == 0) continue;
            total += section.Entries.Count;
            ListHost.Children.Add(BuildDaySep(section.Title, section.Entries.Count));
            foreach (var e in section.Entries) ListHost.Children.Add(BuildRow(e, section.RecentTime));
        }

        if (total == 0)
        {
            EmptyText.Text = historyEmpty
                ? "no takes yet — hold right ctrl and speak"
                : "no matching takes";
            EmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Buckets entries (already newest-first) into ordered day sections computed from their LOCAL
    /// timestamp: today, yesterday, "earlier this week" (the prior 7 days), then older entries
    /// grouped by month name (e.g. "july"). Section order follows first appearance, which — since
    /// input is newest-first — is already chronological (newest section first).
    /// </summary>
    private static List<Section> GroupByDay(IReadOnlyList<DictationHistoryEntry> entries)
    {
        var today = DateTime.Now.Date;
        var yesterday = today.AddDays(-1);
        var weekStart = today.AddDays(-7); // "earlier this week" = date > weekStart && date < yesterday

        var order = new List<string>();
        var byKey = new Dictionary<string, Section>();

        foreach (var e in entries)
        {
            var date = e.TimestampUtc.ToLocalTime().Date;

            string key;
            string title;
            bool recent;
            if (date == today) { key = "today"; title = "today"; recent = true; }
            else if (date == yesterday) { key = "yesterday"; title = "yesterday"; recent = true; }
            else if (date > weekStart && date < yesterday) { key = "week"; title = "earlier this week"; recent = false; }
            else
            {
                // Older: group by month (+ year to keep different years distinct in the key).
                key = "m" + date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                title = date.ToString("MMMM", EnUs).ToLowerInvariant();
                recent = false;
            }

            if (!byKey.TryGetValue(key, out var section))
            {
                section = new Section(title, new List<DictationHistoryEntry>(), recent);
                byKey[key] = section;
                order.Add(key);
            }
            section.Entries.Add(e);
        }

        return order.Select(k => byKey[k]).ToList();
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

    private UIElement BuildRow(DictationHistoryEntry entry, bool recentTime)
    {
        var btn = new Button { Height = 52, Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var mark = BuildMark(entry);
        Grid.SetColumn(mark, 0);
        var text = new TextBlock { Text = entry.Text, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 16, Foreground = (Brush)FindResource("InkPrimary"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);
        var time = new TextBlock { Text = FormatTime(entry.TimestampUtc, recentTime), FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 13, Foreground = (Brush)FindResource("InkTertiary"), MinWidth = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(time, 2);
        grid.Children.Add(mark);
        grid.Children.Add(text);
        grid.Children.Add(time);

        var bd = new Border { Padding = new Thickness(6, 0, 6, 0), BorderBrush = (Brush)FindResource("Hairline"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        btn.Content = bd;
        btn.Template = TransparentButtonTemplate();
        return btn;
    }

    /// <summary>
    /// The 20x20 app mark. Prefer the real Windows app icon (extracted from the exe path), clipped
    /// to the same 5px rounded square. If no icon is available (no exe / extraction failed), fall
    /// back to the colored letter-square keyed to the app name.
    /// </summary>
    private FrameworkElement BuildMark(DictationHistoryEntry entry)
    {
        var icon = AppServices.Icons.Resolve(entry.ExePath);
        if (icon != null)
        {
            var img = new Image
            {
                Source = icon,
                Width = 20,
                Height = 20,
                Stretch = Stretch.UniformToFill,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 13, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = img,
            };
        }

        string app = string.IsNullOrWhiteSpace(entry.AppName) ? "?" : entry.AppName!;
        var markColor = (Color)ColorConverter.ConvertFromString(ColorFor(app));
        return new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(markColor),
            Margin = new Thickness(0, 0, 13, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = app.Substring(0, 1).ToLowerInvariant(), Foreground = Brushes.White, FontFamily = (FontFamily)FindResource("FontBody"), FontWeight = FontWeights.Medium, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }

    /// <summary>Local-time formatting: "2:14 pm" for today/yesterday, "mon 4:05 pm" for older.</summary>
    private static string FormatTime(DateTime utc, bool recentTime)
    {
        var local = utc.ToLocalTime();
        string fmt = recentTime ? "h:mm tt" : "ddd h:mm tt";
        return local.ToString(fmt, EnUs).ToLowerInvariant();
    }

    /// <summary>Deterministic, stable (cross-run) app-name → palette color, so an app's square is
    /// always the same hue. Uses an FNV-1a hash (string.GetHashCode is not stable in .NET Core).</summary>
    private static string ColorFor(string app)
    {
        uint hash = 2166136261;
        foreach (char ch in app.ToLowerInvariant())
        {
            hash ^= ch;
            hash *= 16777619;
        }
        return SquarePalette[hash % (uint)SquarePalette.Length];
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
