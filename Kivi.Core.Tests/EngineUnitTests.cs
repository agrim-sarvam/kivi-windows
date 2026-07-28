// Focused unit tests for the pure engine: the gesture classifier (holdMs 420 / doubleTapMs
// 450 boundaries), the word-level LCS diff, the cue bus + phase-cue funnel, MarkOverride,
// SpeechPace, and multi-rate (24/30/60 Hz) determinism of the dt-corrected easing.
using System;
using System.Collections.Generic;
using System.Linq;
using Kivi.Core.Orb;
using Xunit;

namespace Kivi.Core.Tests;

public sealed class GestureClassifierTests
{
    // Drives the engine with a scripted service so nothing resolves on its own; we test
    // ONLY the pure-time gesture cliffs (release>=420 -> processing; 2nd tap <450 -> edit).
    private static (FlowEngine e, ScriptedDictationService d) Fresh()
    {
        var d = new ScriptedDictationService();
        var e = new FlowEngine(store: new MemoryFlowStore(), random: () => 0.5, dictation: d, edit: new ScriptedEditService());
        e.Apply(FlowSettings.Default());
        return (e, d);
    }

    [Fact]
    public void HoldReleaseAtExactly420Ms_StopsToProcessing()
    {
        var (e, _) = Fresh();
        e.Step(16);
        e.OrbPointerDown();          // pressStart = now
        var down = e.Now;
        e.Step(down + 420);          // advance clock 420ms past press
        e.PointerUp();               // heldFor == 420 >= HOLD_MS -> stopListening
        Assert.Equal(FlowPhase.Processing, e.Phase);
    }

    [Fact]
    public void QuickReleaseUnder420Ms_StaysListening()
    {
        var (e, _) = Fresh();
        e.Step(16);
        e.OrbPointerDown();
        var down = e.Now;
        e.Step(down + 419);          // 419 < HOLD_MS
        e.PointerUp();               // pointerUp: not held long enough -> take stays alive
        Assert.Equal(FlowPhase.Listening, e.Phase);
    }

    // Settle a full dictation so canEdit becomes true (edit available before talk). The
    // presentDone Later(150) fires only after processingMinDisplay(250) elapses AND the box
    // is expanded; then Later(150)->idle sets canEdit=true. Drain by stepping the clock.
    private static void SettleDictationToIdle(FlowEngine e, ScriptedDictationService d)
    {
        e.SetExpanded(true);
        e.Step(e.Now + 16);
        e.OrbPointerDown();
        var t0 = e.Now;
        e.Step(t0 + 500);
        e.PointerUp();               // -> processing (held >= 420)
        d.Emit(new DictationEvent.Final(new TakeResult { RawSegments = new() { "hi" }, FinalLines = new() { "Hi." } }));
        // Step generously so: drain final -> Later(presentAt) -> presentDone -> Later(150) -> idle.
        for (int i = 0; i < 60; i++) e.Step(e.Now + 16);
    }

    [Fact]
    public void SecondTapUnder450Ms_WithEditAvailable_EntersVoiceEdit()
    {
        var (e, d) = Fresh();
        SettleDictationToIdle(e, d);
        Assert.True(e.DebugCanEdit);

        // Quick tap to start listening, then a SECOND tap within 450ms of listen start.
        e.OrbPointerDown();          // -> listening (editAvailableBeforeTalk captured = true)
        var listenStart = e.Now;
        e.PointerUp();               // quick release (<420) keeps the take alive
        e.Step(listenStart + 200);   // 200 < 450
        e.OrbPointerDown();          // second tap -> secondTapAction -> startVoiceEdit
        Assert.Equal(FlowPhase.EditListen, e.Phase);
    }

    [Fact]
    public void SecondTapAfter450Ms_StopsInsteadOfEditing()
    {
        var (e, d) = Fresh();
        SettleDictationToIdle(e, d);
        Assert.True(e.DebugCanEdit);

        e.OrbPointerDown();          // -> listening
        var listenStart = e.Now;
        e.PointerUp();
        e.Step(listenStart + 460);   // 460 >= 450 -> second tap is a STOP, not edit
        e.OrbPointerDown();
        Assert.Equal(FlowPhase.Processing, e.Phase);
    }

