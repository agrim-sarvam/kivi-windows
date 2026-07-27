// Cue stream, ported from packages/orb-core/src/cue.ts (Orb/Core/CueEvent.swift +
// Orb/Core/CueBus.swift). The engine emits a CueEvent on every phase change (and the
// transient non-phase cues). The bus is a lightweight fan-out.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kivi.Core.Orb;

public enum CueEventKind
{
    Idle,
    Listening,
    Processing,
    Done,
    EditListen,
    EditProcess,
    EditDone,
    Acting,
    Confirming,
    SearchThinking,
    ResultReady,
    Error,
    Waiting,
    NoTarget,
    RecoveredRaw,
    LimitHit,
    LanguageMismatch,
    Cancelled,
    Saved,
    Copied,
    DiscoveredItem,
}

public readonly record struct CueEvent(CueEventKind Kind, FlowPhase From, FlowPhase To);

/// Plain fan-out bus (publish records `Last` and notifies subscribers).
public sealed class CueBus
{
    public CueEvent? Last { get; private set; }
    private readonly List<Action<CueEvent>> _subscribers = new();

    public Action Subscribe(Action<CueEvent> fn)
    {
        _subscribers.Add(fn);
        return () => _subscribers.Remove(fn);
    }

    public void Publish(CueEvent ev)
    {
        Last = ev;
        foreach (var s in _subscribers.ToList()) s(ev);
    }
}

/// Frame-counted transient mark override (CueBus.swift:146). Pure (no clock):
/// washes the orb's mark for a window when a cue has no phase of its own.
public sealed class MarkOverride
{
    private KiwiMarkState? _state = null;
    private int _frames = 0;

    public void Set(KiwiMarkState s, int frames)
    {
        _state = s;
        _frames = frames;
    }

    public KiwiMarkState Tick(KiwiMarkState baseState)
    {
        if (baseState != KiwiMarkState.Idle)
        {
            _state = null;
            _frames = 0;
            return baseState;
        }
        if (_state is null || _frames <= 0)
        {
            _state = null;
            return baseState;
        }
        _frames -= 1;
        return _state.Value;
    }
}
