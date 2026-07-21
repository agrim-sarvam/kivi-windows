// Kivi.App/Views/Onboarding/ConfigPage.xaml.cs
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// Final onboarding screen. Only controls with real backing are shown: hotkey capture
/// (IHotkeyService.SetHotkey), orb accent color swatches, transcription-language chips,
/// launch-at-login (registry Run key) and screen-context toggles. "Done" persists config
/// (sets OnboardingCompleted=true) and signals the host window to close onboarding.
/// </summary>
public sealed partial class ConfigPage : Page
{
    // Swatch choices match the Figma orb-color palette primitives already defined in Tokens.xaml.
    private static readonly (string Hex, string Name)[] AccentChoices =
    {
        ("#41691E", "Leaf"),
        ("#4250D5", "Indigo"),
        ("#E96C2F", "Amber"),
        ("#B81514", "Ember"),
        ("#14180E", "Ink"),
    };

    private static readonly (string Code, string Label)[] LanguageChoices =
    {
        ("auto", "Auto"),
        ("en", "English"),
        ("hi", "Hindi"),
        ("es", "Spanish"),
        ("fr", "French"),
    };

    private OnboardingWindow? _host;
    public ConfigViewModel ViewModel { get; }

    private readonly List<Border> _swatchBorders = new();
    private readonly List<Border> _chipBorders = new();

    public ConfigPage()
    {
        ViewModel = Kivi.App.App.Services.GetRequiredService<ConfigViewModel>();
        InitializeComponent();

        HotkeyBox.SetInitial(ViewModel.HotkeyVk);
        HotkeyBox.HotkeyChanged += vk => ViewModel.HotkeyVk = vk;

        BuildColorSwatches();
        BuildLanguageChips();

        LaunchAtLoginToggle.IsOn = ViewModel.LaunchAtLogin;
        ScreenContextToggle.IsOn = ViewModel.ScreenContextEnabled;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
    }

    private void BuildColorSwatches()
    {
        foreach (var (hex, name) in AccentChoices)
        {
            var color = ColorFromHex(hex);
            var swatch = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = (CornerRadius)Application.Current.Resources["KiviRadiusFull"],
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = hex == ViewModel.OrbAccentColor
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviBrandInkBrush"]
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                Tag = hex,
            };
            ToolTipService.SetToolTip(swatch, name);
            swatch.Tapped += (_, _) =>
            {
                ViewModel.OrbAccentColor = hex;
                HighlightSelectedSwatch(hex);
            };
            _swatchBorders.Add(swatch);
            ColorSwatchPanel.Children.Add(swatch);
        }
    }

    private void HighlightSelectedSwatch(string hex)
    {
        var brandInk = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviBrandInkBrush"];
        var transparent = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        foreach (var swatch in _swatchBorders)
        {
            swatch.BorderBrush = (string)swatch.Tag == hex ? brandInk : transparent;
        }
    }

    private void BuildLanguageChips()
    {
        foreach (var (code, label) in LanguageChoices)
        {
            var chip = new Border
            {
                CornerRadius = (CornerRadius)Application.Current.Resources["KiviRadiusFull"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 8, 14, 8),
                Background = code == ViewModel.TranscriptionLanguage
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviBrandInkBrush"]
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviSurfaceAltBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviStrokeBrush"],
                Tag = code,
            };
            var text = new TextBlock
            {
                Text = label,
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["KiviFontFamily"],
                FontSize = (double)Application.Current.Resources["KiviFontSizeCaption"],
                Foreground = code == ViewModel.TranscriptionLanguage
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviSurfaceBrush"]
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextPrimaryBrush"],
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
        var brandInk = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviBrandInkBrush"];
        var surfaceAlt = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviSurfaceAltBrush"];
        var surface = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviSurfaceBrush"];
        var textPrimary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextPrimaryBrush"];

        foreach (var chip in _chipBorders)
        {
            bool selected = (string)chip.Tag == code;
            chip.Background = selected ? brandInk : surfaceAlt;
            if (chip.Child is TextBlock text)
            {
                text.Foreground = selected ? surface : textPrimary;
            }
        }
    }

    private void OnLaunchAtLoginToggled(object sender, RoutedEventArgs e)
        => ViewModel.LaunchAtLogin = LaunchAtLoginToggle.IsOn;

    private void OnScreenContextToggled(object sender, RoutedEventArgs e)
        => ViewModel.ScreenContextEnabled = ScreenContextToggle.IsOn;

    private void OnDone(object sender, RoutedEventArgs e)
    {
        ViewModel.Persist();
        _host?.RaiseCompleted();
    }

    private static Windows.UI.Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }
}
