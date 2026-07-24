// Kivi.App/Views/MainApp/SettingsPage.xaml.cs
using Kivi.App.ViewModels;
using Kivi.Core.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// The always-available Settings page (sidebar destination), distinct from onboarding's
/// ConfigPage. Shares ConfigViewModel with onboarding, but persists every change immediately
/// (no terminal "Done" step) since Settings is meant to be always-editable.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private static readonly int[] DelayChoicesMs = { 0, 50, 100, 150, 250, 400 };

    private static readonly (string Code, string Label)[] LanguageChoices =
    {
        ("auto", "Auto"),
        ("en", "English"),
        ("hi", "Hindi"),
        ("es", "Spanish"),
        ("fr", "French"),
    };

    private readonly List<Border> _chipBorders = new();

    public ConfigViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = Kivi.App.App.Services.GetRequiredService<ConfigViewModel>();
        InitializeComponent();

        HotkeyBox.SetInitial(ViewModel.HotkeyVk);
        HotkeyBox.HotkeyChanged += vk => ViewModel.HotkeyVk = vk;
        EnglishHotkeyBox.SetInitial(ViewModel.EnglishHotkeyVk);
        EnglishHotkeyBox.HotkeyChanged += vk => ViewModel.EnglishHotkeyVk = vk;

        BuildLanguageChips();
        RenderPressAndHoldDelay();
        PressAndHoldDelayText.Tapped += (_, _) => CyclePressAndHoldDelay();

        LaunchAtLoginToggle.IsOn = ViewModel.LaunchAtLogin;
        ScreenContextToggle.IsOn = ViewModel.ScreenContextEnabled;
        IncognitoToggle.IsOn = ViewModel.IncognitoDictationEnabled;
        SoundOnPasteToggle.IsOn = ViewModel.SoundOnPasteEnabled;
    }

    private void BuildLanguageChips()
    {
        foreach (var (code, label) in LanguageChoices)
        {
            var chip = new Border
            {
                Height = 34,
                CornerRadius = new CornerRadius(17),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 0, 16, 0),
                Background = code == ViewModel.TranscriptionLanguage
                    ? (Brush)Application.Current.Resources["KiviBrandInkBrush"]
                    : (Brush)Application.Current.Resources["KiviSurfaceAltBrush"],
                BorderBrush = (Brush)Application.Current.Resources["KiviStrokeBrush"],
                Tag = code,
            };
            var text = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                FontSize = (double)Application.Current.Resources["KiviFontSizeBody"],
                Foreground = code == ViewModel.TranscriptionLanguage
                    ? (Brush)Application.Current.Resources["KiviSurfaceBrush"]
                    : (Brush)Application.Current.Resources["KiviTextPrimaryBrush"],
            };
            chip.Child = text;
            chip.Tapped += (_, _) =>
            {
                ViewModel.TranscriptionLanguage = code;
                HighlightSelectedChip(code);
            };
            _chipBorders.Add(chip);
            LanguageChipPanel.Children.Add(chip);
        }
    }

    private void HighlightSelectedChip(string code)
    {
        var brandInk = (Brush)Application.Current.Resources["KiviBrandInkBrush"];
        var surfaceAlt = (Brush)Application.Current.Resources["KiviSurfaceAltBrush"];
        var surface = (Brush)Application.Current.Resources["KiviSurfaceBrush"];
        var textPrimary = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"];

        foreach (var chip in _chipBorders)
        {
            bool selected = (string)chip.Tag == code;
            chip.Background = selected ? brandInk : surfaceAlt;
            if (chip.Child is TextBlock text) text.Foreground = selected ? surface : textPrimary;
        }
    }

    private void RenderPressAndHoldDelay() => PressAndHoldDelayText.Text = $"{ViewModel.PressAndHoldDelayMs} ms";

    private void CyclePressAndHoldDelay()
    {
        int currentIndex = Array.IndexOf(DelayChoicesMs, ViewModel.PressAndHoldDelayMs);
        int nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % DelayChoicesMs.Length;
        ViewModel.PressAndHoldDelayMs = DelayChoicesMs[nextIndex];
        RenderPressAndHoldDelay();
    }

    private void OnLaunchAtLoginToggled(object sender, RoutedEventArgs e) => ViewModel.LaunchAtLogin = LaunchAtLoginToggle.IsOn;
    private void OnScreenContextToggled(object sender, RoutedEventArgs e) => ViewModel.ScreenContextEnabled = ScreenContextToggle.IsOn;
    private void OnIncognitoToggled(object sender, RoutedEventArgs e) => ViewModel.IncognitoDictationEnabled = IncognitoToggle.IsOn;
    private void OnSoundOnPasteToggled(object sender, RoutedEventArgs e) => ViewModel.SoundOnPasteEnabled = SoundOnPasteToggle.IsOn;

    private async void OnClearHistory(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear all history?",
            Content = "This can't be undone.",
            PrimaryButtonText = "Clear all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            Kivi.App.App.Services.GetRequiredService<ITranscriptStore>().Clear();
        }
    }
}
