using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Kivi.Core.Wire;

// Faithful port of _reference/.../src/main/wire/KiviServiceClient.ts onto System.Net.WebSockets.
// See docs/maps/service-client-wire.md §4 and dictation-audio-pipeline.md §6.
// One instance per take, never reused.
//
// Lifecycle (exact):
//   ConnectAsync (X-Client-* upgrade headers; Authorization only when non-anonymous)
//   → await ack (≤4000ms else fail) → send context IMMEDIATELY → flush pre-connect buffer in order
//   → stream binary PCM frames + {"type":"ping"} every 20000ms
//   → on Stop: DRAIN pending audio THEN send {"type":"end_of_speech"}
//   → await final (≤20000ms, extendable on eos_ack).
// cancel() sends {"type":"cancel"} and does NOT drain (it preempts).

/// <summary>Args for the <see cref="KiviServiceClient.Interim"/> event.</summary>
public readonly record struct InterimEventArgs(int SegmentIdx, string Text, double? LatencyMs);

/// <summary>Args for <see cref="KiviServiceClient.EosAck"/>.</summary>
public readonly record struct EosAckEventArgs(int? RawWords, double? ExpectedFormatMs);

/// <summary>Args for <see cref="KiviServiceClient.FormattingProgress"/>.</summary>
public readonly record struct FormattingProgressEventArgs(double? ElapsedMs, double? ExpectedFormatMs);

/// <summary>Args for <see cref="KiviServiceClient.Error"/>.</summary>
public readonly record struct ServiceErrorEventArgs(string Code, string? Message);

/// <summary>Abstraction over ClientWebSocket so the client is unit-testable without a live socket.</summary>
public interface IWebSocketConnection : IDisposable
{
    WebSocketState State { get; }
    Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken ct);
    Task SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct);
    Task<(WebSocketReceiveResult result, byte[] data)> ReceiveMessageAsync(CancellationToken ct);
    Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct);
    WebSocketCloseStatus? CloseStatus { get; }
}

/// <summary>Default <see cref="IWebSocketConnection"/> backed by <see cref="ClientWebSocket"/>.</summary>
public sealed class ClientWebSocketConnection : IWebSocketConnection
{
    private readonly ClientWebSocket _ws = new();

    public WebSocketState State => _ws.State;
    public WebSocketCloseStatus? CloseStatus => _ws.CloseStatus;

    public async Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        foreach (var (k, v) in headers)
            _ws.Options.SetRequestHeader(k, v);
        // Keepalive is application-level (ping/pong text frames), not a WS control frame.
        _ws.Options.KeepAliveInterval = TimeSpan.Zero;
        await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);
    }

    public Task SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        => _ws.SendAsync(buffer, type, endOfMessage, ct).AsTask();

    public async Task<(WebSocketReceiveResult, byte[])> ReceiveMessageAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return (result, Array.Empty<byte>());
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return (result, ms.ToArray());
    }

    public Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
        => _ws.CloseAsync(status, description, ct);

    public void Dispose() => _ws.Dispose();
}

/// <summary>The STT dictation client. See file header for the lifecycle contract.</summary>
public sealed class KiviServiceClient : IAsyncDisposable
{
    private readonly Uri _wsUrl;
    private readonly ClientIdentity _identity;
    private readonly ContextOptions _ctx;
    private readonly string? _bearer; // null ⇒ anonymous (omit Authorization)
    private readonly Func<IWebSocketConnection> _connFactory;

    private IWebSocketConnection? _ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // Pre-connect buffer: frames captured before ack; flushed in order after context.
    private readonly object _preLock = new();
    private readonly LinkedList<byte[]> _preBuffer = new();
    private bool _acked;
    private bool _stopped;
    private bool _pendingStop;
    private bool _closedEmitted;
    private int _droppedFrames;

    // Ping keepalive / dead-socket detection.
    private volatile bool _everPonged;
    private int _pongMisses;

    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveLoop;
    private Task? _pingLoop;
    private readonly TaskCompletionSource<string> _ackTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<FinalPayload> _finalTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _finalTimeoutCts;

