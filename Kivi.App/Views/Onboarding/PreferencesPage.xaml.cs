// Kivi.App/Views/Onboarding/PreferencesPage.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// Second onboarding screen: language preference (reuses AppConfig.TranscriptionLanguage,
/// the same field ConfigPage exposes later in Settings) and "primary use case", stored for
/// display/analytics only -- never wired into the polish prompt.
/// </summary>
public sealed partial class PreferencesPage : Page
{
    // Codes must be real Sarvam language_code values (BCP-47, e.g. "hi-IN"/"en-IN") -- Sarvam's
    // speech-to-text rejects/mishandles anything else. "Auto" sends no language_code at all
    // (Sarvam auto-detects), which combined with mode=codemix is the right default for mixed
    // Hindi/English speech -- there's no separate "Hinglish" code, codemix mode IS the Hinglish
    // behavior (transcribes English words in English, Hindi words in Devanagari, same utterance).
    private static readonly (string Code, string Label)[] LanguageChoices =
    {
        ("auto", "Auto (Hinglish-friendly)"),
        ("en-IN", "English"),
        ("hi-IN", "Hindi"),
    };

    private static readonly (string Code, string Label)[] UseCaseChoices =
    {
        ("Emails", "Emails"),
        ("Messaging", "Messaging"),
        ("Notes", "Notes"),
        ("Code", "Code / Technical"),
        ("Social", "Social"),
        ("Other", "Other"),
    };

    private OnboardingWindow? _host;
    private Kivi.Core.Config.AppConfig _config = null!;
    private readonly List<Border> _languageChips = new();
    private readonly List<Border> _useCaseChips = new();
    private string _selectedLanguage = "auto";
    private string _selectedUseCase = "Other";

    public PreferencesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
        _config = Kivi.App.App.Services.GetRequiredService<Kivi.Core.Config.AppConfig>();
        _selectedLanguage = _config.TranscriptionLanguage ?? "auto";
        _selectedUseCase = _config.PrimaryUseCase ?? "Other";
        BuildLanguageChips();
        BuildUseCaseChips();
    }

    private void BuildLanguageChips()
    {
        foreach (var (code, label) in LanguageChoices)
        {
            var chip = MakeChip(label, code, code == _selectedLanguage, () =>
            {
                _selectedLanguage = code;
                _config.TranscriptionLanguage = code == "auto" ? null : code;
                RefreshChipHighlight(_languageChips, code);
            });
            _languageChips.Add(chip);
            LanguageChipPanel.Children.Add(chip);
        }
    }

    private void BuildUseCaseChips()
    {
        foreach (var (code, label) in UseCaseChoices)
        {
            var chip = MakeChip(label, code, code == _selectedUseCase, () =>
            {
                _selectedUseCase = code;
                _config.PrimaryUseCase = code;
                RefreshChipHighlight(_useCaseChips, code);
            });
            _useCaseChips.Add(chip);
            UseCasePanel.Children.Add(chip);
        }
    }

    private Border MakeChip(string label, string tag, bool selected, Action onSelect)
    {
        var brandInk = (Brush)Application.Current.Resources["KiviBrandInkBrush"];
        var surfaceAlt = (Brush)Application.Current.Resources["KiviSurfaceAltBrush"];
        var textPrimary = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"];
        var surface = (Brush)Application.Current.Resources["KiviSurfaceBrush"];

        var chip = new Border
        {
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Padding = new Thickness(16, 0, 16, 0),
            Background = selected ? brandInk : surfaceAlt,
            Tag = tag,
        };
        chip.Child = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = selected ? surface : textPrimary,
        };
        chip.Tapped += (_, _) => onSelect();
        return chip;
    }

    private void RefreshChipHighlight(List<Border> chips, string selectedTag)
    {
        var brandInk = (Brush)Application.Current.Resources["KiviBrandInkBrush"];
        var surfaceAlt = (Brush)Application.Current.Resources["KiviSurfaceAltBrush"];
        var textPrimary = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"];
        var surface = (Brush)Application.Current.Resources["KiviSurfaceBrush"];

        foreach (var chip in chips)
        {
            bool selected = (string)chip.Tag == selectedTag;
            chip.Background = selected ? brandInk : surfaceAlt;
            if (chip.Child is TextBlock text) text.Foreground = selected ? surface : textPrimary;
        }
    }

    private void OnContinue(object sender, RoutedEventArgs e) => _host?.NavigateTo(typeof(PermissionsPage));
}
