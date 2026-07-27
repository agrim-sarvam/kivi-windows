using System.Net.WebSockets;
using Kivi.Core.Wire;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>
/// Client lifecycle over a fake socket: ack→context ordering, pre-connect buffer drain-in-order,
/// drain-before-EOS, backpressure drop-oldest, and no-auth-header on anonymous connect.
/// </summary>
public class KiviServiceClientTests
{
    private static ClientIdentity Identity => new(ClientIdentity.PlatformWindows, "0.0.0-test", "Asia/Kolkata");

    private static byte[] Frame(byte marker)
    {
        var f = new byte[DictationAudio.FrameBytes];
        f[0] = marker;
        return f;
    }

    private static async Task<KiviServiceClient> OpenAsync(FakeWebSocket fake, ContextOptions? ctx = null, string? bearer = null)
    {
        var client = new KiviServiceClient(Endpoints.Local.WebSocketUrl, Identity, ctx, bearer, () => fake);
        var open = client.OpenAsync();
        // give the receive loop a beat to start, then ack.
        await Task.Delay(20);
        fake.PushAck();
        await open;
        return client;
    }

    [Fact]
    public async Task PreConnect_Frames_Flush_In_Order_After_Context()
    {
        var fake = new FakeWebSocket();
        var client = new KiviServiceClient(Endpoints.Local.WebSocketUrl, Identity, null, null, () => fake);
        var open = client.OpenAsync();
        await Task.Delay(20);

        // Frames arrive BEFORE ack → they buffer.
        client.SendAudio(Frame(1));
        client.SendAudio(Frame(2));
        client.SendAudio(Frame(3));

        fake.PushAck();
        await open;
        await Task.Delay(50); // let the flush pump run

        // First text frame must be the context; binary frames follow in order.
        Assert.Equal("context", TypeOf(fake.TextFrames[0].Text));

        var bins = fake.BinaryFrames;
        Assert.Equal(3, bins.Count);
        Assert.Equal(1, bins[0].Data[0]);
        Assert.Equal(2, bins[1].Data[0]);
        Assert.Equal(3, bins[2].Data[0]);

        // Context is sent before any binary frame (index in the overall Sent list).
        var sent = fake.Sent;
        var ctxIdx = IndexOfType(sent, "context");
        var firstBinIdx = sent.ToList().FindIndex(f => f.Type == WebSocketMessageType.Binary);
        Assert.True(ctxIdx < firstBinIdx, "context must precede the first audio frame");

        await client.DisposeAsync();
    }

    [Fact]
    public async Task Drain_Before_Eos_All_Audio_Precedes_EndOfSpeech()
    {
        var fake = new FakeWebSocket();
        var client = await OpenAsync(fake);

        client.SendAudio(Frame(10));
        client.SendAudio(Frame(11));
        await Task.Delay(30);
        client.Stop();
        await Task.Delay(50);

        var sent = fake.Sent;
        var eosIdx = IndexOfType(sent, "end_of_speech");
        Assert.True(eosIdx >= 0, "EOS must be sent");
        // Every binary (audio) frame must appear before EOS.
        for (int i = 0; i < sent.Count; i++)
            if (sent[i].Type == WebSocketMessageType.Binary)
                Assert.True(i < eosIdx, "all audio must drain before end_of_speech");

        await client.DisposeAsync();
    }

    [Fact]
    public async Task Backpressure_Drops_Oldest_Past_Cap()
    {
        var fake = new FakeWebSocket();
        var client = new KiviServiceClient(Endpoints.Local.WebSocketUrl, Identity, null, null, () => fake);
        var open = client.OpenAsync();
        await Task.Delay(20);

        // Enqueue 55 frames before ack (cap 50) ⇒ 5 oldest dropped.
        for (int i = 0; i < 55; i++) client.SendAudio(Frame((byte)i));
        Assert.Equal(5, client.DroppedFrames);

        fake.PushAck();
        await open;
        await Task.Delay(80);

        var bins = fake.BinaryFrames;
        Assert.Equal(50, bins.Count);
        // Oldest 5 (markers 0..4) dropped ⇒ first surviving frame is marker 5.
        Assert.Equal(5, bins[0].Data[0]);
        Assert.Equal(54, bins[^1].Data[0]);

        await client.DisposeAsync();
    }

