using System.Collections.Generic;
using System.Linq;
using Kivi.Core.Hotkey;

namespace Kivi.App.Services;

/// <summary>
/// Windows virtual-key codes used by the hotkey UI. Values are the canonical Win32 codes (verified
/// against the Microsoft "Virtual-Key Codes" reference): the left/right-distinguishing modifier codes
/// (0xA2–0xA5, 0x5B/0x5C) are what let us offer "both Ctrls / both Alts / left vs right".
/// </summary>
public static class Vk
{
    public const int Control = 0x11, Menu = 0x12, Shift = 0x10; // side-agnostic (rarely used for chords)
    public const int Space = 0x20, Escape = 0x1B, Tab = 0x09, Delete = 0x2E, F12 = 0x7B;
    public const int LWin = 0x5B, RWin = 0x5C;
    public const int LShift = 0xA0, RShift = 0xA1;
    public const int LControl = 0xA2, RControl = 0xA3;
    public const int LMenu = 0xA4, RMenu = 0xA5; // Alt = "Menu" in Win32
    public const int R = 0x52, L = 0x4C;         // letters (for reserved-combo detection: Win+R, Win+L)
}

/// <summary>A vetted, ready-to-pick push-to-talk chord shown as a card on the onboarding page.</summary>
public sealed record HotkeyPreset(string Title, string Subtitle, HotkeyChord Chord);

/// <summary>How risky a chord is to bind, for the live warning shown while capturing.</summary>
public enum HotkeyRisk { Ok, Warn, Blocked }

public sealed record HotkeyVerdict(HotkeyRisk Risk, string? Message);

/// <summary>
/// The single source of truth for the hotkey UI: the curated preset chords, keycap rendering, and the
/// reserved-combo policy. Kept in one place so the onboarding cards and the free-capture field agree.
///
/// <para>Reserved-combo policy is grounded in the Windows docs: <c>Ctrl+Alt+Del</c> is a Secure
/// Attention Sequence Windows drops from injected/hooked input (can never work → blocked) and
/// <c>Win+L</c> locks the workstation (unrecoverable → blocked). Others (<c>Win+R</c>, <c>Alt+Tab</c>,
/// <c>Alt+F4</c>, <c>Ctrl+Esc</c>, <c>F12</c>, and any Win-key combo — "shortcuts that involve the
/// WINDOWS key are reserved for use by the OS") are allowed but warned.</para>
/// </summary>
public static class HotkeyCatalog
{
    public static IReadOnlyList<HotkeyPreset> Presets { get; } = new[]
    {
        new HotkeyPreset("Right Ctrl",  "the default — out of the way, one hand", new HotkeyChord(Vk.RControl)),
        new HotkeyPreset("Left Ctrl",   "single key, left hand", new HotkeyChord(Vk.LControl)),
        new HotkeyPreset("Right Alt",   "single key, right hand", new HotkeyChord(Vk.RMenu)),
        new HotkeyPreset("Left Alt",    "single key, left hand", new HotkeyChord(Vk.LMenu)),
        new HotkeyPreset("Ctrl + Space", "quick and reachable", new HotkeyChord(Vk.Control, Vk.Space)),
        new HotkeyPreset("Ctrl + Win",  "distinctive, rarely used elsewhere", new HotkeyChord(Vk.Control, Vk.LWin)),
    };

    /// <summary>The app default when the user hasn't chosen one (matches the hook's built-in default).</summary>
    public static HotkeyChord Default => new(Vk.RControl);

    /// <summary>Human keycap tokens for a chord, in a stable display order (modifiers first).</summary>
    public static IReadOnlyList<string> Keycaps(HotkeyChord chord)
    {
        // Preserve a friendly order regardless of the chord's numeric sort.
        var order = new[] { Vk.LControl, Vk.RControl, Vk.Control, Vk.LMenu, Vk.RMenu, Vk.Menu,
                            Vk.LShift, Vk.RShift, Vk.Shift, Vk.LWin, Vk.RWin };
        var caps = new List<string>();
        foreach (var vk in order)
            if (chord.Contains(vk)) caps.Add(KeyLabel(vk));
        // then any remaining (main) keys in chord order
        foreach (var vk in chord.Keys)
            if (!order.Contains(vk)) caps.Add(KeyLabel(vk));
        return caps;
    }

    public static string KeyLabel(int vk) => vk switch
    {
        Vk.LControl => "L Ctrl",
        Vk.RControl => "R Ctrl",
        Vk.Control => "Ctrl",
        Vk.LMenu => "L Alt",
        Vk.RMenu => "R Alt",
        Vk.Menu => "Alt",
        Vk.LShift => "L Shift",
        Vk.RShift => "R Shift",
        Vk.Shift => "Shift",
        Vk.LWin => "⊞ Win",
        Vk.RWin => "⊞ Win",
        Vk.Space => "Space",
        Vk.Escape => "Esc",
        Vk.Tab => "Tab",
        Vk.Delete => "Del",
        Vk.F12 => "F12",
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),   // A–Z
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),   // 0–9
        >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),        // F1–F12
        _ => "0x" + vk.ToString("X2"),
    };

    /// <summary>Pretty one-line label (e.g. "Ctrl + Space").</summary>
    public static string Describe(HotkeyChord chord) => string.Join(" + ", Keycaps(chord));

    private static bool Has(HotkeyChord c, params int[] anyOf) => anyOf.Any(c.Contains);

    /// <summary>Assess a captured chord against the reserved-combo policy.</summary>
    public static HotkeyVerdict Assess(HotkeyChord chord)
    {
        bool ctrl = Has(chord, Vk.Control, Vk.LControl, Vk.RControl);
        bool alt = Has(chord, Vk.Menu, Vk.LMenu, Vk.RMenu);
        bool win = Has(chord, Vk.LWin, Vk.RWin);
        bool shift = Has(chord, Vk.Shift, Vk.LShift, Vk.RShift);

        // BLOCKED — physically cannot work / catastrophic:
        if (ctrl && alt && chord.Contains(Vk.Delete))
            return new(HotkeyRisk.Blocked, "Ctrl + Alt + Del is a secure system sequence — Windows won't let any app use it.");
        if (win && chord.Contains(Vk.L))
            return new(HotkeyRisk.Blocked, "Win + L locks your PC — pick something else.");

        // WARN — usable but likely to clash with an OS/shell shortcut:
        if (win && chord.Contains(Vk.R))
            return new(HotkeyRisk.Warn, "Win + R opens the Run dialog — it may fight your hotkey.");
        if (alt && chord.Contains(Vk.Tab))
            return new(HotkeyRisk.Warn, "Alt + Tab switches windows — it may fight your hotkey.");
        if (alt && chord.Contains(Vk.F12))
            return new(HotkeyRisk.Warn, "F12 is reserved for the debugger.");
        if (ctrl && shift && chord.Contains(Vk.Escape))
            return new(HotkeyRisk.Warn, "Ctrl + Shift + Esc opens Task Manager — it may fight your hotkey.");
        if (ctrl && chord.Contains(Vk.Escape))
            return new(HotkeyRisk.Warn, "Ctrl + Esc opens the Start menu — it may fight your hotkey.");
        if (win)
            return new(HotkeyRisk.Warn, "Windows-key combos are reserved by the OS and may not always reach Kivi.");
        if (chord.Contains(Vk.F12))
            return new(HotkeyRisk.Warn, "F12 is reserved for the debugger.");

        return new(HotkeyRisk.Ok, null);
    }
}
