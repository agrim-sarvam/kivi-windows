using Kivi.Core.Config;
using Kivi.Core.Diagnostics;
using Kivi.Core.Orchestration;
using Xunit;

public class OrchestratorTests
{
    [Fact]
    public async Task FullDictation_RunsStateSequence_AndPastesCleanedText()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics);

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
    public async Task VoiceMacro_BypassesCleanup_PastesPayload()
    {
        var cfg = AppConfig.Default();
        cfg.Macros.Add(new Kivi.Core.Macros.VoiceMacro("hello there", "MACRO PAYLOAD"));
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, cfg, metrics);
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
            new StubStt(), polish, paste, AppConfig.Default(), metrics);

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
    public async Task SuccessfulDictation_PassesThroughDone_BeforeIdle()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics);

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        hotkey.FireStart();
        await Task.Delay(20);
        hotkey.FireEnd();
        await Task.Delay(1500); // allow pipeline + Done->Idle delay to complete

        Assert.Contains(RecordingState.Done, states);
        // Done must occur before the final Idle in the sequence.
        var doneIndex = states.LastIndexOf(RecordingState.Done);
        var lastIdleIndex = states.LastIndexOf(RecordingState.Idle);
        Assert.True(doneIndex < lastIdleIndex, "Done must precede the final Idle transition.");
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
            new StubStt(), new StubPolish(), paste, cfg, metrics);
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
            stt, new StubPolish(), paste, AppConfig.Default(), metrics);

        var partials = new List<string>();
        orch.PartialTranscriptChanged += p => partials.Add(p);
        orch.Start();

        hotkey.FireStart();
        await Task.Delay(700); // past the 500ms warmup -> at least one snapshot should fire
        hotkey.FireEnd();
        await Task.Delay(1500);

        Assert.Contains("partial words", partials);
    }
}
