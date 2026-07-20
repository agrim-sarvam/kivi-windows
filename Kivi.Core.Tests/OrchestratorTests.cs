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
        await Task.Delay(200); // allow the async pipeline to complete

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

        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(200);

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
}
