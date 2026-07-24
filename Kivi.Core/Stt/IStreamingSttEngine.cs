namespace Kivi.Core.Stt;

/// <summary>
/// Real-time streaming speech-to-text: audio is sent to the server WHILE the user speaks,
/// so the transcript is ready almost as soon as they stop, instead of the whole clip being
/// uploaded and transcribed only after recording ends. One session is active at a time.
///
/// Lifecycle:
///   1. StartAsync(mode, ct)   -- open the connection (mode = SttMode.Hinglish/English).
///   2. SendAudioAsync(pcm, ct) -- call repeatedly with raw 16k mono PCM16 chunks as they arrive.
///   3. PartialReceived         -- fires with the cumulative transcript as the server returns it.
///   4. FinishAsync(ct)         -- flush, wait for the final transcript, return it, close.
///   or CancelAsync()           -- abort without waiting for a result.
/// </summary>
public interface IStreamingSttEngine
{
    /// <summary>Fires (off an arbitrary thread) with the latest cumulative transcript.</summary>
    event Action<string>? PartialReceived;

    Task StartAsync(string mode, CancellationToken ct);
    Task SendAudioAsync(byte[] pcm, CancellationToken ct);
    // Force-finalize whatever's been recognized so far, mid-stream -- called periodically
    // during a long hold so live captions keep appearing even without a natural speech pause.
    Task FlushAsync(CancellationToken ct);
    Task<string> FinishAsync(CancellationToken ct);
    Task CancelAsync();
}
