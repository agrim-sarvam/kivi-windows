using System.Collections.Generic;
using Kivi.Core.Hotkey;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>
/// Tests for the pure hotkey chord + matcher — the logic that turns a live physical key stream into
/// Down/Up gesture edges for single keys AND modifier-only combos (Ctrl+Win, both Ctrls, both Alts,
/// Ctrl+Space). VK codes here are the real Win32 ones the docs confirm:
///   Ctrl 0x11, Space 0x20, LWin 0x5B, RWin 0x5C,
///   LCtrl 0xA2, RCtrl 0xA3, LAlt 0xA4, RAlt 0xA5.
/// </summary>
public class HotkeyChordTests
{
    private const int VK_CONTROL = 0x11, VK_SPACE = 0x20, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;

    // ---- HotkeyChord identity & serialization ----

    [Fact]
    public void Chord_IsOrderIndependent_AndDeduped()
    {
        var a = new HotkeyChord(VK_LCONTROL, VK_RCONTROL);
        var b = new HotkeyChord(VK_RCONTROL, VK_LCONTROL, VK_RCONTROL);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Chord_Empty_Throws()
    {
        Assert.Throws<System.ArgumentException>(() => new HotkeyChord(new int[0]));
    }

    [Theory]
    [InlineData(new[] { VK_RCONTROL }, "A3")]
    [InlineData(new[] { VK_LCONTROL, VK_RCONTROL }, "A2-A3")]
    [InlineData(new[] { VK_CONTROL, VK_SPACE }, "11-20")]
    [InlineData(new[] { VK_CONTROL, VK_LWIN }, "11-5B")]
    public void Chord_RoundTripsThroughStorageString(int[] keys, string expected)
    {
        var chord = new HotkeyChord(keys);
        Assert.Equal(expected, chord.ToStorageString());
        Assert.True(HotkeyChord.TryParse(expected, out var parsed));
        Assert.Equal(chord, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ZZ")]      // not hex
    [InlineData("1FF")]     // out of VK range
    [InlineData("-")]       // no parts
    public void Chord_TryParse_RejectsGarbage(string? s)
    {
        Assert.False(HotkeyChord.TryParse(s, out var parsed));
        Assert.Null(parsed);
    }

    // ---- Single-key chord (the old Right-Ctrl behavior must be preserved exactly) ----

    [Fact]
    public void SingleKey_DownThenUp_EmitsEngagedThenReleased()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_RCONTROL));
        Assert.Equal(ChordEdge.Engaged, m.KeyDown(VK_RCONTROL));
        Assert.True(m.IsEngaged);
        Assert.Equal(ChordEdge.Released, m.KeyUp(VK_RCONTROL));
        Assert.False(m.IsEngaged);
    }

