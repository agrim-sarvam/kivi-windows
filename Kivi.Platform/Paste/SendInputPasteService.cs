using System.Runtime.InteropServices;
using System.Windows.Forms;
using Kivi.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Kivi.Platform.Paste;

/// <summary>
/// Injects text into the focused application via the clipboard + a simulated Ctrl+V
/// (SendInput). Implements four hard-won safeguards learned from real-world testing:
///   1. Wait for hotkey modifiers (Shift/Ctrl/Alt/Win) to be released before pasting,
///      so the still-held hotkey combo doesn't corrupt the paste (e.g. Ctrl+Shift+V).
///   2. Save + set + verify the clipboard (rewriting once) to survive clipboard
///      contention from other processes.
///   3. Send Ctrl+V, then wait before restoring the clipboard, so slow apps
///      (e.g. Electron-based editors) have time to read it.
///   4. Restore the previous clipboard contents, and optionally send Enter afterwards.
///
/// Clipboard and SendInput calls require an STA thread; that thread is provided by the
/// application's message pump (Task 16), not by this service.
/// </summary>
public sealed class SendInputPasteService : IPasteService
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CTRL = 0x11;
    private const int VK_ALT = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const ushort VK_RETURN = 0x0D;

    private static readonly int[] ModifierKeys = { VK_SHIFT, VK_CTRL, VK_ALT, VK_LWIN, VK_RWIN };

    public async Task InjectTextAsync(string text, bool pressEnter)
    {
        // Safeguard 1: wait for hotkey modifiers to be released before we touch the clipboard
        // or send Ctrl+V, otherwise the still-held modifiers corrupt the paste keystroke.
        await WaitForModifiersReleasedAsync();

        // Safeguard 2: save the current clipboard, set ours, and verify it stuck.
        string? previousClipboard = SafeGetClipboardText();
        SetClipboardWithRetry(text);

        // Send Ctrl+V to paste into the focused app.
        SendCtrlV();

        // Safeguard 3: give slow apps (e.g. Electron) time to read the clipboard
        // before we restore the user's previous clipboard contents.
        await Task.Delay(400);

        // Safeguard 4: restore the previous clipboard contents.
        if (previousClipboard is not null)
        {
            SetClipboardWithRetry(previousClipboard);
        }

        if (pressEnter)
        {
            await Task.Delay(80);
            SendKeyPress(VK_RETURN);
        }
    }

    private static async Task WaitForModifiersReleasedAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < deadline)
        {
            bool anyModifierDown = false;
            foreach (int vk in ModifierKeys)
            {
                short state = PInvoke.GetAsyncKeyState(vk);
                if ((state & 0x8000) != 0)
                {
                    anyModifierDown = true;
                    break;
                }
            }

            if (!anyModifierDown)
            {
                return;
            }

            await Task.Delay(40);
        }
    }

    private static string? SafeGetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SetClipboardWithRetry(string value)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Clipboard.SetText(value);
                if (Clipboard.GetText() == value)
                {
                    return;
                }
            }
            catch
            {
                // Clipboard is likely locked by another process; retry after a short delay.
            }

            Thread.Sleep(40);
        }
    }

    private static void SendCtrlV()
    {
        SendKeyDown(VK_CONTROL);
        SendKeyDown(VK_V);
        SendKeyUp(VK_V);
        SendKeyUp(VK_CONTROL);
    }

    private static void SendKeyPress(ushort vk)
    {
        SendKeyDown(vk);
        SendKeyUp(vk);
    }

    private static void SendKeyDown(ushort vk) => SendKeyInput(vk, keyUp: false);

    private static void SendKeyUp(ushort vk) => SendKeyInput(vk, keyUp: true);

    private static unsafe void SendKeyInput(ushort vk, bool keyUp)
    {
        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
        };
        input.Anonymous.ki.wVk = (VIRTUAL_KEY)vk;
        input.Anonymous.ki.dwFlags = keyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;

        var inputs = new[] { input };
        PInvoke.SendInput(inputs.AsSpan(), Marshal.SizeOf<INPUT>());
    }
}
