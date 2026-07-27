// TranscriptModel — value-typed port of packages/orb-core/src/transcript.ts
// (Orb/Core/Transcript.swift). The engine mutates it; the view reads a per-frame copy
// embedded in FlowFrame.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Kivi.Core.Orb;

public sealed class TxSnapshot
{
    public string Stage = "";
    public string Payload = "";

    public TxSnapshot() { }
    public TxSnapshot(string stage, string payload) { Stage = stage; Payload = payload; }
}

public interface IFlowStore
{
    FlowSettings LoadSettings();
    void SaveSettings(FlowSettings s);
    List<TxSnapshot> LoadPlayback();
    void SavePlayback(List<TxSnapshot> a);
}

/// In-memory store for tests (the harness's MemoryFlowStore).
public sealed class MemoryFlowStore : IFlowStore
{
    public FlowSettings Settings = FlowSettings.Default();
    public List<TxSnapshot> Playback = new();
    public FlowSettings LoadSettings() => Settings;
    public void SaveSettings(FlowSettings s) => Settings = s;
    public List<TxSnapshot> LoadPlayback() => Playback;
    public void SavePlayback(List<TxSnapshot> a) => Playback = a;
}

public sealed class TranscriptModel
{
    public TxStage Stage = TxStage.Idle;
    public List<TxLine> Lines = new();
    public double? DotsStartedAt = null;
    public bool AwaitingSpeech = false;
    public string? Notice = null;
    public string? Banner = null;
    public DiffMorph? Morph = null;

    public TakeSource Source = DemoFixtures.DemoTakeSource();

    public int Step = 0;
    public int Frame = 2;
    public List<string>? RefineLines = null;
    public bool Browsing = false;
    public int HistoryAt = 0;
    public List<TxSnapshot> History = new();
    public List<TxSnapshot>? BaseFrames = null;
    public TxSnapshot? BasePending = null;
    public TxSnapshot? Prev = null;

    // MARK: editability

    public bool EditableContent
    {
        get
        {
            switch (Stage)
            {
                case TxStage.Idle:
                case TxStage.Typed:
                case TxStage.Pasted:
                    return true;
                case TxStage.Done:
                    if (Morph is not null) return false;
                    return Lines.All(line =>
                    {
                        if (line.Role == TxLineRole.Tokens)
                            return (line.Tokens ?? new()).All(t => t.Kind == TxTokenKind.Same || t.Kind == TxTokenKind.Final);
                        return true;
                    });
                default: // Listen, Wave, EditPlain, EditWave
                    return false;
            }
        }
    }

    public string EditorSeed => Stage == TxStage.Idle ? "" : PlainText;

    // MARK: snapshots

    private static string ArchivedLineJson(TxLine line)
    {
        // Swift's JSONEncoder(.sortedKeys) omits nil optionals; TS reproduces the
        // exact string via JSON.stringify. Match its key order + escaping.
        if (line.Role == TxLineRole.Tokens)
        {
            var toks = (line.Tokens ?? new()).Select(t => new[] { t.Kind.RawValue(), t.Text }).ToList();
            var sb = new StringBuilder();
            sb.Append("{\"role\":\"tokens\",\"text\":\"\",\"tokens\":[");
            for (int i = 0; i < toks.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(JsonStr(toks[i][0])).Append(',').Append(JsonStr(toks[i][1])).Append(']');
            }
            sb.Append("]}");
            return sb.ToString();
        }
        return "{\"role\":" + JsonStr(line.Role.RawValue()) + ",\"text\":" + JsonStr(line.Text) + "}";
    }

    private static string JsonStr(string s) => JsonSerializer.Serialize(s);

    private static string TrimWs(string s) => s.Trim();

    public TxSnapshot Snapshot()
    {
        var payload = "[" + string.Join(",", Lines.Select(ArchivedLineJson)) + "]";
        return new TxSnapshot(Stage.RawValue(), payload);
    }

    private sealed class ArchivedLine
    {
        public string role { get; set; } = "";
        public string text { get; set; } = "";
        public string[][]? tokens { get; set; }
    }

