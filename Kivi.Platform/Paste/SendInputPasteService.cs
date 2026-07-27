using System.Runtime.InteropServices;
using Kivi.Core.Contracts;

namespace Kivi.Platform.Paste;

/// <summary>
/// REAL clipboard + synthesized-paste insertion (the OpenWhispr windows-fast-paste.c pattern, in-process
/// P/Invoke). Sequence per platform-coupling-audit §5 / dictation-audio-pipeline §8:
///   1. Secure field? → return SecureFieldBlocked (NO clipboard write, NO paste).
///   2. Snapshot the current clipboard text.
///   3. Write the payload to the clipboard.
///   4. ~40 ms settle.
///   5. Release any modifiers the user is still holding (PTT means Right-Ctrl is likely down; a stray
///      held Ctrl would corrupt the synthesized chord).
///   6. Synthesize Ctrl+V — or Ctrl+Shift+V when PasteMeta.IsTerminal — via SendInput. We do NOT
///      re-foreground the target (the orb is non-activating, so focus never left the target).
///   7. Restore the previous clipboard.
///
/// Newlines are carried literally in the clipboard payload (a paste inserts a literal line break); we
/// never synthesize a Return key, which some apps treat as submit.
/// </summary>
public sealed class SendInputPasteService : IPasteService
{
    private const int ClipboardSettleMs = 40;

    public async Task<PasteOutcome> InsertAsync(string text, PasteMeta meta)
    {
        // 1. Secure-field gate — no clipboard write, no paste.
        if (meta.IsSecureField)
            return PasteOutcome.SecureFieldBlocked;

        if (string.IsNullOrEmpty(text))
            return PasteOutcome.Ok;

        try
        {
            // 2 + 3. Snapshot + write on an STA thread (Win32 clipboard requires STA).
            string? previous = GetClipboardTextSta();
            SetClipboardTextSta(text);

            // 4. Settle so the target app observes the new clipboard content.
            await Task.Delay(ClipboardSettleMs).ConfigureAwait(false);

            // 5. Release held modifiers (Ctrl/Shift/Alt/Win, both sides).
            ReleaseModifiers();

            // 6. Synthesize the paste chord.
            SendPasteChord(meta.IsTerminal);

            // Give the target a moment to consume the paste before we restore the clipboard.
            await Task.Delay(ClipboardSettleMs).ConfigureAwait(false);

            // 7. Restore the user's previous clipboard.
            if (previous is not null) SetClipboardTextSta(previous);
            else ClearClipboardSta();

            return PasteOutcome.Ok;
        }
        catch
        {
            return PasteOutcome.Failed;
        }
    }

    // --- Paste chord synthesis ---

    private static void SendPasteChord(bool terminal)
    {
        // Terminals map Ctrl+V to SIGINT-ish behavior; the universal paste there is Ctrl+Shift+V.
        var inputs = terminal
            ? BuildChord(VK_CONTROL, VK_SHIFT, VK_V)
            : BuildChord(VK_CONTROL, 0, VK_V);
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT[] BuildChord(ushort mod1, ushort mod2, ushort key)
    {
        bool hasMod2 = mod2 != 0;
        var list = new List<INPUT>(6)
        {
            KeyDown(mod1),
        };
        if (hasMod2) list.Add(KeyDown(mod2));
        list.Add(KeyDown(key));
        list.Add(KeyUp(key));
        if (hasMod2) list.Add(KeyUp(mod2));
        list.Add(KeyUp(mod1));
        return list.ToArray();
    }

    private static void ReleaseModifiers()
    {
        // Send key-up for every modifier that is currently physically/logically down, so the paste chord
        // isn't polluted by the still-held PTT key.
        var ups = new List<INPUT>(8);
        foreach (ushort vk in new ushort[] { VK_LCONTROL, VK_RCONTROL, VK_LSHIFT, VK_RSHIFT, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN })
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                ups.Add(KeyUp(vk));
        }
        if (ups.Count > 0)
            SendInput((uint)ups.Count, ups.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyDown(ushort vk) => MakeKey(vk, 0);
    private static INPUT KeyUp(ushort vk) => MakeKey(vk, KEYEVENTF_KEYUP);

    private static INPUT MakeKey(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = flags, time = 0, dwExtraInfo = UIntPtr.Zero }
        }
    };

    // --- Clipboard on an STA thread ---

    private static string? GetClipboardTextSta()
    {
        string? result = null;
        RunSta(() =>
        {
            if (System.Windows.Forms.Clipboard.ContainsText())
                result = System.Windows.Forms.Clipboard.GetText();
        });
        return result;
    }

    private static void SetClipboardTextSta(string text)
        => RunSta(() => System.Windows.Forms.Clipboard.SetText(text));

    private static void ClearClipboardSta()
        => RunSta(System.Windows.Forms.Clipboard.Clear);

    private static void RunSta(Action action)
    {
        Exception? captured = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        if (captured is not null) throw captured;
    }

    // --- Win32 ---

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
