using Kivi.Core.Orb;
using Kivi.Core.Wire;

namespace Kivi.App;

/// <summary>
/// Bridges the pure <see cref="FlowEngine"/> (which drives the orb) to the real
/// <see cref="KiviServiceClient"/> STT socket. Implements the engine's <see cref="IDictationService"/>
/// seam: the engine calls Begin/RequestStop/Cancel; this service opens the WebSocket, forwards wire
/// events back into the engine's <see cref="DictationSink"/>, and surfaces the final paste text.
///
/// Mirrors the Electron LiveDictationService (src/renderer/src/orb/LiveDictationService.ts):
/// wire events (ack/interim/final/error) are mapped to engine <see cref="DictationEvent"/>s.
///
/// One <see cref="KiviServiceClient"/> per take (never reused) — matches the reference contract.
/// </summary>
public sealed class WireDictationService : IDictationService
{
    private readonly Func<KiviServiceClient> _clientFactory;
    private readonly Action<Action<byte[]>> _registerAudioSink;

    private KiviServiceClient? _client;
    private DictationSink? _sink;
    private int _generation;

    /// <summary>Raised when a take produces a final result to paste (formatted_text, fallback raw).</summary>
    public event Action<string>? PasteRequested;

    /// <param name="clientFactory">Creates a fresh KiviServiceClient per take (loopback/anonymous for MVP).</param>
    /// <param name="registerAudioSink">
    /// Called on Begin with a delegate the orchestrator routes each captured PCM frame into
    /// (so audio flows mic → orchestrator → this delegate → client.SendAudio).
    /// </param>
    public WireDictationService(Func<KiviServiceClient> clientFactory, Action<Action<byte[]>> registerAudioSink)
    {
        _clientFactory = clientFactory;
        _registerAudioSink = registerAudioSink;
    }

    public void Begin(TakeKind kind, TakeContext context, bool renderActive, DictationSink sink)
    {
        _sink = sink;
        int gen = ++_generation;

        var client = _clientFactory();
        _client = client;

        // Wire events → engine DictationEvents (generation-guarded: a stale take's events are dropped).
        client.Ack += sessionId => Emit(gen, new DictationEvent.Opened(sessionId));
        client.SpeechStart += () => Emit(gen, new DictationEvent.SpeechStart());
        client.Interim += a => Emit(gen, new DictationEvent.Segment(a.SegmentIdx, a.Text));
        client.EosAck += a => Emit(gen, new DictationEvent.FormattingBudget(a.RawWords, a.ExpectedFormatMs ?? 0));
        client.FormattingProgress += a => Emit(gen, new DictationEvent.FormattingProgress(a.ElapsedMs, a.ExpectedFormatMs ?? 0));
        client.Final += payload =>
        {
            var result = new TakeResult
            {
                RawSegments = { payload.RawTranscript ?? string.Empty },
                FinalLines = { payload.PasteText },
            };
            Emit(gen, new DictationEvent.Final(result));
            if (!string.IsNullOrEmpty(payload.PasteText))
                PasteRequested?.Invoke(payload.PasteText);
        };
        client.Error += e => Emit(gen, new DictationEvent.Failure(MapError(e.Code)));
        client.Closed += _ => { /* engine already has final/failure; nothing to do for MVP */ };

        // Let audio start flowing; frames sent before the handshake are buffered + flushed in order.
        _registerAudioSink(client.SendAudio);

        // Open the socket (handshake → context). Fire-and-forget; failures surface via Error/Closed.
        _ = OpenAsync(client, gen);
    }

    private async Task OpenAsync(KiviServiceClient client, int gen)
    {
        try
        {
            await client.OpenAsync().ConfigureAwait(false);
        }
        catch (WireException wex)
        {
            Emit(gen, new DictationEvent.Failure(MapError(wex.Code)));
        }
        catch
        {
            Emit(gen, new DictationEvent.Failure(new TakeFailure.Network(KeepSegments: true)));
        }
    }

    public void RequestStop(EndOfSpeechInfo info) => _client?.Stop();   // drains audio, then EOS

    public void Cancel(CancelReason? reason = null)
    {
        _generation++; // void any in-flight events
        var c = _client;
        _client = null;
        _ = c?.CancelAsync();
    }

    public void Tick(double now) { /* the client is event-driven; nothing per-frame for MVP */ }

    public void ResyncRender() { /* no-op for MVP (orb re-render is P4) */ }

    public bool BeginRetry(DictationSink sink) => false; // retained-audio replay is a later milestone

    public bool CanRetry => false;

    private void Emit(int gen, DictationEvent ev)
    {
        if (gen != _generation) return; // stale take — drop
        _sink?.Invoke(ev);
    }

    private static TakeFailure MapError(string code) => code switch
    {
        "EMPTY_TRANSCRIPT" => new TakeFailure.Empty(),
        "UNAUTHORIZED" => new TakeFailure.Unauthorized(),
        "USAGE_LIMIT_EXCEEDED" => new TakeFailure.UsageLimit(),
        "SERVICE_BUSY" => new TakeFailure.Busy(),
        "IDLE_TIMEOUT" => new TakeFailure.IdleTimeout(),
        _ => new TakeFailure.Server(code),
    };
}
