// The §17 service seam + the demo/scripted implementations, ported from
// packages/orb-core/src/services.ts (KiviKit/Contracts/DictationContracts.swift,
// Orb/Services/{DemoScript,DemoServices,ScriptedServices}.swift).
//
// TS tagged unions are modeled as sealed record hierarchies so the engine can
// pattern-match (`is`/switch) exactly like the TS `switch (event.type)`.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kivi.Core.Orb;

public enum TakeKind { Dictation, EditInstruction, Action }

public readonly record struct TakeContext;
public readonly record struct EndOfSpeechInfo;

public enum CancelReason { FinalTimeout, Other }

public sealed class TakeResult
{
    public List<string> RawSegments = new();
    public List<string> FinalLines = new();
    public List<List<TxToken>>? DiffLines = null;
    public bool AudioDegraded = false;
}

// --- TakeFailure union ---
public abstract record TakeFailure
{
    public sealed record Empty : TakeFailure;
    public sealed record Unauthorized : TakeFailure;
    public sealed record UsageLimit : TakeFailure;
    public sealed record Busy : TakeFailure;
    public sealed record Network(bool KeepSegments) : TakeFailure;
    public sealed record FinalTimeoutFailure : TakeFailure;
    public sealed record Server(string Code) : TakeFailure;
    public sealed record IdleTimeout : TakeFailure;
}

// --- DictationLinkStatus union ---
public abstract record DictationLinkStatus
{
    public sealed record Interrupted : DictationLinkStatus;
    public sealed record Lost(string Reason) : DictationLinkStatus;
    public sealed record Restored : DictationLinkStatus;
}

// --- DictationEvent union ---
public abstract record DictationEvent
{
    public sealed record Opened(string SessionID) : DictationEvent;
    public sealed record SpeechStart : DictationEvent;
    public sealed record Segment(int Index, string Text) : DictationEvent;
    public sealed record FormattingBudget(int? RawWords, double ExpectedFormatMs) : DictationEvent;
    public sealed record FormattingProgress(double? ElapsedMs, double ExpectedFormatMs) : DictationEvent;
    public sealed record Final(TakeResult Result) : DictationEvent;
    public sealed record Failure(TakeFailure Value) : DictationEvent;
    public sealed record LinkStatus(DictationLinkStatus Status) : DictationEvent;
    public sealed record Retrying(int Attempt) : DictationEvent;
    public sealed record LateFinalChecking : DictationEvent;
    public sealed record LateFinalRecovered : DictationEvent;
    public sealed record LateFinalWaiting : DictationEvent;
    public sealed record LateFinalUnavailable : DictationEvent;
}

// --- EditInstruction union ---
public abstract record EditInstruction
{
    public sealed record Preset(string Name) : EditInstruction;
    public sealed record CustomStyle(string Slug) : EditInstruction;
    public sealed record Spoken(string RawInstruction) : EditInstruction;
}

public sealed class EditResult
{
    public List<string> Lines = new();
    public string? ClientAction = null;
}

// --- EditFailure union ---
public abstract record EditFailure
{
    public sealed record Cancelled : EditFailure;
    public sealed record Network : EditFailure;
    public sealed record Rejected(string? Reason) : EditFailure;
    public sealed record Server(string Code) : EditFailure;
}

// --- EditOutcome ---
public abstract record EditOutcome
{
    public sealed record Ok(EditResult Result) : EditOutcome;
    public sealed record Fail(EditFailure Failure) : EditOutcome;
}

public delegate void DictationSink(DictationEvent ev);
public delegate void EditSink(EditOutcome outcome);

public interface IDictationService
{
    void Begin(TakeKind kind, TakeContext context, bool renderActive, DictationSink sink);
    void RequestStop(EndOfSpeechInfo info);
    void Cancel(CancelReason? reason = null);
    void Tick(double now);
    void ResyncRender();
    bool BeginRetry(DictationSink sink);
    bool CanRetry { get; }
}

public interface IEditService
{
    void ApplyEdit(string text, EditInstruction instruction, EditSink sink);
    void CancelEdit();
    void Tick(double now);
}

// MARK: - DemoScript fixtures

public static class DemoScript
{
    public static readonly string[] Chunks =
    {
        "Arre haan so basically I was thinking we should meet this weekend yaar.",
        "Umm maybe Saturday evening, like around 6-7 at the new cafe in Indiranagar.",
        "We can grab some coffee and uh just chill, it's been so long since we caught up.",
        "Lemme know if that works, otherwise we can figure out Sunday also no problem.",
    };

