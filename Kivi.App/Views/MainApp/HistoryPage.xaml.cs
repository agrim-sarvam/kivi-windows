// Kivi.App/Views/MainApp/HistoryPage.xaml.cs
using System.Linq;
using Kivi.Core.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// Real, persisted transcript history -- reads from ITranscriptStore, no hardcoded sample
/// data. Newest dictation first, matching the mockup's reverse-chronological list.
/// </summary>
public sealed partial class HistoryPage : Page
{
    private readonly IReadOnlyList<TranscriptEntry> _entries;
    private readonly List<Border> _rowBorders = new();
    private readonly List<Button> _rowButtons = new();

    public HistoryPage()
    {
        InitializeComponent();
        var store = Kivi.App.App.Services.GetRequiredService<ITranscriptStore>();
        _entries = store.LoadAll().Reverse().ToList();
        BuildRows();
        if (_entries.Count > 0) Select(0);
        else ShowEmptyState();

        SearchBox.TextChanged += OnSearchTextChanged;
    }

    private void ShowEmptyState()
    {
        DetailText.Text = "No dictations yet — hold Right Ctrl anywhere to start.";
        DetailWordCount.Text = "";
        DetailApp.Text = "";
        DetailTime.Text = "";
    }

    private void BuildRows()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            int index = i;

            var border = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var appCol = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            appCol.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(255, 0xF0, 0x65, 0x3B)) });
            appCol.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(entry.AppName) ? "Unknown" : entry.AppName,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                FontSize = 12.5,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(appCol, 0);

            var textBlock = new TextBlock
            {
                Text = entry.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                FontSize = 13,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(textBlock, 1);

            var timeBlock = new TextBlock
            {
                Text = entry.Timestamp.LocalDateTime.ToString("h:mm tt"),
                HorizontalAlignment = HorizontalAlignment.Right,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(timeBlock, 2);

            grid.Children.Add(appCol);
            grid.Children.Add(textBlock);
            grid.Children.Add(timeBlock);
            border.Child = grid;

            var button = new Button
            {
                Content = border,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) => Select(index);

            _rowBorders.Add(border);
            _rowButtons.Add(button);
            RowsPanel.Children.Add(button);
        }
    }

    private void Select(int index)
    {
        var accent = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviAccentBrush"];
        var warmTint = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviWarmTintBrush"];
        var transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

        for (int i = 0; i < _rowBorders.Count; i++)
        {
            bool selected = i == index;
            _rowBorders[i].BorderBrush = selected ? accent : transparent;
            _rowBorders[i].Background = selected ? warmTint : transparent;
        }

        var entry = _entries[index];
        DetailText.Text = entry.Text;
        DetailWordCount.Text = $"{entry.WordCount} words";
        DetailApp.Text = string.IsNullOrEmpty(entry.AppName) ? "Unknown" : entry.AppName;
        DetailTime.Text = entry.Timestamp.LocalDateTime.ToString("MMM d, h:mm tt");
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? "";
        for (int i = 0; i < _entries.Count; i++)
        {
            bool matches = string.IsNullOrEmpty(query)
                || _entries[i].Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                || _entries[i].AppName.Contains(query, StringComparison.OrdinalIgnoreCase);
            _rowButtons[i].Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
