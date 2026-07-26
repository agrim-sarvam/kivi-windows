// Kivi.App/Views/Onboarding/WalkthroughPage.xaml.cs
using Kivi.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private int _step = 1;

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
            if (_step1Completed) return;

            switch (state)
            {
                case RecordingState.Listening:
                    StatusChip.Text = "Listening… speak now.";
                    break;
                case RecordingState.Processing:
                    StatusChip.Text = "Transcribing…";
                    break;
                case RecordingState.Error:
                    StatusChip.Text = _orchestrator.LastErrorMessage is { Length: > 0 } msg
                        ? $"Couldn't dictate: {msg}"
                        : "Couldn't dictate that. Try again, or check Settings > API key.";
                    _ = PracticeField.Focus(FocusState.Programmatic);
                    break;
                case RecordingState.Idle when PracticeField.Text.Length > 0:
                    _step1Completed = true;
                    ShowStep2();
                    break;
                case RecordingState.Idle:
                    StatusChip.Text = "Hold Right Ctrl and say something";
                    break;
            }
        });
    }

    private void ShowStep1()
    {
        _step = 1;
        Step1Panel.Visibility = Visibility.Visible;
        Step2Panel.Visibility = Visibility.Collapsed;
        Step3Panel.Visibility = Visibility.Collapsed;
        StatusChip.Text = "Hold Right Ctrl and say something";

        // Dictation pastes into whatever control has OS-level keyboard focus system-wide
        // (SendInputPasteService simulates Ctrl+V) -- without an explicit focus call here,
        // the practice field may never actually be the paste target, making the walkthrough
        // look like "nothing happens" even when dictation itself succeeded elsewhere.
        _ = PracticeField.Focus(FocusState.Programmatic);
    }

    private void ShowStep2()
    {
        _step = 2;
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Visible;
        Step3Panel.Visibility = Visibility.Collapsed;
        StatusChip.Text = "Now double-tap Right Ctrl for hands-free mode";
    }

    private void ShowStep3()
    {
        _step = 3;
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Collapsed;
        Step3Panel.Visibility = Visibility.Visible;
        StatusChip.Text = "Right Alt writes in English, in any language you speak";
        _ = PracticeFieldAlt.Focus(FocusState.Programmatic);
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Finish();

    // Continue steps through the walkthrough (Right Ctrl → hands-free → Right Alt) before finishing.
    private void OnContinue(object sender, RoutedEventArgs e)
    {
        if (_step < 3)
        {
            if (_step == 1) ShowStep2();
            else ShowStep3();
            return;
        }
        Finish();
    }

    private void Finish()
    {
        _orchestrator.StateChanged -= OnOrchestratorStateChanged;
        _host?.NavigateTo(typeof(ConfigPage));
    }
}
