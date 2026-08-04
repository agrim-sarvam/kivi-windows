using System.Collections.Generic;

namespace Kivi.Core.Hotkey;

/// <summary>The transition a key event produced in <see cref="ChordMatcher"/>.</summary>
public enum ChordEdge
{
    /// <summary>No change in engaged-state.</summary>
    None,
    /// <summary>The chord just became fully engaged (all keys down) — emit a Down gesture edge.</summary>
    Engaged,
    /// <summary>The chord just stopped being engaged (a chord key released) — emit an Up gesture edge.</summary>
    Released,
}

/// <summary>
/// Pure, single-threaded state machine that tracks which virtual keys are physically down and, for a
/// given <see cref="HotkeyChord"/>, reports the exact moment the chord becomes engaged (all keys down)
/// or disengaged (any chord key up). It is intentionally OS-free: the platform hook calls
/// <see cref="KeyDown"/>/<see cref="KeyUp"/> with raw VKs from WH_KEYBOARD_LL and forwards the
/// returned <see cref="ChordEdge"/> as a gesture edge.
///
/// <para>Auto-repeat safety: <see cref="KeyDown"/> for a key already marked down is idempotent — it
/// never re-fires <see cref="ChordEdge.Engaged"/> (mirrors the old hook's <c>_triggerDown</c> debounce,
/// now generalized to the whole chord).</para>
///
/// <para>Rebinding: <see cref="SetChord"/> swaps the chord live. The engaged-state is recomputed
/// against the current down-keys, but a rebind never <i>emits</i> an edge on its own — a chord that
/// happens to already be held when rebound is treated as "engaged, no fresh Down" until the next
/// clean release+press, so a rebind can't spuriously start a take.</para>
/// </summary>
public sealed class ChordMatcher
{
    private readonly HashSet<int> _down = new();
    private HotkeyChord _chord;
    private bool _engaged;

    public ChordMatcher(HotkeyChord chord)
    {
        _chord = chord;
        _engaged = false;
    }

    /// <summary>The chord currently being matched.</summary>
    public HotkeyChord Chord => _chord;

    /// <summary>Whether the chord is fully held right now.</summary>
    public bool IsEngaged => _engaged;

    /// <summary>Whether <paramref name="vk"/> is part of the active chord (hook uses this to decide
    /// whether to track / swallow the key).</summary>
    public bool IsChordKey(int vk) => _chord.Contains(vk);

    /// <summary>Record a physical key-down. Returns <see cref="ChordEdge.Engaged"/> exactly once, on the
    /// press that completes the chord.</summary>
    public ChordEdge KeyDown(int vk)
    {
        _down.Add(vk); // idempotent for auto-repeat
        if (_engaged) return ChordEdge.None; // already engaged — auto-repeat or an extra key
        if (_chord.IsEngaged(_down))
        {
            _engaged = true;
            return ChordEdge.Engaged;
        }
        return ChordEdge.None;
    }

    /// <summary>Record a physical key-up. Returns <see cref="ChordEdge.Released"/> exactly once, on the
    /// release that breaks a previously-engaged chord.</summary>
    public ChordEdge KeyUp(int vk)
    {
        _down.Remove(vk);
        if (_engaged && !_chord.IsEngaged(_down))
        {
            _engaged = false;
            return ChordEdge.Released;
        }
        return ChordEdge.None;
    }

    /// <summary>
    /// Rebind to a new chord without emitting an edge. If the new chord happens to be fully held right
    /// now, it is adopted as already-engaged (so we don't fire a spurious Down); the next Up that breaks
    /// it will still emit <see cref="ChordEdge.Released"/> cleanly. If it isn't held, we start disengaged.
    /// </summary>
    public void SetChord(HotkeyChord chord)
    {
        _chord = chord;
        _engaged = _chord.IsEngaged(_down);
    }

    /// <summary>Clear all tracked key state (e.g. on focus loss / session reset). Never emits an edge.</summary>
    public void Reset()
    {
        _down.Clear();
        _engaged = false;
    }
}
