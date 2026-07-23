// Kivi.App/Views/MainApp/PresetsPage.xaml.cs
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// UI-only per the design spec: backed by WorkspaceMockData.Presets (in-memory, resets on
/// restart). See PersonasPage's doc comment for the same real-backend-deferred rationale.
/// </summary>
public sealed partial class PresetsPage : Page
{
    public PresetsPage()
    {
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        PresetsPanel.Children.Clear();

        if (WorkspaceMockData.Presets.Count == 0)
        {
            PresetsPanel.Children.Add(new TextBlock
            {
                Text = "No presets yet — create one to reuse an instruction across dictations.",
                Foreground = (Brush)Application.Current.Resources["KiviTextTertiaryBrush"],
                FontSize = 13,
            });
            return;
        }

        for (int i = 0; i < WorkspaceMockData.Presets.Count; i++)
        {
            int index = i;
            var preset = WorkspaceMockData.Presets[i];

            var card = new Border { Style = (Style)Application.Current.Resources["KiviCardStyle"], Padding = new Thickness(18) };
            var stack = new StackPanel { Spacing = 8 };
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = preset.Name,
                FontSize = 15,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(nameText, 0);

            var deleteButton = new Button
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
                Content = new TextBlock { Text = "delete", Foreground = (Brush)Application.Current.Resources["KiviDangerBrush"], FontSize = 12.5 },
            };
            deleteButton.Click += (_, _) => { WorkspaceMockData.Presets.RemoveAt(index); Render(); };
            Grid.SetColumn(deleteButton, 1);

            header.Children.Add(nameText);
            header.Children.Add(deleteButton);

            var instructionText = new TextBlock
            {
                Text = preset.Instruction,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontSize = 13,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                Foreground = (Brush)Application.Current.Resources["KiviTextSecondaryBrush"],
            };

            stack.Children.Add(header);
            stack.Children.Add(instructionText);
            card.Child = stack;
            PresetsPanel.Children.Add(card);
        }
    }

    private async void OnNewPreset(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Preset name" };
        var instructionBox = new TextBox { PlaceholderText = "Instruction", AcceptsReturn = true, Height = 80, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(nameBox);
        panel.Children.Add(instructionBox);

        var dialog = new ContentDialog
        {
            Title = "New preset",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            WorkspaceMockData.Presets.Add(new PresetModel { Name = nameBox.Text.Trim(), Instruction = instructionBox.Text.Trim() });
            Render();
        }
    }
}
