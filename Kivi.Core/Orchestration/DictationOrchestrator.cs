using System.Diagnostics;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;
using Kivi.Core.Diagnostics;
using Kivi.Core.History;
using Kivi.Core.Macros;
using Kivi.Core.Polish;
using Kivi.Core.Stt;

namespace Kivi.Core.Orchestration;

public sealed class DictationOrchestrator : IDictationOrchestrator
{
    private readonly IHotkeyService _hotkey;
    private readonly IAudioCaptureService _audio;
    private readonly IScreenContextProvider _context;
    private readonly ISttEngine _stt;                  // batch REST engine -- the streaming fallback
    private readonly IStreamingSttEngine _streaming;   // live WebSocket engine -- the primary path
    private readonly IPolishClient _polish;
    private readonly IPasteService _paste;
    private readonly AppConfig _config;
    private readonly KiviMetrics _metrics;
    private readonly ITranscriptStore _transcriptStore;
    private readonly object _lock = new();
    // How often the PCM pump drains newly-captured audio into the streaming socket.
    private const int PumpIntervalMs = 100;
    // How often to force a mid-stream finalize so captions appear during continuous speech.
    private const int FlushIntervalMs = 1500;

    private Task<string> _contextTask = Task.FromResult("");
    private CancellationTokenSource _cts = new();
    private CancellationTokenSource _pumpCts = new();
    private Task _pumpTask = Task.CompletedTask;
    private bool _capturing;
    private bool _streamingLive;   // true once the WebSocket session opened for this capture
    // The Sarvam STT mode for the in-flight capture, chosen by which hotkey started it:
    // primary hotkey -> Hinglish (romanized code-mix), English hotkey -> translate-to-English.
    private string _activeMode = SttMode.Hinglish;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public string? LastErrorMessage { get; private set; }

    public event Action<RecordingState>? StateChanged;
    public event Action<string>? PartialTranscriptChanged;

    public DictationOrchestrator(IHotkeyService hotkey, IAudioCaptureService audio, IScreenContextProvider context,
        ISttEngine stt, IStreamingSttEngine streaming, IPolishClient polish, IPasteService paste, AppConfig config,
        KiviMetrics metrics, ITranscriptStore transcriptStore)
    {
        (_hotkey, _audio, _context, _stt, _streaming, _polish, _paste, _config, _metrics, _transcriptStore)
           = (hotkey, audio, context, stt, streaming, polish, paste, config, metrics, transcriptStore);
        _polish.EnteringCooldown += _ => SetState(RecordingState.Waiting);
        // The streaming server returns transcript segments live -- surface them to the orb.
        _streaming.PartialReceived += t => PartialTranscriptChanged?.Invoke(t);
    }

    public void Start()
    {
        _hotkey.HoldStarted += OnHoldStarted;
        _hotkey.HoldEnded += OnHoldEnded;
        _hotkey.EnglishHoldStarted += OnEnglishHoldStarted;
        _hotkey.EnglishHoldEnded += OnEnglishHoldEnded;
        _hotkey.HandsFreeToggled += OnHandsFreeToggled;
        _hotkey.Start();
    }

    public void Stop()
    {
        _hotkey.HoldStarted -= OnHoldStarted;
        _hotkey.HoldEnded -= OnHoldEnded;
        _hotkey.EnglishHoldStarted -= OnEnglishHoldStarted;
        _hotkey.EnglishHoldEnded -= OnEnglishHoldEnded;
        _hotkey.HandsFreeToggled -= OnHandsFreeToggled;
        _hotkey.Stop();
    }

    private void SetState(RecordingState s)
    {
        lock (_lock) { State = s; }
        StateChanged?.Invoke(s);
    }

    // Primary hotkey (Right Ctrl): Hinglish dictation -- English stays English, Hindi is
    // romanized into Latin letters, all mixed in one transcript.
    private void OnHoldStarted() => BeginCapture(SttMode.Hinglish);

    // English hotkey (Right Alt): translate whatever was spoken (Hindi/other/English) into
    // proper English.
    private void OnEnglishHoldStarted() => BeginCapture(SttMode.English);

    // Double-tap a hotkey: first toggle starts a hands-free capture (key released), the next
    // toggle (a single tap of that same key) stops it. Uses the same pipeline as hold-to-talk,
    // just started/stopped by taps instead of a physical hold.
    private void OnHandsFreeToggled(bool isEnglish)
    {
        if (_capturing) EndCapture();
        else BeginCapture(isEnglish ? SttMode.English : SttMode.Hinglish);
    }

    private void BeginCapture(string mode)
    {
        if (_capturing) return; // both hotkeys held at once is unsupported -- ignore the second
        _capturing = true;
        _activeMode = mode;
        _streamingLive = false;
        _cts = new CancellationTokenSource();
        _pumpCts = new CancellationTokenSource();
        SetState(RecordingState.Listening);
        _ = _audio.StartRecordingAsync(_cts.Token);
        _contextTask = _config.ScreenContextEnabled
            ? _context.CaptureContextAsync(_cts.Token)
            : Task.FromResult("");
        _pumpTask = RunPumpLoopAsync(mode, _pumpCts.Token);
    }

