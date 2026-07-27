// FlowEngine — pure, injected-time port of packages/orb-core/src/flowEngine.ts
// (Orb/Core/FlowEngine.swift).
//
// Contract (preserved):
//  1. every animated scalar eases toward a target per tick:
//     v += (target − v) · ease60(k, dt) with ease60(k) = 1 − (1−k)^(dt/16).
//  2. every timed step is scheduled via a generation-guarded Later(ms); any new
//     gesture calls ClearTimers() which bumps `seq` and voids in-flight sequences.
//
// No DateTime/Timer — the clock is injected. Step(nowMs) returns a FlowFrame.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kivi.Core.Orb;

public sealed class FlowEngine
{
    // JS-compatible helpers ---------------------------------------------------
    private static double Trunc(double v) => Math.Truncate(v);
    // JS Math.round: round-half-UP toward +∞ (Math.floor(x+0.5)); NOT banker's.
    private static double JsRound(double v) => Math.Floor(v + 0.5);
    private static string TrimWs(string s) => s.Trim();

    private sealed class Scheduled
    {
        public double Due;
        public int Generation;
        public Action Fire = () => { };
    }

    private abstract record ServiceIntake
    {
        public sealed record Dictation(DictationEvent Event) : ServiceIntake;
        public sealed record Edit(EditOutcome Outcome) : ServiceIntake;
    }

    private abstract record ExternalEditPreparationState
    {
        public sealed record Idle : ExternalEditPreparationState;
        public sealed record Pending(int Generation) : ExternalEditPreparationState;
        public sealed record Resolved(int Generation, bool HasTarget) : ExternalEditPreparationState;
    }

    // injected
    private readonly IFlowStore _store;
    private readonly IDictationService _dictation;
    private readonly IEditService _edit;

    // service intake
    private int _takeGeneration = 0;
    private List<(int generation, ServiceIntake intake)> _pendingServiceEvents = new();
    private List<string> _takeSegments = new();
    private double _processingStartAt = 0;
    private double _editProcessStartAt = 0;
    private string _editBaseText = "";
    private string? _externalEditBaseText = null;
    private double? _finalTimeoutDue = null;
    private double? _finalTimeoutAbsoluteCapDue = null;
    private double? _spokenEditFlushDue = null;
    private bool _processingStillWorkingShown = false;
    private bool _processingLongerThanUsualShown = false;
    private bool _pathInterruptedDuringTake = false;
    private bool _linkLostDuringTake = false;
    public bool RetryableFailurePresented = false;
    public Func<bool> IsLikelyOffline = () => false;

    private ExternalEditPreparationState _externalEditPreparationState = new ExternalEditPreparationState.Idle();
    private EditInstruction? _pendingEditDispatchAfterExternalPreparation = null;
    private string? _asyncExternalEditOrbFallbackText = null;

    // clock
    public double Now = 0;

    // state machine
    private FlowPhase _phase = FlowPhase.Rest;
    public FlowSettings Settings = FlowSettings.Default();

    private double _open = 0;
    private double _eo = 0;
    private double _h2f = 0;
    private double _ref = 0;
    private double _setFade = 0;
    private double _expFade = 0;
    private double _cxf = 0;
    private double _eyeOpenE = 0;
    private double _exp = 0;

    private double _holdUntil = 0;
    private double _botHideAt = 0;
    private double _editHideAt = 0;
    private double _cancelHideAt = 0;
    private double _expFaintUntil = 0;
    private double _shakeUntil = 0;
    private double _orbShakeUntil = 0;
    private bool _editResultKeptInOrb = false;

    private bool _hovered = false;
    private bool _orbNear = false;
    private bool _groupHover = false;
    private bool _canEdit = false;
    private bool _editOpen = false;
    private bool _hintHidden = false;
    private bool _expanded = false;
    private int _boxHostCount = 0;
    public bool WorkspaceSink = false;
    private bool _pressed = false;

    private bool _satEditHover = false;
    private bool _satSettingsHover = false;
    private bool _satExpandHover = false;
    private bool _satCancelHover = false;
    private HoverTarget? _lastHoverTarget = null;
    private bool _boxOnLeft = false;
    private double _edgeRoomLeft = double.MaxValue;
    private double _edgeRoomRight = double.MaxValue;
    private bool _flipY = false;

    private bool _editAvailableBeforeTalk = false;
    private double _listenStartAt = 0;
    private int _micWarnStage = 0;
    private double _pressStart = 0;
    private bool _pressedFromIdle = false;
    private double _keyPressStart = 0;
    private bool _keyPressedFromIdle = false;
    private bool _fnHeld = false;

    private HintContent _hintContent = ModelFactory.MakeHint("tap / hold to talk", true, HintAccent.Idle);
    private string _hint2Verb = "to edit";
    private int _takeRating = 0;
    private bool _takeRatable = false;
    private string? _lastSettledTurn = null; // "dictation" | "edit"
    private string? _activeOwnBoxContextKind = null;
    private string? _activeContextCardKind = null;
    private string? _pendingSpokenEditBase = null;
    private EditInstruction? _lastEditInstruction = null;
    private string? _lastEditHint = null;

    private double _pillPop = 0;
    private string? _takeHostAppBundleID = null;
    private string _selectionPillText = "";
    private string? _selectionPillAppBundleID = null;
    private string _selectionPillDisplayText = "";
    private string? _selectionPillDisplayAppBundleID = null;
    private double _selectionPillProgress = 0;

    // pointer light
    private double _lightTX = Constants.REST_LIGHT.x;
    private double _lightTY = Constants.REST_LIGHT.y;
    private double _lightX = Constants.REST_LIGHT.x;
    private double _lightY = Constants.REST_LIGHT.y;

    // glow colour
    private double _glowColR = Constants.REST_GLOW[0];
    private double _glowColG = Constants.REST_GLOW[1];
    private double _glowColB = Constants.REST_GLOW[2];

    // toast + copy feedback
    private string _toastText = "";
    private double _toastUntil = 0;
    private double _copyFlashUntil = 0;

    // generation-guarded sequences
    private int _seq = 0;
    private List<Scheduled> _scheduled = new();

    // transient timers
    private double? _groupLeaveAt = null;
    private double? _editPaneCloseAt = null;
    private double? _satSettingsLeaveAt = null;
    private double? _satExpandLeaveAt = null;
    private double? _satCancelLeaveAt = null;
    private double _editTipForcedUntil = 0;
    private double _hintFlashUntil = 0;
    private string _hintFlashText = "";

    // transcript
    public TranscriptModel Tx = new();
    private int _txSession = 0;
    private int _scrollSerial = 0;
    private (int id, ScrollTarget target)? _pendingScroll = null;
    private double _boxW = Constants.BOX_DEFAULT.W;
    private double _boxGrowUp = 0;
    private double _boxGrowDown = 0;
    private double _boxWTarget = Constants.BOX_DEFAULT.W;
    private double _boxGrowDownTarget = 0;
    private double _fitRequestedH = Constants.BOX_DEFAULT.H;
    private double _fitRequestedW = Constants.BOX_DEFAULT.W;
    public bool BoxMaxi = false;
    private bool _userScrolledInTake = false;
    private double? _holdTimerDue = null;
    private int _holdTimerSession = 0;
    private string _txEditOpt = "polish";
    private double _histShakeUntil = 0;
    private double _boxShakeUntil = 0;
    private double _copyHintUntil = 0;
    private bool _manualCopyRevealPending = false;
    private bool _pendingRevealArmed = false;
    private string? _pendingRevealBanner = null;
    private double _manualCopySatUntil = 0;
    private double _manualCopySatHoverUntil = 0;

    private bool _boxExpansionAllowed = true;
    public bool KeepCollapsed = false;
    public bool OrbShowcase = false;
    public KiwiMarkState ShowcaseMarkState = KiwiMarkState.Idle;
    public bool LockEdit = false;
    public bool LockOpenKivi = false;
    public bool LockPlayback = false;
    public bool LockOpenKiviSilent = false;
    public string HotkeyLabel = "fn";
    public string EditComboLabel = "⌃";
    public double MicLevel = 0;
    public string? LastCommittedText = null;

    // host hooks (unset by default)
    public Action<CueEvent>? OnStateTransition = null;
    public Action? OnTakeStart = null;
    public Action<string>? OnDictationCommit = null;
    public Action<string, string?>? OnExternalEditResult = null;
    public Action? OnExternalEditCancelled = null;
    public Action<string, string>? OnEditCommitted = null;
    public Action? OnOpenKivi = null;
    public Action? OnFocusBoxRequested = null;
    public Func<List<string>?>? OnRecallLatestHistory = null;
    public Action<string>? OnManualPasteRequested = null;
    public Action<string, int>? OnTakeRated = null;
    public Action? OnServiceWorkEnqueued = null;
    public Func<bool> DictationGate = () => true;
    public Func<bool> SessionStartGate = () => true;
    public Func<string> DictationBlockedMessage = () => "sign in to dictate";
    public Func<string> SessionBlockedMessage = () => "recording in progress";
    public Func<string> ActBlockedMessage = () => "sign in to act";

    public FlowEngine(IFlowStore? store = null, Func<double>? random = null,
        IDictationService? dictation = null, IEditService? edit = null)
    {
        _store = store ?? new MemoryFlowStore();
        var rnd = random ?? (() => new Random().NextDouble());
        _dictation = dictation ?? new DemoDictationService(rnd);
        _edit = edit ?? new DemoEditService();
        Settings = _store.LoadSettings();
        Tx.History = _store.LoadPlayback();
    }

    // --- phase (single mutation funnel) ---
    public FlowPhase Phase => _phase;

    public bool NeedsRuntimeTicks =>
        PhaseHelpers.PhaseIsActive(_phase) ||
        _phase == FlowPhase.Done ||
        _phase == FlowPhase.EditDone ||
        _scheduled.Count > 0 ||
        _finalTimeoutDue != null ||
        _spokenEditFlushDue != null ||
        _holdTimerDue != null ||
        _groupLeaveAt != null ||
        _editPaneCloseAt != null ||
        _satSettingsLeaveAt != null ||
        _satExpandLeaveAt != null ||
        _satCancelLeaveAt != null;

    private void SetPhase(FlowPhase p)
    {
        if (_phase == p) return;
        var old = _phase;
        _phase = p;
        OnStateTransition?.Invoke(new CueEvent(CueKind(p), old, p));
    }

    public static CueEventKind CueKind(FlowPhase p) => p switch
    {
        FlowPhase.Listening => CueEventKind.Listening,
        FlowPhase.Processing => CueEventKind.Processing,
        FlowPhase.Done => CueEventKind.Done,
        FlowPhase.EditListen => CueEventKind.EditListen,
        FlowPhase.EditProcess => CueEventKind.EditProcess,
        FlowPhase.EditDone => CueEventKind.EditDone,
        FlowPhase.ActListen => CueEventKind.Acting,
        FlowPhase.ActProcess => CueEventKind.Acting,
        FlowPhase.ActConfirm => CueEventKind.Confirming,
        FlowPhase.Idle => CueEventKind.Idle,
        FlowPhase.Rest => CueEventKind.Idle,
        _ => CueEventKind.Idle,
    };

    private void EmitCue(CueEventKind kind)
    {
        OnStateTransition?.Invoke(new CueEvent(kind, _phase, _phase));
    }

    private bool BoxLive => _expanded || _boxHostCount > 0;
    private double BoxH => Constants.BOX_DEFAULT.H + _boxGrowUp + _boxGrowDown;

    private Size3 WokenSize => Settings.OrbSize switch
    {
        OrbSize.Mini => Constants.WAKE_MINI,
        OrbSize.Pill => Constants.REST,
        _ => Constants.WAKE,
    };

