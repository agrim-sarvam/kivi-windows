using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;

namespace Kivi.Core.Stt;

/// <summary>
/// Streaming STT over Sarvam's WebSocket endpoint (wss://api.sarvam.ai/speech-to-text/ws).
/// Audio is pushed as base64 WAV chunks (Sarvam's WS only accepts encoding "audio/wav") while
/// the user speaks; the server returns "data" messages carrying transcript segments, and a
/// "flush" finalizes the last segment.
///
/// This engine keeps NO cross-session state beyond the single in-flight socket, so a new
/// StartAsync fully re-initializes. Never logs transcript text or the API key.
/// </summary>
public sealed class SarvamStreamingSttEngine : IStreamingSttEngine, IDisposable
{
    private readonly AppConfig _config;
    private readonly ISecretStore _secrets;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;
    private readonly StringBuilder _transcript = new();
    private readonly object _lock = new();

    // Temporary diagnostics: when %APPDATA%\Kivi\stream-debug.on exists, connection/send/receive
    // events are appended to stream-debug.log so we can see exactly what Sarvam's WS returns and
    // when. No transcript text policy concern here -- it's an opt-in local debug file the user
    // creates deliberately. Remove once streaming partials are confirmed working.
    private static readonly bool DebugEnabled =
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi", "stream-debug.on"));
    private static readonly string DebugLogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi", "stream-debug.log");

    private static void Log(string line)
    {
        if (!DebugEnabled) return;
        try { File.AppendAllText(DebugLogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}\n"); } catch { }
    }

    public event Action<string>? PartialReceived;

    public SarvamStreamingSttEngine(AppConfig config, ISecretStore secrets)
        => (_config, _secrets) = (config, secrets);

    public async Task StartAsync(string mode, CancellationToken ct)
    {
        var key = _secrets.GetApiKey() ?? throw new InvalidOperationException("Missing API key");

        lock (_lock) { _transcript.Clear(); }

        var baseWs = _config.TranscriptionBaseUrl
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        var lang = string.IsNullOrWhiteSpace(_config.TranscriptionLanguage) ? "unknown" : _config.TranscriptionLanguage!;
        var url = $"{baseWs}/speech-to-text/ws" +
                  $"?model={Uri.EscapeDataString(_config.TranscriptionModel)}" +
                  $"&mode={Uri.EscapeDataString(mode)}" +
                  $"&language-code={Uri.EscapeDataString(lang)}" +
                  $"&sample_rate=16000&input_audio_codec=wav";

        Log($"CONNECT {url}");
        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("api-subscription-key", key);
        await ws.ConnectAsync(new Uri(url), ct);
        Log($"CONNECTED state={ws.State}");

        _ws = ws;
        _receiveCts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(ws, _receiveCts.Token));
    }

    public async Task SendAudioAsync(byte[] pcm, CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null || ws.State != WebSocketState.Open || pcm.Length == 0) return;

        // Sarvam's WS validation only accepts encoding "audio/wav" (it rejects raw pcm_s16le),
        // so each chunk is wrapped in a minimal 16k mono PCM16 WAV header before base64. The
        // header's data-size field just needs to cover this chunk's bytes.
        var wav = WrapPcmInWav(pcm);
        var msg = JsonSerializer.Serialize(new
        {
            audio = new
            {
                data = Convert.ToBase64String(wav),
                sample_rate = "16000",
                encoding = "audio/wav",
            }
        });
        var bytes = Encoding.UTF8.GetBytes(msg);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        Log($"SEND audio {pcm.Length} bytes pcm -> {wav.Length} bytes wav");
    }

    // Builds a 16 kHz, mono, 16-bit PCM WAV (44-byte canonical header) around a raw PCM chunk.
    private static byte[] WrapPcmInWav(byte[] pcm)
    {
        const int sampleRate = 16000, channels = 1, bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm.Length);            // ChunkSize
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                         // Subchunk1Size (PCM)
        w.Write((short)1);                   // AudioFormat = PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write((short)bitsPerSample);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(pcm.Length);                 // Subchunk2Size
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    public async Task<string> FinishAsync(CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null) return CurrentTranscript();

        try
        {
            if (ws.State == WebSocketState.Open)
            {
                var flush = Encoding.UTF8.GetBytes("{\"type\":\"flush\"}");
                await ws.SendAsync(flush, WebSocketMessageType.Text, true, ct);
                Log("SEND flush");
            }

            // Give the server a moment to emit the final "data" for the flushed tail. The
            // receive loop keeps updating _transcript until the socket closes or this window
            // elapses -- whichever comes first.
            var deadline = Task.Delay(TimeSpan.FromSeconds(3), ct);
            if (_receiveLoop is not null)
                await Task.WhenAny(_receiveLoop, deadline);
        }
        catch { /* network hiccup on flush -- return whatever we accumulated */ }
        finally
        {
            await CloseAndCleanupAsync();
        }

        return CurrentTranscript();
    }

    public async Task CancelAsync() => await CloseAndCleanupAsync();

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) { Log($"RECV close status={result.CloseStatus} desc={result.CloseStatusDescription}"); return; }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                Log($"RECV {sb}");
                HandleMessage(sb.ToString());
            }
        }
        catch (OperationCanceledException) { Log("RECV loop cancelled"); }
        catch (Exception ex) { Log($"RECV loop error: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "data") return;
            if (!root.TryGetProperty("data", out var data)) return;
            if (!data.TryGetProperty("transcript", out var t)) return;

            var piece = t.GetString();
            if (string.IsNullOrEmpty(piece)) return;

            string full;
            lock (_lock)
            {
                // Sarvam streams each finalized segment as its own "data" message, so segments
                // are appended (with a separating space) to build the whole utterance.
                if (_transcript.Length > 0) _transcript.Append(' ');
                _transcript.Append(piece);
                full = _transcript.ToString();
            }
            PartialReceived?.Invoke(full);
        }
        catch { /* malformed frame -- ignore */ }
    }

    private string CurrentTranscript()
    {
        lock (_lock) { return _transcript.ToString(); }
    }

    private async Task CloseAndCleanupAsync()
    {
        var ws = _ws;
        var cts = _receiveCts;
        _ws = null;
        _receiveCts = null;

        cts?.Cancel();
        if (ws is not null)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
            catch { /* already closing/closed */ }
            ws.Dispose();
        }
        cts?.Dispose();
        _receiveLoop = null;
    }

    public void Dispose()
    {
        try { _receiveCts?.Cancel(); } catch { }
        try { _ws?.Dispose(); } catch { }
        try { _receiveCts?.Dispose(); } catch { }
    }
}