    [Fact]
    public void FnDownIgnoresAutoRepeat()
    {
        var (e, _) = Fresh();
        e.Step(16);
        e.FnDown();                  // -> listening
        Assert.Equal(FlowPhase.Listening, e.Phase);
        var gen = e.DebugTakeGeneration;
        e.FnDown();                  // auto-repeat: fnHeld guard should no-op
        Assert.Equal(FlowPhase.Listening, e.Phase);
        Assert.Equal(gen, e.DebugTakeGeneration); // no new take started
    }
}

public sealed class DiffTokenTests
{
    private static (string kind, string text)[] Toks(string before, string after) =>
        FlowEngine.DiffTokens(before, after).Select(t => (t.Kind.RawValue(), t.Text)).ToArray();

    [Fact]
    public void IdenticalStrings_AllSame()
    {
        var t = Toks("hello world", "hello world");
        Assert.All(t, x => Assert.Equal("same", x.kind));
        Assert.Equal(new[] { "hello ", "world " }, t.Select(x => x.text).ToArray());
    }

    [Fact]
    public void PureDeletion_MarksDel()
    {
        var t = Toks("the quick brown fox", "the brown fox");
        Assert.Contains(t, x => x.kind == "del" && x.text == "quick ");
        Assert.DoesNotContain(t, x => x.kind == "ins");
    }

    [Fact]
    public void PureInsertion_MarksIns()
    {
        var t = Toks("the fox", "the quick fox");
        Assert.Contains(t, x => x.kind == "ins" && x.text == "quick ");
        Assert.DoesNotContain(t, x => x.kind == "del");
    }

    [Fact]
    public void BothEmpty_ReturnsEmpty()
    {
        Assert.Empty(FlowEngine.DiffTokens("", ""));
    }

    [Fact]
    public void WordCount_MatchesWhitespaceSplit()
    {
        Assert.Equal(0, FlowEngine.WordCount(""));
        Assert.Equal(0, FlowEngine.WordCount("   "));
        Assert.Equal(3, FlowEngine.WordCount("one two three"));
        Assert.Equal(3, FlowEngine.WordCount("  leading\tand trailing  ")); // tabs+spaces both split
    }
}

public sealed class CueBusTests
{
    [Fact]
    public void Publish_RecordsLast_AndNotifiesSubscribers()
    {
        var bus = new CueBus();
        var seen = new List<CueEvent>();
        var unsub = bus.Subscribe(seen.Add);
        var ev = new CueEvent(CueEventKind.Listening, FlowPhase.Idle, FlowPhase.Listening);
        bus.Publish(ev);
        Assert.Equal(ev, bus.Last);
        Assert.Single(seen);
        Assert.Equal(ev, seen[0]);
        unsub();
        bus.Publish(new CueEvent(CueEventKind.Done, FlowPhase.Processing, FlowPhase.Done));
        Assert.Single(seen); // unsubscribed
    }

    [Fact]
    public void PhaseSetter_EmitsCueOnEveryRealChange()
    {
        var d = new ScriptedDictationService();
        var e = new FlowEngine(store: new MemoryFlowStore(), random: () => 0.5, dictation: d, edit: new ScriptedEditService());
        e.Apply(FlowSettings.Default());
        var cues = new List<CueEvent>();
        e.OnStateTransition = cues.Add;
        e.Step(16);
        e.OrbPointerDown();       // rest/idle -> listening (cue: listening)
        e.Step(e.Now + 500);
        e.PointerUp();            // -> processing (cue: processing)
        Assert.Contains(cues, c => c.Kind == CueEventKind.Listening);
        Assert.Contains(cues, c => c.Kind == CueEventKind.Processing);
    }