    // --- timers ---
    private void ClearTimers()
    {
        _seq += 1;
        _scheduled = new();
    }
    private void Later(double ms, Action fn)
    {
        _scheduled.Add(new Scheduled { Due = Now + ms, Generation = _seq, Fire = fn });
    }

    private void EnqueueServiceEvent(int generation, ServiceIntake intake)
    {
        _pendingServiceEvents.Add((generation, intake));
        OnServiceWorkEnqueued?.Invoke();
    }

    // --- settings ---
    public void Apply(FlowSettings settings)
    {
        Settings = settings;
        _store.SaveSettings(settings);
    }
    public void DisableTooltips()
    {
        Settings = Settings.Clone();
        Settings.Tooltips = false;
        _store.SaveSettings(Settings);
    }

    // --- hint helpers ---
    private void SetHint(string text, bool key, HintAccent? accent = null)
    {
        _hintContent = new HintContent(text, key, accent ?? _hintContent.Accent);
    }
    private void ToIdle()
    {
        SetPhase(FlowPhase.Idle);
        KeepCollapsed = false;
        SetHint("tap / hold to talk", true, HintAccent.Idle);
    }
    private void FlashHint(string message, double duration = 1800)
    {
        _hintFlashText = message;
        _hintFlashUntil = Now + duration;
    }
    private void ShowToast(string message)
    {
        _toastText = message;
        _toastUntil = Now + 1500;
    }
    private bool GuardDictationGate()
    {
        if (!DictationGate())
        {
            ShowToast(DictationBlockedMessage());
            return false;
        }
        return true;
    }
    private bool GuardSessionStart()
    {
        if (!SessionStartGate())
        {
            ShowToast(SessionBlockedMessage());
            return false;
        }
        return true;
    }

    // --- primary talk flow ---
    private void StartListening()
    {
        ClearTimers();
        OnTakeStart?.Invoke();
        _editAvailableBeforeTalk = _canEdit;
        _listenStartAt = Now;
        _canEdit = false;
        _editOpen = false;
        MicLevel = 0;
        _micWarnStage = 0;
        if (Settings.DefaultExpansion == DefaultExpansion.Expanded && !_expanded) SetExpanded(true);
        SetPhase(FlowPhase.Listening);
        _cancelHideAt = Now + 2600;
        SetHint("tap / release to transcribe", true, HintAccent.Listen);
        Tx.Banner = null;
        Tx.AwaitingSpeech = true;
        Tx.Notice = null;
        if (BoxLive) TxStartListen();
        BeginTake(TakeKind.Dictation);
    }

    private void BeginTake(TakeKind kind)
    {
        _takeGeneration += 1;
        _editResultKeptInOrb = false;
        _takeSegments = new();
        ClearFinalTimeoutBudget();
        _spokenEditFlushDue = null;
        _processingStillWorkingShown = false;
        _processingLongerThanUsualShown = false;
        _pathInterruptedDuringTake = false;
        _linkLostDuringTake = false;
        RetryableFailurePresented = false;
        _takeRating = 0;
        _takeRatable = false;
        ResetExternalEditPreparationState();
        var generation = _takeGeneration;
        _dictation.Begin(kind, default, BoxLive, (ev) =>
        {
            EnqueueServiceEvent(generation, new ServiceIntake.Dictation(ev));
        });
    }

    private void StopListening()
    {
        ClearTimers();
        SetPhase(FlowPhase.Processing);
        _processingStartAt = Now;
        _processingStillWorkingShown = false;
        _processingLongerThanUsualShown = false;
        _cancelHideAt = Now + 2600;
        SetHint("transcribing", false);
        if (_linkLostDuringTake)
        {
            Tx.Banner = "connection lost — kivi will retry";
            FlashHint("connection lost — kivi will retry", 2800);
        }
        else if (_pathInterruptedDuringTake)
        {
            Tx.Banner = "connection interrupted — still waiting for kivi";
            FlashHint("connection interrupted — still waiting for kivi", 2800);
        }
        if (BoxLive) TxToProcessing();
        ArmFinalTimeoutBudget();
        _dictation.RequestStop(default);
    }

    private void ArmFinalTimeoutBudget()
    {
        _finalTimeoutDue = Now + Constants.FINAL_TIMEOUT_MS;
        _finalTimeoutAbsoluteCapDue = Now + Constants.FORMATTING_PROGRESS_ABSOLUTE_CAP_MS;
    }
    private void ClearFinalTimeoutBudget()
    {
        _finalTimeoutDue = null;
        _finalTimeoutAbsoluteCapDue = null;
    }

    private void CommitDictationToHost(TakeResult result)
    {
        if (WorkspaceSink) return;
        var final = string.Join("\n", result.FinalLines);
        if (TrimWs(final) == "") return;
        LastCommittedText = final;
        OnDictationCommit?.Invoke(final);
    }

    private void PresentDone(TakeResult result, string hintText = "done")
    {
        if (_pathInterruptedDuringTake || _linkLostDuringTake)
        {
            Tx.Banner = null;
            _pathInterruptedDuringTake = false;
            _linkLostDuringTake = false;
        }
        var finalText = TrimWs(string.Join("\n", result.FinalLines));
        var rawText = TrimWs(string.Join(" ", result.RawSegments));
        if (finalText == "" && rawText == "")
        {
            _pendingRevealArmed = false;
            PresentNoSpeech();
            return;
        }
        Tx.BaseFrames = null;
        Tx.BasePending = null;
        Tx.RecordHistory(finalText == "" ? rawText : finalText, _store);
        _takeGeneration += 1;
        ClearFinalTimeoutBudget();
        SetPhase(FlowPhase.Done);
        _takeRatable = true;
        _lastSettledTurn = "dictation";
        _activeOwnBoxContextKind = null;
        _activeContextCardKind = null;
        SetHint(hintText, false);
        _expFaintUntil = Now + 4000;
        var diff =
            TokensFromLite(result.DiffLines) ??
            (BoxLive ? new List<List<TxToken>> { DiffTokens(rawText, finalText) } : null);
        Tx.Source = new TakeSource { Raw = result.RawSegments, Final = result.FinalLines, Diff = diff };
        if (BoxLive && !_manualCopyRevealPending && !_pendingRevealArmed) TxDiff();
        else Tx.SettleDoneFrame();
        _manualCopyRevealPending = false;
        if (_pendingRevealArmed)
        {
            _pendingRevealArmed = false;
            _pendingRevealBanner = null;
        }
        Later(150, () =>
        {
            _canEdit = true;
            ToIdle();
            _holdUntil = Now + 350;
            _editHideAt = Now + 350;
            _hint2Verb = "to edit";
        });
    }

    private void PresentNotice(string text)
    {
        _takeGeneration += 1;
        ClearFinalTimeoutBudget();
        SetPhase(FlowPhase.Done);
        Tx.StopMorph();
        Tx.Stage = TxStage.Done;
        Tx.Lines = new();
        Tx.DotsStartedAt = null;
        Tx.AwaitingSpeech = false;
        Tx.Notice = text;
        _canEdit = false;
        SetHint(text, false);
        FlashHint(text, 2200);
        _expFaintUntil = Now + 2600;
        Later(2200, () =>
        {
            ToIdle();
            _holdUntil = Now + 1500;
        });
    }
    private void PresentNoSpeech() => PresentNotice("no speech detected");
    private void PresentIdleTimeout() => PresentNotice("Idle timeout");

    private static List<List<TxToken>>? TokensFromLite(List<List<TxToken>>? lite)
    {
        if (lite is null) return null;
        return lite.Select(line => line.Select(t => new TxToken(t.Kind, t.Text)).ToList()).ToList();
    }

    private static readonly Regex WordSplit = new(@"[ \n\t]", RegexOptions.Compiled);

    public static List<TxToken> DiffTokens(string before, string after)
    {
        var a = WordSplit.Split(before).Where(s => s.Length > 0).ToArray();
        var b = WordSplit.Split(after).Where(s => s.Length > 0).ToArray();
        int m = a.Length;
        int n = b.Length;
        if (m == 0 && n == 0) return new();
        if ((double)m * n > Constants.MAX_DIFF_TOKEN_PRODUCT)
            return b.Select(w => new TxToken(TxTokenKind.Same, w + " ")).ToList();
        var dp = new int[m + 1][];
        for (int i = 0; i <= m; i++) dp[i] = new int[n + 1];
        if (m > 0 && n > 0)
        {
            for (int i = m - 1; i >= 0; i--)
                for (int j = n - 1; j >= 0; j--)
                    dp[i][j] = a[i] == b[j] ? dp[i + 1][j + 1] + 1 : Math.Max(dp[i + 1][j], dp[i][j + 1]);
        }
        var outp = new List<TxToken>();
        int ii = 0, jj = 0;
        while (ii < m && jj < n)
        {
            if (a[ii] == b[jj]) { outp.Add(new TxToken(TxTokenKind.Same, a[ii] + " ")); ii++; jj++; }
            else if (dp[ii + 1][jj] >= dp[ii][jj + 1]) { outp.Add(new TxToken(TxTokenKind.Del, a[ii] + " ")); ii++; }
            else { outp.Add(new TxToken(TxTokenKind.Ins, b[jj] + " ")); jj++; }
        }
        while (ii < m) { outp.Add(new TxToken(TxTokenKind.Del, a[ii] + " ")); ii++; }
        while (jj < n) { outp.Add(new TxToken(TxTokenKind.Ins, b[jj] + " ")); jj++; }
        return outp;
    }

    private void SecondTapAction()
    {
        if (_editAvailableBeforeTalk && Now - _listenStartAt < Constants.DOUBLE_TAP_MS)
            StartVoiceEdit();
        else
            StopListening();
    }

    // --- edit flow ---
    private void RunEditProcess(string? opt = null)
    {
        var capturingInstruction = _phase == FlowPhase.EditListen && (opt ?? _txEditOpt) == "custom";
        ClearTimers();
        _editOpen = false;
        if (_phase != FlowPhase.EditListen && _phase != FlowPhase.EditProcess)
        {
            Tx.SnapshotPrev();
            Tx.CaptureBasePending();
        }
        Tx.Browsing = false;
        _txEditOpt = opt ?? _txEditOpt;
        SetPhase(FlowPhase.EditProcess);
        _editProcessStartAt = Now;
        _cancelHideAt = Now + 2600;
        SetHint("editing", false);
        if (capturingInstruction && _linkLostDuringTake)
        {
            Tx.Banner = "connection lost — finishing with what was captured";
            FlashHint("connection lost — finishing with what was captured", 2800);
        }
        else if (capturingInstruction && _pathInterruptedDuringTake)
        {
            Tx.Banner = "connection interrupted — finishing your edit";
            FlashHint("connection interrupted — finishing your edit", 2800);
        }
        else if (!capturingInstruction && (_pathInterruptedDuringTake || _linkLostDuringTake))
        {
            Tx.Banner = null;
            _pathInterruptedDuringTake = false;
            _linkLostDuringTake = false;
            _hintFlashUntil = 0;
        }
        if (BoxLive) TxEditWave();
        if (capturingInstruction)
        {
            _dictation.RequestStop(default);
            _spokenEditFlushDue = Now + Constants.SPOKEN_EDIT_FLUSH_MS;
            return;
        }
        _dictation.Cancel();
        DispatchEdit(InstructionFor(_txEditOpt));
    }

    private void DispatchEdit(EditInstruction instruction)
    {
        _lastEditInstruction = instruction;
        _takeGeneration += 1;
        ResetExternalEditPreparationState();
        var generation = _takeGeneration;
        var over = _externalEditBaseText;
        _externalEditBaseText = null;
        var stash = _pendingSpokenEditBase;
        _pendingSpokenEditBase = null;
        if (over != null && TrimWs(over) != "")
            _editBaseText = over;
        else if (stash != null && TrimWs(stash) != "")
            _editBaseText = stash;
        else
            _editBaseText = Tx.PlainText;
        _edit.ApplyEdit(_editBaseText, instruction, (outcome) =>
        {
            EnqueueServiceEvent(generation, new ServiceIntake.Edit(outcome));
        });
    }

