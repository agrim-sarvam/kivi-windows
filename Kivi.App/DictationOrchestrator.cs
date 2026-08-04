using System;
using Kivi.App.Services;
using Kivi.Core.Contracts;
using Kivi.Core.Orb;
using Kivi.Core.Wire;
using Kivi.Platform.Auth;

namespace Kivi.App;

/// <summary>
/// The single owner of the dictation loop (M0). Was OrbHost (Swift) / DictationController (Electron).
///
/// Loop: hotkey down → capture the frontmost app + start the FlowEngine take (which starts the
/// mic via the wire bridge) → stream PCM frames to the STT socket → hotkey up → engine stops the
/// take (drain → end_of_speech) → on final, paste the formatted text into the captured target.
///
/// The pure <see cref="FlowEngine"/> owns take state + timing (the 420/450/600ms gesture rules live
/// in its FnDown/FnUp transitions); <see cref="WireDictationService"/> bridges it to the real socket;
/// the platform seams (hotkey/mic/paste/frontmost) are injected. The orb *visuals* (rendering the
/// engine's FlowFrame) are P4 — for M0 the loop is functional/headless with a stub indicator.
/// </summary>
public sealed class DictationOrchestrator : IDisposable
{
    private readonly IHotkeyService _hotkey;
    private readonly IAudioCapture _audio;
    private readonly IPasteService _paste;
    private readonly IFrontmostApp _frontmost;

    private readonly FlowEngine _engine;
    private readonly WireDictationService _wire;

    // The current take's audio sink (set by the wire bridge on Begin; frames route here).
    private Action<byte[]>? _audioSink;
    // The paste target captured at key-down (paste must land in the app the user was in, not Kivi).
    private AppTarget? _target;
    private bool _capturing;

    // Endpoint + bearer selection (set by AuthController wiring in App.xaml.cs): signed-in -> Qa
    // endpoint + minted org JWT bearer; skipped/anonymous -> Local endpoint + null bearer (today's
    // default). Read once per take at FnDown, mirroring how _target is captured at key-down.
    private readonly AuthController? _auth;

    // Persists every completed take (text + captured app + timestamp) for the History/Record pages.
    // Optional so the app still builds/runs before DI registration is added (a later integration step).
    private readonly IDictationHistoryStore? _history;

    // Observation center (optional): records per-take TTFT + end-to-end latency + errors. TTFT is
    // key-release → first interim; latency is key-release → final pasted. These timing anchors are
    // captured below (stopwatch stamped on Up; first-interim/paste consumed once each per take).
    private readonly ObservationRecorder? _observations;
    private long _releaseTicks;                 // Environment.TickCount64 at key-release (0 = none)
    private double? _ttftMs;                     // stamped when the first interim arrives after release

    /// <summary>True once the user has completed Google sign-in this session (vs. skipped/anonymous).</summary>
    public bool UseHostedEndpoint { get; set; }

    public DictationOrchestrator(
        IHotkeyService hotkey,
        IAudioCapture audio,
        IPasteService paste,
        IFrontmostApp frontmost,
        IFlowStore? flowStore = null,
        AuthController? auth = null,
        IDictationHistoryStore? history = null,
        ObservationRecorder? observations = null)
    {
        _hotkey = hotkey;
        _audio = audio;
        _paste = paste;
        _frontmost = frontmost;
        _auth = auth;
        _history = history;
        _observations = observations;

        // The wire bridge creates one KiviServiceClient per take. Endpoint/bearer are resolved at
        // connect time (per the AuthController's current sign-in state), not baked in at startup.
        _wire = new WireDictationService(
            clientFactory: CreateClient,
            registerAudioSink: sink => _audioSink = sink);
        _wire.PasteRequested += OnPasteRequested;
        _wire.InterimReceived += OnInterimForTiming;
        _wire.TakeFailed += OnTakeFailedForTiming;

        // The engine drives the orb and pulls STT through the wire bridge. Settings + playback
        // history persist to %APPDATA%\Kivi\flowstore.json via JsonFlowStore (falls back to
        // MemoryFlowStore when the caller doesn't provide one, e.g. in tests).
        _engine = new FlowEngine(store: flowStore, dictation: _wire);

        _audio.Frame += OnAudioFrame;
        _hotkey.Edge += OnHotkeyEdge;
    }

    /// The live orb engine (rendered by FlowRuntime in the non-demo path).
    public FlowEngine Engine => _engine;

    public void Start() => _hotkey.Start();

    /// <summary>Rebind the global talk-key to a new chord (from onboarding / settings). Live — no
    /// restart of the hook. Safe to call before or after <see cref="Start"/>.</summary>
    public void SetHotkeyChord(Kivi.Core.Hotkey.HotkeyChord chord) => _hotkey.Rebind(chord);

    // Endpoint + bearer resolved once per take (at key-down, alongside _target) so CreateClient
    // (called synchronously from WireDictationService.Begin) never has to await. Signed-in ->
    // Qa endpoint + freshly-minted org JWT; skipped/anonymous -> Local endpoint + null bearer
    // (today's default) — see AuthController/App.xaml.cs wiring.
    private KiviEndpoint _takeEndpoint = Endpoints.Local;
    private string? _takeBearer;