    public static readonly List<List<TxToken>> Diff = new()
    {
        new() { new(TxTokenKind.Same, "Arre haan, so "), new(TxTokenKind.Del, "basically "), new(TxTokenKind.Same, "I was thinking we should meet this weekend, yaar.") },
        new() { new(TxTokenKind.Del, "Umm "), new(TxTokenKind.Same, "maybe Saturday evening, "), new(TxTokenKind.Del, "like "), new(TxTokenKind.Same, "around 6-7 at the new cafe in Indiranagar.") },
        new() { new(TxTokenKind.Same, "We can grab some coffee and "), new(TxTokenKind.Del, "uh "), new(TxTokenKind.Same, "just chill — it's been so long since we caught up.") },
        new() { new(TxTokenKind.Same, "Lemme know if that works, "), new(TxTokenKind.Del, "otherwise "), new(TxTokenKind.Same, "we can figure out Sunday too. No problem!") },
    };

    public static readonly Dictionary<string, string[]> Refined = new()
    {
        ["polish"] = new[]
        {
            "Arre haan, so I was thinking we should meet this weekend, yaar.",
            "Maybe Saturday evening, around 6–7, at the new cafe in Indiranagar.",
            "We can grab some coffee and just chill — it's been so long since we caught up.",
            "Let me know if that works; otherwise, we can figure out Sunday too.",
        },
        ["formal"] = new[]
        {
            "Hi! I was hoping we could meet this weekend.",
            "Would Saturday evening, around 6–7, at the new cafe in Indiranagar suit you?",
            "We could get coffee and catch up — it has been a while.",
            "Do let me know if that works; otherwise, Sunday is fine as well.",
        },
        ["casual"] = new[]
        {
            "arre let's catch up this weekend, yaar!",
            "saturday evening work? around 6-7 at that new indiranagar cafe.",
            "we grab coffee, just chill — it's been ages!",
            "lemme know, else sunday's also chill. no stress.",
        },
        ["custom"] = new[]
        {
            "Hey! Catch up this weekend? Sat 6–7 at the new Indiranagar cafe — coffee + chill. Sunday works too. Lemme know!",
        },
    };

    public const string SpokenInstruction = "make it crisper";

    public static List<string> FinalLines() =>
        Diff.Select(line => string.Concat(line.Where(t => t.Kind != TxTokenKind.Del).Select(t => t.Text))).ToList();
}

public static class DemoFixtures
{
    public static TakeSource DemoTakeSource() => new()
    {
        Raw = DemoScript.Chunks.ToList(),
        Final = DemoScript.FinalLines(),
        Diff = DemoScript.Diff.Select(l => l.Select(t => t.Clone()).ToList()).ToList(),
    };

    public static TakeResult DemoTakeResult() => new()
    {
        RawSegments = DemoScript.Chunks.ToList(),
        FinalLines = DemoScript.FinalLines(),
        DiffLines = DemoScript.Diff.Select(l => l.Select(t => t.Clone()).ToList()).ToList(),
    };
}

// MARK: - Scripted services (emit nothing on their own)

public sealed class ScriptedDictationService : IDictationService
{
    public int BeginCount = 0;
    public int StopCount = 0;
    public int CancelCount = 0;
    public int ResyncCount = 0;
    public int BeginRetryCount = 0;
    public bool CanRetryScripted = false;
    public bool RetryStarts = false;
    public bool ResyncReplaysSegments = true;

    private DictationSink? _sink = null;
    private List<(int index, string text)> _emittedSegments = new();

    public void Begin(TakeKind kind, TakeContext context, bool renderActive, DictationSink sink)
    {
        BeginCount++;
        _emittedSegments = new();
        _sink = sink;
    }

    public void RequestStop(EndOfSpeechInfo info) => StopCount++;
    public void Cancel(CancelReason? reason = null) => CancelCount++;
    public void Tick(double now) { }

    public bool BeginRetry(DictationSink sink)
    {
        BeginRetryCount++;
        if (!RetryStarts) return false;
        _sink = sink;
        return true;
    }

    public bool CanRetry => CanRetryScripted;

    public void ResyncRender()
    {
        ResyncCount++;
        if (!ResyncReplaysSegments) return;
        foreach (var seg in _emittedSegments) _sink?.Invoke(new DictationEvent.Segment(seg.index, seg.text));
    }

    public void Emit(DictationEvent ev)
    {
        if (ev is DictationEvent.Segment seg) _emittedSegments.Add((seg.Index, seg.Text));
        _sink?.Invoke(ev);
    }
}

public sealed class ScriptedEditService : IEditService
{
    public int ApplyCount = 0;
    public int CancelCount = 0;
    private readonly List<EditSink> _sinks = new();

