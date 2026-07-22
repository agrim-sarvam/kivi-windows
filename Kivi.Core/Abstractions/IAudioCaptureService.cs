namespace Kivi.Core.Abstractions;

// Returns 16k mono PCM16 WAV bytes.
public interface IAudioCaptureService
{
    Task StartRecordingAsync(CancellationToken ct);
    Task<byte[]> StopRecordingAsync();
    // Returns a valid WAV of everything captured so far WITHOUT stopping capture -- used to
    // drive live partial transcription while a recording is in progress. Empty array if no
    // recording is in progress.
    byte[] SnapshotRecording();
    event Action<string>? DeviceChanged;
}
