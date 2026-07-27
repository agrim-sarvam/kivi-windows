using System;
using Kivi.Core.Contracts;
using Kivi.Core.Orb;
using Kivi.Core.Wire;

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

    public DictationOrchestrator(
        IHotkeyService hotkey,
        IAudioCapture audio,
        IPasteService paste,
        IFrontmostApp frontmost)
    {
        _hotkey = hotkey;
        _audio = audio;
        _paste = paste;
        _frontmost = frontmost;

        // The wire bridge creates one KiviServiceClient per take against the local (anonymous) endpoint.
        _wire = new WireDictationService(
            clientFactory: CreateClient,
            registerAudioSink: sink => _audioSink = sink);
        _wire.PasteRequested += OnPasteRequested;

        // The engine drives the orb and pulls STT through the wire bridge.
        _engine = new FlowEngine(dictation: _wire);

        _audio.Frame += OnAudioFrame;
        _hotkey.Edge += OnHotkeyEdge;
    }

    /// The live orb engine (rendered by FlowRuntime in the non-demo path).
    public FlowEngine Engine => _engine;

    public void Start() => _hotkey.Start();

    private static KiviServiceClient CreateClient()
    {
        var endpoint = Endpoints.Local; // ws://127.0.0.1:8788 — anonymous on loopback
        var identity = new ClientIdentity(
            ClientIdentity.PlatformWindows,
            ClientIdentity.DefaultVersion,
            TimeZoneInfo.Local.Id);
        return new KiviServiceClient(endpoint.WebSocketUrl, identity, bearer: null);
    }

    private void OnHotkeyEdge(GestureEdge edge)
    {
        switch (edge.Kind)
        {
            case GestureEdgeKind.Down:
                // Capture the paste target BEFORE the orb can take focus.
                _target = _frontmost.Current;
                _capturing = true;
                _audio.Start();                 // mic frames begin flowing (buffered until handshake)
                _engine.FnDown();               // engine → wire.Begin → connect + context
                break;

            case GestureEdgeKind.Up:
                _capturing = false;
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

    private async void OnPasteRequested(string text)
    {
        var meta = new PasteMeta(IsTerminal: IsTerminal(_target), IsSecureField: false);
        await _paste.InsertAsync(text, meta).ConfigureAwait(false);
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