    public void SetExternalEditBaseText(string? text)
    {
        var trimmed = text == null ? null : TrimWs(text);
        _externalEditBaseText = trimmed != null && trimmed != "" ? text : null;
    }

    private void ResetExternalEditPreparationState()
    {
        _externalEditPreparationState = new ExternalEditPreparationState.Idle();
        _pendingEditDispatchAfterExternalPreparation = null;
        _asyncExternalEditOrbFallbackText = null;
    }

    private bool DeferEditDispatchUntilExternalPreparationResolves(EditInstruction instruction)
    {
        if (_externalEditPreparationState is not ExternalEditPreparationState.Pending pending ||
            pending.Generation != _takeGeneration)
            return false;
        _pendingEditDispatchAfterExternalPreparation = instruction;
        SetHint("editing", false);
        return true;
    }

    private void DispatchEditIfBaseTextAvailable(EditInstruction instruction)
    {
        var over = _externalEditBaseText == null ? null : TrimWs(_externalEditBaseText);
        var stash = _pendingSpokenEditBase == null ? null : TrimWs(_pendingSpokenEditBase);
        var engineText = TrimWs(Tx.PlainText);
        if (!(over != null && over != "") && !(stash != null && stash != "") && engineText == "")
        {
            EditCancelledRestore("nothing to edit — talk, type or paste first");
            return;
        }
        DispatchEdit(instruction);
    }

    private void ResolveSpokenEdit(string? finalInstruction = null)
    {
        if (_phase != FlowPhase.EditProcess || _spokenEditFlushDue == null) return;
        _spokenEditFlushDue = null;
        _dictation.Cancel();
        var joined = finalInstruction != null ? TrimWs(finalInstruction) : JoinSpokenInstruction(_takeSegments);
        if (joined == "")
        {
            EditCancelledRestore();
            return;
        }
        EditInstruction instruction = new EditInstruction.Spoken(joined);
        if (DeferEditDispatchUntilExternalPreparationResolves(instruction)) return;
        DispatchEditIfBaseTextAvailable(instruction);
    }

    private void ResolveSpokenEditAfterFlushBudget()
    {
        if (_phase != FlowPhase.EditProcess || _spokenEditFlushDue == null) return;
        var accumulated = JoinSpokenInstruction(_takeSegments);
        if (accumulated == "")
        {
            var finalDue = _editProcessStartAt + Constants.FINAL_TIMEOUT_MS;
            if (Now < finalDue)
            {
                _spokenEditFlushDue = finalDue;
                return;
            }
        }
        ResolveSpokenEdit();
    }

    private static string? SpokenInstructionFrom(TakeResult result)
    {
        var final = JoinSpokenInstruction(result.FinalLines);
        if (final != "") return final;
        var raw = JoinSpokenInstruction(result.RawSegments);
        return raw == "" ? null : raw;
    }

    private static string JoinSpokenInstruction(IEnumerable<string> parts)
    {
        return string.Join(" ", parts.Select(TrimWs).Where(p => p != ""));
    }

    private static EditInstruction InstructionFor(string option)
    {
        if (option.StartsWith("style:")) return new EditInstruction.CustomStyle(option.Substring("style:".Length));
        return new EditInstruction.Preset(option);
    }

    private void PresentEditDone(EditResult result)
    {
        _takeGeneration += 1;
        SetPhase(FlowPhase.EditDone);
        _takeRatable = true;
        _lastSettledTurn = "edit";
        _editResultKeptInOrb = false;
        SetHint("updated", false);
        _expFaintUntil = Now + 4000;
        var editedText = string.Join("\n", result.Lines);
        OnExternalEditResult?.Invoke(editedText, result.ClientAction);
        if (_editBaseText != editedText)
            OnEditCommitted?.Invoke(_editBaseText, editedText);
        Tx.ApplyEditResult(result.Lines);
        if (BoxLive)
        {
            var diff = DiffTokens(_editBaseText, string.Join("\n", result.Lines));
            if (!Settings.ReduceMotion && diff.Any(t => t.Kind == TxTokenKind.Del || t.Kind == TxTokenKind.Ins))
                Tx.StartMorph(Now, new List<List<TxToken>> { diff });
            RequestScroll(ScrollTarget.Top);
        }
        Later(500, () =>
        {
            SetPhase(FlowPhase.Idle);
            KeepCollapsed = false;
            _canEdit = true;
            SetHint("tap / hold to talk", true, HintAccent.Idle);
            _hint2Verb = "to re-edit";
            if (_editResultKeptInOrb)
            {
                _holdUntil = Math.Max(_holdUntil, Now + Constants.EDIT_REVIEW_HOLD);
                _editHideAt = Math.Max(_editHideAt, Now + Constants.EDIT_REVIEW_HOLD);
            }
            else
            {
                _holdUntil = Now + 600;
                _editHideAt = Now + 600;
            }
        });
    }

    private void EditFailed()
    {
        ClearTimers();
        _takeGeneration += 1;
        ResetExternalEditPreparationState();
        _edit.CancelEdit();
        OnExternalEditCancelled?.Invoke();
        _txSession += 1;
        Tx.StopMorph();
        _editOpen = false;
        SetPhase(FlowPhase.Idle);
        Tx.BasePending = null;
        _canEdit = true;
        _editHideAt = Now + 4000;
        if (BoxLive)
        {
            if (!Tx.RestorePrev()) TxShowFrame(Math.Min(Tx.Frame, 2));
            RequestScroll(ScrollTarget.Top);
        }
        EmitCue(CueEventKind.Error);
        SetHint("edit failed — try again", false);
        FlashHint("edit failed — try again", 2600);
        Later(2600, () => ToIdle());
        _holdUntil = Now + 4000;
    }

    private void EditCancelledRestore(string hintText = "edit cancelled")
    {
        ClearTimers();
        _takeGeneration += 1;
        ClearFinalTimeoutBudget();
        _spokenEditFlushDue = null;
        ResetExternalEditPreparationState();
        _externalEditBaseText = null;
        OnExternalEditCancelled?.Invoke();
        _dictation.Cancel();
        _edit.CancelEdit();
        _txSession += 1;
        Tx.StopMorph();
        _editOpen = false;
        SetPhase(FlowPhase.Idle);
        Tx.BasePending = null;
        _pendingSpokenEditBase = null;
        _canEdit = true;
        _editHideAt = Now + 3000;
        if (BoxLive)
        {
            if (!Tx.RestorePrev()) TxShowFrame(Math.Min(Tx.Frame, 2));
            RequestScroll(ScrollTarget.Top);
        }
        SetHint(hintText, false);
        if (hintText != "edit cancelled") FlashHint(hintText, 2200);
        Later(1000, () => ToIdle());
        _holdUntil = Now + 1800;
    }

    private static string? ContextKind(string? targetHint) => targetHint switch
    {
        "Editing selection" => "your selection",
        "Editing last dictation" => "last dictation",
        _ => null,
    };

    private void StartVoiceEdit(string? targetHint = null)
    {
        _lastEditHint = targetHint;
        ClearTimers();
        _editOpen = false;
        _txEditOpt = "custom";
        _takeRating = 0;
        _takeRatable = false;
        if (_phase != FlowPhase.EditListen && _phase != FlowPhase.EditProcess)
        {
            Tx.SnapshotPrev();
            Tx.CaptureBasePending();
            _pendingSpokenEditBase = Tx.PlainText;
            _activeOwnBoxContextKind = _lastSettledTurn switch
            {
                "edit" => "last edit",
                "dictation" => "last dictation",
                _ => "your text",
            };
            var ownBox = targetHint == null || targetHint == "Editing Kivi text";
            _activeContextCardKind = ContextKind(targetHint) ?? (ownBox ? _activeOwnBoxContextKind : "your text");
            if (BoxLive)
            {
                _txSession += 1;
                Tx.Stage = TxStage.Listen;
                Tx.Lines = new() { TxLine.Make(TxLineRole.Waiting) };
                Tx.Notice = null;
                Tx.Banner = null;
                Tx.AwaitingSpeech = true;
                Tx.DotsStartedAt = null;
            }
        }
        Tx.Browsing = false;
        SetPhase(FlowPhase.EditListen);
        _cancelHideAt = Now + 2600;
        if (targetHint != null)
        {
            SetHint(targetHint, false, HintAccent.Edit);
            Later(500, () =>
            {
                if (_phase == FlowPhase.EditListen) SetHint("say your edit, then tap", true, HintAccent.Edit);
            });
        }
        else
        {
            SetHint("say your edit, then tap", true, HintAccent.Edit);
        }
        _dictation.Cancel();
        BeginTake(TakeKind.EditInstruction);
    }

    // --- act mode ---
    public void StartActTake()
    {
        if (_phase != FlowPhase.Idle && _phase != FlowPhase.Rest) return;
        if (!DictationGate())
        {
            ShowToast(ActBlockedMessage());
            return;
        }
        SetPhase(FlowPhase.ActListen);
    }
    public void ActToProcessing()
    {
        if (_phase == FlowPhase.ActListen) SetPhase(FlowPhase.ActProcess);
    }
    public void ActToConfirm()
    {
        if (_phase == FlowPhase.ActProcess) SetPhase(FlowPhase.ActConfirm);
    }
    public void ActResolved()
    {
        if (_phase == FlowPhase.ActListen || _phase == FlowPhase.ActProcess || _phase == FlowPhase.ActConfirm)
            SetPhase(FlowPhase.Idle);
    }
    public void ResolveAppQuery(bool found)
    {
        if (_phase == FlowPhase.ActListen || _phase == FlowPhase.ActProcess || _phase == FlowPhase.ActConfirm)
        {
            EmitCue(found ? CueEventKind.ResultReady : CueEventKind.Waiting);
            SetPhase(FlowPhase.Idle);
        }
    }

    public void CancelClick()
    {
        _manualCopySatUntil = 0;
        _manualCopySatHoverUntil = 0;
        if (_phase == FlowPhase.EditListen || _phase == FlowPhase.EditProcess)
        {
            EditCancelledRestore();
            return;
        }
        ClearTimers();
        _takeGeneration += 1;
        ClearFinalTimeoutBudget();
        _spokenEditFlushDue = null;
        ResetExternalEditPreparationState();
        _externalEditBaseText = null;
        OnExternalEditCancelled?.Invoke();
        _dictation.Cancel();
        _edit.CancelEdit();
        _txSession += 1;
        Tx.StopMorph();
        _editOpen = false;
        SetPhase(FlowPhase.Idle);
        _canEdit = false;
        _takeRatable = false;
        if (BoxLive) TxIdle();
        SetHint("cancelled", false);
        Later(1000, () => ToIdle());
        _holdUntil = Now + 1800;
    }

    // --- orb gestures ---
    public void OrbPointerDown()
    {
        if (OrbShowcase) return;
        _pressed = true;
        switch (_phase)
        {
            case FlowPhase.Idle:
            case FlowPhase.Rest:
            case FlowPhase.Done:
            case FlowPhase.EditDone:
                if (!GuardDictationGate()) return;
                if (!GuardSessionStart()) return;
                WorkspaceSink = false;
                StartListening();
                _pressStart = Now;
                _pressedFromIdle = true;
                break;
            case FlowPhase.Listening:
                SecondTapAction();
                break;
            case FlowPhase.EditListen:
                RunEditProcess();
                break;
        }
    }

    public void PointerUp()
    {
        if (OrbShowcase) return;
        _pressed = false;
        if (!_pressedFromIdle) return;
        _pressedFromIdle = false;
        if (Now - _pressStart >= Constants.HOLD_MS && _phase == FlowPhase.Listening)
            StopListening();
    }