    public void Restore(TxSnapshot s)
    {
        var valid = new[] { "stage-idle", "stage-listen", "stage-wave", "stage-done", "stage-edit-plain", "stage-edit-wave", "stage-typed", "stage-pasted" };
        Stage = RawValues.TxStageFromRaw(valid.Contains(s.Stage) ? s.Stage : "stage-idle");
        try
        {
            var archived = JsonSerializer.Deserialize<List<ArchivedLine>>(s.Payload) ?? new();
            Lines = archived.Select(a =>
            {
                if (a.role == "tokens")
                {
                    var tokens = (a.tokens ?? Array.Empty<string[]>())
                        .Where(p => p.Length == 2)
                        .Select(p => new TxToken(KindFromRaw(p[0]), p[1]))
                        .ToList();
                    return TxLine.Make(TxLineRole.Tokens, tokens: tokens);
                }
                var role = new[] { "waiting", "speaking", "final", "dim" }.Contains(a.role) ? RoleFromRaw(a.role) : TxLineRole.Plain;
                return TxLine.Make(role, text: a.text);
            }).ToList();
        }
        catch
        {
            Lines = new();
        }
        DotsStartedAt = null;
        Morph = null;
    }

    private static TxTokenKind KindFromRaw(string s) => s switch
    {
        "same" => TxTokenKind.Same,
        "del" => TxTokenKind.Del,
        "ins" => TxTokenKind.Ins,
        "final" => TxTokenKind.Final,
        _ => TxTokenKind.Same,
    };

    private static TxLineRole RoleFromRaw(string s) => s switch
    {
        "waiting" => TxLineRole.Waiting,
        "speaking" => TxLineRole.Speaking,
        "final" => TxLineRole.Final,
        "dim" => TxLineRole.Dim,
        _ => TxLineRole.Plain,
    };

    public void SnapshotPrev() => Prev = Snapshot();

    public bool RestorePrev()
    {
        if (Prev is null) return false;
        Restore(Prev);
        return true;
    }

    public void CaptureBasePending()
    {
        if (BaseFrames is not null) { BasePending = null; return; }
        if (Browsing || Stage == TxStage.Typed || Stage == TxStage.Pasted || Stage == TxStage.Done)
            BasePending = Snapshot();
        else
            BasePending = null;
    }

    // MARK: archive

    public string PlainText => string.Join("\n", Lines.Select(line =>
    {
        if (line.Role == TxLineRole.Tokens)
            return string.Concat((line.Tokens ?? new()).Where(t => t.Kind != TxTokenKind.Del).Select(t => t.Text));
        return line.Text;
    }));

    public void SnapHistory(IFlowStore store)
    {
        if (Browsing) return;
        if (Stage != TxStage.Done && Stage != TxStage.Typed && Stage != TxStage.Pasted) return;
        RecordHistory(PlainText, store);
    }

    public void RecordHistory(string text, IFlowStore store)
    {
        var clean = TrimWs(text);
        if (clean == "") return;
        var payload = "[" + ArchivedLineJson(TxLine.Make(TxLineRole.Plain, text: clean)) + "]";
        var entry = new TxSnapshot("stage-done", payload);
        var last = History.Count > 0 ? History[^1] : null;
        if (last != null && last.Payload == entry.Payload) return;
        History.Add(entry);
        if (History.Count > 24) History = History.Skip(History.Count - 24).ToList();
        store.SavePlayback(History);
    }

    public void RecallLast()
    {
        if (History.Count == 0) return;
        HistoryAt = History.Count - 1;
        Browsing = true;
        BaseFrames = null;
        BasePending = null;
        Restore(History[HistoryAt]);
    }

    public void LoadSession(List<string> states)
    {
        var snapshots = new List<TxSnapshot>();
        foreach (var text in states)
        {
            var clean = TrimWs(text);
            if (clean == "") continue;
            var payload = "[" + ArchivedLineJson(TxLine.Make(TxLineRole.Plain, text: clean)) + "]";
            snapshots.Add(new TxSnapshot("stage-done", payload));
        }
        if (snapshots.Count == 0) return;
        History = snapshots;
        HistoryAt = snapshots.Count - 1;
        Browsing = true;
        BaseFrames = null;
        BasePending = null;
        Restore(History[HistoryAt]);
    }

    public void HistoryStep(int delta)
    {
        if (History.Count == 0) return;
        var target = Math.Max(0, Math.Min(History.Count - 1, HistoryAt + delta));
        if (target == HistoryAt) return;
        HistoryAt = target;
        Browsing = true;
        BaseFrames = null;
        BasePending = null;
        Restore(History[target]);
    }

    // MARK: review frames

    public int MaxFrame => RefineLines is not null ? 3 : 2;

