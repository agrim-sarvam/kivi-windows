// Kivi.App/Views/Onboarding/PermissionsPage.xaml.cs
using Kivi.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// Real Windows mic-capability check (MicPermission, fail-open if the API is
/// unavailable). Screen context is informational/always-granted on Windows: UIA
/// (used by IScreenContextProvider) needs no OS-level permission grant, unlike macOS
/// Accessibility, so there is nothing to request there.
/// </summary>
public sealed partial class PermissionsPage : Page
{
    // Segoe Fluent Icons glyphs: checkmark (granted) / cancel "X" (denied).
    private const string GlyphGranted = "";
    private const string GlyphDenied = "";

    private OnboardingWindow? _host;

    public PermissionsPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
        await RefreshMicAsync();
    }

    private async Task RefreshMicAsync()
    {
        bool granted = await MicPermission.CheckAsync();
        if (!granted) granted = await MicPermission.RequestAsync();

        var brushKey = granted ? "KiviPositiveBrush" : "KiviDangerBrush";
        var brush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[brushKey];

        MicChip.Text = granted ? "granted" : "denied";
        MicChip.Foreground = brush;
        MicChipIcon.Foreground = brush;
        MicChipIcon.Glyph = granted ? GlyphGranted : GlyphDenied;

        ContinueButton.IsEnabled = granted;
        OpenSettingsLink.Visibility = granted ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => MicPermission.OpenSettings();

    private async void OnRecheck(object sender, RoutedEventArgs e) => await RefreshMicAsync();

    private void OnContinue(object sender, RoutedEventArgs e) => _host?.NavigateTo(typeof(ConfigPage));
}