    public void FnDown()
    {
        if (_fnHeld) return;
        _fnHeld = true;
        switch (_phase)
        {
            case FlowPhase.Idle:
            case FlowPhase.Rest:
            case FlowPhase.Done:
            case FlowPhase.EditDone:
                if (!GuardDictationGate()) return;
                if (!GuardSessionStart()) return;
                StartListening();
                _keyPressStart = Now;
                _keyPressedFromIdle = true;
                break;
            case FlowPhase.Listening:
                SecondTapAction();
                break;
            case FlowPhase.EditListen:
                RunEditProcess();
                break;
        }
    }

    public void FnUp()
    {
        _fnHeld = false;
        if (!_keyPressedFromIdle) return;
        var heldFor = Now - _keyPressStart;
        _keyPressedFromIdle = false;
        if (_phase != FlowPhase.Listening) return;
        if (heldFor < Constants.HOLD_MS) return;
        StopListening();
    }

    public void EditClick()
    {
        if (LockEdit) return;
        if (_phase == FlowPhase.EditListen)
        {
            RunEditProcess();
            return;
        }
        if (_phase == FlowPhase.EditProcess) return;
        if (!GuardDictationGate()) return;
        if (!GuardSessionStart()) return;
        if (_canEdit)
        {
            _editOpen = false;
            StartVoiceEdit();
        }
        else if (HasSettledEditableContent)
        {
            _editOpen = false;
            if (!_expanded) SetExpanded(true);
            SignalReplaceFallback("can’t edit in this app — editing here, then paste");
            StartVoiceEdit();
        }
        else
        {
            _shakeUntil = Now + 450;
            _editTipForcedUntil = Now + 1500;
            FlashHint("nothing to edit — talk, type or paste first");
        }
    }

    private bool HasSettledEditableContent =>
        (Tx.Stage == TxStage.Done || Tx.Stage == TxStage.Typed || Tx.Stage == TxStage.Pasted) &&
        TrimWs(Tx.PlainText) != "";

    public void SignalReplaceFallback(string reason)
    {
        _orbShakeUntil = Now + 600;
        FlashHint(reason, 2800);
    }

    public void SettingsClick()
    {
        if (LockOpenKivi || LockOpenKiviSilent) return;
        FlashHint("opening kivi", 1200);
        OnOpenKivi?.Invoke();
    }

    public void ExpandClick()
    {
        var wasExpanded = _expanded;
        SetExpanded(!_expanded);
        if (!wasExpanded && _expanded) OnFocusBoxRequested?.Invoke();
    }

    public void CollapseClick()
    {
        SetExpanded(false);
        _holdUntil = Now + 1200;
    }

    public void HintCloseClick() => DisableTooltips();

    // --- expansion ---
    public void SetBoxSide(bool onLeft) => _boxOnLeft = onLeft;
    public void SetEdgeRoom(double left, double right)
    {
        _edgeRoomLeft = left;
        _edgeRoomRight = right;
    }
    public void SetVerticalFlip(bool flip) => _flipY = flip;
    public void SetBoxExpansionAllowed(bool allowed)
    {
        _boxExpansionAllowed = allowed;
        if (!allowed && _expanded) SetExpanded(false);
    }
    public void SetExpanded(bool on)
    {
        if (on && !_boxExpansionAllowed) return;
        _expanded = on;
        if (!on)
        {
            BoxMaxi = false;
            _boxW = Constants.BOX_DEFAULT.W;
            _boxGrowUp = 0;
            _boxGrowDown = 0;
            _boxWTarget = Constants.BOX_DEFAULT.W;
            _boxGrowDownTarget = 0;
            _fitRequestedH = Constants.BOX_DEFAULT.H;
            _fitRequestedW = Constants.BOX_DEFAULT.W;
            return;
        }
        CatchUpBoxToPhase();
    }
    public void AddBoxHost()
    {
        _boxHostCount += 1;
        if (_boxHostCount == 1 && !_expanded) CatchUpBoxToPhase();
    }
    public void RemoveBoxHost()
    {
        _boxHostCount = Math.Max(0, _boxHostCount - 1);
    }
    private void CatchUpBoxToPhase()
    {
        switch (_phase)
        {
            case FlowPhase.Listening:
                TxStartListen();
                _dictation.ResyncRender();
                break;
            case FlowPhase.Processing:
                TxToProcessing();
                break;
            case FlowPhase.Done:
            case FlowPhase.EditDone:
                TxDiff();
                break;
            default:
                TxIdle();
                break;
        }
    }

    public void Resize(double? w, double? h)
    {
        if (w != null)
        {
            _boxWTarget = Math.Max(Constants.BOX_DEFAULT.W, Math.Min(Constants.BOX_MAX.W, w.Value));
            _boxW = _boxWTarget;
            _fitRequestedW = w.Value;
        }
        if (h != null)
        {
            var total = Math.Max(Constants.BOX_DEFAULT.H, Math.Min(Constants.BOX_MAX.H, h.Value));
            _boxGrowUp = 0;
            _boxGrowDownTarget = total - Constants.BOX_DEFAULT.H;
            _boxGrowDown = _boxGrowDownTarget;
            _fitRequestedH = h.Value;
        }
    }

    private void ApplyBoxTargets()
    {
        _boxGrowUp = 0;
        var ceiling = NormalCeiling();
        _boxWTarget = Math.Max(Constants.BOX_DEFAULT.W, Math.Min(ceiling.W, _fitRequestedW));
        _boxGrowDownTarget = Math.Max(0, Math.Min(ceiling.H, _fitRequestedH) - Constants.BOX_DEFAULT.H);
    }
    private Size2 NormalCeiling() => new(Constants.BOX_MAX.W, Constants.BOX_MAX.H);
    private bool MaxiHasScope
    {
        get
        {
            var normal = NormalCeiling();
            return _fitRequestedH > normal.H + 0.5 || _fitRequestedW > normal.W + 0.5;
        }
    }

    public void FitBoxToContent(double width, double height)
    {
        if (!BoxLive) return;
        _fitRequestedW = JsRound(width);
        _fitRequestedH = JsRound(height);
        ApplyBoxTargets();
    }

    // --- transcript stages ---
    public void NoteUserBoxScroll() => _userScrolledInTake = true;
    private void RequestScroll(ScrollTarget target)
    {
        if (target == ScrollTarget.Bottom && _userScrolledInTake) return;
        if (target == ScrollTarget.Top) _userScrolledInTake = false;
        _scrollSerial += 1;
        _pendingScroll = (_scrollSerial, target);
    }

    private void TxIdle()
    {
        Tx.SnapHistory(_store);
        Tx.Browsing = false;
        Tx.Stage = TxStage.Idle;
        Tx.Lines = new();
        Tx.DotsStartedAt = null;
        Tx.AwaitingSpeech = false;
        Tx.Banner = null;
    }

    private void TxStartListen()
    {
        _txSession += 1;
        _manualCopyRevealPending = false;
        _pendingRevealArmed = false;
        _manualCopySatUntil = 0;
        _manualCopySatHoverUntil = 0;
        Tx.SnapHistory(_store);
        Tx.Browsing = false;
        Tx.RefineLines = null;
        Tx.Stage = TxStage.Listen;
        Tx.Lines = new() { TxLine.Make(TxLineRole.Waiting) };
        Tx.Notice = null;
        Tx.AwaitingSpeech = true;
        Tx.DotsStartedAt = null;
    }

    private void TxBeginSpeech()
    {
        Tx.AwaitingSpeech = false;
        Tx.DotsStartedAt = Now;
    }

    private void TxResumeDotsOnLastChunk()
    {
        var waitIdx = LastIndex(Tx.Lines, l => l.Role == TxLineRole.Waiting);
        if (waitIdx < 0)
        {
            Tx.DotsStartedAt = Now;
            _holdTimerDue = Now + Constants.SILENCE_REVERT_MS;
            _holdTimerSession = _txSession;
            return;
        }
        Tx.Lines.RemoveAt(waitIdx);
        var lastIdx = LastIndex(Tx.Lines, l => l.Role == TxLineRole.Final || l.Role == TxLineRole.Dim);
        if (lastIdx >= 0) Tx.Lines[lastIdx].Role = TxLineRole.Speaking;
        Tx.DotsStartedAt = Now;
        _holdTimerDue = Now + Constants.SILENCE_REVERT_MS;
        _holdTimerSession = _txSession;
        RequestScroll(ScrollTarget.Bottom);
    }

    private void ShowChunkLine(string text)
    {
        var liveIdx = LastIndex(Tx.Lines, l => l.Role == TxLineRole.Waiting || l.Role == TxLineRole.Speaking);
        if (liveIdx >= 0)
        {
            if (Tx.Lines[liveIdx].Role == TxLineRole.Speaking) Tx.Lines[liveIdx].Role = TxLineRole.Final;
            else Tx.Lines.RemoveAt(liveIdx);
        }
        foreach (var l in Tx.Lines) if (l.Role == TxLineRole.Final) l.Role = TxLineRole.Dim;
        Tx.Lines.Add(TxLine.Make(TxLineRole.Speaking, text: text, fadeInStart: Now));
        RequestScroll(ScrollTarget.Bottom);
        _holdTimerDue = Now + Constants.SILENCE_REVERT_MS;
        _holdTimerSession = _txSession;
    }

    private void FinalizeAndWait()
    {
        var liveIdx = LastIndex(Tx.Lines, l => l.Role == TxLineRole.Speaking);
        if (liveIdx >= 0) Tx.Lines[liveIdx].Role = TxLineRole.Final;
        Tx.Lines.Add(TxLine.Make(TxLineRole.Waiting));
        RequestScroll(ScrollTarget.Bottom);
    }

    private void TxToProcessing()
    {
        _txSession += 1;
        _holdTimerDue = null;
        var liveIdx = LastIndex(Tx.Lines, l => l.Role == TxLineRole.Waiting || l.Role == TxLineRole.Speaking);
        if (liveIdx >= 0)
        {
            if (Tx.Lines[liveIdx].Role == TxLineRole.Waiting) Tx.Lines.RemoveAt(liveIdx);
            else Tx.Lines[liveIdx].Role = TxLineRole.Final;
        }
        foreach (var l in Tx.Lines) if (l.Role == TxLineRole.Dim) l.Role = TxLineRole.Final;
        Tx.DotsStartedAt = null;
        RequestScroll(ScrollTarget.Top);
        var session = _txSession;
        Later(460, () =>
        {
            if (_txSession != session || !_expanded || _phase != FlowPhase.Processing) return;
            if (!Settings.ReduceMotion) Tx.Stage = TxStage.Wave;
        });
    }

    private void TxDiff()
    {
        _txSession += 1;
        _holdTimerDue = null;
        Tx.DotsStartedAt = null;
        Tx.Step = Tx.Source.Raw.Count;
        Tx.ShowFrame(2);
        Tx.Frame = 2;
        var hasChanges = (Tx.Source.Diff ?? new()).Any(line => line.Any(t => t.Kind == TxTokenKind.Del || t.Kind == TxTokenKind.Ins));
        if (hasChanges && !Settings.ReduceMotion)
            Tx.StartMorph(Now, Tx.Source.Diff ?? new());
        RequestScroll(ScrollTarget.Top);
    }

    private void TxShowFrame(int n)
    {
        Tx.StopMorph();
        Tx.ShowFrame(n);
        RequestScroll(ScrollTarget.Top);
    }

