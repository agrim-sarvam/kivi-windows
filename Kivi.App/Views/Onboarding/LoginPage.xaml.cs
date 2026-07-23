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
    // Registered as a Desktop app OAuth client in Google Cloud Console (project: Kivi Dictation).
    // No client secret is needed for the loopback id_token flow used by GoogleSignIn.
    private const string GoogleClientId = "552223359415-in50ck0ijpcai5fj4pd4ihdjnj0ogqqt.apps.googleusercontent.com";

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

        var result = await GoogleSignIn.SignInAsync(GoogleClientId, default);

        if (result.Profile is { } profile)
        {
            var config = Kivi.App.App.Services.GetRequiredService<Kivi.Core.Config.AppConfig>();
            config.ProfileName = profile.Name;
            config.ProfileEmail = profile.Email;
            config.ProfileAvatarUrl = profile.AvatarUrl;
            _host?.NavigateTo(typeof(PreferencesPage));
            return;
        }

        // Log the real reason to crash.log (same file App.xaml.cs's UnhandledException
        // handler writes to) so a failed sign-in is diagnosable instead of just "incomplete".
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"),
                $"{DateTime.Now:o}\nGoogle sign-in failed: {result.Error}\n\n");
        }
        catch { /* best-effort logging only */ }

        GoogleButton.IsEnabled = true;
        StatusText.Text = "Sign-in didn't complete. Try again, or use your work email instead.";
    }

    private void OnEmail(object sender, RoutedEventArgs e) => _host?.NavigateTo(typeof(PreferencesPage));
}
