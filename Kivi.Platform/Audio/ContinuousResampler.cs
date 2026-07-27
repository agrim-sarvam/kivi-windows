namespace Kivi.Platform.Audio;

/// <summary>
/// A stateful linear resampler from an arbitrary source rate to 16 kHz, mono, Int16 LE, that packs
/// into fixed 100 ms (1600-sample / 3200-byte) frames.
///
/// CRITICAL — resampler continuity (the ".noDataNow" rule, R10): the fractional read position and the
/// last input sample are carried ACROSS calls. Reinitializing per input buffer would drop the boundary
/// sample and reset the phase every ~100 ms, producing clicks and — in the macOS reference — the
/// infamous "one-frame-then-dead" bug where the converter reported end-of-stream after the first
/// buffer. Here we simply never reset between <see cref="Process"/> calls; only <see cref="Reset"/>
/// (a new take) clears state.
///
/// This is deliberately dependency-free (no NAudio) so it is unit-testable headlessly and its behavior
/// is fully under our control. Input is mono float samples in [-1, 1] (the capture service down-mixes
/// interleaved channels to mono before calling this).
/// </summary>
public sealed class ContinuousResampler
{
    public const int TargetRate = 16000;
    public const int SamplesPerFrame = 1600;      // 100 ms @ 16 kHz
    public const int BytesPerFrame = SamplesPerFrame * 2; // Int16

    private readonly double _ratio;               // sourceRate / targetRate = input samples per output sample
    private double _pos;                           // fractional read position into the "virtual" input stream
    private float _prevSample;                     // last input sample of the previous buffer (for cross-buffer interpolation)
    private bool _hasPrev;

    // Accumulates resampled Int16 samples until we have a full 3200-byte frame.
    private readonly List<short> _outAccum = new(SamplesPerFrame * 2);

    public ContinuousResampler(int sourceRate)
    {
        if (sourceRate <= 0) throw new ArgumentOutOfRangeException(nameof(sourceRate));
        _ratio = (double)sourceRate / TargetRate;
        Reset();
    }

    /// <summary>Clear all state — call only at the start of a fresh take, never between frames.</summary>
    public void Reset()
    {
        _pos = 0;
        _prevSample = 0;
        _hasPrev = false;
        _outAccum.Clear();
    }

    /// <summary>
    /// Feed a mono float buffer; returns zero or more complete 3200-byte frames. Any partial trailing
    /// frame stays buffered for the next call (continuity). Fractional phase is preserved across calls.
    /// </summary>
    public IReadOnlyList<byte[]> Process(ReadOnlySpan<float> mono)
    {
        List<byte[]>? frames = null;

        if (mono.Length == 0)
            return Array.Empty<byte[]>();

        // Virtual input index space for THIS call: sample[-1] = _prevSample (carried), sample[0..n-1] = mono.
        // We read output samples at positions _pos, _pos+ratio, ... while _pos < mono.Length.
        // _pos is expressed relative to the start of the current buffer (index 0 = mono[0]).
        double pos = _pos;
        int n = mono.Length;

        while (pos < n)
        {
            int i0 = (int)Math.Floor(pos);
            double frac = pos - i0;

            float a = SampleAt(i0, mono);
            float b = SampleAt(i0 + 1, mono);
            float interp = (float)(a + (b - a) * frac);

            _outAccum.Add(FloatToInt16(interp));
            if (_outAccum.Count == SamplesPerFrame)
            {
                (frames ??= new List<byte[]>()).Add(DrainFrame());
            }

            pos += _ratio;
        }

        // Carry the phase remainder into the next buffer; the last real sample becomes _prevSample so
        // an output tap that falls between buffers interpolates correctly.
        _pos = pos - n;
        _prevSample = mono[n - 1];
        _hasPrev = true;

        return (IReadOnlyList<byte[]>?)frames ?? Array.Empty<byte[]>();
    }

    // Reads the virtual input at index i where i == -1 refers to the carried previous sample.
    private float SampleAt(int i, ReadOnlySpan<float> mono)
    {
        if (i < 0)
            return _hasPrev ? _prevSample : mono[0];
        if (i >= mono.Length)
            return mono[mono.Length - 1]; // clamp at the right edge; the remainder is carried via _pos
        return mono[i];
    }

    private byte[] DrainFrame()
    {
        var bytes = new byte[BytesPerFrame];
        for (int i = 0; i < SamplesPerFrame; i++)
        {
            short s = _outAccum[i];
            bytes[i * 2] = (byte)(s & 0xFF);            // little-endian
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        _outAccum.RemoveRange(0, SamplesPerFrame);
        return bytes;
    }

    private static short FloatToInt16(float f)
    {
        // Clamp to [-1, 1] then scale. Use 32767 for the positive full-scale to avoid overflow at +1.
        if (f > 1f) f = 1f;
        else if (f < -1f) f = -1f;
        return (short)Math.Round(f * 32767f);
    }
}
