using Kivi.Core.Text;

namespace Kivi.Core.Orchestration;

public interface IDictationOrchestrator
{
    RecordingState State { get; }
    bool IsRewriteCapture { get; }
    string? Instruction { get; }
    string? LastErrorMessage { get; }
    IReadOnlyList<DiffToken>? Diff { get; }
    event Action<RecordingState> StateChanged;
    event Action<string> PartialTranscriptChanged;
    void Start();
    void Stop();
}
