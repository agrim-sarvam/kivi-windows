using Kivi.Core.Config;
using Kivi.Core.Diagnostics;
using Kivi.Core.History;
using Kivi.Core.Orchestration;
using Kivi.Core.Stt;
using Xunit;

public class OrchestratorTests
{
    private sealed class FakeTranscriptStore : ITranscriptStore
    {
        public List<TranscriptEntry> Entries { get; } = new();
        public IReadOnlyList<TranscriptEntry> LoadAll() => Entries;
        public void Append(TranscriptEntry entry) => Entries.Add(entry);
        public void Clear() => Entries.Clear();
    }

    [Fact]
    public async Task FullDictation_RunsStateSequence_AndPastesCleanedText()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        hotkey.FireStart();
        await Task.Delay(50);
        hotkey.FireEnd();
        await Task.Delay(1500); // allow the async pipeline + Done->Idle delay to complete

        Assert.Equal("Hello there.", paste.Pasted);
        Assert.Contains(RecordingState.Listening, states);
        Assert.Contains(RecordingState.Processing, states);
        Assert.Contains(RecordingState.Speaking, states);
        Assert.Equal(RecordingState.Idle, orch.State);
    }

    [Fact]
    public async Task CompletedDictation_AppendsEntryToTranscriptStore()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var transcriptStore = new FakeTranscriptStore();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics, transcriptStore);
        orch.Start();

        hotkey.FireStart();
        await Task.Delay(50);
        hotkey.FireEnd();
        await Task.Delay(1500); // allow the async pipeline + Done->Idle delay to complete

        Assert.Single(transcriptStore.Entries);
        Assert.Equal("Hello there.", transcriptStore.Entries[0].Text);
        Assert.False(transcriptStore.Entries[0].WasRewrite);
    }

    [Fact]
    public async Task VoiceMacro_BypassesCleanup_PastesPayload()
    {
        var cfg = AppConfig.Default();
        cfg.Macros.Add(new Kivi.Core.Macros.VoiceMacro("hello there", "MACRO PAYLOAD"));
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, cfg, metrics, new FakeTranscriptStore());
        orch.Start();

        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);

        Assert.Equal("MACRO PAYLOAD", paste.Pasted);
    }

    [Fact]
    public async Task RateLimitedPolish_EmitsWaitingState_BeforeProcessingResumes()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var polish = new CooldownStubPolish();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), polish, paste, AppConfig.Default(), metrics, new FakeTranscriptStore());

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        hotkey.FireStart();
        await Task.Delay(20);
        hotkey.FireEnd();
        await Task.Delay(200);

        Assert.Contains(RecordingState.Waiting, states);
        Assert.Equal("Hello there.", paste.Pasted);
    }

    [Fact]
    public async Task SuccessfulDictation_EndsAtIdle_WithoutADoneState()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        hotkey.FireStart();
        await Task.Delay(20);
        hotkey.FireEnd();
        await Task.Delay(1500);

        // The "Done" confirmation state was removed -- a successful dictation goes straight
        // Speaking -> Idle after pasting, with no visible Done pause.
        Assert.DoesNotContain(RecordingState.Done, states);
        Assert.Contains(RecordingState.Speaking, states);
        Assert.Equal(RecordingState.Idle, orch.State);
    }

    [Fact]
    public async Task ScreenContextDisabled_SkipsContextCapture()
    {
        var cfg = AppConfig.Default();
        cfg.ScreenContextEnabled = false;
        var ctx = new SpyContext();
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), ctx,
            new StubStt(), new StubPolish(), paste, cfg, metrics, new FakeTranscriptStore());
        orch.Start();

        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);

        Assert.Equal(0, ctx.Calls);
        Assert.Equal("Hello there.", paste.Pasted);
    }

    [Fact]
    public async Task Listening_EmitsPartialTranscript_AfterWarmup()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var stt = new StubStt { Result = "partial words" };
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            stt, new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());

        var partials = new List<string>();
        orch.PartialTranscriptChanged += p => partials.Add(p);
        orch.Start();

        hotkey.FireStart();
        await Task.Delay(700); // past the 500ms warmup -> at least one snapshot should fire
        hotkey.FireEnd();
        await Task.Delay(1500);

        Assert.Contains("partial words", partials);
    }

    [Fact]
    public async Task PrimaryHotkey_UsesHinglishMode()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var stt = new StubStt();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            stt, new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());
        orch.Start();

        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);

        Assert.Equal(SttMode.Hinglish, stt.LastMode);
    }

    [Fact]
    public async Task EnglishHotkey_UsesTranslateMode_AndPastesCleanedText()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var stt = new StubStt();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            stt, new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());
        orch.Start();

        hotkey.FireEnglishStart(); await Task.Delay(20); hotkey.FireEnglishEnd(); await Task.Delay(1500);

        Assert.Equal(SttMode.English, stt.LastMode);
        Assert.Equal("Hello there.", paste.Pasted); // English hotkey runs the same clean+paste pipeline
    }

    [Fact]
    public async Task BothHotkeysHeldAtOnce_SecondHoldIsIgnored()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var stt = new StubStt();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            stt, new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());
        orch.Start();

        hotkey.FireStart();
        hotkey.FireEnglishStart(); // ignored: a capture is already in progress
        await Task.Delay(20);
        hotkey.FireEnd();
        await Task.Delay(1500);

        Assert.Equal("Hello there.", paste.Pasted);
        Assert.Equal(SttMode.Hinglish, stt.LastMode); // the primary (first) hotkey's mode won, not English
    }

    [Fact]
    public async Task MismatchedHoldEnded_IsIgnored_CaptureContinuesUninterrupted()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());
        orch.Start();

        // Start a capture with the primary hotkey, then fire the English hotkey's end event
        // (never started) -- EndCapture must no-op cleanly, primary capture keeps running.
        hotkey.FireStart();
        await Task.Delay(20);
        hotkey.FireEnglishEnd();
        hotkey.FireEnd(); // the real end for the primary capture
        await Task.Delay(1500);

        Assert.Equal("Hello there.", paste.Pasted); // completed exactly once, normally
    }
}