    private void TxEditWave()
    {
        _txSession += 1;
        Tx.StopMorph();
        Tx.Stage = TxStage.EditPlain;
        Tx.Lines = Tx.CurrentPlainFinal();
        RequestScroll(ScrollTarget.Top);
        var session = _txSession;
        Later(360, () =>
        {
            if (_txSession != session || !_expanded || _phase != FlowPhase.EditProcess) return;
            if (!Settings.ReduceMotion) Tx.Stage = TxStage.EditWave;
        });
    }

    // band actions
    public void RateTake(bool up)
    {
        var v = up ? 1 : -1;
        _takeRating = _takeRating == v ? 0 : v;
        OnTakeRated?.Invoke(Tx.FinalText(), _takeRating);
    }
    public string CopyClick()
    {
        _copyFlashUntil = Now + 1100;
        EmitCue(CueEventKind.Copied);
        return Tx.FinalText();
    }
    public void PrevClick()
    {
        if (Tx.Browsing) Tx.HistoryStep(-1);
        else
        {
            Tx.StepShow(Tx.Frame - 1);
            RequestScroll(ScrollTarget.Top);
        }
    }
    public void NextClick()
    {
        if (Tx.Browsing) Tx.HistoryStep(1);
        else
        {
            Tx.StepShow(Tx.Frame + 1);
            RequestScroll(ScrollTarget.Top);
        }
    }

    // --- service intake drain ---
    private void DrainServiceEvents()
    {
        for (; ; )
        {
            var batch = _pendingServiceEvents;
            _pendingServiceEvents = new();
            if (batch.Count == 0) break;
            foreach (var item in batch)
            {
                if (item.generation != _takeGeneration) continue;
                if (item.intake is ServiceIntake.Dictation d) Handle(d.Event);
                else if (item.intake is ServiceIntake.Edit e) HandleEditOutcome(e.Outcome);
            }
        }
    }

    private void Handle(DictationEvent ev)
    {
        switch (ev)
        {
            case DictationEvent.Opened:
                break;
            case DictationEvent.SpeechStart:
                if (_phase == FlowPhase.Listening || _phase == FlowPhase.EditListen)
                {
                    if (Tx.AwaitingSpeech) TxBeginSpeech();
                    else TxResumeDotsOnLastChunk();
                }
                break;
            case DictationEvent.Segment segEv:
            {
                if (Tx.AwaitingSpeech) TxBeginSpeech();
                var index = segEv.Index;
                if (index < 0 || index > _takeSegments.Count + Constants.MAX_SEGMENT_BACKFILL) return;
                if (index < _takeSegments.Count) _takeSegments[index] = segEv.Text;
                else
                {
                    while (_takeSegments.Count < index) _takeSegments.Add("");
                    _takeSegments.Add(segEv.Text);
                }
                if (BoxLive && (_phase == FlowPhase.Listening || _phase == FlowPhase.EditListen) && Tx.Stage == TxStage.Listen)
                    ShowChunkLine(segEv.Text);
                break;
            }
            case DictationEvent.FormattingBudget fb:
                ApplyFormattingWait(fb.ExpectedFormatMs, null);
                break;
            case DictationEvent.FormattingProgress fp:
                ApplyFormattingWait(fp.ExpectedFormatMs, fp.ElapsedMs);
                ApplyFormattingProgressHint(fp.ElapsedMs);
                break;
            case DictationEvent.Final finalEv:
            {
                var result = finalEv.Result;
                if (_phase == FlowPhase.EditProcess && _spokenEditFlushDue != null)
                {
                    Tx.Banner = null;
                    _pathInterruptedDuringTake = false;
                    _linkLostDuringTake = false;
                    _hintFlashUntil = 0;
                    ResolveSpokenEdit(SpokenInstructionFrom(result));
                    return;
                }
                if (_phase != FlowPhase.Processing) return;
                _takeGeneration += 1;
                ClearFinalTimeoutBudget();
                _dictation.Cancel();
                CommitDictationToHost(result);
                var doneHint = result.AudioDegraded ? "done — connection hiccup; check the tail" : "done";
                var presentAt = Math.Max(Now, _processingStartAt + Constants.PROCESSING_MIN_DISPLAY_MS);
                if (presentAt <= Now)
                    PresentDone(result, doneHint);
                else
                    Later(presentAt - Now, () =>
                    {
                        if (_phase == FlowPhase.Processing) PresentDone(result, doneHint);
                    });
                break;
            }
            case DictationEvent.Retrying:
                if (_phase != FlowPhase.Processing) return;
                _processingStartAt = Now;
                _processingStillWorkingShown = false;
                _processingLongerThanUsualShown = false;
                Tx.Banner = null;
                _pathInterruptedDuringTake = false;
                _linkLostDuringTake = false;
                _hintFlashUntil = 0;
                ArmFinalTimeoutBudget();
                SetHint("hmm — retrying…", false);
                break;
            case DictationEvent.LateFinalChecking:
                if (_phase != FlowPhase.Processing) return;
                _processingStartAt = Now;
                _processingStillWorkingShown = false;
                _processingLongerThanUsualShown = false;
                ClearFinalTimeoutBudget();
                SetHint("checking for final…", false);
                break;
            case DictationEvent.LateFinalRecovered:
                if (_phase != FlowPhase.Processing) return;
                PresentLateFinalFallback("polished version saved to history", false);
                break;
            case DictationEvent.LateFinalWaiting:
                if (_phase != FlowPhase.Processing) return;
                PresentLateFinalFallback("cleanup still running — raw text is here — press ⌥⏎ to check", _dictation.CanRetry);
                break;
            case DictationEvent.LateFinalUnavailable:
                if (_phase != FlowPhase.Processing) return;
                PresentLateFinalFallback("cleanup failed — raw text is here", false);
                break;
            case DictationEvent.LinkStatus ls:
                HandleLinkStatus(ls.Status);
                break;
            case DictationEvent.Failure fl:
                HandleFailure(fl.Value);
                break;
        }
    }

    private void ApplyFormattingWait(double expectedFormatMs, double? elapsedMs)
    {
        if (_phase != FlowPhase.Processing) return;
        var capDue = _finalTimeoutAbsoluteCapDue ?? _processingStartAt + Constants.FORMATTING_PROGRESS_ABSOLUTE_CAP_MS;
        _finalTimeoutAbsoluteCapDue = capDue;
        var margin = Constants.FORMATTING_PROGRESS_DELIVERY_MARGIN_MS;
        double candidateDue;
        if (elapsedMs != null)
        {
            var remainingMs = Math.Max(0, expectedFormatMs - elapsedMs.Value);
            candidateDue = Now + remainingMs + margin;
        }
        else
        {
            var eosBudgetMs = Math.Max(Constants.FINAL_TIMEOUT_MS, expectedFormatMs + margin);
            candidateDue = _processingStartAt + eosBudgetMs;
        }
        var boundedDue = Math.Min(candidateDue, capDue);
        _finalTimeoutDue = Math.Max(_finalTimeoutDue ?? 0, boundedDue);
    }
    private void ApplyFormattingProgressHint(double? elapsedMs)
    {
        if (_phase != FlowPhase.Processing) return;
        _processingStillWorkingShown = true;
        _processingLongerThanUsualShown = true;
        var seconds = elapsedMs != null ? Trunc(Math.Max(0, elapsedMs.Value) / 1000) : 0;
        SetHint(seconds > 0 ? $"still working — {seconds}s in" : "still working…", false);
    }

    private void PresentLateFinalFallback(string hintText, bool retryOffered)
    {
        var raw = _takeSegments.Where(s => s != "").ToList();
        if (raw.Count == 0)
        {
            RetryableFailurePresented = false;
            TakeFailedSoft();
            return;
        }
        RetryableFailurePresented = retryOffered;
        ClearFinalTimeoutBudget();
        var result = new TakeResult { RawSegments = raw, FinalLines = raw, DiffLines = null };
        PresentDone(result, hintText);
        FlashHint(hintText, 2600);
        if (retryOffered)
        {
            Tx.Banner = hintText;
            _holdUntil = Math.Max(_holdUntil, Now + 7000);
        }
    }

    private void TakeFinalTimeout()
    {
        ClearFinalTimeoutBudget();
        _dictation.Cancel(CancelReason.FinalTimeout);
        var raw = _takeSegments.Where(s => s != "").ToList();
        if (raw.Count == 0)
        {
            TakeFailedSoft();
            return;
        }
        var result = new TakeResult { RawSegments = raw, FinalLines = raw, DiffLines = null };
        CommitDictationToHost(result);
        var recoveryOffered = _dictation.CanRetry;
        RetryableFailurePresented = recoveryOffered;
        var hint = recoveryOffered
            ? "cleanup still running — raw text is here — press ⌥⏎ to check"
            : "cleanup failed — raw text is here";
        PresentDone(result, hint);
        FlashHint(hint, 2600);
        if (recoveryOffered)
        {
            Tx.Banner = hint;
            _holdUntil = Math.Max(_holdUntil, Now + 7000);
        }
    }

    private void TakeFailedSoft(string? hintText = null, CueEventKind cue = CueEventKind.Waiting)
    {
        var text = hintText ?? $"didn't catch that — press {HotkeyLabel} again";
        ClearTimers();
        _takeGeneration += 1;
        ClearFinalTimeoutBudget();
        _holdTimerDue = null;
        _dictation.Cancel();
        _txSession += 1;
        Tx.StopMorph();
        _editOpen = false;
        SetPhase(FlowPhase.Idle);
        EmitCue(cue);
        _canEdit = _editAvailableBeforeTalk;
        TxIdle();
        SetHint(text, false);
        FlashHint(text, 2800);
        Later(1800, () => ToIdle());
        _holdUntil = Now + 2600;
    }

    private void TakeFailedKeepRaw(string copy = "saved what we heard — copy from here", bool historyRetry = false)
    {
        var raw = _takeSegments.Where(s => s != "").ToList();
        if (raw.Count == 0)
        {
            TakeFailedSoft($"something failed — press {HotkeyLabel} again");
            return;
        }
        var retryOffered = _dictation.CanRetry && !historyRetry;
        RetryableFailurePresented = retryOffered;
        var bannerCopy = retryOffered ? $"{copy} — press ⌥⏎ to retry" : copy;
        ClearTimers();
        _takeGeneration += 1;
        ClearFinalTimeoutBudget();
        _holdTimerDue = null;
        _dictation.Cancel();
        _txSession += 1;
        Tx.StopMorph();
        _editOpen = false;
        SetPhase(FlowPhase.Idle);
        if (!_expanded) _expanded = true;
        Tx.Source = new TakeSource { Raw = raw, Final = raw, Diff = null };
        Tx.Browsing = false;
        Tx.BaseFrames = null;
        Tx.BasePending = null;
        Tx.RefineLines = null;
        Tx.Stage = TxStage.Done;
        Tx.Lines = raw.Select(s => TxLine.Make(TxLineRole.Plain, text: s)).ToList();
        Tx.Frame = 2;
        Tx.DotsStartedAt = null;
        _canEdit = true;
        _editAvailableBeforeTalk = true;
        RequestScroll(ScrollTarget.Top);
        EmitCue(CueEventKind.RecoveredRaw);
        Tx.Banner = bannerCopy;
        var dwellMs = retryOffered || historyRetry ? 7000 : 2600;
        _copyHintUntil = Now + 4000;
        SetHint(bannerCopy, false);
        FlashHint(bannerCopy, 2800);
        Later(dwellMs, () => ToIdle());
        _holdUntil = Now + dwellMs + 400;
        _editHideAt = Now + 3000;
    }

