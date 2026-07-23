// Kivi.App/Views/MainApp/MemoryPage.xaml.cs
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// UI-only per the design spec: backed by WorkspaceMockData.MemoryEntries (in-memory,
/// resets on restart). See PersonasPage's doc comment for the same real-backend-deferred
/// rationale -- no real correction-learning pipeline or prompt injection exists yet.
/// </summary>
public sealed partial class MemoryPage : Page
{
    public MemoryPage()
    {
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        MemoryPanel.Children.Clear();

        if (WorkspaceMockData.MemoryEntries.Count == 0)
        {
            MemoryPanel.Children.Add(new TextBlock
            {
                Text = "No corrections learned yet.",
                Margin = new Thickness(12, 10, 12, 10),
                Foreground = (Brush)Application.Current.Resources["KiviTextTertiaryBrush"],
                FontSize = 13,
            });
            return;
        }

        var stroke = (Brush)Application.Current.Resources["KiviStrokeBrush"];

        for (int i = 0; i < WorkspaceMockData.MemoryEntries.Count; i++)
        {
            int index = i;
            var entry = WorkspaceMockData.MemoryEntries[i];

            var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(12, 10, 12, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = $"{entry.Original}  →  {entry.Corrected}",
                FontSize = 13.5,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                Foreground = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(text, 0);

            var dateText = new TextBlock
            {
                Text = entry.AddedAt.LocalDateTime.ToString("MMM d"),
                FontSize = 12,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviMonoFontFamily"]),
                Foreground = (Brush)Application.Current.Resources["KiviTextTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(dateText, 1);

            var removeButton = new Button
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
                Content = new TextBlock { Text = "remove", FontSize = 12.5, Foreground = (Brush)Application.Current.Resources["KiviDangerBrush"] },
            };
            removeButton.Click += (_, _) => { WorkspaceMockData.MemoryEntries.RemoveAt(index); Render(); };
            Grid.SetColumn(removeButton, 2);

            row.Children.Add(text);
            row.Children.Add(dateText);
            row.Children.Add(removeButton);
            MemoryPanel.Children.Add(row);

            if (i < WorkspaceMockData.MemoryEntries.Count - 1)
            {
                MemoryPanel.Children.Add(new Rectangle { Height = 1, Fill = stroke });
            }
        }
    }
}