    // Opens the streaming STT session and pumps live PCM into it for the duration of the hold.
    // If the socket can't open (or errors), _streamingLive stays false and the stop path falls
    // back to a single batch transcription of the whole recording.
    private async Task RunPumpLoopAsync(string mode, CancellationToken ct)
    {
        try
        {
            await _streaming.StartAsync(mode, ct);
            _streamingLive = true;
        }
        catch
        {
            _streamingLive = false; // fall back to batch on stop
            return;
        }

        try
        {
            var lastFlush = DateTime.UtcNow;
            while (!ct.IsCancellationRequested)
            {
                var pcm = _audio.ReadNewPcm();
                if (pcm.Length > 0) await _streaming.SendAudioAsync(pcm, ct);

                // Force a mid-stream finalize every ~1.5s so captions keep appearing even during
                // continuous, pause-free speech (matches Sarvam's own realtime example).
                if ((DateTime.UtcNow - lastFlush).TotalMilliseconds >= FlushIntervalMs)
                {
                    await _streaming.FlushAsync(ct);
                    lastFlush = DateTime.UtcNow;
                }

                await Task.Delay(PumpIntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { /* hold ended -> stop pumping */ }
        catch { /* send failed mid-stream -- FinishAsync/fallback handles recovery */ }
    }

    private void OnHoldEnded() => EndCapture();
    private void OnEnglishHoldEnded() => EndCapture();

    private void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _pumpCts.Cancel();
        _ = RunPipelineAsync();
    }

    private async Task RunPipelineAsync()
    {
        var total = Stopwatch.StartNew();
        try
        {
            // Send any last captured PCM before flushing, and stop the WASAPI capture. We always
            // stop the recorder (its WAV is the fallback source); its result is only used if
            // streaming didn't produce a transcript.
            try { await _pumpTask; } catch { /* pump already unwound */ }

            var recSw = Stopwatch.StartNew();
            byte[] wav = Array.Empty<byte>();
            string raw = "";

            var sttSw = Stopwatch.StartNew();
            if (_streamingLive)
            {
                // Drain the final PCM the pump loop may not have sent, then flush and collect the
                // near-instant final transcript. Stop the recorder in parallel so its WAV is ready
                // as a fallback if the stream produced nothing.
                var tail = _audio.ReadNewPcm();
                if (tail.Length > 0) { try { await _streaming.SendAudioAsync(tail, _cts.Token); } catch { } }
                var stopTask = _audio.StopRecordingAsync();
                raw = await _streaming.FinishAsync(_cts.Token);
                wav = await stopTask;
            }
            else
            {
                wav = await _audio.StopRecordingAsync();
            }
            _metrics.RecordStage("record", recSw.Elapsed.TotalMilliseconds);

            // Fallback: streaming yielded nothing (socket never opened, errored, or returned
            // empty) -> transcribe the whole WAV in one batch REST call, same as the old path.
            if (string.IsNullOrEmpty(raw) && wav.Length > 0)
                raw = await _stt.TranscribeAsync(wav, _activeMode, _cts.Token);
            _metrics.RecordStage("stt", sttSw.Elapsed.TotalMilliseconds);
            if (string.IsNullOrEmpty(raw)) { SetState(RecordingState.Idle); return; }

            SetState(RecordingState.Processing);
            var cmd = TranscriptCommands.Parse(raw, _config.PressEnterCommandEnabled);
            string textToPaste;

            var macro = MacroMatcher.FindMatch(cmd.Transcript, _config.Macros);
            if (macro is not null)
            {
                textToPaste = macro.Payload;
            }
            else
            {
                var context = await _contextTask;
                var cleanSw = Stopwatch.StartNew();
                var cleaned = await _polish.CleanupAsync(cmd.Transcript, context, _cts.Token);
                _metrics.RecordStage("cleanup", cleanSw.Elapsed.TotalMilliseconds);
                if (string.IsNullOrEmpty(cleaned)) { SetState(RecordingState.Idle); return; }
                textToPaste = cleaned;
            }

            SetState(RecordingState.Speaking);
            var pasteSw = Stopwatch.StartNew();
            await _paste.InjectTextAsync(textToPaste, cmd.ShouldPressEnter);
            _metrics.RecordStage("paste", pasteSw.Elapsed.TotalMilliseconds);

            _transcriptStore.Append(new TranscriptEntry(
                Guid.NewGuid().ToString("N"),
                textToPaste,
                DateTimeOffset.UtcNow,
                "", // no foreground-app-name signal exists yet -- IScreenContextProvider returns
                    // free-text context, not a structured app name; do not fabricate one here.
                _config.TranscriptionLanguage,
                textToPaste.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length,
                false));

            // No "Done" confirmation state -- once the text is pasted, collapse straight back
            // to the rest pill (Idle). The orb's box->pill shrink animation still plays.
            SetState(RecordingState.Idle);
        }
        catch (Exception ex)
        {
            _metrics.RecordStage($"pipeline-error:{ex.GetType().Name}", total.Elapsed.TotalMilliseconds);
            LastErrorMessage = "Couldn't catch that.";
            SetState(RecordingState.Error);
            SetState(RecordingState.Idle);
        }
        finally
        {
            // Make sure the streaming socket is always torn down, even on the error path where
            // FinishAsync wasn't reached, so a failed capture never leaks an open WebSocket.
            try { await _streaming.CancelAsync(); } catch { }
            _metrics.RecordTotal(total.Elapsed.TotalMilliseconds);
        }
    }
}
