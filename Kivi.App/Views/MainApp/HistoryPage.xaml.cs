// Kivi.App/Views/MainApp/HistoryPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// Sample transcript rows for this visual-only pass -- no transcript-history storage exists
/// yet, so this is hardcoded, in-memory demo content, not real data.
/// </summary>
public sealed partial class HistoryPage : Page
{
    private sealed record Row(string Text, string Time, int WordCount);

    private static readonly Row[] SampleRows =
    {
        new("ask any doubts you have before starting", "3:55 pm", 7),
        new("Let's get it out here before starting", "3:55 pm", 8),
        new("Can you give me the questions which you have listed, which is the slightly fuller version, in a .txt format and just the question…", "2:14 pm", 24),
        new("Are you saying that only 3 out of 9 yeses make a company you are with for a second meeting?", "1:03 pm", 19),
        new("A bit more.", "1:02 pm", 3),
        new("Betcore.", "1:02 pm", 1),
        new("This is good, but I cannot ask a lot of questions in the first meet itself. So just make this concise and just give me a set of ques…", "1:02 pm", 26),
        new(", for majorly Edge but a basic reroute logic that it fits some other team or something like that", "12:59 pm", 18),
    };

    private readonly List<Border> _rowBorders = new();

    public HistoryPage()
    {
        InitializeComponent();
        BuildRows();
        Select(0);
    }

    private void BuildRows()
    {
        for (int i = 0; i < SampleRows.Length; i++)
        {
            var row = SampleRows[i];
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
                Text = "Brave",
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                FontSize = 12.5,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(appCol, 0);

            var textBlock = new TextBlock
            {
                Text = row.Text,
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
                Text = row.Time,
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

        var row = SampleRows[index];
        DetailText.Text = row.Text;
        DetailWordCount.Text = $"{row.WordCount} words";
        DetailApp.Text = "Brave Browser";
        DetailTime.Text = "19 hr ago";
    }
}
