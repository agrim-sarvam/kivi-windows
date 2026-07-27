using System.Runtime.InteropServices;
using Kivi.Core.Contracts;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Kivi.Platform.Audio;

/// <summary>
/// REAL microphone capture via WASAPI (NAudio <see cref="WasapiCapture"/>). Captures at the device's
/// native rate/format, down-mixes to mono, and resamples to 16 kHz Int16 mono LE using a
/// <see cref="ContinuousResampler"/> whose state persists across capture callbacks (R10 — never reset
/// per buffer, or the session caps at one frame). Emits ~100 ms = 1600-sample = 3200-byte frames via
/// <see cref="Frame"/>.
///
/// Also exposes an RMS <see cref="Level"/> (0..1, EMA-smoothed) for the orb "listening" animation ONLY
/// — level is never a take authority (the gesture is; see dictation-audio-pipeline §2/§3.4).
///
/// The capture callback thread does no allocation-heavy or async work beyond building frame byte[]s and
/// invoking the (fast) resampler; frames are handed straight to subscribers.
/// </summary>
public sealed class WasapiCaptureService : IAudioCapture, IDisposable
{
    // Level meter smoothing per dictation-audio-pipeline §3.4 (EMA alpha 0.3).
    private const float LevelEmaAlpha = 0.3f;

    public event Action<byte[]>? Frame;

    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private ContinuousResampler? _resampler;
    private WaveFormat? _format;
    private float[] _monoScratch = Array.Empty<float>();
    private volatile float _level;
    private TaskCompletionSource<bool>? _stopTcs;

    /// <summary>RMS level 0..1 for animation only. Never gates a take.</summary>
    public float Level => _level;

    public void Start()
    {
        lock (_gate)
        {
            if (_capture is not null) return;

            // Default communications capture device; the voice-communication category enables the OS
            // mic-path AEC/NS where the device supports it (dictation-audio-pipeline §3.2).
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

            var capture = new WasapiCapture(device)
            {
                // Shared mode; NAudio surfaces the device mix format (typically 32-bit float).
                ShareMode = AudioClientShareMode.Shared,
            };
            _format = capture.WaveFormat;
            _resampler = new ContinuousResampler(_format.SampleRate);
            _level = 0f;

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            _capture = capture;
            capture.StartRecording();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var fmt = _format;
        var resampler = _resampler;
        if (fmt is null || resampler is null || e.BytesRecorded == 0) return;

        int channels = fmt.Channels;
        int frameCount; // interleaved sample frames (per channel)
        Span<float> mono;

        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
        {
            int totalFloats = e.BytesRecorded / 4;
            frameCount = totalFloats / channels;
            mono = EnsureMono(frameCount);
            var src = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
            DownmixFloat(src, channels, frameCount, mono);
        }
        else if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
        {
            int totalShorts = e.BytesRecorded / 2;
            frameCount = totalShorts / channels;
            mono = EnsureMono(frameCount);
            var src = MemoryMarshal.Cast<byte, short>(e.Buffer.AsSpan(0, e.BytesRecorded));
            DownmixInt16(src, channels, frameCount, mono);
        }
        else
        {
            // Unexpected device format — skip rather than emit garbage. (Shared-mode mic is virtually
            // always 32-bit float; 16-bit PCM handled above covers the rest.)
            return;
        }

        UpdateLevel(mono);

        var frames = resampler.Process(mono);
        if (frames.Count == 0) return;

        var handler = Frame;
        if (handler is null) return;
        for (int i = 0; i < frames.Count; i++)
            handler(frames[i]);
    }

    private Span<float> EnsureMono(int frameCount)
    {
        if (_monoScratch.Length < frameCount)
            _monoScratch = new float[frameCount];
        return _monoScratch.AsSpan(0, frameCount);
    }

    private static void DownmixFloat(ReadOnlySpan<float> src, int channels, int frames, Span<float> mono)
    {
        if (channels == 1)
        {
            src.Slice(0, frames).CopyTo(mono);
            return;
        }
        for (int f = 0; f < frames; f++)
        {
            float sum = 0f;
            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++) sum += src[baseIdx + c];
            mono[f] = sum / channels;
        }
    }

    private static void DownmixInt16(ReadOnlySpan<short> src, int channels, int frames, Span<float> mono)
    {
        const float inv = 1f / 32768f;
        if (channels == 1)
        {
            for (int f = 0; f < frames; f++) mono[f] = src[f] * inv;
            return;
        }
        for (int f = 0; f < frames; f++)
        {
            int sum = 0;
            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++) sum += src[baseIdx + c];
            mono[f] = (sum / (float)channels) * inv;
        }
    }

    private void UpdateLevel(ReadOnlySpan<float> mono)
    {
        if (mono.Length == 0) return;
        double sumSq = 0;
        for (int i = 0; i < mono.Length; i++) sumSq += mono[i] * (double)mono[i];
        double rms = Math.Sqrt(sumSq / mono.Length);

        // dBFS curve clamped to [-45, -3], normalized 0..1 (dictation-audio-pipeline §3.4).
        double db = rms > 1e-9 ? 20.0 * Math.Log10(rms) : -90.0;
        if (db < -45) db = -45;
        else if (db > -3) db = -3;
        float norm = (float)((db + 45.0) / 42.0);

        _level = _level + LevelEmaAlpha * (norm - _level);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stopTcs?.TrySetResult(true);
    }

    public Task StopAsync()
    {
        WasapiCapture? capture;
        TaskCompletionSource<bool> tcs;
        lock (_gate)
        {
            if (_capture is null) return Task.CompletedTask;
            capture = _capture;
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTcs = tcs;
        }

        capture.StopRecording(); // triggers RecordingStopped asynchronously
        return tcs.Task.ContinueWith(_ =>
        {
            lock (_gate)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                capture.Dispose();
                if (ReferenceEquals(_capture, capture)) _capture = null;
                _resampler = null;
                _format = null;
                _level = 0f;
            }
        });
    }

    public void Dispose()
    {
        try { StopAsync().Wait(2000); } catch { /* best-effort teardown */ }
    }
}
