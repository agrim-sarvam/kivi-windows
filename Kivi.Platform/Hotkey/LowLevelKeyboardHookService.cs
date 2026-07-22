using System.Runtime.InteropServices;
using Kivi.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Kivi.Platform.Hotkey;

public sealed class LowLevelKeyboardHookService : IHotkeyService, IDisposable
{
    private uint _boundVk = 0xA3;    // VK_RCONTROL default; changeable via SetHotkey
    private uint _rewriteVk = 0xA5;  // VK_RMENU default; changeable via SetRewriteHotkey
    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
    private const uint VK_RETURN = 0x0D, VK_ESCAPE = 0x1B;

    public event Action? HoldStarted;
    public event Action? HoldEnded;
    public event Action? RewriteHoldStarted;
    public event Action? RewriteHoldEnded;
    public event Action? ReviewAccepted;
    public event Action? ReviewCancelled;

    private HOOKPROC? _proc;   // keep alive to avoid GC of the delegate while the hook is installed
    private UnhookWindowsHookExSafeHandle? _hook;
    private bool _held;
    private bool _rewriteHeld;
    private volatile bool _reviewArmed;
    private bool _swallowingReturn;
    private bool _swallowingEscape;

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

    public void SetRewriteHotkey(uint virtualKeyCode)
    {
        _rewriteVk = virtualKeyCode;
        if (_rewriteHeld) { _rewriteHeld = false; RewriteHoldEnded?.Invoke(); }
    }

    public void ArmReviewKeys() => _reviewArmed = true;
    public void DisarmReviewKeys() => _reviewArmed = false;

    private LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint msg = (uint)wParam.Value;
            bool isDown = msg == WM_KEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (data.vkCode == _boundVk)
            {
                if (isDown && !_held) { _held = true; HoldStarted?.Invoke(); }
                else if (isUp && _held) { _held = false; HoldEnded?.Invoke(); }
            }
            else if (data.vkCode == _rewriteVk)
            {
                if (isDown && !_rewriteHeld) { _rewriteHeld = true; RewriteHoldStarted?.Invoke(); }
                else if (isUp && _rewriteHeld) { _rewriteHeld = false; RewriteHoldEnded?.Invoke(); }
            }
            else if (data.vkCode == VK_RETURN && (_reviewArmed || _swallowingReturn))
            {
                if (isDown)
                {
                    if (_reviewArmed)
                    {
                        _reviewArmed = false;
                        _swallowingReturn = true;
                        ReviewAccepted?.Invoke();
                    }
                    return new LRESULT(1); // swallow this keydown, including OS auto-repeat while held: confirms the rewrite, not a newline in the target app
                }
                if (isUp)
                {
                    _swallowingReturn = false;
                    return new LRESULT(1); // swallow the key-up for the same physical hold that was captured on keydown
                }
            }
            else if (data.vkCode == VK_ESCAPE && (_reviewArmed || _swallowingEscape))
            {
                if (isDown)
                {
                    if (_reviewArmed)
                    {
                        _reviewArmed = false;
                        _swallowingEscape = true;
                        ReviewCancelled?.Invoke();
                    }
                    return new LRESULT(1); // swallow this keydown, including OS auto-repeat while held: discards the rewrite, not an app-level cancel
                }
                if (isUp)
                {
                    _swallowingEscape = false;
                    return new LRESULT(1); // swallow the key-up for the same physical hold that was captured on keydown
                }
            }
        }

        return PInvoke.CallNextHookEx(_hook, nCode, wParam, lParam); // non-suppressing (except the two swallowed cases above)
    }
}
