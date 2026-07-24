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
    // Returns raw 16k mono PCM16 bytes captured since the previous call (no WAV header) --
    // used to pump audio into the streaming STT WebSocket while recording is in progress.
    // Empty array if nothing new has been captured. Consuming is destructive: each byte is
    // returned by exactly one ReadNewPcm call.
    byte[] ReadNewPcm();
    event Action<string>? DeviceChanged;
}