    [Fact]
    public void CueKind_MapsActPhasesToActingAndConfirming()
    {
        Assert.Equal(CueEventKind.Acting, FlowEngine.CueKind(FlowPhase.ActListen));
        Assert.Equal(CueEventKind.Acting, FlowEngine.CueKind(FlowPhase.ActProcess));
        Assert.Equal(CueEventKind.Confirming, FlowEngine.CueKind(FlowPhase.ActConfirm));
        Assert.Equal(CueEventKind.Idle, FlowEngine.CueKind(FlowPhase.Rest));
    }
}

public sealed class MarkOverrideTests
{
    [Fact]
    public void OverrideWashesForFrameWindow_ThenClears()
    {
        var mo = new MarkOverride();
        mo.Set(KiwiMarkState.Error, 3);
        Assert.Equal(KiwiMarkState.Error, mo.Tick(KiwiMarkState.Idle)); // frame 3->2
        Assert.Equal(KiwiMarkState.Error, mo.Tick(KiwiMarkState.Idle)); // 2->1
        Assert.Equal(KiwiMarkState.Error, mo.Tick(KiwiMarkState.Idle)); // 1->0
        Assert.Equal(KiwiMarkState.Idle, mo.Tick(KiwiMarkState.Idle));  // exhausted
    }

    [Fact]
    public void NonIdleBasePhase_SupersedesAndClearsOverride()
    {
        var mo = new MarkOverride();
        mo.Set(KiwiMarkState.Waiting, 90);
        Assert.Equal(KiwiMarkState.Listening, mo.Tick(KiwiMarkState.Listening)); // base wins
        Assert.Equal(KiwiMarkState.Idle, mo.Tick(KiwiMarkState.Idle));           // override was cleared
    }
}

public sealed class SpeechPaceTests
{
    [Fact]
    public void RisesToSpeakingAfterOnConfirm_AndFallsAfterSilenceHold()
    {
        var sp = new SpeechPace();
        // Feed above onLevel for >= onConfirm (0.1s).
        for (int i = 0; i < 10; i++) sp.Feed(0.5, 0.05);
        Assert.True(sp.Speaking);
        Assert.True(sp.Pace > 0.5);
        var peak = sp.Pace;
        // Feed below offLevel for >= silenceHold (0.9s): stops speaking, pace decays.
        for (int i = 0; i < 40; i++) sp.Feed(0.0, 0.05);
        Assert.False(sp.Speaking);
        Assert.True(sp.Pace < peak); // decayed toward 0 (fallTau is slow by design)
    }

    [Fact]
    public void EasedIsSmoothstepOfPace()
    {
        var sp = new SpeechPace();
        for (int i = 0; i < 10; i++) sp.Feed(1.0, 0.05);
        var p = sp.Pace;
        Assert.Equal(p * p * (3 - 2 * p), sp.Eased, 12);
    }
}

public sealed class MultiRateDeterminismTests
{
    // The dt-correction (ease60(k)=1-(1-k)^(dt/16)) must make animations cover the same
    // distance per unit TIME regardless of frame rate. Run the SAME wall-clock timeline at
    // 24/30/60 Hz and assert the eased `open` converges to the same value at the same time.
    private static double OpenAfter(double hz, double totalMs)
    {
        var e = new FlowEngine(store: new MemoryFlowStore(), random: () => 0.5,
            dictation: new ScriptedDictationService(), edit: new ScriptedEditService());
        e.Apply(FlowSettings.Default());
        e.OrbPointerDown(); // wants open (listening)
        var stepMs = 1000.0 / hz;
        double now = 0;
        FlowFrame f = e.Step(0);
        while (now < totalMs)
        {
            now += stepMs;
            f = e.Step(now);
        }
        return f.Open;
    }

    [Fact]
    public void OpenConvergesAcrossFrameRates()
    {
        // After ~500ms the wake ease is essentially saturated; all rates must agree closely.
        var o60 = OpenAfter(60, 512);
        var o30 = OpenAfter(30, 510);
        var o24 = OpenAfter(24, 500);
        Assert.True(Math.Abs(o60 - o30) < 5e-3, $"60 vs 30: {o60} vs {o30}");
        Assert.True(Math.Abs(o60 - o24) < 5e-3, $"60 vs 24: {o60} vs {o24}");
        Assert.True(o60 > 0.99);
    }
}

