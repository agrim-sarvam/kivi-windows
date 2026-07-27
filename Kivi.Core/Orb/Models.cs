// Value models + enums, ported from packages/orb-core/src/models.ts
// (itself ported from Orb/Models/FlowModels.swift). Enum raw string values are
// carried VERBATIM (they serialize into the golden JSON), exposed via RawValue().
using System.Collections.Generic;
using System.Linq;

namespace Kivi.Core.Orb;

// MARK: - Phases

public enum FlowPhase
{
    Rest,
    Idle,
    Listening,
    Processing,
    Done,
    EditListen,
    EditProcess,
    EditDone,
    ActListen,
    ActProcess,
    ActConfirm,
}

// KiwiMarkEngine.swift:29-31
public enum KiwiMarkState
{
    Idle,
    Listening,
    Processing,
    Editing,
    Speaking,
    Done,
    Error,
    Waiting,
    Acting,
    Confirming,
}

public enum PageStyle { Light, Dark }
public enum OrbStyle { Forest, Mist }
public enum OrbSize { Normal, Mini, Pill }
public enum DefaultExpansion { Collapsed, Expanded }
public enum DefaultPosition { Top, Bottom }

public enum HintAccent { Idle, Listen, Edit }

/// lb-tx stage classes, verbatim (CSS class raw values).
public enum TxStage
{
    Idle,
    Listen,
    Wave,
    Done,
    EditPlain,
    EditWave,
    Typed,
    Pasted,
}

public enum TxTokenKind { Same, Del, Ins, Final }

public enum TxLineRole { Waiting, Speaking, Final, Dim, Plain, Tokens }

public enum HoverTarget
{
    Orb,
    SatEdit,
    SatCancel,
    SatSettings,
    SatExpand,
    Pane,
    Hint,
    Box,
    DragHandle,
    Field,
}

/// The raw string values used by the Swift enums and the golden JSON.
public static class RawValues
{
    public static string RawValue(this FlowPhase p) => p switch
    {
        FlowPhase.Rest => "rest",
        FlowPhase.Idle => "idle",
        FlowPhase.Listening => "listening",
        FlowPhase.Processing => "processing",
        FlowPhase.Done => "done",
        FlowPhase.EditListen => "editListen",
        FlowPhase.EditProcess => "editProcess",
        FlowPhase.EditDone => "editDone",
        FlowPhase.ActListen => "actListen",
        FlowPhase.ActProcess => "actProcess",
        FlowPhase.ActConfirm => "actConfirm",
        _ => "rest",
    };

    public static string RawValue(this KiwiMarkState s) => s switch
    {
        KiwiMarkState.Idle => "idle",
        KiwiMarkState.Listening => "listening",
        KiwiMarkState.Processing => "processing",
        KiwiMarkState.Editing => "editing",
        KiwiMarkState.Speaking => "speaking",
        KiwiMarkState.Done => "done",
        KiwiMarkState.Error => "error",
        KiwiMarkState.Waiting => "waiting",
        KiwiMarkState.Acting => "acting",
        KiwiMarkState.Confirming => "confirming",
        _ => "idle",
    };

    public static string RawValue(this PageStyle p) => p == PageStyle.Light ? "light" : "dark";
    public static string RawValue(this OrbStyle o) => o == OrbStyle.Forest ? "forest" : "mist";
    public static string RawValue(this OrbSize o) => o switch
    {
        OrbSize.Normal => "normal",
        OrbSize.Mini => "mini",
        OrbSize.Pill => "pill",
        _ => "normal",
    };
    public static string RawValue(this DefaultExpansion e) => e == DefaultExpansion.Collapsed ? "collapsed" : "expanded";
    public static string RawValue(this DefaultPosition p) => p == DefaultPosition.Top ? "top" : "bottom";

    public static string RawValue(this HintAccent a) => a switch
    {
        HintAccent.Idle => "idle",
        HintAccent.Listen => "listen",
        HintAccent.Edit => "edit",
        _ => "idle",
    };

    public static string RawValue(this TxStage s) => s switch
    {
        TxStage.Idle => "stage-idle",
        TxStage.Listen => "stage-listen",
        TxStage.Wave => "stage-wave",
        TxStage.Done => "stage-done",
        TxStage.EditPlain => "stage-edit-plain",
        TxStage.EditWave => "stage-edit-wave",
        TxStage.Typed => "stage-typed",
        TxStage.Pasted => "stage-pasted",
        _ => "stage-idle",
    };

    public static string RawValue(this TxTokenKind k) => k switch
    {
        TxTokenKind.Same => "same",
        TxTokenKind.Del => "del",
        TxTokenKind.Ins => "ins",
        TxTokenKind.Final => "final",
        _ => "same",
    };

    public static string RawValue(this TxLineRole r) => r switch
    {
        TxLineRole.Waiting => "waiting",
        TxLineRole.Speaking => "speaking",
        TxLineRole.Final => "final",
        TxLineRole.Dim => "dim",
        TxLineRole.Plain => "plain",
        TxLineRole.Tokens => "tokens",
        _ => "plain",
    };

