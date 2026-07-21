// Kivi.App/Views/Onboarding/LoginPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// UI-flow-only stub: both Google and "use work email instead" simply advance to
/// Permissions. No OAuth is wired up here (out of scope for this task). Windows build
/// intentionally omits the "Continue with Apple" option present in the macOS mockup.
/// </summary>
public sealed partial class LoginPage : Page
{
    private OnboardingWindow? _host;

    public LoginPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
    }

    private void OnGoogle(object sender, RoutedEventArgs e) => _host?.NavigateTo(typeof(PermissionsPage));
    private void OnEmail(object sender, RoutedEventArgs e) => _host?.NavigateTo(typeof(PermissionsPage));
}