    public void ApplyEdit(string text, EditInstruction instruction, EditSink sink)
    {
        ApplyCount++;
        _sinks.Add(sink);
    }

    public void CancelEdit() => CancelCount++;
    public void Tick(double now) { }

    public void Emit(EditOutcome outcome)
    {
        if (_sinks.Count > 0) _sinks[^1](outcome);
    }

    public void EmitViaCall(EditOutcome outcome, int index)
    {
        if (index >= 0 && index < _sinks.Count) _sinks[index](outcome);
    }
}

// MARK: - Demo services (prototype cadence)

public sealed class DemoDictationService : IDictationService
{
    public const double SpeechStartMs = 250;

    private readonly Func<double> _random;
    private double _lastNow = 0;
    private DictationSink? _sink = null;
    private List<(double due, int index, string text)> _pendingSegments = new();
    private double? _finalDue = null;
    private double? _speechStartDue = null;
    private TakeKind? _activeKind = null;

    public DemoDictationService(Func<double>? random = null)
    {
        _random = random ?? (() => Rng.NextDouble());
    }

    public void Begin(TakeKind kind, TakeContext context, bool renderActive, DictationSink sink)
    {
        _sink = sink;
        _activeKind = kind;
        _pendingSegments = new();
        _finalDue = null;
        _speechStartDue = kind == TakeKind.EditInstruction ? null : _lastNow + SpeechStartMs;
        if (kind != TakeKind.Dictation || !renderActive) return;
        ScheduleEmission(_lastNow);
    }

    public void RequestStop(EndOfSpeechInfo info)
    {
        _pendingSegments = new();
        _speechStartDue = null;
        switch (_activeKind)
        {
            case TakeKind.Dictation:
            case TakeKind.Action:
                _finalDue = _lastNow + 2000;
                break;
            case TakeKind.EditInstruction:
                _activeKind = null;
                _sink?.Invoke(new DictationEvent.Final(new TakeResult
                {
                    RawSegments = new() { DemoScript.SpokenInstruction },
                    FinalLines = new(),
                }));
                break;
        }
    }

    public void Cancel(CancelReason? reason = null)
    {
        _pendingSegments = new();
        _finalDue = null;
        _speechStartDue = null;
        _activeKind = null;
    }

    public void Tick(double now)
    {
        _lastNow = now;
        if (_speechStartDue != null && now >= _speechStartDue)
        {
            _speechStartDue = null;
            _sink?.Invoke(new DictationEvent.SpeechStart());
        }
        while (_pendingSegments.Count > 0 && _pendingSegments[0].due <= now)
        {
            var next = _pendingSegments[0];
            _pendingSegments.RemoveAt(0);
            _sink?.Invoke(new DictationEvent.Segment(next.index, next.text));
        }
        if (_finalDue != null && now >= _finalDue)
        {
            _finalDue = null;
            _activeKind = null;
            _sink?.Invoke(new DictationEvent.Final(DemoFixtures.DemoTakeResult()));
        }
    }

    public void ResyncRender()
    {
        if (_activeKind != TakeKind.Dictation || _finalDue != null) return;
        ScheduleEmission(_lastNow);
    }

    public bool BeginRetry(DictationSink sink) => false;
    public bool CanRetry => false;

    private void ScheduleEmission(double start)
    {
        _pendingSegments = new();
        double t = 1150.0;
        for (int i = 0; i < DemoScript.Chunks.Length; i++)
        {
            _pendingSegments.Add((start + t, i, DemoScript.Chunks[i]));
            t += 1000 + _random() * 1800;
        }
    }

    private static readonly Random Rng = new();
}

public sealed class DemoEditService : IEditService
{
    private double _lastNow = 0;
    private (double due, List<string> lines, EditSink sink)? _pending = null;

    public void ApplyEdit(string text, EditInstruction instruction, EditSink sink)
    {
        string key = instruction switch
        {
            EditInstruction.Preset p => p.Name,
            EditInstruction.CustomStyle => "polish",
            EditInstruction.Spoken => "custom",
            _ => "polish",
        };
        var lines = (DemoScript.Refined.TryGetValue(key, out var l) ? l : DemoScript.Refined["polish"]).ToList();
        _pending = (_lastNow + 1700, lines, sink);
    }

    public void CancelEdit() => _pending = null;

    public void Tick(double now)
    {
        _lastNow = now;
        if (_pending != null && now >= _pending.Value.due)
        {
            var p = _pending.Value;
            _pending = null;
            p.sink(new EditOutcome.Ok(new EditResult { Lines = p.lines }));
        }
    }
}