    private void HandleFailure(TakeFailure failure)
    {
        if (_phase == FlowPhase.EditListen)
        {
            EditCancelledRestore(EditListenFailureHint(failure));
            return;
        }
        if (_phase == FlowPhase.EditProcess && _spokenEditFlushDue != null)
        {
            ResolveSpokenEdit();
            return;
        }
        if (_phase != FlowPhase.Listening && _phase != FlowPhase.Processing) return;
        ClearFinalTimeoutBudget();
        switch (failure)
        {
            case TakeFailure.Network net:
                if (net.KeepSegments)
                {
                    if (IsLikelyOffline())
                        TakeFailedKeepRaw("connection lost — saved to history; retry it there", true);
                    else TakeFailedKeepRaw("connection dropped — raw text saved to history");
                }
                else
                {
                    TakeFailedSoft($"something failed — press {HotkeyLabel} again");
                }
                break;
            case TakeFailure.Server srv:
                if (_takeSegments.Any(s => s != "")) TakeFailedKeepRaw();
                else if (srv.Code == "TENANT_CONTEXT_CHANGED")
                    TakeFailedSoft($"workspace updated — press {HotkeyLabel} again");
                else TakeFailedSoft($"something failed — press {HotkeyLabel} again");
                break;
            case TakeFailure.FinalTimeoutFailure:
                TakeFinalTimeout();
                break;
            case TakeFailure.IdleTimeout:
                PresentIdleTimeout();
                break;
            case TakeFailure.Empty:
                TakeFailedSoft();
                break;
            case TakeFailure.UsageLimit:
                TakeFailedSoft("monthly words used up", CueEventKind.LimitHit);
                break;
            case TakeFailure.Unauthorized:
                TakeFailedSoft(IsLikelyOffline() ? "you're offline — check your connection" : "sign in to use kivi");
                break;
            case TakeFailure.Busy:
                TakeFailedSoft("recording in progress");
                break;
        }
    }

    private static string EditListenFailureHint(TakeFailure failure) => failure switch
    {
        TakeFailure.Network => "connection lost — edit cancelled",
        TakeFailure.Server => "connection lost — edit cancelled",
        TakeFailure.FinalTimeoutFailure => "connection lost — edit cancelled",
        TakeFailure.Unauthorized => "sign-in needed — edit cancelled",
        TakeFailure.UsageLimit => "usage limit reached — edit cancelled",
        TakeFailure.Busy => "service busy — edit cancelled",
        TakeFailure.IdleTimeout => "idle timeout — edit cancelled",
        TakeFailure.Empty => "edit cancelled",
        _ => "edit cancelled",
    };

    private void HandleLinkStatus(DictationLinkStatus status)
    {
        var p = _phase;
        var inTake = p == FlowPhase.Listening || p == FlowPhase.Processing || p == FlowPhase.EditListen || p == FlowPhase.EditProcess;
        if (status is DictationLinkStatus.Interrupted)
        {
            if (_linkLostDuringTake || !inTake) return;
            _pathInterruptedDuringTake = true;
            string copy = p switch
            {
                FlowPhase.Listening => "connection interrupted — still recording locally",
                FlowPhase.EditListen => "connection interrupted — still recording your edit locally",
                FlowPhase.EditProcess => "connection interrupted — finishing your edit",
                _ => "connection interrupted — still waiting for kivi",
            };
            Tx.Banner = copy;
            FlashHint(copy, 2800);
        }
        else if (status is DictationLinkStatus.Lost)
        {
            if (!inTake) return;
            _pathInterruptedDuringTake = false;
            _linkLostDuringTake = true;
            string copy = p switch
            {
                FlowPhase.Listening => "connection lost — keep speaking; kivi will retry when you finish",
                FlowPhase.EditListen => "connection lost — keep speaking; we'll use what was captured",
                FlowPhase.EditProcess => "connection lost — finishing with what was captured",
                _ => "connection lost — kivi will retry",
            };
            Tx.Banner = copy;
            FlashHint(copy, 2800);
        }
        else // Restored
        {
            if (!inTake) return;
            if (_linkLostDuringTake)
            {
                string copy = p switch
                {
                    FlowPhase.Listening => "connection back — keep speaking; kivi will retry when you finish",
                    FlowPhase.EditListen => "connection back — keep speaking",
                    FlowPhase.EditProcess => "connection back — finishing your edit",
                    _ => "connection back — kivi is retrying",
                };
                Tx.Banner = copy;
                FlashHint(copy, 2800);
            }
            else if (_pathInterruptedDuringTake)
            {
                _pathInterruptedDuringTake = false;
                Tx.Banner = null;
                string baseHint = p switch
                {
                    FlowPhase.Listening => "tap / release to transcribe",
                    FlowPhase.EditListen => "say your edit, then tap",
                    FlowPhase.EditProcess => "editing",
                    _ => "transcribing",
                };
                HintAccent? accent = (p == FlowPhase.EditListen || p == FlowPhase.EditProcess) ? HintAccent.Edit : null;
                SetHint(baseHint, p == FlowPhase.Listening || p == FlowPhase.EditListen, accent);
                FlashHint("connection back");
            }
        }
    }

    private void HandleEditOutcome(EditOutcome outcome)
    {
        if (_phase != FlowPhase.EditProcess) return;
        if (outcome is EditOutcome.Ok ok)
        {
            _takeGeneration += 1;
            var presentAt = Math.Max(Now, _editProcessStartAt + Constants.EDIT_MIN_DISPLAY_MS);
            if (presentAt <= Now)
                PresentEditDone(ok.Result);
            else
                Later(presentAt - Now, () =>
                {
                    if (_phase == FlowPhase.EditProcess) PresentEditDone(ok.Result);
                });
        }
        else if (outcome is EditOutcome.Fail fail && fail.Failure is EditFailure.Cancelled)
        {
            EditCancelledRestore();
        }
        else
        {
            EditFailed();
        }
    }

    private static int LastIndex<T>(List<T> arr, Func<T, bool> pred)
    {
        for (int i = arr.Count - 1; i >= 0; i--) if (pred(arr[i])) return i;
        return -1;
    }