    private KiviServiceClient CreateClient()
    {
        var identity = new ClientIdentity(
            ClientIdentity.PlatformWindows,
            ClientIdentity.DefaultVersion,
            TimeZoneInfo.Local.Id);
        return new KiviServiceClient(_takeEndpoint.WebSocketUrl, identity, bearer: _takeBearer);
    }

    private async void OnHotkeyEdge(GestureEdge edge)
    {
        switch (edge.Kind)
        {
            case GestureEdgeKind.Down:
                // Capture the paste target BEFORE the orb can take focus.
                _target = _frontmost.Current;
                _capturing = true;

                // Resolve endpoint + bearer for this take. Signed-in -> hosted Qa endpoint with a
                // freshly-minted (auto-refreshed) org JWT; otherwise -> Local, anonymous — exactly
                // as before this auth work landed.
                if (UseHostedEndpoint && _auth is { IsSignedIn: true })
                {
                    // Prod (kivi.sarvam.ai) — the public launch service. Was Qa (internal test env);
                    // an external/colleague hand-off must ride the real prod service, which is now
                    // reachable over the public internet (no VPN) and mints against a @sarvam.ai JWT.
                    _takeEndpoint = Endpoints.Prod;
                    _takeBearer = await _auth.GetCurrentBearerAsync().ConfigureAwait(true);
                }
                else
                {
                    _takeEndpoint = Endpoints.Local;
                    _takeBearer = null;
                }

                _audio.Start();                 // mic frames begin flowing (buffered until handshake)
                _engine.FnDown();               // engine → wire.Begin → connect + context
                break;

            case GestureEdgeKind.Up:
                _capturing = false;
                // Observation timing anchor: key-release starts the TTFT + latency clocks for this take.
                _releaseTicks = Environment.TickCount64;
                _ttftMs = null;
                _ = _audio.StopAsync();          // stop mic; queued frames still drain before EOS
                _engine.FnUp();                  // engine → wire.RequestStop → drain + end_of_speech
                break;
        }
    }

    private void OnAudioFrame(byte[] frame)
    {
        if (_capturing)
            _audioSink?.Invoke(frame); // → KiviServiceClient.SendAudio (pre-handshake frames buffered)
    }

    /// TTFT anchor: the first interim AFTER key-release. Interims that stream while the user is still
    /// speaking (before release) are ignored for TTFT by the _releaseTicks>0 && _ttftMs==null guard.
    private void OnInterimForTiming(string _)
    {
        if (_releaseTicks > 0 && _ttftMs is null)
            _ttftMs = Environment.TickCount64 - _releaseTicks;
    }

    /// Record an errored take (no interim/final produced) so the observation snapshot shows failures.
    private void OnTakeFailedForTiming(string code)
    {
        _observations?.RecordTake(new Kivi.Core.Observability.TakeObservation(
            WhenUtc: DateTime.UtcNow,
            TtftMs: _ttftMs,
            LatencyMs: null,
            WordCount: 0,
            AppName: _target?.AppName,
            Error: code));
        _releaseTicks = 0;
        _ttftMs = null;
    }

    private async void OnPasteRequested(string text)
    {
        var meta = new PasteMeta(IsTerminal: IsTerminal(_target), IsSecureField: false);
        // Pass the captured target so the paste service can restore its focus if it has drifted
        // (e.g. the user looked at / clicked the now-always-visible transcript box while the take
        // was completing) — see SendInputPasteService's class doc for why this is necessary.
        await _paste.InsertAsync(text, meta, _target).ConfigureAwait(false);

        // Observation: end-to-end latency = key-release → final pasted (measured right after the paste
        // completes). Word count from the pasted text. One record per take.
        double? latency = _releaseTicks > 0 ? Environment.TickCount64 - _releaseTicks : (double?)null;
        int words = string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        _observations?.RecordTake(new Kivi.Core.Observability.TakeObservation(
            WhenUtc: DateTime.UtcNow,
            TtftMs: _ttftMs,
            LatencyMs: latency,
            WordCount: words,
            AppName: _target?.AppName,
            Error: null));
        _releaseTicks = 0;
        _ttftMs = null;

        // Record the completed take for the History/Record pages. Optional store (may be unwired
        // pre-DI); AppName/ExePath come through null when no target was captured — the page handles that.
        _history?.Add(new DictationHistoryEntry(text, RawText: null, _target?.AppName, _target?.ExePath, DateTime.UtcNow));
    }

    private static bool IsTerminal(AppTarget? target)
    {
        var exe = target?.ExePath;
        if (string.IsNullOrEmpty(exe)) return false;
        var name = System.IO.Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        return name is "windowsterminal" or "cmd" or "powershell" or "pwsh" or "conhost" or "wt";
    }

    public void Dispose()
    {
        _hotkey.Edge -= OnHotkeyEdge;
        _audio.Frame -= OnAudioFrame;
        (_hotkey as IDisposable)?.Dispose();
        (_audio as IDisposable)?.Dispose();
    }
}
