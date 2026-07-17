namespace Kivi.Core.Orchestration;
public interface IDictationOrchestrator
{
    RecordingState State { get; }
    event Action<RecordingState> StateChanged;
    void Start();
    void Stop();
}
