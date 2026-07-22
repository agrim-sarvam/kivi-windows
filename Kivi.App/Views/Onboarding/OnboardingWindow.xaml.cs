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

    /// <summary>
    /// True when this window was opened for a post-onboarding mic-permission
    /// re-check (Task 6). In that mode, PermissionsPage's Continue completes the
    /// flow directly instead of navigating on to ConfigPage.
    /// </summary>
    public bool PermissionsOnly { get; }

    private OnboardingWindow(Type startPage, bool permissionsOnly, string title)
    {
        InitializeComponent();
        Title = title;
        PermissionsOnly = permissionsOnly;
        RootFrame.Navigate(startPage, this);
    }

    public OnboardingWindow(bool startAtPermissions)
        : this(startAtPermissions ? typeof(PermissionsPage) : typeof(LoginPage), startAtPermissions, "Kivi")
    {
    }

    /// <summary>
    /// Reopens just the Config page as a standalone settings re-entry after onboarding has
    /// already completed (triggered by the orb's hover gear icon). Here, Completed means
    /// "close this window" -- the caller wires it that way -- distinct from the first-run /
    /// permission-recheck constructor above, where Completed means "show the orb".
    /// </summary>
    public static OnboardingWindow ForSettingsReentry() => new(typeof(ConfigPage), permissionsOnly: false, "Kivi Settings");

    public void NavigateTo(Type page) => RootFrame.Navigate(page, this);

    public void RaiseCompleted() => Completed?.Invoke();
}