    /// <summary>Client-generated session id (used for the context message).</summary>
    public string SessionId { get; } = Guid.NewGuid().ToString();

    /// <summary>Server-echoed session id from the ack (may differ from <see cref="SessionId"/>).</summary>
    public string? ServerSessionId { get; private set; }

    /// <summary>Number of oldest frames dropped for backpressure (past the 50-frame cap).</summary>
    public int DroppedFrames => Volatile.Read(ref _droppedFrames);

    // Events mirror the TS EventEmitter.
    public event Action<string>? Ack;
    public event Action? SpeechStart;
    public event Action<InterimEventArgs>? Interim;
    public event Action<EosAckEventArgs>? EosAck;
    public event Action<FormattingProgressEventArgs>? FormattingProgress;
    public event Action<FinalPayload>? Final;
    public event Action<ServiceErrorEventArgs>? Error;
    public event Action<int>? Closed;

    public KiviServiceClient(
        Uri wsUrl,
        ClientIdentity identity,
        ContextOptions? ctx = null,
        string? bearer = null,
        Func<IWebSocketConnection>? connectionFactory = null)
    {
        _wsUrl = wsUrl;
        _identity = identity;
        _ctx = ctx ?? new ContextOptions();
        _bearer = bearer;
        _connFactory = connectionFactory ?? (() => new ClientWebSocketConnection());
    }

    /// <summary>Await this to know the handshake completed (resolves on ack, faults on failure).</summary>
    public Task<string> AckTask => _ackTcs.Task;

    /// <summary>Await this for the single <c>final</c> result (faults on error / timeout / close).</summary>
    public Task<FinalPayload> FinalTask => _finalTcs.Task;

    /// <summary>Connect + run the handshake. Returns once <c>ack</c> arrives (or throws on failure).</summary>
    public async Task<string> OpenAsync(CancellationToken ct = default)
    {
        _ws = _connFactory();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Client-Platform"] = _identity.Platform,
            ["X-Client-Version"] = _identity.Version,
            ["X-Client-Timezone"] = _identity.Timezone,
        };
        if (!string.IsNullOrEmpty(_bearer))
            headers["Authorization"] = $"Bearer {_bearer}";

