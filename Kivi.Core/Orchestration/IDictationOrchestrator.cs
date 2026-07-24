namespace Kivi.Core.Orchestration;

public interface IDictationOrchestrator
{
    RecordingState State { get; }
    string? LastErrorMessage { get; }
    event Action<RecordingState> StateChanged;
    event Action<string> PartialTranscriptChanged;
    void Start();
    void Stop();
}