    // --- the frame step ---
    public FlowFrame Step(double newNow)
    {
        var previous = Now;
        Now = newNow;
        var dtFrames = Math.Min(3.0, Math.Max(0.0, (Now - previous) / 16.0));
        double Ease60(double k) => 1 - Math.Pow(1 - k, dtFrames);

        _dictation.Tick(Now);
        _edit.Tick(Now);

        if (_scheduled.Count > 0)
        {
            var due = _scheduled.Where(s => s.Due <= Now && s.Generation == _seq).ToList();
            _scheduled = _scheduled.Where(s => !(s.Due <= Now)).ToList();
            foreach (var item in due) item.Fire();
        }
        DrainServiceEvents();
        TickMicHealth();
        if (_phase == FlowPhase.Processing)
        {
            if (!_processingStillWorkingShown && Now >= _processingStartAt + Constants.PROCESSING_STILL_WORKING_MS)
            {
                _processingStillWorkingShown = true;
                SetHint("still working…", false);
            }
            if (!_processingLongerThanUsualShown && Now >= _processingStartAt + Constants.PROCESSING_LONGER_THAN_USUAL_MS)
            {
                _processingLongerThanUsualShown = true;
                SetHint("taking longer than usual — hold on", false);
            }
        }
        if (_finalTimeoutDue != null && Now >= _finalTimeoutDue)
        {
            ClearFinalTimeoutBudget();
            if (_phase == FlowPhase.Processing) TakeFinalTimeout();
        }
        if (_spokenEditFlushDue != null && Now >= _spokenEditFlushDue)
            ResolveSpokenEditAfterFlushBudget();
        if (_holdTimerDue != null && Now >= _holdTimerDue)
        {
            _holdTimerDue = null;
            if (_holdTimerSession == _txSession && _phase == FlowPhase.Listening && BoxLive)
                FinalizeAndWait();
        }
        if (_groupLeaveAt != null && Now >= _groupLeaveAt)
        {
            _groupLeaveAt = null;
            _groupHover = false;
            _hovered = _groupHover || _orbNear;
        }
        if (_editPaneCloseAt != null && Now >= _editPaneCloseAt)
        {
            _editPaneCloseAt = null;
            _editOpen = false;
        }
        if (_satSettingsLeaveAt != null && Now >= _satSettingsLeaveAt)
        {
            _satSettingsLeaveAt = null;
            _satSettingsHover = false;
        }
        if (_satExpandLeaveAt != null && Now >= _satExpandLeaveAt)
        {
            _satExpandLeaveAt = null;
            _satExpandHover = false;
        }
        if (_satCancelLeaveAt != null && Now >= _satCancelLeaveAt)
        {
            _satCancelLeaveAt = null;
            _satCancelHover = false;
        }

        var f = new FlowFrame();
        f.Now = Now;
        f.Settings = Settings;

        var hardPill = !_boxExpansionAllowed;
        if (_editResultKeptInOrb && _orbNear)
            _holdUntil = Math.Max(_holdUntil, Now + Constants.EDIT_REVIEW_HOLD);
        var autoWake =
            PhaseHelpers.PhaseIsActive(_phase) || Now < _holdUntil || _expanded || Now < _manualCopySatUntil;
        var wantOpen = hardPill
            ? false
            : Settings.OrbSize == OrbSize.Pill
            ? _expanded
            : KeepCollapsed
            ? _hovered
            : _hovered || autoWake || OrbShowcase;
        var target = wantOpen ? 1.0 : 0.0;
        var ease = Ease60(target > _open ? 0.3 : 0.16 + (1 - _open) * 0.24);
        if (Settings.ReduceMotion) _open = target;
        else _open += (target - _open) * ease;
        if (Math.Abs(target - _open) < 0.0008) _open = target;

        if (_open > 0.86 && _phase == FlowPhase.Rest)
        {
            ToIdle();
            _botHideAt = Now + 2600;
        }
        if (_open < 0.04 && (_phase == FlowPhase.Idle || _phase == FlowPhase.Rest))
        {
            if (_phase != FlowPhase.Rest)
            {
                SetPhase(FlowPhase.Rest);
                ClearTimers();
            }
            _canEdit = false;
            _editOpen = false;
            _hintHidden = false;
            _editResultKeptInOrb = false;
        }

        f.Phase = _phase;
        f.Open = _open;
        f.MarkState = OrbShowcase ? ShowcaseMarkState : PhaseHelpers.PhaseMarkState(_phase);
        f.Inverted = Settings.Orb == OrbStyle.Forest;

        if (Settings.ReduceMotion) _exp = _expanded ? 1 : 0;
        else
        {
            var expTarget = _expanded ? 1.0 : 0.0;
            _exp += (expTarget - _exp) * Ease60(0.24);
            if (Math.Abs(expTarget - _exp) < 0.0005) _exp = expTarget;
        }
        f.Exp = _exp;
        f.Expanded = _expanded;
        f.BoxOnLeft = Settings.Movable && _boxOnLeft;
        f.FlipY = _flipY;
        var halfBox = _boxW / 2 + 8;
        var shiftRight = Math.Max(0, halfBox - _edgeRoomLeft);
        var shiftLeft = Math.Max(0, halfBox - _edgeRoomRight);
        f.FlowShiftX = (shiftRight - shiftLeft) * _exp;
        if (Settings.ReduceMotion)
        {
            _boxW = _boxWTarget;
            _boxGrowDown = _boxGrowDownTarget;
        }
        else
        {
            _boxW += (_boxWTarget - _boxW) * Ease60(0.22);
            _boxGrowDown += (_boxGrowDownTarget - _boxGrowDown) * Ease60(0.22);
            if (Math.Abs(_boxWTarget - _boxW) < 0.5) _boxW = _boxWTarget;
            if (Math.Abs(_boxGrowDownTarget - _boxGrowDown) < 0.5) _boxGrowDown = _boxGrowDownTarget;
        }
        f.BoxW = _boxW;
        f.BoxH = BoxH;
        f.BoxGrowUp = _boxGrowUp;
        f.BoxMaxi = BoxMaxi;
        f.BoxCanMaxi = BoxMaxi || MaxiHasScope;
        f.TxClipped = _fitRequestedH > Constants.BOX_DEFAULT.H + _boxGrowDownTarget + 0.5;
        if (_exp >= 0.997)
        {
            f.TxWrapWidth = _boxW;
            f.TxWrapHeight = BoxH;
            f.TxWrapClips = false;
        }
        else
        {
            f.TxWrapWidth = _boxW;
            f.TxWrapHeight = BoxH * _exp;
            f.TxWrapClips = true;
        }
        f.TxOpacity = Math.Min(1, _exp * 2.2);
        f.TxInteractive = _exp > 0.6;

        // orb geometry
        f.OrbWidth = Constants.REST.W + (WokenSize.W - Constants.REST.W) * _open;
        f.OrbHeight = Constants.REST.H + (WokenSize.H - Constants.REST.H) * _open;
        f.OrbRadius = Constants.REST.R + (WokenSize.R - Constants.REST.R) * _open;
        if (_exp > 0.001)
        {
            f.OrbWidth += (Constants.WAKE_MINI.W - f.OrbWidth) * _exp;
            f.OrbHeight += (Constants.WAKE_MINI.H - f.OrbHeight) * _exp;
            f.OrbRadius += (Constants.WAKE_MINI.R - f.OrbRadius) * _exp;
        }
        var pillPopTarget =
            Settings.OrbSize == OrbSize.Pill && _exp < 0.5 && (PhaseHelpers.PhaseIsRecording(_phase) || _hovered) ? 1.0 : 0.0;
        if (pillPopTarget == 1 && _pillPop < 0.01)
            _botHideAt = Math.Max(_botHideAt, Now + Constants.BOT_HIDE_MS);
        if (Settings.ReduceMotion) _pillPop = pillPopTarget;
        else
        {
            _pillPop += (pillPopTarget - _pillPop) * Ease60(0.18);
            if (Math.Abs(pillPopTarget - _pillPop) < 0.0008) _pillPop = pillPopTarget;
        }
        if (_pillPop > 0.0001)
        {
            f.OrbWidth += (Constants.PILL_TAKE_W - f.OrbWidth) * _pillPop;
            f.OrbHeight += (Constants.PILL_TAKE_H - f.OrbHeight) * _pillPop;
            f.OrbRadius += (Constants.PILL_TAKE_H / 2 - f.OrbRadius) * _pillPop;
        }
        f.PillPop = _pillPop;

        var selectionTarget =
            (_selectionPillText != "" || _selectionPillAppBundleID != null) && !PhaseHelpers.PhaseIsRecording(_phase) ? 1.0 : 0.0;
        if (Settings.ReduceMotion)
            _selectionPillProgress = selectionTarget;
        else
        {
            var k = Ease60(selectionTarget > _selectionPillProgress ? 0.24 : 0.32);
            _selectionPillProgress += (selectionTarget - _selectionPillProgress) * k;
            if (Math.Abs(selectionTarget - _selectionPillProgress) < 0.001) _selectionPillProgress = selectionTarget;
        }
        if (selectionTarget > 0)
        {
            _selectionPillDisplayText = _selectionPillText;
            _selectionPillDisplayAppBundleID = _selectionPillAppBundleID;
        }
        else if (_selectionPillProgress == 0)
        {
            _selectionPillDisplayText = "";
            _selectionPillDisplayAppBundleID = null;
        }
        f.SelectionPillText = _selectionPillDisplayText;
        f.SelectionPillAppBundleID = _selectionPillDisplayAppBundleID;
        f.SelectionPillOpacity = _selectionPillProgress;
        f.SelectionPillWidth = SelectionPillWidthFor(_selectionPillDisplayText, _selectionPillDisplayAppBundleID);

        f.Drop = -6 * (1 - _open) - ((Constants.PILL_TAKE_H - Constants.REST.H) / 2) * _pillPop;
        f.Press = _pressed ? 0.95 : 1;

        // hint pill
        var tipsOff = !Settings.Tooltips;
        var flashNarratedByBox =
            BoxLive && (Tx.Banner == _hintFlashText || Tx.Notice == _hintFlashText);
        var flashing = Now < _hintFlashUntil && !flashNarratedByBox;
        double hShow;
        if (flashing)
        {
            hShow = 1;
            f.Hint = ModelFactory.MakeHint(_hintFlashText, false, HintAccent.Edit);
            f.HintForced = true;
        }
        else
        {
            hShow = _hintHidden || _expanded || tipsOff ? 0 : Math.Max(0, Math.Min(1, (_open - 0.4) / 0.55));
            f.Hint = _hintContent.Clone();
            f.HintForced = false;
        }
        f.HotkeyLabel = HotkeyLabel;
        f.EditComboLabel = EditComboLabel;
        f.HintOpacity = hShow;
        f.HintRise = 5 * (1 - hShow);
        f.HintInteractive = hShow > 0.4 && !tipsOff && !flashing;

        // hint2
        var h2on =
            _canEdit &&
            _phase == FlowPhase.Idle &&
            _open > 0.6 &&
            !_hintHidden &&
            !_expanded &&
            !tipsOff &&
            (Now < _editHideAt || _satEditHover)
                ? 1.0
                : 0.0;
        _h2f += (h2on - _h2f) * Ease60(0.18);
        f.Hint2Opacity = _h2f;
        f.Hint2Rise = 4 * (1 - _h2f);
        f.Hint2Verb = _hint2Verb;

        // bottom satellites
        var pillMode = Settings.OrbSize == OrbSize.Pill;
        var openGeo = Math.Max(0, Math.Min(1, (_open - 0.5) / 0.5));
        var geo = pillMode ? Math.Max(_pillPop, openGeo) : openGeo;
        var recording = PhaseHelpers.PhaseIsRecording(_phase);
        var idleish = (_open > 0.5 || pillMode) && (_phase == FlowPhase.Idle || _phase == FlowPhase.Rest);
        var doneish = _phase == FlowPhase.Done || _phase == FlowPhase.EditDone;
        var low = 0.42;
        var faint = 0.32;
        var setReveal = _satSettingsHover || (!recording && Now < _botHideAt);
        var setBase = recording ? faint : idleish ? low : 0;
        var setTarget = _open > 0.5 || pillMode ? (setReveal ? 1 : setBase) : 0;
        var expReveal = _satExpandHover || (!recording && Now < _botHideAt);
        var expBase = recording ? faint : idleish ? low : doneish && Now < _expFaintUntil ? low : 0;
        var expTarget2 = _open > 0.5 || pillMode ? (expReveal ? 1 : expBase) : 0;
        _setFade += (setTarget - _setFade) * Ease60(0.16);
        _expFade += (expTarget2 - _expFade) * Ease60(0.16);
        var setShow = geo * _setFade;
        var expShow = geo * _expFade;
        f.SatSettingsOpacity = setShow;
        f.SatExpandOpacity = _expanded ? 0 : expShow;
        f.SatSettingsScale = 0.4 + 0.6 * Math.Min(1, setShow / 0.6);
        f.SatExpandScale = 0.4 + 0.6 * Math.Min(1, expShow / 0.6);
        f.SatBottomInteractive = _open > 0.6 || pillMode;

        // edit bubble
        var editingActive = _phase == FlowPhase.EditListen || _phase == FlowPhase.EditProcess || _phase == FlowPhase.EditDone;
        var idleEdit = _phase == FlowPhase.Idle || _phase == FlowPhase.Rest || _phase == FlowPhase.Done;
        var hostIconTake = recording && !editingActive && _takeHostAppBundleID != null;
        var pillShown = pillMode && _pillPop > 0.3;
        var refineShown =
            ((_canEdit || editingActive || idleEdit) && (_open > 0.5 || pillShown) && !recording) ||
            (editingActive && (_open > 0.5 || pillShown));
        var displayShown = refineShown || (hostIconTake && (_open > 0.5 || pillShown));
        var refineFull = editingActive || _satEditHover || Now < _editHideAt || (!recording && Now < _botHideAt);
        var rtarget = displayShown ? (refineFull || hostIconTake ? 1.0 : 0.15) : 0.0;
        _ref += (rtarget - _ref) * Ease60(0.12);
        f.SatEditShown = refineShown;
        f.SatEditAppBundleID = recording ? _takeHostAppBundleID : null;
        if (_phase == FlowPhase.EditListen || _phase == FlowPhase.EditProcess)
        {
            var baseStr =
                _externalEditBaseText ??
                _pendingSpokenEditBase ??
                (_phase == FlowPhase.EditProcess ? _editBaseText : Tx.PlainText);
            f.TxWordCount = WordCount(baseStr);
        }
        else
        {
            f.TxWordCount = 0;
        }
        var b = 0.5 + 0.5 * Math.Sin((Now / 1000) * ((2 * Math.PI) / Constants.BREATH_PERIOD_S));
        f.Breath = b;
        var orbShakeRemaining = _orbShakeUntil - Now;
        f.OrbShakeX =
            orbShakeRemaining > 0 ? Math.Sin(orbShakeRemaining / 22) * 3.0 * Math.Min(1, orbShakeRemaining / 450) : 0;
        if (displayShown)
        {
            var shakeRemaining = _shakeUntil - Now;
            var shk = shakeRemaining > 0 ? Math.Sin(shakeRemaining / 24) * 2.6 * Math.Min(1, shakeRemaining / 420) : 0;
            f.SatEditOpacity = pillMode ? _ref * geo : _ref;
            f.SatEditScale = 0.96 + 0.05 * b;
            f.SatEditShakeX = shk;
            if (_phase == FlowPhase.EditListen)
            {
                f.SatEditTint = new SatTint
                {
                    Type = SatTintType.Green,
                    R = Trunc(40 + 70 * b),
                    G = Trunc(78 + 82 * b),
                    B = Trunc(26 + 34 * b),
                    GlowRadius = 3 + 6 * b,
                    GlowAlpha = 0.2 + 0.22 * b,
                };
            }
            else if (_phase == FlowPhase.EditProcess)
            {
                f.SatEditTint = new SatTint
                {
                    Type = SatTintType.Blue,
                    R = Trunc(80 + 40 * b),
                    G = Trunc(100 + 40 * b),
                    B = Trunc(200 + 55 * b),
                    GlowRadius = 3 + 6 * b,
                    GlowAlpha = 0.2 + 0.22 * b,
                };
            }
            else
            {
                f.SatEditTint = SatTint.None();
            }
        }
        else
        {
            f.SatEditOpacity = 0;
            f.SatEditScale = 0.4;
            f.SatEditTint = SatTint.None();
        }

        // edit pane
        _eo += (((_editOpen && _canEdit) ? 1 : 0) - _eo) * Ease60(0.28);
        f.PaneOpacity = _eo;
        f.PaneScale = 0.92 + 0.08 * _eo;
        f.PaneShiftX = 8 * (1 - _eo);

        // cancel bubble
        var cancellable = recording;
        var manualCopyHot = Now < _manualCopySatUntil;
        f.SatManualCopyHot = manualCopyHot;
        f.SatManualCopy = Now < _manualCopySatHoverUntil;
        var copyKeyLive = manualCopyHot || (f.SatManualCopy && (_hovered || _satCancelHover));
        var cxTarget =
            (cancellable || copyKeyLive) && (_open > 0.5 || pillMode)
                ? (_satCancelHover || Now < _cancelHideAt || copyKeyLive ? 1.0 : 0.18)
                : 0.0;
        _cxf += (cxTarget - _cxf) * Ease60(0.16);
        f.SatCancelOpacity = _cxf;
        f.SatCancelScale = 0.4 + 0.6 * Math.Min(1, _cxf / 0.6);
        f.SatCancelInteractive = (cancellable || f.SatManualCopy) && (_open > 0.6 || pillMode);

        if (OrbShowcase)
        {
            f.SatEditShown = false;
            f.SatEditOpacity = 0;
            f.SatSettingsOpacity = 0;
            f.SatExpandOpacity = 0;
            f.SatCancelOpacity = 0;
            f.SatBottomInteractive = false;
            f.SatCancelInteractive = false;
            f.HintOpacity = 0;
            f.HintInteractive = false;
        }

        var lockScale = 0.96 + 0.05 * b;
        if (LockEdit)
        {
            f.SatEditLocked = true;
            f.SatEditShown = true;
            f.SatEditOpacity = 0.5;
            f.SatEditScale = lockScale;
            f.SatEditTint = SatTint.None();
            f.SatEditShakeX = 0;
        }
        if (LockOpenKivi)
        {
            f.SatSettingsLocked = true;
            f.SatBottomInteractive = true;
            f.SatSettingsOpacity = 0.5;
            f.SatSettingsScale = lockScale;
        }

        // fill / glass / glow
        var restA = Settings.Orb == OrbStyle.Forest ? Constants.REST_ALPHA_FOREST : Constants.REST_ALPHA_MIST;
        f.FillAlpha = restA + (1 - restA) * _open;
        f.BackdropBlur = 10 * (1 - _open);
        var isDark = Settings.Page == PageStyle.Dark;
        var glowA = isDark ? 0.4 : 0.24;
        var glowBlur = isDark ? 60.0 : 40.0;
        var glowSpread = isDark ? 9.0 : 4.0;
        var dropBase = isDark ? 0.42 : 0.28;
        var dropAdd = isDark ? 0.16 : 0.12;
        var glowIdle = isDark ? Constants.REST_GLOW : Constants.GLOW_IDLE_LIGHT;
        var glowTarget = f.MarkState == KiwiMarkState.Idle ? glowIdle : Constants.MarkStateColor(f.MarkState, isDark);
        var gk = Settings.ReduceMotion ? 1.0 : Ease60(0.09);
        _glowColR += (glowTarget[0] - _glowColR) * gk;
        _glowColG += (glowTarget[1] - _glowColG) * gk;
        _glowColB += (glowTarget[2] - _glowColB) * gk;
        f.GlowColor = new RGB(JsRound(_glowColR), JsRound(_glowColG), JsRound(_glowColB));
        var bq = JsRound(b * 12) / 12;
        var glowBreathA = 0.91 + 0.09 * bq;
        var glowBreathS = 0.95 + 0.1 * bq;
        var glowCore = new ShadowSpec { Blur = 0, Spread = 0, YOffset = 0, Alpha = 0 };
        glowCore.Blur = glowBlur * 0.55 * _open;
        glowCore.Spread = glowSpread * 0.5 * _open * glowBreathS;
        glowCore.Alpha = glowA * _open * glowBreathA;
        f.GlowCore = glowCore;
        var glowHalo = new ShadowSpec { Blur = 0, Spread = 0, YOffset = 0, Alpha = 0 };
        glowHalo.Blur = glowBlur * 1.15 * _open;
        glowHalo.Spread = glowSpread * _open * glowBreathS;
        glowHalo.Alpha = glowA * 0.7 * _open * glowBreathA;
        f.GlowHalo = glowHalo;
        var dropShadow = new ShadowSpec { Blur = 0, Spread = 0, YOffset = 0, Alpha = 0 };
        dropShadow.YOffset = 6 + 8 * _open;
        dropShadow.Blur = 18 + 12 * _open;
        dropShadow.Spread = -4;
        dropShadow.Alpha = dropBase + dropAdd * _open;
        f.DropShadow = dropShadow;

        // eyes
        var eyeScale = 0.9 + (1.1 - 0.9) * b;
        var eyeVis = Math.Max(0, 1 - _open * 2.4);
        f.EyeScale = eyeScale;
        f.EyeOpacity = (0.7 + 0.3 * b) * eyeVis;
        var eyeOpenTarget = f.MarkState == KiwiMarkState.Idle ? 0.0 : 1.0;
        if (Settings.ReduceMotion) _eyeOpenE = eyeOpenTarget;
        else _eyeOpenE += (eyeOpenTarget - _eyeOpenE) * Ease60(0.18);
        if (Math.Abs(eyeOpenTarget - _eyeOpenE) < 0.0008) _eyeOpenE = eyeOpenTarget;
        f.EyeOpen = _eyeOpenE;

        // mark + sphere
        f.MarkOpacity = Math.Max(0, (_open - 0.12) / 0.88);
        f.SphereOpacity = Math.Max(0, (_open - 0.2) / 0.8);

        // pointer light
        if (!_hovered && !OrbShowcase)
        {
            _lightTX = Constants.REST_LIGHT.x;
            _lightTY = Constants.REST_LIGHT.y;
        }
        _lightX += (_lightTX - _lightX) * Ease60(0.16);
        _lightY += (_lightTY - _lightY) * Ease60(0.16);
        f.LightX = _lightX;
        f.LightY = _lightY;

        // transcript content + diff morph progress
        Tx.TickMorph(Now);
        f.TxStage = Tx.Stage;
        if (Tx.Morph != null)
        {
            f.TxLines = Tx.Morph.Lines.Select(toks => TxLine.Make(TxLineRole.Tokens, tokens: toks.Select(t => t.Clone()).ToList())).ToList();
        }
        else
        {
            f.TxLines = Tx.Lines.Select(l => new TxLine
            {
                Role = l.Role,
                Text = l.Text,
                Tokens = l.Tokens?.Select(t => t.Clone()).ToList(),
                FadeInStart = l.FadeInStart,
            }).ToList();
        }
        f.TxAwaitingSpeech = Tx.AwaitingSpeech;
        f.TxWaitingPhase = _micWarnStage;
        f.TxNotice = Tx.Notice;
        f.TxBanner = Tx.Banner;
        f.TxEditable = Tx.EditableContent && !PhaseHelpers.PhaseIsRecording(_phase);
        f.TxEditorSeed = Tx.EditorSeed;
        f.HoveredTarget = _lastHoverTarget;
        if (Tx.DotsStartedAt != null)
        {
            var n = 1 + ((int)Math.Floor((Now - Tx.DotsStartedAt.Value) / Constants.DOTS_MS) % 3);
            f.TxDots = new string('.', n);
        }
        if (Tx.Morph != null)
        {
            var t = Now - Tx.Morph.StartedAt;
            var ra = Math.Max(0, Math.Min(1, t / 150));
            f.DiffProgress = new DiffProgress
            {
                Landing = ra,
                LandingEased = EaseIO(ra),
                Collapse = EaseIO(Math.Max(0, Math.Min(1, (t - 150 - 100) / 250))),
            };
        }
        f.ScrollCommand = _pendingScroll != null ? new ScrollCommand { Id = _pendingScroll.Value.id, Target = _pendingScroll.Value.target } : null;

        // side band
        var histOn = (_expanded || _boxHostCount > 0) && Tx.Stage == TxStage.Idle && !Tx.Browsing;
        f.BandHistOn = histOn;
        f.BandHistDim = histOn && Tx.History.Count == 0;
        f.BandHistShake = Now < _histShakeUntil;
        f.BandNoSteps =
            (Tx.Stage == TxStage.Idle || Tx.Stage == TxStage.Typed || Tx.Stage == TxStage.Pasted) && !Tx.Browsing;
        var canScroll = Tx.Browsing
            ? Tx.History.Count > 1
            : Tx.BaseFrames != null || (Tx.Stage == TxStage.Done && !Tx.Browsing);
        f.BandStepsDim = !canScroll;
        if (Tx.Browsing)
        {
            f.BandCanPrev = Tx.HistoryAt > 0;
            f.BandCanNext = Tx.HistoryAt < Tx.History.Count - 1;
        }
        else if (Tx.BaseFrames != null)
        {
            f.BandCanPrev = Tx.Frame > 0;
            f.BandCanNext = Tx.Frame < Tx.BaseFrames.Count - 1;
        }
        else if (Tx.Stage == TxStage.Done)
        {
            f.BandCanPrev = Tx.Frame > 0;
            f.BandCanNext = Tx.Frame < Tx.MaxFrame;
        }
        else
        {
            f.BandCanPrev = false;
            f.BandCanNext = false;
        }
        if (Tx.Browsing)
        {
            f.TxPagerIndex = Tx.HistoryAt;
            f.TxPagerCount = Tx.History.Count;
        }
        else if (Tx.BaseFrames != null)
        {
            f.TxPagerIndex = Tx.Frame;
            f.TxPagerCount = Tx.BaseFrames.Count;
        }
        else if (Tx.Stage == TxStage.Done)
        {
            f.TxPagerIndex = Tx.Frame;
            f.TxPagerCount = Tx.MaxFrame + 1;
        }
        else
        {
            f.TxPagerIndex = 0;
            f.TxPagerCount = 0;
        }
        f.TakeHostAppBundleID = _takeHostAppBundleID;
        f.RetryOffered = RetryableFailurePresented;
        f.TakeRating = _takeRating;
        f.TakeRatable = _takeRatable;
        f.HasEditChain = Tx.BaseFrames != null;
        var editingNow = _phase == FlowPhase.EditListen || _phase == FlowPhase.EditProcess || _phase == FlowPhase.EditDone;
        var ownBoxHint = _lastEditHint == null || _lastEditHint == "Editing Kivi text";
        if (editingNow || (Tx.BaseFrames != null && Tx.Stage == TxStage.Done))
        {
            var kind =
                _activeContextCardKind ??
                ContextKind(_lastEditHint) ??
                (ownBoxHint ? _activeOwnBoxContextKind : "your text");
            if (kind != null)
            {
                var baseStr = _externalEditBaseText ?? _pendingSpokenEditBase ?? _editBaseText;
                var trimmed = TrimWs(baseStr);
                if (trimmed != "")
                {
                    f.EditContextKind = kind;
                    var previewMax = BoxMaxi ? 600 : 140;
                    var runes = trimmed.EnumerateRunes().ToList();
                    f.EditContextPreview =
                        runes.Count > previewMax
                            ? string.Concat(runes.Take(previewMax).Select(r => r.ToString())) + "…"
                            : trimmed;
                }
            }
        }
        f.CopyFlash = Now < _copyFlashUntil;
        f.CopyHint = Now < _copyHintUntil;
        if (Now < _boxShakeUntil)
        {
            var remaining = _boxShakeUntil - Now;
            f.BoxShakeX = Math.Sin(remaining / 22) * 2.5 * Math.Min(1, remaining / 300);
        }
        else
        {
            f.BoxShakeX = 0;
        }

        f.ToastText = _toastText;
        f.ToastVisible = Now < _toastUntil;

        return f;
    }

    private void TickMicHealth()
    {
        if (_phase != FlowPhase.Listening || !Tx.AwaitingSpeech)
        {
            _micWarnStage = 0;
            return;
        }
        var elapsed = Now - _listenStartAt;
        var p = elapsed >= 20000 ? 3 : elapsed >= 10000 ? 2 : elapsed >= 5000 ? 1 : 0;
        if (p != _micWarnStage)
        {
            if (p == 2) _boxShakeUntil = Now + 600;
            else if (p == 3) _boxShakeUntil = Now + 1300;
            _micWarnStage = p;
        }
    }

    private double SelectionPillWidthFor(string text, string? appBundleID)
    {
        if (text == "") return appBundleID == null ? Constants.REST.W : 34;
        return 70;
    }

    private static readonly Regex WhitespaceSplit = new(@"\s+", RegexOptions.Compiled);
    public static int WordCount(string text) => WhitespaceSplit.Split(text).Count(s => s.Length > 0);

    private static double EaseIO(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;

    // test hooks
    public double DebugOpen => _open;
    public bool DebugCanEdit => _canEdit;
    public bool DebugExpanded => _expanded;
    public int DebugTakeGeneration => _takeGeneration;
}