    public static string RawValue(this HoverTarget t) => t switch
    {
        HoverTarget.Orb => "orb",
        HoverTarget.SatEdit => "satEdit",
        HoverTarget.SatCancel => "satCancel",
        HoverTarget.SatSettings => "satSettings",
        HoverTarget.SatExpand => "satExpand",
        HoverTarget.Pane => "pane",
        HoverTarget.Hint => "hint",
        HoverTarget.Box => "box",
        HoverTarget.DragHandle => "dragHandle",
        HoverTarget.Field => "field",
        _ => "orb",
    };

    public static TxStage TxStageFromRaw(string raw) => raw switch
    {
        "stage-idle" => TxStage.Idle,
        "stage-listen" => TxStage.Listen,
        "stage-wave" => TxStage.Wave,
        "stage-done" => TxStage.Done,
        "stage-edit-plain" => TxStage.EditPlain,
        "stage-edit-wave" => TxStage.EditWave,
        "stage-typed" => TxStage.Typed,
        "stage-pasted" => TxStage.Pasted,
        _ => TxStage.Idle,
    };
}

public static class PhaseHelpers
{
    /// "Active" keeps the orb open + shows cancel — all but rest/idle.
    public static bool PhaseIsActive(FlowPhase p) => p != FlowPhase.Rest && p != FlowPhase.Idle;

    /// listening / processing / editListen / editProcess / actListen / actProcess
    public static bool PhaseIsRecording(FlowPhase p) => p switch
    {
        FlowPhase.Listening => true,
        FlowPhase.Processing => true,
        FlowPhase.EditListen => true,
        FlowPhase.EditProcess => true,
        FlowPhase.ActListen => true,
        FlowPhase.ActProcess => true,
        _ => false,
    };

    /// Phase → KiwiMarkState (FlowModels.swift:32).
    public static KiwiMarkState PhaseMarkState(FlowPhase p) => p switch
    {
        FlowPhase.Listening => KiwiMarkState.Listening,
        FlowPhase.Processing => KiwiMarkState.Processing,
        FlowPhase.Done => KiwiMarkState.Done,
        FlowPhase.EditDone => KiwiMarkState.Done,
        FlowPhase.EditListen => KiwiMarkState.Speaking,
        FlowPhase.EditProcess => KiwiMarkState.Processing,
        FlowPhase.ActListen => KiwiMarkState.Acting,
        FlowPhase.ActProcess => KiwiMarkState.Acting,
        FlowPhase.ActConfirm => KiwiMarkState.Confirming,
        FlowPhase.Rest => KiwiMarkState.Idle,
        FlowPhase.Idle => KiwiMarkState.Idle,
        _ => KiwiMarkState.Idle,
    };
}

// MARK: - Settings

public sealed class FlowSettings
{
    public PageStyle Page = PageStyle.Light;
    public OrbStyle Orb = OrbStyle.Forest;
    public OrbSize OrbSize = OrbSize.Normal;
    public bool Tooltips = true;
    public DefaultExpansion DefaultExpansion = DefaultExpansion.Collapsed;
    public bool Movable = false;
    public DefaultPosition DefaultPosition = DefaultPosition.Top;
    public bool ReduceMotion = false;
    public bool Haptics = true;
    public bool Sounds = true;

    public FlowSettings Clone() => new()
    {
        Page = Page,
        Orb = Orb,
        OrbSize = OrbSize,
        Tooltips = Tooltips,
        DefaultExpansion = DefaultExpansion,
        Movable = Movable,
        DefaultPosition = DefaultPosition,
        ReduceMotion = ReduceMotion,
        Haptics = Haptics,
        Sounds = Sounds,
    };

    public static FlowSettings Default() => new();
}

// MARK: - Hint

public sealed class HintContent
{
    public string Text = "";
    public bool ShowsKey;
    public HintAccent Accent = HintAccent.Idle;

    public HintContent() { }
    public HintContent(string text, bool showsKey, HintAccent accent = HintAccent.Idle)
    {
        Text = text;
        ShowsKey = showsKey;
        Accent = accent;
    }

    public HintContent Clone() => new(Text, ShowsKey, Accent);
}

public static class ModelFactory
{
    public static HintContent MakeHint(string text, bool key, HintAccent accent = HintAccent.Idle) => new(text, key, accent);
}

// MARK: - Transcript tokens / lines

public sealed class TxToken
{
    public TxTokenKind Kind;
    public string Text = "";

    public TxToken() { }
    public TxToken(TxTokenKind kind, string text) { Kind = kind; Text = text; }
    public TxToken Clone() => new(Kind, Text);
}

/// A transcript line. When Role == Tokens, Tokens holds the diff tokens and
/// Text is empty; otherwise Text holds the line content.
public sealed class TxLine
{
    public TxLineRole Role;
    public string Text = "";
    public List<TxToken>? Tokens;
    public double? FadeInStart;

    public static TxLine Make(TxLineRole role, string? text = null, List<TxToken>? tokens = null, double? fadeInStart = null)
        => new() { Role = role, Text = text ?? "", Tokens = tokens, FadeInStart = fadeInStart };

    public TxLine Clone() => new()
    {
        Role = Role,
        Text = Text,
        Tokens = Tokens?.Select(t => t.Clone()).ToList(),
        FadeInStart = FadeInStart,
    };
}

public sealed class TakeSource
{
    public List<string> Raw = new();
    public List<string> Final = new();
    public List<List<TxToken>>? Diff;
}

public sealed class DiffMorph
{
    public double StartedAt;
    public List<List<TxToken>> Lines = new();
}
