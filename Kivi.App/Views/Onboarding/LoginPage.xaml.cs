// Kivi.App/Views/Onboarding/LoginPage.xaml.cs
using Kivi.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// "Continue with Google" launches the system browser for client-side-only identity
/// capture (name/email/avatar for personalization; no backend, no account creation --
/// see GoogleSignIn). "Use work email instead" skips straight to Permissions without
/// capturing a profile. Windows build intentionally omits the "Continue with Apple"
/// option present in the macOS mockup.
/// </summary>
public sealed partial class LoginPage : Page
{
    // TODO(config): replace with Kivi's real registered OAuth client ID before shipping.
    private const string GoogleClientId = "REPLACE_WITH_REAL_GOOGLE_OAUTH_CLIENT_ID";

    private OnboardingWindow? _host;

    public LoginPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
    }

    private async void OnGoogle(object sender, RoutedEventArgs e)
    {
        GoogleButton.IsEnabled = false;
        StatusText.Text = "Waiting for sign-in in your browser…";
        StatusText.Visibility = Visibility.Visible;

        var profile = await GoogleSignIn.SignInAsync(GoogleClientId, default);

        if (profile is not null)
        {
            var config = Kivi.App.App.Services.GetRequiredService<Kivi.Core.Config.AppConfig>();
            config.ProfileName = profile.Name;
            config.ProfileEmail = profile.Email;
            config.ProfileAvatarUrl = profile.AvatarUrl;
            _host?.NavigateTo(typeof(PreferencesPage));
            return;
        }

        GoogleButton.IsEnabled = true;
        StatusText.Text = "Sign-in didn't complete. Try again, or use your work email instead.";
    }

    private void OnEmail(object sender, RoutedEventArgs e) => _host?.NavigateTo(typeof(PreferencesPage));
}
