using System.Diagnostics;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;
using Kivi.Core.Diagnostics;
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
    private readonly object _lock = new();
    private const int DoneDisplayMs = 1200;

    private Task<string> _contextTask = Task.FromResult("");
    private CancellationTokenSource _cts = new();

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public event Action<RecordingState>? StateChanged;

    public DictationOrchestrator(IHotkeyService hotkey, IAudioCaptureService audio, IScreenContextProvider context,
        ISttEngine stt, IPolishClient polish, IPasteService paste, AppConfig config, KiviMetrics metrics)
    {
        (_hotkey, _audio, _context, _stt, _polish, _paste, _config, _metrics)
           = (hotkey, audio, context, stt, polish, paste, config, metrics);
        _polish.EnteringCooldown += _ => SetState(RecordingState.Waiting);
    }

    public void Start()
    {
        _hotkey.HoldStarted += OnHoldStarted;
        _hotkey.HoldEnded += OnHoldEnded;
        _hotkey.Start();
    }

    public void Stop()
    {
        _hotkey.HoldStarted -= OnHoldStarted;
        _hotkey.HoldEnded -= OnHoldEnded;
        _hotkey.Stop();
    }

    private void SetState(RecordingState s)
    {
        lock (_lock) { State = s; }
        StateChanged?.Invoke(s);
    }

    private void OnHoldStarted()
    {
        _cts = new CancellationTokenSource();
        SetState(RecordingState.Listening);
        _contextTask = _config.ScreenContextEnabled
            ? _context.CaptureContextAsync(_cts.Token)
            : Task.FromResult("");
        _ = _audio.StartRecordingAsync(_cts.Token);
    }

    private void OnHoldEnded() => _ = RunPipelineAsync();

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
            var raw = await _stt.TranscribeAsync(wav, _cts.Token);
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

            SetState(RecordingState.Done);
            await Task.Delay(DoneDisplayMs, _cts.Token);
            SetState(RecordingState.Idle);
        }
        catch
        {
            SetState(RecordingState.Error);
            SetState(RecordingState.Idle);
        }
        finally
        {
            _metrics.RecordTotal(total.Elapsed.TotalMilliseconds);
        }
    }
}