    public void ShowFrame(int n)
    {
        Morph = null;
        Stage = TxStage.Done;
        if (n == 3 && RefineLines is not null)
        {
            Lines = RefineLines.Select(t => TxLine.Make(TxLineRole.Plain, text: t)).ToList();
            return;
        }
        switch (n)
        {
            case 0:
                if (Source.Diff is not null)
                    Lines = Source.Diff.Select(toks => TxLine.Make(TxLineRole.Plain,
                        text: string.Concat(toks.Where(t => t.Kind != TxTokenKind.Ins).Select(t => t.Text)))).ToList();
                else
                    Lines = Source.Raw.Select(t => TxLine.Make(TxLineRole.Plain, text: t)).ToList();
                break;
            case 1:
                if (Source.Diff is not null)
                    Lines = Source.Diff.Select(toks => TxLine.Make(TxLineRole.Tokens, tokens: toks.Select(t => t.Clone()).ToList())).ToList();
                else
                    Lines = Source.Final.Select(t => TxLine.Make(TxLineRole.Plain, text: t)).ToList();
                break;
            default:
                if (Source.Final.Count == 0 && Source.Diff is not null)
                    Lines = Source.Diff.Select(toks => TxLine.Make(TxLineRole.Plain,
                        text: string.Concat(toks.Where(t => t.Kind != TxTokenKind.Del).Select(t => t.Text)))).ToList();
                else
                    Lines = Source.Final.Select(t => TxLine.Make(TxLineRole.Plain, text: t)).ToList();
                break;
        }
    }

    public void SettleDoneFrame()
    {
        Step = Source.Raw.Count;
        ShowFrame(2);
        Frame = 2;
    }

    public void StepShow(int n)
    {
        if (BaseFrames is not null)
        {
            Frame = Math.Max(0, Math.Min(BaseFrames.Count - 1, n));
            Morph = null;
            Restore(BaseFrames[Frame]);
        }
        else
        {
            Frame = Math.Max(0, Math.Min(MaxFrame, n));
            ShowFrame(Frame);
        }
    }

    // MARK: edit results

    public void ApplyEditResult(List<string> newLines)
    {
        RefineLines = newLines;
        Stage = TxStage.Done;
        Morph = null;
        Lines = newLines.Select(t => TxLine.Make(TxLineRole.Plain, text: t)).ToList();
        if (BasePending is not null)
        {
            BaseFrames = new() { BasePending, Snapshot() };
            BasePending = null;
            Frame = 1;
        }
        else if (BaseFrames is not null)
        {
            BaseFrames.Add(Snapshot());
            Frame = BaseFrames.Count - 1;
        }
        else
        {
            Step = Source.Raw.Count;
            Frame = 3;
        }
    }

    public List<TxLine> CurrentPlainFinal()
    {
        if (Stage == TxStage.Typed || Stage == TxStage.Pasted || Browsing || BaseFrames is not null || BasePending is not null)
        {
            return Lines.Select(line =>
            {
                if (line.Role == TxLineRole.Tokens)
                    return TxLine.Make(TxLineRole.Plain, text: string.Concat((line.Tokens ?? new()).Where(t => t.Kind != TxTokenKind.Del).Select(t => t.Text)));
                return TxLine.Make(TxLineRole.Plain, text: line.Text);
            }).ToList();
        }
        if (Source.Diff is not null)
            return Source.Diff.Select(toks => TxLine.Make(TxLineRole.Plain,
                text: string.Concat(toks.Where(t => t.Kind != TxTokenKind.Del).Select(t => t.Text)))).ToList();
        return Source.Final.Select(t => TxLine.Make(TxLineRole.Plain, text: t)).ToList();
    }

    public string FinalText()
    {
        if (Stage == TxStage.Typed || Stage == TxStage.Pasted || Browsing || BaseFrames is not null)
            return PlainText;
        if (Frame == 3 && RefineLines is not null)
            return string.Join("\n", RefineLines);
        return string.Join("\n", Source.Final);
    }

    // MARK: diff morph

    public void StartMorph(double time, List<List<TxToken>> diffLines)
    {
        Morph = new DiffMorph { StartedAt = time, Lines = diffLines };
    }

    public void StopMorph() => Morph = null;

    public void TickMorph(double now)
    {
        if (Morph is null) return;
        if (now - Morph.StartedAt >= 150 + 100 + 250)
            Morph = null;
    }
}
