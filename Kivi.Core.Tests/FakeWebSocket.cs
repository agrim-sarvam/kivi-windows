using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Kivi.Core.Wire;

namespace Kivi.Core.Tests;

/// <summary>
/// An in-memory <see cref="IWebSocketConnection"/> that records everything the client sends (in
/// order) and lets a test push server frames. Lets us exercise the full client lifecycle with no
/// real socket.
/// </summary>
public sealed class FakeWebSocket : IWebSocketConnection
{
    public record SentFrame(WebSocketMessageType Type, byte[] Data)
    {
        public string Text => Encoding.UTF8.GetString(Data);
    }

    private readonly List<SentFrame> _sent = new();
    private readonly object _sentLock = new();
    private readonly Channel<(WebSocketReceiveResult, byte[])> _inbound =
        Channel.CreateUnbounded<(WebSocketReceiveResult, byte[])>();

    public WebSocketState State { get; private set; } = WebSocketState.None;
    public WebSocketCloseStatus? CloseStatus { get; private set; }

    /// <summary>Snapshot of frames the client has sent, in order.</summary>
    public IReadOnlyList<SentFrame> Sent
    {
        get { lock (_sentLock) return _sent.ToArray(); }
    }

    public IReadOnlyList<SentFrame> TextFrames =>
        Sent.Where(f => f.Type == WebSocketMessageType.Text).ToArray();

    public IReadOnlyList<SentFrame> BinaryFrames =>
        Sent.Where(f => f.Type == WebSocketMessageType.Binary).ToArray();

    public Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        State = WebSocketState.Open;
        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
    {
        lock (_sentLock) _sent.Add(new SentFrame(type, buffer.ToArray()));
        return Task.CompletedTask;
    }

    public async Task<(WebSocketReceiveResult result, byte[] data)> ReceiveMessageAsync(CancellationToken ct)
    {
        var (r, d) = await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
        return (r, d);
    }

    public Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
    {
        State = WebSocketState.Closed;
        CloseStatus = status;
        return Task.CompletedTask;
    }

    public void Dispose() { }

    /// <summary>Push a server text frame to the client's receive loop.</summary>
    public void PushText(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var result = new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true);
        _inbound.Writer.TryWrite((result, bytes));
    }

    /// <summary>Push a server ack (drives context + flush).</summary>
    public void PushAck(string sessionId = "srv-session") =>
        PushText($"{{\"type\":\"ack\",\"session_id\":\"{sessionId}\"}}");
}
