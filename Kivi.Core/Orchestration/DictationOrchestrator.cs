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
    private readonly ISttEngine _stt;
    private readonly IPolishClient _polish;
    private readonly IPasteService _paste;
    private readonly AppConfig _config;
    private readonly KiviMetrics _metrics;
    private readonly ITranscriptStore _transcriptStore;
    private readonly object _lock = new();
    private const int DoneDisplayMs = 1200;
    private const int PartialIntervalMs = 1000;
    private const int PartialWarmupMs = 500;

    private Task<string> _contextTask = Task.FromResult("");
    private CancellationTokenSource _cts = new();
    private CancellationTokenSource _partialLoopCts = new();
    private bool _capturing;
    // The Sarvam STT mode for the in-flight capture, chosen by which hotkey started it:
    // primary hotkey -> Hinglish (romanized code-mix), English hotkey -> translate-to-English.
    private string _activeMode = SttMode.Hinglish;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public string? LastErrorMessage { get; private set; }

    public event Action<RecordingState>? StateChanged;
    public event Action<string>? PartialTranscriptChanged;

    public DictationOrchestrator(IHotkeyService hotkey, IAudioCaptureService audio, IScreenContextProvider context,
        ISttEngine stt, IPolishClient polish, IPasteService paste, AppConfig config, KiviMetrics metrics,
        ITranscriptStore transcriptStore)
    {
        (_hotkey, _audio, _context, _stt, _polish, _paste, _config, _metrics, _transcriptStore)
           = (hotkey, audio, context, stt, polish, paste, config, metrics, transcriptStore);
        _polish.EnteringCooldown += _ => SetState(RecordingState.Waiting);
    }

    public void Start()
    {
        _hotkey.HoldStarted += OnHoldStarted;
        _hotkey.HoldEnded += OnHoldEnded;
        _hotkey.EnglishHoldStarted += OnEnglishHoldStarted;
        _hotkey.EnglishHoldEnded += OnEnglishHoldEnded;
        _hotkey.Start();
    }

    public void Stop()
    {
        _hotkey.HoldStarted -= OnHoldStarted;
        _hotkey.HoldEnded -= OnHoldEnded;
        _hotkey.EnglishHoldStarted -= OnEnglishHoldStarted;
        _hotkey.EnglishHoldEnded -= OnEnglishHoldEnded;
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

    private void BeginCapture(string mode)
    {
        if (_capturing) return; // both hotkeys held at once is unsupported -- ignore the second
        _capturing = true;
        _activeMode = mode;
        _cts = new CancellationTokenSource();
        _partialLoopCts = new CancellationTokenSource();
        SetState(RecordingState.Listening);
        _ = _audio.StartRecordingAsync(_cts.Token);
        _contextTask = _config.ScreenContextEnabled
            ? _context.CaptureContextAsync(_cts.Token)
            : Task.FromResult("");
        _ = RunPartialLoopAsync(_partialLoopCts.Token);
    }

    private async Task RunPartialLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(PartialWarmupMs, ct);
            while (!ct.IsCancellationRequested)
            {
                var wav = _audio.SnapshotRecording();
                if (wav.Length > 0)
                {
                    var partial = await _stt.TranscribeAsync(wav, _activeMode, ct);
                    if (!string.IsNullOrEmpty(partial))
                        PartialTranscriptChanged?.Invoke(partial);
                }
                await Task.Delay(PartialIntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { /* recording ended -> stop snapshotting */ }
    }

    private void OnHoldEnded() => EndCapture();
    private void OnEnglishHoldEnded() => EndCapture();

    private void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _partialLoopCts.Cancel();
        _ = RunPipelineAsync();
    }

    private async Task RunPipelineAsync()
    {
        var total = Stopwatch.StartNew();
        try
        {
            SetState(RecordingState.Processing);
            var recSw = Stopwatch.StartNew();
            var wav = await _audio.StopRecordingAsync();
            _metrics.RecordStage("record", recSw.Elapsed.TotalMilliseconds);

            var sttSw = Stopwatch.StartNew();
            var raw = await _stt.TranscribeAsync(wav, _activeMode, _cts.Token);
            _metrics.RecordStage("stt", sttSw.Elapsed.TotalMilliseconds);
            if (string.IsNullOrEmpty(raw)) { SetState(RecordingState.Idle); return; }

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

            SetState(RecordingState.Done);
            await Task.Delay(DoneDisplayMs, _cts.Token);
            SetState(RecordingState.Idle);
        }
        catch
        {
            LastErrorMessage = "Couldn't catch that.";
            SetState(RecordingState.Error);
            SetState(RecordingState.Idle);
        }
        finally
        {
            _metrics.RecordTotal(total.Elapsed.TotalMilliseconds);
        }
    }
}
