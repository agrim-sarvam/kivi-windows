using Kivi.Core.Contracts;

namespace Kivi.Platform.Audio;

/// <summary>
/// PHASE P3 (M0) — STUB. Real impl: WASAPI capture → down-mix mono → resample to 16 kHz Int16 LE,
/// emit ~100ms (3200-byte) frames. MUST keep resampler state continuous across frames (the
/// .noDataNow rule — else the session caps at one frame). RMS level for animation only.
/// </summary>
public sealed class WasapiCaptureService : IAudioCapture
{
    public event Action<byte[]>? Frame;
    public void Start() { /* P3 */ }
    public Task StopAsync() => Task.CompletedTask; // P3
}
