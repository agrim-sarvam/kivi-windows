// Kivi.App/Views/MainApp/AnalyticsPage.xaml.cs
using System.Linq;
using Kivi.Core.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// Real analytics derived from ITranscriptStore -- no mock data. Words/min and time-spoken
/// use a fixed 150 wpm average speaking rate to estimate spoken duration, since
/// TranscriptEntry doesn't record actual audio duration (out of scope for this pass).
/// </summary>
public sealed partial class AnalyticsPage : Page
{
    public AnalyticsPage()
    {
        InitializeComponent();
        var store = Kivi.App.App.Services.GetRequiredService<ITranscriptStore>();
        Render(store.LoadAll());
    }

    private void Render(IReadOnlyList<TranscriptEntry> entries)
    {
        int totalWords = entries.Sum(e => e.WordCount);
        TotalWordsText.Text = totalWords.ToString("N0");
        DictationCountText.Text = entries.Count.ToString("N0");

        double estimatedMinutes = totalWords / 150.0;
        WordsPerMinText.Text = estimatedMinutes > 0 ? Math.Round(totalWords / estimatedMinutes).ToString("N0") : "0";
        TimeSpokenText.Text = FormatDuration(TimeSpan.FromMinutes(estimatedMinutes));

        RenderWordsOverTime(entries);
        RenderTopApps(entries);
        RenderDictationType(entries);
    }

    private static string FormatDuration(TimeSpan span)
        => span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m";

    private void RenderWordsOverTime(IReadOnlyList<TranscriptEntry> entries)
    {
        var accent = (Brush)Application.Current.Resources["KiviAccentBrush"];
        var textTertiary = (Brush)Application.Current.Resources["KiviTextTertiaryBrush"];

        var byDay = entries
            .GroupBy(e => e.Timestamp.LocalDateTime.Date)
            .OrderBy(g => g.Key)
            .TakeLast(14)
            .Select(g => (Day: g.Key, Words: g.Sum(e => e.WordCount)))
            .ToList();

        if (byDay.Count == 0)
        {
            WordsOverTimeEmptyText.Visibility = Visibility.Visible;
            return;
        }

        int max = byDay.Max(d => d.Words);
        if (max == 0) max = 1;

        foreach (var (day, words) in byDay)
        {
            var column = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom, Width = 24 };
            var bar = new Border
            {
                Width = 18,
                Height = Math.Max(4, 96.0 * words / max),
                CornerRadius = new CornerRadius(4),
                Background = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            ToolTipService.SetToolTip(bar, $"{day:MMM d}: {words} words");

            var label = new TextBlock
            {
                Text = day.ToString("d"),
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviMonoFontFamily"]),
                FontSize = 9.5,
                Foreground = textTertiary,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            column.Children.Add(bar);
            column.Children.Add(label);
            WordsOverTimePanel.Children.Add(column);
        }
    }

    private void RenderTopApps(IReadOnlyList<TranscriptEntry> entries)
    {
        var textPrimary = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"];
        var textSecondary = (Brush)Application.Current.Resources["KiviTextSecondaryBrush"];
        var accent = (Brush)Application.Current.Resources["KiviAccentBrush"];
        var stroke = (Brush)Application.Current.Resources["KiviStrokeBrush"];

        var byApp = entries
            .Where(e => !string.IsNullOrEmpty(e.AppName))
            .GroupBy(e => e.AppName)
            .Select(g => (App: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        if (byApp.Count == 0)
        {
            TopAppsEmptyText.Visibility = Visibility.Visible;
            return;
        }

        int max = byApp.Max(a => a.Count);

        foreach (var (app, count) in byApp)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock { Text = app, Foreground = textSecondary, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 0);

            var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = stroke, VerticalAlignment = VerticalAlignment.Center };
            track.Child = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(4, 220.0 * count / max),
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = accent,
            };
            Grid.SetColumn(track, 1);

            var countText = new TextBlock { Text = count.ToString("N0"), Foreground = textPrimary, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(countText, 2);

            row.Children.Add(label);
            row.Children.Add(track);
            row.Children.Add(countText);
            TopAppsPanel.Children.Add(row);
        }
    }

    private void RenderDictationType(IReadOnlyList<TranscriptEntry> entries)
    {
        var textPrimary = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"];
        var textSecondary = (Brush)Application.Current.Resources["KiviTextSecondaryBrush"];
        var accent = (Brush)Application.Current.Resources["KiviAccentBrush"];

        int dictations = entries.Count(e => !e.WasRewrite);
        int rewrites = entries.Count(e => e.WasRewrite);
        int total = Math.Max(1, dictations + rewrites);

        void AddRow(string label, int count)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelText = new TextBlock { Text = label, Foreground = textSecondary, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(labelText, 0);

            var countText = new TextBlock { Text = count.ToString("N0"), Foreground = textPrimary, FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(countText, 1);

            row.Children.Add(labelText);
            row.Children.Add(countText);
            DictationTypePanel.Children.Add(row);
        }

        AddRow("dictations", dictations);
        AddRow("hey kivi rewrites", rewrites);

        var barTrack = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = (Brush)Application.Current.Resources["KiviStrokeBrush"] };
        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(dictations, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(rewrites, 0.0001), GridUnitType.Star) });
        var dictationsFill = new Border { CornerRadius = new CornerRadius(4, 0, 0, 4), Background = accent };
        var rewritesFill = new Border { CornerRadius = new CornerRadius(0, 4, 4, 0), Background = (Brush)Application.Current.Resources["KiviTextTertiaryBrush"] };
        Grid.SetColumn(dictationsFill, 0);
        Grid.SetColumn(rewritesFill, 1);
        barGrid.Children.Add(dictationsFill);
        barGrid.Children.Add(rewritesFill);
        barTrack.Child = barGrid;
        DictationTypePanel.Children.Add(barTrack);
        _ = total;
    }
}