/// Footer action bar behaviors (orb-visual-and-box.md §8d, this pass's completion items 3/4):
/// RateTake (thumbs), CopyClick (copy chip), and NewSessionClick (the "+" pill) — driven against a
/// settled take exactly like the golden timelines do, then asserted purely against FlowEngine
/// state/callbacks (no rendering involved).
public sealed class FooterActionTests
{
    private static (FlowEngine e, ScriptedDictationService d) SettledTake()
    {
        var d = new ScriptedDictationService();
        var e = new FlowEngine(store: new MemoryFlowStore(), random: () => 0.5, dictation: d, edit: new ScriptedEditService());
        e.Apply(FlowSettings.Default());
        e.SetExpanded(true);
        e.Step(16);
        e.OrbPointerDown();
        d.Emit(new DictationEvent.Opened("t"));
        e.Step(32);
        d.Emit(new DictationEvent.SpeechStart());
        e.Step(48);
        d.Emit(new DictationEvent.Segment(0, "hello world"));
        e.Step(64);
        e.Step(500); // past HOLD_MS from press
        e.PointerUp(); // -> processing
        e.Step(516);
        d.Emit(new DictationEvent.Final(new TakeResult
        {
            RawSegments = new() { "hello world" },
            FinalLines = new() { "Hello, world." },
        }));
        // PresentDone is deferred via Later() until processingStartAt + PROCESSING_MIN_DISPLAY_MS
        // (250ms) has elapsed (FlowEngine.Handle(DictationEvent.Final)) — step well past that
        // absolute deadline (processing started at 500) so the scheduled callback actually fires
        // and the take settles to Done/Idle before the footer actions below run against it.
        e.Step(900);
        e.Step(1100); // past the further Later(150) inside PresentDone that flips Done -> Idle
        return (e, d);
    }

    [Fact]
    public void RateTake_TogglesAndReportsViaCallback()
    {
        var (e, _) = SettledTake();
        string? ratedText = null; int ratedValue = 0;
        e.OnTakeRated = (text, rating) => { ratedText = text; ratedValue = rating; };

        e.RateTake(up: true);
        Assert.Equal(1, ratedValue);
        Assert.Contains("Hello", ratedText);
        var f1 = e.Step(1116);
        Assert.Equal(1, f1.TakeRating);

        // clicking the SAME thumb again toggles it back off (RateTake: `_takeRating == v ? 0 : v`).
        e.RateTake(up: true);
        Assert.Equal(0, ratedValue);
        var f2 = e.Step(1132);
        Assert.Equal(0, f2.TakeRating);

        e.RateTake(up: false);
        Assert.Equal(-1, ratedValue);
        var f3 = e.Step(1148);
        Assert.Equal(-1, f3.TakeRating);
    }

    [Fact]
    public void CopyClick_ReturnsFinalTextAndArmsCopyFlash()
    {
        var (e, _) = SettledTake();
        var text = e.CopyClick();
        Assert.Equal("Hello, world.", text);
        var f = e.Step(1116); // still within the 1100ms copyFlash window
        Assert.True(f.CopyFlash);
    }

    [Fact]
    public void NewSessionClick_ClearsBoxAndStaysIdleExpanded()
    {
        var (e, _) = SettledTake();
        var wasExpandedBefore = e.DebugExpanded;
        e.NewSessionClick();
        var f = e.Step(1116);
        Assert.True(wasExpandedBefore); // stayed expanded the whole time (never collapsed)
        Assert.True(e.DebugExpanded); // "stay expanded" per orb-engine-behavior.md §3.4
        Assert.Equal(FlowPhase.Idle, f.Phase); // "stay ... idle"
        Assert.Empty(f.TxLines); // "clear box to empty editable"
        Assert.False(f.TakeRatable); // a voided take is no longer ratable
    }
}
