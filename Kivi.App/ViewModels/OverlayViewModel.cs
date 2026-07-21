using CommunityToolkit.Mvvm.ComponentModel;
using Kivi.Core.Orchestration;
using Microsoft.UI.Dispatching;

namespace Kivi.App.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    private readonly IDictationOrchestrator _orch;
    private readonly DispatcherQueue _ui;

    public OverlayViewModel(IDictationOrchestrator orch, DispatcherQueue ui)
    {
        _orch = orch;
        _ui = ui;
        _orch.StateChanged += OnOrchestratorStateChanged;
        Apply(_orch.State);
    }

    [ObservableProperty] private RecordingState _state;

    public bool IsVisible    => true;
    public bool IsListening  => State == RecordingState.Listening;
    public bool IsProcessing => State == RecordingState.Processing;
    public bool IsSpeaking   => State == RecordingState.Speaking;
    public bool IsWaiting    => State == RecordingState.Waiting;
    public bool IsDone       => State == RecordingState.Done;
    public bool IsError      => State == RecordingState.Error;

    public string StateColorTokenKey => State switch
    {
        RecordingState.Idle       => "OverlayIdleBrush",
        RecordingState.Listening  => "OverlayListeningBrush",
        RecordingState.Processing => "OverlayProcessingBrush",
        RecordingState.Speaking   => "OverlaySpeakingBrush",
        RecordingState.Waiting    => "OverlayWaitingBrush",
        RecordingState.Done       => "OverlayDoneBrush",
        RecordingState.Error      => "OverlayErrorBrush",
        _                         => "OverlayIdleBrush"
    };

    private void OnOrchestratorStateChanged(RecordingState newState) => _ui.TryEnqueue(() => Apply(newState));

    private void Apply(RecordingState state)
    {
        State = state;
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsSpeaking));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(StateColorTokenKey));
    }
}
