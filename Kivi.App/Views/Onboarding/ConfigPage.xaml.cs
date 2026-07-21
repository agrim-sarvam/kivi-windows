// Kivi.App/Views/Onboarding/ConfigPage.xaml.cs
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// Empty shell stub. Task 4 fills in the real config/hotkey/persona content and wires
/// OnboardingWindow.RaiseCompleted() when the user finishes setup.
/// </summary>
public sealed partial class ConfigPage : Page
{
    private OnboardingWindow? _host;

    public ConfigPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
    }
}