    [Fact]
    public async Task Anonymous_Connect_Omits_Authorization_Header()
    {
        // Use a header-capturing fake.
        var captured = new Dictionary<string, string>();
        var fake = new HeaderCapturingFake(captured);
        var client = new KiviServiceClient(Endpoints.Local.WebSocketUrl, Identity, null, bearer: null, () => fake);
        var open = client.OpenAsync();
        await Task.Delay(20);
        fake.PushAck();
        await open;

        Assert.False(captured.ContainsKey("Authorization"));
        Assert.Equal(ClientIdentity.PlatformWindows, captured["X-Client-Platform"]);
        Assert.Equal("Asia/Kolkata", captured["X-Client-Timezone"]);

        await client.DisposeAsync();
    }

    [Fact]
    public async Task NonAnonymous_Connect_Sends_Bearer()
    {
        var captured = new Dictionary<string, string>();
        var fake = new HeaderCapturingFake(captured);
        var client = new KiviServiceClient(Endpoints.Prod.WebSocketUrl, Identity, null, bearer: "jwt-abc", () => fake);
        var open = client.OpenAsync();
        await Task.Delay(20);
        fake.PushAck();
        await open;

        Assert.Equal("Bearer jwt-abc", captured["Authorization"]);

        await client.DisposeAsync();
    }

    [Fact]
    public async Task Final_Resolves_FinalTask_With_FormattedText()
    {
        var fake = new FakeWebSocket();
        var client = await OpenAsync(fake);
        client.Stop();
        fake.PushText("{\"type\":\"final\",\"formatted_text\":\"Done.\",\"raw_transcript\":\"done\"}");
        var final = await client.FinalTask;
        Assert.Equal("Done.", final.FormattedText);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Interim_Event_Fires_For_Settled_Segment()
    {
        var fake = new FakeWebSocket();
        var client = await OpenAsync(fake);
        InterimEventArgs? got = null;
        client.Interim += a => got = a;
        fake.PushText("{\"type\":\"interim\",\"segment_idx\":1,\"text\":\"hello there\"}");
        await Task.Delay(30);
        Assert.NotNull(got);
        Assert.Equal("hello there", got!.Value.Text);
        Assert.Equal(1, got.Value.SegmentIdx);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Error_Frame_Faults_FinalTask()
    {
        var fake = new FakeWebSocket();
        var client = await OpenAsync(fake);
        fake.PushText("{\"type\":\"error\",\"code\":\"SERVICE_BUSY\",\"message\":\"busy\"}");
        var ex = await Assert.ThrowsAsync<WireException>(() => client.FinalTask);
        Assert.Equal("SERVICE_BUSY", ex.Code);
        await client.DisposeAsync();
    }

    // --- helpers ---

    private static string TypeOf(string json) =>
        System.Text.Json.Nodes.JsonNode.Parse(json)!["type"]!.GetValue<string>();

    private static int IndexOfType(IReadOnlyList<FakeWebSocket.SentFrame> sent, string type)
    {
        for (int i = 0; i < sent.Count; i++)
        {
            if (sent[i].Type != WebSocketMessageType.Text) continue;
            try { if (TypeOf(sent[i].Text) == type) return i; } catch { }
        }
        return -1;
    }

    private sealed class HeaderCapturingFake : IWebSocketConnection
    {
        private readonly Dictionary<string, string> _captured;
        private readonly FakeWebSocket _inner = new();
        public HeaderCapturingFake(Dictionary<string, string> captured) => _captured = captured;
        public WebSocketState State => _inner.State;
        public WebSocketCloseStatus? CloseStatus => _inner.CloseStatus;
        public Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        {
            foreach (var (k, v) in headers) _captured[k] = v;
            return _inner.ConnectAsync(uri, headers, ct);
        }
        public Task SendAsync(ReadOnlyMemory<byte> b, WebSocketMessageType t, bool e, CancellationToken ct) => _inner.SendAsync(b, t, e, ct);
        public Task<(WebSocketReceiveResult, byte[])> ReceiveMessageAsync(CancellationToken ct) => _inner.ReceiveMessageAsync(ct);
        public Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => _inner.CloseAsync(s, d, ct);
        public void Dispose() => _inner.Dispose();
        public void PushAck(string sessionId = "srv-session") => _inner.PushAck(sessionId);
    }
}
