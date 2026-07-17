namespace Kivi.Core.Abstractions;

// Returns 16k mono PCM16 WAV bytes.
public interface IAudioCaptureService
{
    Task StartRecordingAsync(CancellationToken ct);
    Task<byte[]> StopRecordingAsync();
    event Action<string>? DeviceChanged;
}
