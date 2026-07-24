using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;

namespace Kivi.Core.Stt;

/// <summary>
/// Streaming STT over Sarvam's WebSocket endpoint (wss://api.sarvam.ai/speech-to-text/ws).
/// Audio is pushed as base64 PCM messages while the user speaks; the server returns "data"
/// messages carrying the cumulative transcript, and a "flush" finalizes the last segment.
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
                  $"&sample_rate=16000&input_audio_codec=pcm_s16le";

        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("api-subscription-key", key);
        await ws.ConnectAsync(new Uri(url), ct);

        _ws = ws;
        _receiveCts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(ws, _receiveCts.Token));
    }

    public async Task SendAudioAsync(byte[] pcm, CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null || ws.State != WebSocketState.Open || pcm.Length == 0) return;

        // NOTE: audio format is governed by the connection query (sample_rate=16000,
        // input_audio_codec=pcm_s16le). The per-message encoding here must describe the SAME
        // raw 16-bit PCM we actually send -- NOT "audio/wav" (we send headerless PCM, not a
        // WAV). If Sarvam rejects/garbles audio, this field's exact expected value is the first
        // thing to verify against a live session, since the docs example used a WAV payload.
        var msg = JsonSerializer.Serialize(new
        {
            audio = new
            {
                data = Convert.ToBase64String(pcm),
                sample_rate = "16000",
                encoding = "pcm_s16le",
            }
        });
        var bytes = Encoding.UTF8.GetBytes(msg);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
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
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                HandleMessage(sb.ToString());
            }
        }
        catch (OperationCanceledException) { /* finishing/cancelling */ }
        catch { /* socket error -- FinishAsync returns whatever was accumulated, caller falls back */ }
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
