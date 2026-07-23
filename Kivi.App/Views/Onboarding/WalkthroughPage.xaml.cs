// Kivi.App/Views/Onboarding/WalkthroughPage.xaml.cs
using Kivi.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// Interactive walkthrough: the user actually holds Right Ctrl and dictates into a
/// practice field (real orchestrator round-trip via the configured STT/polish engines),
/// then double-taps Right Ctrl to see hands-free mode engage. Confirms the real pipeline
/// works end-to-end before the user reaches the main app. "Skip" is always available.
/// </summary>
public sealed partial class WalkthroughPage : Page
{
    private OnboardingWindow? _host;
    private IDictationOrchestrator _orchestrator = null!;
    private DispatcherQueue _dispatcher = null!;
    private bool _step1Completed;

    public WalkthroughPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
        _orchestrator = Kivi.App.App.Services.GetRequiredService<IDictationOrchestrator>();
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _orchestrator.StateChanged += OnOrchestratorStateChanged;
        ShowStep1();
    }

    private void OnOrchestratorStateChanged(RecordingState state)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (!_step1Completed && state == RecordingState.Idle && PracticeField.Text.Length > 0)
            {
                _step1Completed = true;
                ShowStep2();
            }
        });
    }

    private void ShowStep1()
    {
        Step1Panel.Visibility = Visibility.Visible;
        Step2Panel.Visibility = Visibility.Collapsed;
        StatusChip.Text = "Hold Right Ctrl and say something";
    }

    private void ShowStep2()
    {
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Visible;
        StatusChip.Text = "Now double-tap Right Ctrl for hands-free mode";
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Finish();

    private void OnContinue(object sender, RoutedEventArgs e) => Finish();

    private void Finish()
    {
        _orchestrator.StateChanged -= OnOrchestratorStateChanged;
        _host?.NavigateTo(typeof(ConfigPage));
    }
}
