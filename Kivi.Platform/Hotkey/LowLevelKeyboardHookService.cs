using System.Runtime.InteropServices;
using Kivi.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Kivi.Platform.Hotkey;

public sealed class LowLevelKeyboardHookService : IHotkeyService, IDisposable
{
    private uint _boundVk = 0xA3;    // VK_RCONTROL default; changeable via SetHotkey
    private uint _englishVk = 0xA5;  // VK_RMENU default; changeable via SetEnglishHotkey
    // Alt keys (VK_RMENU / Right Alt, the default English hotkey) are *system* keys, so
    // Windows delivers WM_SYSKEYDOWN/WM_SYSKEYUP for them, not WM_KEYDOWN/WM_KEYUP. Both
    // pairs must be handled or a Right-Alt hold's key-DOWN is never seen and the hotkey
    // silently does nothing.
    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    public event Action? HoldStarted;
    public event Action? HoldEnded;
    public event Action? EnglishHoldStarted;
    public event Action? EnglishHoldEnded;

    private HOOKPROC? _proc;   // keep alive to avoid GC of the delegate while the hook is installed
    private UnhookWindowsHookExSafeHandle? _hook;
    private bool _held;
    private bool _englishHeld;
    private volatile bool _enabled = true;

    public unsafe void Start()
    {
        _proc = HookCallback;
        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _proc,
            PInvoke.GetModuleHandle((string?)null), 0);
    }

    public void Stop()
    {
        _hook?.Dispose();
        _hook = null;
    }

    public void Dispose() => Stop();

    public void SetHotkey(uint virtualKeyCode)
    {
        _boundVk = virtualKeyCode;
        // If a hold was in progress on the old key, clear it so state doesn't stick.
        if (_held) { _held = false; HoldEnded?.Invoke(); }
    }

    public void SetEnglishHotkey(uint virtualKeyCode)
    {
        _englishVk = virtualKeyCode;
        if (_englishHeld) { _englishHeld = false; EnglishHoldEnded?.Invoke(); }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        // Clear any in-progress hold so state doesn't stick across a pause.
        if (!enabled)
        {
            if (_held) { _held = false; HoldEnded?.Invoke(); }
            if (_englishHeld) { _englishHeld = false; EnglishHoldEnded?.Invoke(); }
        }
    }

    private LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0 && _enabled)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint msg = (uint)wParam.Value;
            bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (data.vkCode == _boundVk)
            {
                if (isDown && !_held) { _held = true; HoldStarted?.Invoke(); }
                else if (isUp && _held) { _held = false; HoldEnded?.Invoke(); }
            }
            else if (data.vkCode == _englishVk)
            {
                if (isDown && !_englishHeld) { _englishHeld = true; EnglishHoldStarted?.Invoke(); }
                else if (isUp && _englishHeld) { _englishHeld = false; EnglishHoldEnded?.Invoke(); }
            }
        }

        return PInvoke.CallNextHookEx(_hook, nCode, wParam, lParam); // non-suppressing: dictation hotkeys pass through to the OS
    }
}
