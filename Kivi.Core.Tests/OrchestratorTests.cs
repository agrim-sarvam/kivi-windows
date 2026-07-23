using Kivi.Core.Config;
using Kivi.Core.Diagnostics;
using Kivi.Core.History;
using Kivi.Core.Orchestration;
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
    public async Task SuccessfulDictation_PassesThroughDone_BeforeIdle()
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
    public async Task RewriteHotkey_WithNoPriorDictation_ShowsErrorAndReturnsToIdle()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        hotkey.FireRewriteStart();
        await Task.Delay(20);
        hotkey.FireRewriteEnd();
        await Task.Delay(1500);

        Assert.Contains(RecordingState.Error, states);
        Assert.Equal(RecordingState.Idle, orch.State);
        Assert.Null(paste.Pasted); // nothing was ever pasted
    }

    [Fact]
    public async Task RewriteHotkey_AfterDictation_ComputesDiff_AndArmsReviewKeys()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var polish = new StubPolish(); // RewriteAsync -> "Rewritten text."
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), polish, paste, AppConfig.Default(), metrics, new FakeTranscriptStore());

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        // First, a normal dictation so there's something to rewrite.
        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);
        Assert.Equal("Hello there.", paste.Pasted);

        // Now hold the rewrite hotkey.
        hotkey.FireRewriteStart();
        await Task.Delay(20);
        hotkey.FireRewriteEnd();
        await Task.Delay(500);

        Assert.Contains(RecordingState.RewritePending, states);
        Assert.Equal(RecordingState.RewriteReview, orch.State);
        Assert.True(hotkey.ReviewArmed);
        Assert.NotNull(orch.Diff);
        Assert.Equal("Rewritten text.", string.Concat(orch.Diff!.Where(t => t.Op != Kivi.Core.Text.DiffOp.Delete).Select(t => t.Text)));
    }

    [Fact]
    public async Task ReviewAccepted_UndoesThenPastesRewrittenText()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());
        orch.Start();

        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);
        hotkey.FireRewriteStart(); await Task.Delay(20); hotkey.FireRewriteEnd(); await Task.Delay(500);

        hotkey.FireReviewAccepted();
        await Task.Delay(1500);

        Assert.Equal(1, paste.UndoCalls);
        Assert.Equal("Rewritten text.", paste.Pasted);
        Assert.Equal(RecordingState.Idle, orch.State);
        Assert.False(hotkey.ReviewArmed);

        var undoIndex = paste.CallOrder.IndexOf("Undo");
        var pasteIndex = paste.CallOrder.IndexOf("Inject:Rewritten text.");
        Assert.True(undoIndex >= 0 && pasteIndex >= 0 && undoIndex < pasteIndex,
            "Undo must happen before the rewritten text is pasted, or the real document would be corrupted.");
    }

    [Fact]
    public async Task ReviewCancelled_LeavesOriginalPasteUntouched()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());
        orch.Start();

        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);
        hotkey.FireRewriteStart(); await Task.Delay(20); hotkey.FireRewriteEnd(); await Task.Delay(500);

        hotkey.FireReviewCancelled();

        Assert.Equal(0, paste.UndoCalls);
        Assert.Equal("Hello there.", paste.Pasted); // still the original dictation, never overwritten
        Assert.Equal(RecordingState.Idle, orch.State);
        Assert.False(hotkey.ReviewArmed);
    }

    [Fact]
    public async Task BothHotkeysHeldAtOnce_SecondHoldIsIgnored()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());
        orch.Start();

        hotkey.FireStart();
        hotkey.FireRewriteStart(); // ignored: a capture is already in progress
        await Task.Delay(20);
        hotkey.FireEnd();
        await Task.Delay(1500);

        Assert.Equal("Hello there.", paste.Pasted); // normal dictation completed; rewrite never ran
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

        // Start a PRIMARY dictation capture, then fire the REWRITE hotkey's end event
        // (never started) -- OnRewriteHoldEnded must no-op since IsRewriteCapture is false.
        hotkey.FireStart();
        await Task.Delay(20);
        hotkey.FireRewriteEnd(); // mismatched: should be ignored, primary capture keeps running
        await Task.Delay(20);

        // The primary capture must still be exactly where it was -- not derailed into the
        // rewrite pipeline, and not yet finished (only the real FireEnd() below completes it).
        Assert.Null(paste.Pasted);

        hotkey.FireEnd(); // the REAL end event for the primary capture
        await Task.Delay(1500);

        Assert.Equal("Hello there.", paste.Pasted); // primary pipeline completed normally
        Assert.Equal(0, paste.UndoCalls); // rewrite pipeline never ran
    }

    [Fact]
    public async Task MismatchedPrimaryHoldEnded_IsIgnored_RewriteCaptureContinuesUninterrupted()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics, new FakeTranscriptStore());

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        // A normal dictation first, so there's something for the rewrite to target.
        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);
        Assert.Equal("Hello there.", paste.Pasted);
        states.Clear();

        // Start a REWRITE capture, then fire the PRIMARY hotkey's end event (never started
        // in this capture) -- OnHoldEnded must no-op since IsRewriteCapture is true.
        hotkey.FireRewriteStart();
        await Task.Delay(20);
        hotkey.FireEnd(); // mismatched: should be ignored, rewrite capture keeps running
        await Task.Delay(20);

        // The rewrite capture must not have been derailed into the normal dictation
        // pipeline -- no second normal paste happened, and we're still mid-capture.
        Assert.Equal("Hello there.", paste.Pasted); // unchanged since the dictation above
        Assert.DoesNotContain(RecordingState.Processing, states); // normal pipeline never ran

        hotkey.FireRewriteEnd(); // the REAL end event for the rewrite capture
        await Task.Delay(500);

        // The rewrite pipeline completed normally: computed a diff and armed review keys.
        Assert.Equal(RecordingState.RewriteReview, orch.State);
        Assert.True(hotkey.ReviewArmed);
        Assert.NotNull(orch.Diff);
    }
}
