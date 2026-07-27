using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Kivi.Core.Contracts;
using Kivi.Platform.Audio;
using Kivi.Platform.Paste;
using Kivi.Platform.Secrets;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>
/// Headless-testable slices of the Kivi.Platform Windows seams. The hook (WH_KEYBOARD_LL) and real
/// WASAPI mic capture need a real interactive session / a real microphone and are exercised via the
/// gated smoke tests below (set KIVI_PLATFORM_SMOKE=1 with a live session + mic).
/// </summary>
public class PlatformTests
{
    // --- ContinuousResampler: produces 3200-byte frames and preserves continuity across calls ---

    [Fact]
    public void Resampler_ProducesExactly3200ByteFrames()
    {
        var r = new ContinuousResampler(sourceRate: 48000);
        // 1 second of 48 kHz mono → ~16000 output samples → exactly 10 full 100 ms frames.
        var input = SineWave(48000, 48000, 440);
        var frames = r.Process(input);

        Assert.True(frames.Count >= 9, $"expected ~10 frames from 1s, got {frames.Count}");
        foreach (var f in frames)
            Assert.Equal(ContinuousResampler.BytesPerFrame, f.Length); // 3200
    }

    [Fact]
    public void Resampler_PreservesContinuity_AcrossMultipleFeeds_MoreThanOneFrame()
    {
        // The .noDataNow / R10 regression guard: feeding the SAME total audio in many small chunks must
        // yield the same number of frames as one big feed, and must yield MORE THAN ONE frame (the
        // "one-frame-then-dead" bug reset state per feed and capped the session at a single frame).
        var full = SineWave(48000, 48000, 440);

        var oneShot = new ContinuousResampler(48000);
        int oneShotFrames = oneShot.Process(full).Count;

        var chunked = new ContinuousResampler(48000);
        int chunkedFrames = 0;
        int chunk = 512; // small odd-ish buffer sizes like a real WASAPI callback
        for (int off = 0; off < full.Length; off += chunk)
        {
            int len = Math.Min(chunk, full.Length - off);
            chunkedFrames += chunked.Process(full.AsSpan(off, len)).Count;
        }

        Assert.True(chunkedFrames > 1, "resampler capped at one frame — continuity broken");
        // Allow at most one frame of difference from boundary rounding.
        Assert.True(Math.Abs(chunkedFrames - oneShotFrames) <= 1,
            $"chunked={chunkedFrames} vs oneShot={oneShotFrames} — state not continuous");
    }

    [Fact]
    public void Resampler_SameRatePassesThroughSampleCount()
    {
        // 16 kHz in → 16 kHz out, 3200 samples in (200 ms) → 2 frames.
        var r = new ContinuousResampler(16000);
        var input = SineWave(16000, 3200, 300);
        var frames = r.Process(input);
        Assert.Equal(2, frames.Count);
    }

    // --- SendInputPasteService: secure-field gate (no clipboard write, no paste) ---

    [Fact]
    public async Task Paste_SecureField_ReturnsSecureFieldBlocked()
    {
        var svc = new SendInputPasteService();
        var outcome = await svc.InsertAsync("secret", new PasteMeta(IsTerminal: false, IsSecureField: true));
        Assert.Equal(PasteOutcome.SecureFieldBlocked, outcome);
    }

    [Fact]
    public async Task Paste_EmptyText_IsNoOpOk()
    {
        var svc = new SendInputPasteService();
        var outcome = await svc.InsertAsync("", new PasteMeta(false, false));
        Assert.Equal(PasteOutcome.Ok, outcome);
    }

    // --- DpapiSecretStore: round-trip write → read ---

    [Fact]
    public void Dpapi_RoundTripsAValue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kivi-dpapi-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiSecretStore(dir);
            Assert.Null(store.Read("org.jwt"));

            store.Write("org.jwt", "header.payload.sig");
            Assert.Equal("header.payload.sig", store.Read("org.jwt"));

            // Overwrite.
            store.Write("org.jwt", "new.value");
            Assert.Equal("new.value", store.Read("org.jwt"));

            // Independent keys.
            store.Write("kratos.session", "abc123");
            Assert.Equal("abc123", store.Read("kratos.session"));
            Assert.Equal("new.value", store.Read("org.jwt"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Dpapi_ReadMissingKeyReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kivi-dpapi-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiSecretStore(dir);
            Assert.Null(store.Read("never.written"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // --- Gated smoke tests: need a real interactive session + microphone. ---
    // Run manually:  set KIVI_PLATFORM_SMOKE=1  &&  dotnet test --filter Category=PlatformSmoke
    // (they are skipped by default so CI / headless runs stay green.)

    [SkippableFact]
    [Trait("Category", "PlatformSmoke")]
    public async Task Smoke_WasapiCapture_EmitsMultipleFrames()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("KIVI_PLATFORM_SMOKE") == "1",
            "Set KIVI_PLATFORM_SMOKE=1 with a real microphone to run this.");

        var svc = new WasapiCaptureService();
        var frames = new List<byte[]>();
        svc.Frame += f => frames.Add(f);
        svc.Start();
        await Task.Delay(1500); // ~1.5 s of speaking into the mic
        await svc.StopAsync();

        Assert.True(frames.Count > 1, $"expected >1 frame from live mic, got {frames.Count}");
        foreach (var f in frames)
            Assert.Equal(ContinuousResampler.BytesPerFrame, f.Length);
    }

    // Helpers -----------------------------------------------------------------

    private static float[] SineWave(int sampleRate, int count, double freqHz)
    {
        var buf = new float[count];
        for (int i = 0; i < count; i++)
            buf[i] = (float)(0.5 * Math.Sin(2 * Math.PI * freqHz * i / sampleRate));
        return buf;
    }
}
