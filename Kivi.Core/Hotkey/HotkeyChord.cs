using System;
using System.Collections.Generic;
using System.Linq;

namespace Kivi.Core.Hotkey;

/// <summary>
/// A push-to-talk hotkey chord — a set of physical keys (by Windows virtual-key code) that must all
/// be held for the hotkey to be "engaged". Pure and OS-free so it can be unit-tested and persisted;
/// the platform hook (<c>Kivi.Platform.Hotkey</c>) feeds it the live key stream and it decides the
/// Down/Up gesture edges.
///
/// <para>Why a chord and not a single VK: the product needs modifier-only combos the current single-VK
/// hook and <c>RegisterHotKey</c> can't express — Ctrl+Win, both Ctrls, both Alts (verified against
/// the Win32 docs: RegisterHotKey takes one <c>vk</c> + modifier flags and can't distinguish
/// left/right, so a low-level hook matching an explicit VK set is the right mechanism).</para>
///
/// <para>Semantics: the chord is <b>engaged</b> when every VK in <see cref="Keys"/> is physically down.
/// The <b>trigger</b> is whichever chord key was pressed <i>last</i> to complete the set — the Down
/// edge fires the instant the set becomes complete, and the Up edge fires the instant it stops being
/// complete (any chord key released). This matches how a PTT chord should feel: hold the combo →
/// talk, release any part → stop. For a single-key chord (e.g. Right-Ctrl) this reduces exactly to
/// the old behavior.</para>
/// </summary>
public sealed class HotkeyChord : IEquatable<HotkeyChord>
{
    /// <summary>The virtual-key codes that must ALL be held. Order-independent; never empty.</summary>
    public IReadOnlyList<int> Keys { get; }

    public HotkeyChord(IEnumerable<int> keys)
    {
        // Dedupe + sort for a canonical, order-independent identity.
        var set = new SortedSet<int>(keys);
        if (set.Count == 0)
            throw new ArgumentException("A hotkey chord must contain at least one key.", nameof(keys));
        Keys = set.ToArray();
    }

    public HotkeyChord(params int[] keys) : this((IEnumerable<int>)keys) { }

    /// <summary>True once every chord key is present in the set of currently-down VKs.</summary>
    public bool IsEngaged(IReadOnlySet<int> downVks)
    {
        foreach (var k in Keys)
            if (!downVks.Contains(k)) return false;
        return true;
    }

    /// <summary>Whether <paramref name="vk"/> participates in this chord (so the hook knows which keys
    /// are relevant to track / potentially swallow).</summary>
    public bool Contains(int vk) => Keys.Contains(vk);

    // ---- serialization (stable storage form for the flow-store JSON) ----
    // Canonical form: hyphen-joined uppercase hex VKs, e.g. "A3" (Right-Ctrl), "A2-A3" (both Ctrls),
    // "11-20" (Ctrl+Space). Hex keeps it compact and unambiguous vs. decimal.

    public string ToStorageString() => string.Join("-", Keys.Select(k => k.ToString("X2")));

    public static bool TryParse(string? s, out HotkeyChord? chord)
    {
        chord = null;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        var vks = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (!int.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out var vk)) return false;
            if (vk is <= 0 or > 0xFF) return false; // valid VK range
            vks.Add(vk);
        }
        chord = new HotkeyChord(vks);
        return true;
    }

    public bool Equals(HotkeyChord? other)
    {
        if (other is null || other.Keys.Count != Keys.Count) return false;
        for (int i = 0; i < Keys.Count; i++)
            if (Keys[i] != other.Keys[i]) return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as HotkeyChord);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var k in Keys) h.Add(k);
        return h.ToHashCode();
    }

    public override string ToString() => ToStorageString();
}
