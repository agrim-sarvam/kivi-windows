// Kivi.App/Views/Onboarding/OnboardingWindow.xaml.cs
using Microsoft.UI.Xaml;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// Chromed shell window hosting the onboarding flow: Login -> Permissions -> Config
/// (Config added in Task 4). Pages receive this window as their navigation parameter
/// so they can call NavigateTo/RaiseCompleted without a separate view-model layer.
/// </summary>
public sealed partial class OnboardingWindow : Window
{
    public event Action? Completed;

    public OnboardingWindow(bool startAtPermissions)
    {
        InitializeComponent();
        Title = "Kivi";
        RootFrame.Navigate(startAtPermissions ? typeof(PermissionsPage) : typeof(LoginPage), this);
    }

    public void NavigateTo(Type page) => RootFrame.Navigate(page, this);

    public void RaiseCompleted() => Completed?.Invoke();
}