    [Fact]
    public void SingleKey_AutoRepeatDown_DoesNotReEmit()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_RCONTROL));
        Assert.Equal(ChordEdge.Engaged, m.KeyDown(VK_RCONTROL));
        Assert.Equal(ChordEdge.None, m.KeyDown(VK_RCONTROL)); // auto-repeat
        Assert.Equal(ChordEdge.None, m.KeyDown(VK_RCONTROL));
        Assert.Equal(ChordEdge.Released, m.KeyUp(VK_RCONTROL));
    }

    [Fact]
    public void UnrelatedKeys_AreIgnored()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_RCONTROL));
        Assert.Equal(ChordEdge.None, m.KeyDown(VK_SPACE));
        Assert.Equal(ChordEdge.None, m.KeyUp(VK_SPACE));
        Assert.False(m.IsEngaged);
    }

    // ---- Two-modifier chord: Ctrl+Win ----

    [Fact]
    public void CtrlWin_EngagesOnlyWhenBothHeld_InEitherOrder()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_CONTROL, VK_LWIN));
        Assert.Equal(ChordEdge.None, m.KeyDown(VK_CONTROL));    // partial
        Assert.False(m.IsEngaged);
        Assert.Equal(ChordEdge.Engaged, m.KeyDown(VK_LWIN));    // completes
        Assert.True(m.IsEngaged);
        Assert.Equal(ChordEdge.Released, m.KeyUp(VK_CONTROL));  // any release breaks it
        Assert.False(m.IsEngaged);
        // trailing release of the other key does not re-emit
        Assert.Equal(ChordEdge.None, m.KeyUp(VK_LWIN));
    }

    // ---- Both Ctrls (left/right distinction) ----

    [Fact]
    public void BothCtrls_RequireLeftAndRight_NotJustOne()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_LCONTROL, VK_RCONTROL));
        Assert.Equal(ChordEdge.None, m.KeyDown(VK_LCONTROL));
        Assert.Equal(ChordEdge.Engaged, m.KeyDown(VK_RCONTROL));
        Assert.True(m.IsEngaged);
        Assert.Equal(ChordEdge.Released, m.KeyUp(VK_RCONTROL));
    }

    // ---- Both Alts ----

    [Fact]
    public void BothAlts_Engage_AndReleaseCleanly()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_LMENU, VK_RMENU));
        Assert.Equal(ChordEdge.None, m.KeyDown(VK_LMENU));
        Assert.Equal(ChordEdge.Engaged, m.KeyDown(VK_RMENU));
        Assert.Equal(ChordEdge.Released, m.KeyUp(VK_LMENU));
        Assert.Equal(ChordEdge.None, m.KeyUp(VK_RMENU));
    }

    // ---- Ctrl+Space (modifier + main key) ----

    [Fact]
    public void CtrlSpace_Engages_WhenSpacePressedWhileCtrlHeld()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_CONTROL, VK_SPACE));
        Assert.Equal(ChordEdge.None, m.KeyDown(VK_CONTROL));
        Assert.Equal(ChordEdge.Engaged, m.KeyDown(VK_SPACE));
        // releasing space alone breaks the chord (Ctrl still held)
        Assert.Equal(ChordEdge.Released, m.KeyUp(VK_SPACE));
        Assert.False(m.IsEngaged);
        // pressing space again re-engages (Ctrl still down)
        Assert.Equal(ChordEdge.Engaged, m.KeyDown(VK_SPACE));
    }

    // ---- Rebind semantics ----

    [Fact]
    public void Rebind_WhileNotHeld_StartsDisengaged()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_RCONTROL));
        m.SetChord(new HotkeyChord(VK_CONTROL, VK_LWIN));
        Assert.False(m.IsEngaged);
        Assert.Equal(ChordEdge.None, m.KeyDown(VK_CONTROL));
        Assert.Equal(ChordEdge.Engaged, m.KeyDown(VK_LWIN));
    }

    [Fact]
    public void Rebind_ToAnAlreadyHeldChord_DoesNotFireSpuriousDown()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_SPACE));
        // user is holding Ctrl for unrelated reasons
        m.KeyDown(VK_CONTROL);
        // rebind to a chord that Ctrl alone satisfies
        m.SetChord(new HotkeyChord(VK_CONTROL));
        Assert.True(m.IsEngaged);      // adopted as engaged...
        // ...but no Down was emitted (SetChord returns void, and no KeyDown fired it)
        // releasing still gives a clean Released
        Assert.Equal(ChordEdge.Released, m.KeyUp(VK_CONTROL));
        Assert.False(m.IsEngaged);
    }

    [Fact]
    public void Reset_ClearsHeldKeys_NoEdge()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_CONTROL, VK_SPACE));
        m.KeyDown(VK_CONTROL);
        m.KeyDown(VK_SPACE);
        Assert.True(m.IsEngaged);
        m.Reset();
        Assert.False(m.IsEngaged);
        // after reset, a fresh press sequence engages again
        m.KeyDown(VK_CONTROL);
        Assert.Equal(ChordEdge.Engaged, m.KeyDown(VK_SPACE));
    }

    [Fact]
    public void IsChordKey_ReflectsActiveChord()
    {
        var m = new ChordMatcher(new HotkeyChord(VK_CONTROL, VK_SPACE));
        Assert.True(m.IsChordKey(VK_CONTROL));
        Assert.True(m.IsChordKey(VK_SPACE));
        Assert.False(m.IsChordKey(VK_LWIN));
    }
}
