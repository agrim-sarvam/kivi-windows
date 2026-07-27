using System.Net.Http;
using Kivi.Core.Wire;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>
/// GATED integration test against the live local kivi-service at ws://127.0.0.1:8788.
///
/// HOW TO RUN (once the NetBird VPN is up and the service is running):
///   1. Start the local kivi-service (Postgres + `DICTATE_AUTH_MODE=none PORT=8788 cargo run -p kivi-service`;
///      set LOAD_TEST_MODE=synthetic to bypass Sarvam/Gemini).
///   2. `dotnet test` — this test auto-detects the service via GET /ready and RUNS. If the service
///      is unreachable it SKIPS (never hard-fails the suite).
///
/// Override the endpoint with env var KIVI_WS_URL (default ws://127.0.0.1:8788/v1/dictate/stream).
/// To point at a fixture WAV instead of the synthetic tone, set KIVI_FIXTURE_PCM to a path holding
/// raw 16 kHz Int16 mono LE PCM (headerless). Otherwise a 2 s synthetic tone is streamed — enough
/// to exercise the full handshake→final loop; assert a final comes back (with LOAD_TEST_MODE the
/// transcript is a stub, so we assert a final ARRIVES rather than golden text).
/// </summary>
public class LiveServiceIntegrationTests
{
    private static string WsUrl =>
        Environment.GetEnvironmentVariable("KIVI_WS_URL") ?? Endpoints.Local.WebSocketUrl.ToString();

    private static async Task<bool> ServiceUpAsync()
    {
        try
        {
            var ep = new Uri(WsUrl);
            var restBase = new UriBuilder(ep) { Scheme = ep.Scheme == "wss" ? "https" : "http", Path = "/ready", Query = "" }.Uri;
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };
            using var resp = await http.GetAsync(restBase);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static byte[][] SyntheticFrames(double seconds = 2.0)
    {
        // 440 Hz sine at 16 kHz Int16 mono, chunked into 3200-byte (1600-sample) frames.
        int totalSamples = (int)(DictationAudio.SampleRate * seconds);
        var frames = new List<byte[]>();
        int sampleIdx = 0;
        while (sampleIdx < totalSamples)
        {
            var frame = new byte[DictationAudio.FrameBytes];
            for (int i = 0; i < DictationAudio.FrameSamples; i++)
            {
                double t = (double)(sampleIdx + i) / DictationAudio.SampleRate;
                short s = (short)(Math.Sin(2 * Math.PI * 440 * t) * 8000);
                frame[i * 2] = (byte)(s & 0xFF);
                frame[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            frames.Add(frame);
            sampleIdx += DictationAudio.FrameSamples;
        }
        return frames.ToArray();
    }

    private static byte[][] FramesFromFixture(string path)
    {
        var pcm = File.ReadAllBytes(path);
        var frames = new List<byte[]>();
        for (int off = 0; off < pcm.Length; off += DictationAudio.FrameBytes)
        {
            int len = Math.Min(DictationAudio.FrameBytes, pcm.Length - off);
            var frame = new byte[DictationAudio.FrameBytes];
            Array.Copy(pcm, off, frame, 0, len);
            frames.Add(frame);
        }
        return frames.ToArray();
    }

    [SkippableFact]
    public async Task Streams_Fixture_And_Receives_Final()
    {
        Skip.IfNot(await ServiceUpAsync(),
            $"local kivi-service not reachable at {WsUrl} (start it behind the VPN to run this)");

        var identity = new ClientIdentity(ClientIdentity.PlatformWindows, "0.0.0-test", "Asia/Kolkata");
        var ctx = new ContextOptions { TranscriptionMode = "codemix", FormattingEnabled = true };
        // Loopback ⇒ anonymous ⇒ no bearer.
        await using var client = new KiviServiceClient(new Uri(WsUrl), identity, ctx, bearer: null);

        var fixture = Environment.GetEnvironmentVariable("KIVI_FIXTURE_PCM");
        var frames = !string.IsNullOrEmpty(fixture) && File.Exists(fixture)
            ? FramesFromFixture(fixture)
            : SyntheticFrames();

        await client.OpenAsync();
        foreach (var f in frames) client.SendAudio(f);
        client.Stop();

        var final = await client.FinalTask.WaitAsync(TimeSpan.FromSeconds(30));
        // With LOAD_TEST_MODE=synthetic the text is a stub; assert the loop completed and returned a final.
        Assert.NotNull(final);
        Assert.NotNull(final.PasteText); // formatted_text or raw_transcript
    }
}