        try
        {
            await _ws.ConnectAsync(_wsUrl, headers, _cts.Token).ConfigureAwait(false);
        }
        catch (WebSocketException wex)
        {
            // 401/403 on the upgrade surfaces here distinctly from a plain network drop.
            Fail("STT_CONNECT_FAILED", wex.Message);
            throw;
        }

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        // ack budget.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var ackTimeout = Task.Delay(DictationBudgets.AckTimeoutMs, linked.Token);
        var completed = await Task.WhenAny(_ackTcs.Task, ackTimeout).ConfigureAwait(false);
        if (completed == ackTimeout && !_ackTcs.Task.IsCompleted)
        {
            Fail("ACK_TIMEOUT", "no ack within budget");
            throw new TimeoutException("no ack within budget");
        }
        return await _ackTcs.Task.ConfigureAwait(false);
    }

    /// <summary>Queue/flush a raw PCM frame (Int16 mono 16k, ~3200 bytes).</summary>
    public void SendAudio(byte[] frame)
    {
        if (_stopped) return;
        // TOCTOU-safe drain-then-flip: while not acked (or socket not open), buffer; else direct-send.
        if (!_acked || _ws is null || _ws.State != WebSocketState.Open)
        {
            lock (_preLock)
            {
                if (!_acked) // re-check under lock — the flush flips _acked while holding _preLock
                {
                    _preBuffer.AddLast(frame);
                    if (_preBuffer.Count > DictationBudgets.MaxPendingAudioFrames)
                    {
                        _preBuffer.RemoveFirst(); // drop OLDEST (backpressure)
                        Interlocked.Increment(ref _droppedFrames);
                    }
                    return;
                }
            }
        }
        _ = SendBinaryAsync(frame);
    }

    /// <summary>Drain queued audio then send end_of_speech; await final.</summary>
    public void Stop()
    {
        if (_stopped) return;
        if (!_acked)
        {
            _pendingStop = true; // EOS after flush on ack
            return;
        }
        _ = DoStopAsync();
    }

    /// <summary>Abandon the take: send {"type":"cancel"} (does NOT drain) then close.</summary>
    public async Task CancelAsync()
    {
        _stopped = true;
        CancelTimers();
        try { await SendTextAsync(WireEncoder.Encode(WireEncoder.BuildCancel())).ConfigureAwait(false); }
        catch { /* ignore */ }
        await CloseSocketAsync().ConfigureAwait(false);
    }

    private async Task DoStopAsync()
    {
        _stopped = true;
        // Audio frames were sent live (ordered) already; EOS serializes behind them via _sendLock,
        // so all queued/in-flight binary sends complete before the EOS text frame goes out.
        try { await SendTextAsync(WireEncoder.Encode(WireEncoder.BuildEndOfSpeech())).ConfigureAwait(false); }
        catch { /* ignore */ }
        ArmFinalTimeout(DictationBudgets.FinalTimeoutMs);
    }

    private void ArmFinalTimeout(int ms)
    {
        _finalTimeoutCts?.Cancel();
        _finalTimeoutCts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _finalTimeoutCts = cts;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(ms, cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            Fail("FINAL_TIMEOUT", "no final within budget");
        });
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _ws is not null)
            {
                var (result, data) = await _ws.ReceiveMessageAsync(ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    EmitClosed((int)(_ws.CloseStatus ?? WebSocketCloseStatus.NormalClosure));
                    return;
                }
                if (result.MessageType == WebSocketMessageType.Binary) continue; // server never sends binary
                OnText(Encoding.UTF8.GetString(data));
            }
        }
        catch (OperationCanceledException) { /* teardown */ }
        catch (Exception e)
        {
            Fail("WS_ERROR", e.Message);
        }
    }

    private void OnText(string raw)
    {
        var msg = WireDecoder.Decode(raw);
        if (msg is null) return;
        switch (msg.Kind)
        {
            case ServerMessageKind.Ack:
                OnAck(msg.SessionId);
                break;
            case ServerMessageKind.SpeechStart:
                SpeechStart?.Invoke();
                break;
            case ServerMessageKind.Interim:
                // Ok carries is_final (absent ⇒ true). No partial rendering: skip is_final:false.
                if (msg.Ok == true && msg.Text is not null)
                    Interim?.Invoke(new InterimEventArgs(msg.SegmentIdx, msg.Text, msg.LatencyMs));
                break;
            case ServerMessageKind.EosAck:
                EosAck?.Invoke(new EosAckEventArgs(msg.RawWords, msg.ExpectedFormatMs));
                // long-form formatter estimate can exceed the base budget; extend.
                if (msg.ExpectedFormatMs is { } efm)
                    ArmFinalTimeout((int)Math.Max(DictationBudgets.FinalTimeoutMs, efm + 8000));
                break;
            case ServerMessageKind.FormattingProgress:
                FormattingProgress?.Invoke(new FormattingProgressEventArgs(msg.ElapsedMs, msg.ExpectedFormatMs));
                break;
            case ServerMessageKind.Final:
                CancelTimers();
                if (msg.Final is { } fp)
                {
                    _finalTcs.TrySetResult(fp);
                    Final?.Invoke(fp);
                }
                _ = CloseSocketAsync();
                break;
            case ServerMessageKind.Error:
                Fail(msg.ErrorCode ?? "ERROR", msg.ErrorMessage);
                break;
            case ServerMessageKind.Pong:
                _everPonged = true;
                Interlocked.Exchange(ref _pongMisses, 0);
                break;
            case ServerMessageKind.RouteHint:
            case ServerMessageKind.AuthRefreshAck:
            case ServerMessageKind.Unknown:
            default:
                break; // ignore
        }
    }

    private void OnAck(string? serverSessionId)
    {
        _acked = true;
        ServerSessionId = serverSessionId ?? SessionId;

        // Context MUST be on the wire before ANY audio ("Expected context message first").
        // Send context, then flush the pre-connect buffer — all under _sendLock so ordering holds.
        _ = FlushHandshakeAsync();

        _ackTcs.TrySetResult(ServerSessionId);
        Ack?.Invoke(ServerSessionId);
    }

    private async Task FlushHandshakeAsync()
    {
        // 1. context first.
        try { await SendTextAsync(WireEncoder.Encode(WireEncoder.BuildContext(SessionId, _ctx))).ConfigureAwait(false); }
        catch { /* ignore */ }

        // 2. drain the pre-connect buffer in order, then flip _acked-gated buffering off (TOCTOU-safe).
        while (true)
        {
            byte[] frame;
            lock (_preLock)
            {
                if (_preBuffer.Count == 0)
                {
                    // Queue empty AND _acked already true ⇒ SendAudio now direct-sends. Nothing can
                    // be enqueued after this point because SendAudio re-checks _acked under _preLock.
                    break;
                }
                frame = _preBuffer.First!.Value;
                _preBuffer.RemoveFirst();
            }
            await SendBinaryAsync(frame).ConfigureAwait(false);
        }

        StartPing();
        if (_pendingStop) await DoStopAsync().ConfigureAwait(false);
    }

    private void StartPing()
    {
        _pingLoop = Task.Run(async () =>
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(DictationBudgets.PingIntervalMs, _cts.Token).ConfigureAwait(false);
                    if (_ws is null || _ws.State != WebSocketState.Open) return;
                    try { await SendTextAsync(WireEncoder.Encode(WireEncoder.BuildPing())).ConfigureAwait(false); }
                    catch { /* ignore */ }
                    // Dead-socket detection GATED on ever having received a pong.
                    if (_everPonged)
                    {
                        var misses = Interlocked.Increment(ref _pongMisses);
                        if (misses >= DictationBudgets.PongMissLimit)
                        {
                            Fail("SOCKET_DEAD", "pong miss limit");
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { /* teardown */ }
        });
    }

    private async Task SendTextAsync(string json)
    {
        if (_ws is null) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
        try { await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false); }
        finally { _sendLock.Release(); }
    }

    private async Task SendBinaryAsync(byte[] frame)
    {
        if (_ws is null) return;
        await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
        try { await _ws.SendAsync(frame, WebSocketMessageType.Binary, true, _cts.Token).ConfigureAwait(false); }
        finally { _sendLock.Release(); }
    }

    private void Fail(string code, string? message)
    {
        if (_closedEmitted) return;
        CancelTimers();
        Error?.Invoke(new ServiceErrorEventArgs(code, message));
        _ackTcs.TrySetException(new WireException(code, message));
        _finalTcs.TrySetException(new WireException(code, message));
        _ = CloseSocketAsync();
    }

    private void EmitClosed(int code)
    {
        if (_closedEmitted) return;
        _closedEmitted = true;
        CancelTimers();
        _ackTcs.TrySetException(new WireException("CLOSED", $"socket closed ({code})"));
        _finalTcs.TrySetException(new WireException("CLOSED", $"socket closed ({code})"));
        Closed?.Invoke(code);
    }

    private void CancelTimers()
    {
        try { _finalTimeoutCts?.Cancel(); } catch { /* ignore */ }
    }

    private async Task CloseSocketAsync()
    {
        try
        {
            if (_ws is not null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived))
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
        }
        catch { /* ignore */ }
        finally
        {
            if (!_closedEmitted) EmitClosed((int)WebSocketCloseStatus.NormalClosure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { if (_receiveLoop is not null) await _receiveLoop.ConfigureAwait(false); } catch { /* ignore */ }
        try { if (_pingLoop is not null) await _pingLoop.ConfigureAwait(false); } catch { /* ignore */ }
        _finalTimeoutCts?.Dispose();
        _cts.Dispose();
        _sendLock.Dispose();
        _ws?.Dispose();
    }
}

/// <summary>A wire-protocol failure (carries the service error code).</summary>
public sealed class WireException : Exception
{
    public string Code { get; }
    public WireException(string code, string? message) : base(message ?? code) => Code = code;
}
