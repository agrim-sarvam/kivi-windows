using CommunityToolkit.Mvvm.ComponentModel;
using Kivi.Core.Orchestration;
using Kivi.Core.Text;
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
        _orch.PartialTranscriptChanged += OnOrchestratorPartialTranscriptChanged;
        Apply(_orch.State);
    }

    [ObservableProperty] private RecordingState _state;
    [ObservableProperty] private string _partialTranscript = "";

    public bool IsVisible    => true;
    public bool IsListening  => State == RecordingState.Listening;
    public bool IsProcessing => State == RecordingState.Processing;
    public bool IsSpeaking   => State == RecordingState.Speaking;
    public bool IsWaiting    => State == RecordingState.Waiting;
    public bool IsDone       => State == RecordingState.Done;
    public bool IsError      => State == RecordingState.Error;
    public bool IsRewritePending => State == RecordingState.RewritePending;
    public bool IsRewriteReview  => State == RecordingState.RewriteReview;

    public bool IsRewriteCapture => _orch.IsRewriteCapture;
    public string? Instruction => _orch.Instruction;
    public string? LastErrorMessage => _orch.LastErrorMessage;
    public IReadOnlyList<DiffToken>? Diff => _orch.Diff;

    public string StateColorTokenKey => State switch
    {
        RecordingState.Idle       => "OverlayIdleBrush",
        RecordingState.Listening  => "OverlayListeningBrush",
        RecordingState.Processing => "OverlayProcessingBrush",
        RecordingState.Speaking   => "OverlaySpeakingBrush",
        RecordingState.Waiting    => "OverlayWaitingBrush",
        RecordingState.Done       => "OverlayDoneBrush",
        RecordingState.Error      => "OverlayErrorBrush",
        RecordingState.RewritePending => "OverlayProcessingBrush",
        RecordingState.RewriteReview  => "OverlayProcessingBrush",
        _                         => "OverlayIdleBrush"
    };

    private void OnOrchestratorStateChanged(RecordingState newState) => _ui.TryEnqueue(() => Apply(newState));
    private void OnOrchestratorPartialTranscriptChanged(string text) => _ui.TryEnqueue(() => PartialTranscript = text);

    private void Apply(RecordingState state)
    {
        State = state;
        if (state != RecordingState.Listening) PartialTranscript = ""; // clear stale partial once recording ends
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsSpeaking));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsRewritePending));
        OnPropertyChanged(nameof(IsRewriteReview));
        OnPropertyChanged(nameof(IsRewriteCapture));
        OnPropertyChanged(nameof(Instruction));
        OnPropertyChanged(nameof(LastErrorMessage));
        OnPropertyChanged(nameof(Diff));
        OnPropertyChanged(nameof(StateColorTokenKey));
    }
}
