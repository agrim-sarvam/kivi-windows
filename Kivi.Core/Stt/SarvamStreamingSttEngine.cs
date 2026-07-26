using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;

namespace Kivi.Core.Stt;

/// <summary>
/// Real-time streaming STT over Sarvam's realtime WebSocket
/// (wss://api.sarvam.ai/speech-to-text-realtime/ws, model saaras:v3-realtime). Protocol taken
/// from Sarvam's own cookbook (Realtime_Speech_Captioning):
///   - headerless raw 16k mono PCM16 is sent as {"event":"audio_input","audio":"<base64>"}
///     (encoding=linear16 is declared in the connection query, so no WAV header per chunk);
///   - the server emits {"event":"transcript.partial"|"transcript.final","text":"..."} as VAD
///     segments speech; "flush" force-finalizes the current segment (sent periodically so
///     captions appear even during continuous speech), "end" closes the session.
///
/// stream_type=fast + endpointing=vad + a short silence window are what make it feel live
/// rather than arriving seconds late. The realtime model is beta/gated on Sarvam's side; if the
/// socket can't open, the orchestrator falls back to batch REST transcription.
///
/// Keeps NO cross-session state beyond the single in-flight socket. Never logs transcript text
/// or the API key.
/// </summary>
public sealed class SarvamStreamingSttEngine : IStreamingSttEngine, IDisposable
{
    private readonly AppConfig _config;
    private readonly ISecretStore _secrets;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;
    // Completes the instant the server acknowledges end-of-session (session.end) or the socket
    // closes, so FinishAsync returns as soon as the final transcript is in -- not after a fixed
    // timeout. Recreated per session in StartAsync.
    private TaskCompletionSource _sessionDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // Finalized segments (from transcript.final) joined into the committed transcript, plus the
    // latest in-progress partial for live display. Final result = _finalized only.
    private readonly StringBuilder _finalized = new();
    private string _currentPartial = "";
    private readonly object _lock = new();

    // Temporary diagnostics: when %APPDATA%\Kivi\stream-debug.on exists, connection/send/receive
    // events are appended to stream-debug.log. Opt-in local debug file the user creates
    // deliberately. Remove once streaming partials are confirmed working.
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

        lock (_lock) { _finalized.Clear(); _currentPartial = ""; }
        _sessionDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var baseWs = _config.TranscriptionBaseUrl
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        // "unknown" (our auto-detect sentinel) maps to the realtime endpoint's "auto".
        var lang = string.IsNullOrWhiteSpace(_config.TranscriptionLanguage) ? "auto" : _config.TranscriptionLanguage!;
        var url = $"{baseWs}/speech-to-text-realtime/ws" +
                  $"?model=saaras:v3-realtime" +
                  $"&language_code={Uri.EscapeDataString(lang)}" +
                  $"&mode={Uri.EscapeDataString(mode)}" +
                  $"&stream_type=fast" +
                  $"&endpointing=vad" +
                  $"&silence_duration_ms=300" +
                  $"&min_speech_duration_ms=150" +
                  $"&encoding=linear16" +
                  $"&sample_rate=16000" +
                  $"&return_timestamps=false";

        Log($"CONNECT {url}");
        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("API-SUBSCRIPTION-KEY", key);
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

        // Headerless raw PCM -- encoding=linear16/sample_rate=16000 are declared in the query,
        // so the payload is the raw sample bytes (no WAV header per chunk).
        var msg = JsonSerializer.Serialize(new
        {
            @event = "audio_input",
            audio = Convert.ToBase64String(pcm),
        });
        await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, endOfMessage: true, ct);
        Log($"SEND audio_input {pcm.Length} bytes");
    }

    // Periodic mid-stream flush so a caption appears even during long, pause-free speech (the
    // orchestrator calls this on an interval while the user is still holding the key).
    public async Task FlushAsync(CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null || ws.State != WebSocketState.Open) return;
        await ws.SendAsync(Encoding.UTF8.GetBytes("{\"event\":\"flush\"}"), WebSocketMessageType.Text, true, ct);
        Log("SEND flush");
    }

    public async Task<string> FinishAsync(CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null) return CurrentFinal();

        try
        {
            if (ws.State == WebSocketState.Open)
            {
                // Flush the last buffered audio, then end the session; the server emits the
                // final transcript.final(s) before closing.
                await ws.SendAsync(Encoding.UTF8.GetBytes("{\"event\":\"flush\"}"), WebSocketMessageType.Text, true, ct);
                await ws.SendAsync(Encoding.UTF8.GetBytes("{\"event\":\"end\"}"), WebSocketMessageType.Text, true, ct);
                Log("SEND flush+end");
            }

            // Return as soon as the server signals session.end (the final transcript is in by
            // then) -- the 3s is only a safety cap for a socket that never acknowledges.
            var deadline = Task.Delay(TimeSpan.FromSeconds(3), ct);
            await Task.WhenAny(_sessionDone.Task, deadline);
        }
        catch { /* network hiccup -- return whatever finalized so far */ }
        finally
        {
            await CloseAndCleanupAsync();
        }

        return CurrentFinal();
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
        finally
        {
            // Whatever ends the loop (close, cancel, error), release FinishAsync so it never
            // waits out the full safety timeout when no session.end ack arrives.
            _sessionDone.TrySetResult();
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var evProp)) return;
            var ev = evProp.GetString();
            // The server acks end-of-session here; unblock FinishAsync immediately (the final
            // transcript has already been delivered by this point).
            if (ev == "session.end") { _sessionDone.TrySetResult(); return; }
            if (ev != "transcript.partial" && ev != "transcript.final") return;

            var text = (root.TryGetProperty("text", out var t) ? t.GetString() : null)?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            string live;
            lock (_lock)
            {
                if (ev == "transcript.final")
                {
                    // Commit this utterance and clear the in-progress partial.
                    if (_finalized.Length > 0) _finalized.Append(' ');
                    _finalized.Append(text);
                    _currentPartial = "";
                }
                else
                {
                    // Partial = the growing current utterance (replaces, not appends).
                    _currentPartial = text;
                }
                // Live display = committed finals + the current partial.
                live = _currentPartial.Length > 0
                    ? (_finalized.Length > 0 ? _finalized + " " + _currentPartial : _currentPartial)
                    : _finalized.ToString();
            }
            PartialReceived?.Invoke(live);
        }
        catch { /* malformed frame -- ignore */ }
    }

    private string CurrentFinal()
    {
        lock (_lock)
        {
            // On finish, fold any still-uncommitted partial into the result so a final
            // utterance that never got its own transcript.final isn't lost.
            if (_currentPartial.Length > 0)
                return _finalized.Length > 0 ? _finalized + " " + _currentPartial : _currentPartial;
            return _finalized.ToString();
        }
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
