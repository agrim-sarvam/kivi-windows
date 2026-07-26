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

    // A press released before TapMaxMs reads as a "tap"; longer becomes a hold. A second tap
    // within DoubleTapWindowMs of the first tap's release triggers hands-free.
    private const int TapMaxMs = 350;
    private const long DoubleTapWindowMs = 400;

    // Which event fires as a result of one key transition. Computed under lock, invoked after
    // releasing it (never dispatch events while holding a lock -- handlers may re-enter/block).
    private enum Fire { None, HoldStart, HoldEnd, HandsFreeToggle }

    public event Action? HoldStarted;
    public event Action? HoldEnded;
    public event Action? EnglishHoldStarted;
    public event Action? EnglishHoldEnded;
    public event Action<bool>? HandsFreeToggled;

    private HOOKPROC? _proc;   // keep alive to avoid GC of the delegate while the hook is installed
    private UnhookWindowsHookExSafeHandle? _hook;
    private volatile bool _enabled = true;

    private readonly object _sync = new();
    private KeyState _primary;
    private KeyState _english;
    private System.Threading.Timer? _primaryHoldTimer;
    private System.Threading.Timer? _englishHoldTimer;

    private struct KeyState
    {
        public bool Down;            // key physically down right now
        public bool HoldActive;      // a press-and-hold dictation is in progress
        public bool HandsFree;       // a hands-free (double-tap) dictation is in progress
        public long DownAt;          // tick of the current key-down
        public long LastTapUpAt;     // tick of the last short-press release (double-tap timing)
    }

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

    public void Dispose()
    {
        Stop();
        _primaryHoldTimer?.Dispose();
        _englishHoldTimer?.Dispose();
    }

    public void SetHotkey(uint virtualKeyCode) { _boundVk = virtualKeyCode; Reset(isEnglish: false); }
    public void SetEnglishHotkey(uint virtualKeyCode) { _englishVk = virtualKeyCode; Reset(isEnglish: true); }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled) { Reset(isEnglish: false); Reset(isEnglish: true); }
    }

    // Clears any in-progress hold/hands-free for a key so state doesn't stick across a rebind or
    // a pause, firing the matching stop event if one was active.
    private void Reset(bool isEnglish)
    {
        bool endHold = false, endHandsFree = false;
        lock (_sync)
        {
            ref var k = ref (isEnglish ? ref _english : ref _primary);
            if (k.HoldActive) { k.HoldActive = false; endHold = true; }
            if (k.HandsFree) { k.HandsFree = false; endHandsFree = true; }
            k.Down = false; k.DownAt = 0; k.LastTapUpAt = 0;
        }
        DisarmHoldTimer(isEnglish);
        if (endHold) { if (isEnglish) EnglishHoldEnded?.Invoke(); else HoldEnded?.Invoke(); }
        if (endHandsFree) HandsFreeToggled?.Invoke(isEnglish);
    }

    private LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0 && _enabled)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint msg = (uint)wParam.Value;
            bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (data.vkCode == _boundVk) Handle(isEnglish: false, isDown, isUp);
            else if (data.vkCode == _englishVk) Handle(isEnglish: true, isDown, isUp);
        }
        return PInvoke.CallNextHookEx(_hook, nCode, wParam, lParam); // non-suppressing: dictation hotkeys pass through
    }

    private void Handle(bool isEnglish, bool isDown, bool isUp)
    {
        long now = Environment.TickCount64;
        var fire = Fire.None;
        bool armTimer = false;

        lock (_sync)
        {
            ref var k = ref (isEnglish ? ref _english : ref _primary);

            if (isDown && !k.Down)
            {
                k.Down = true;
                k.DownAt = now;

                if (k.HandsFree) { /* the tap that stops hands-free -- finalized on release */ }
                else if (k.LastTapUpAt != 0 && now - k.LastTapUpAt <= DoubleTapWindowMs)
                {
                    // Second quick tap -> start hands-free. No hold begins.
                    k.LastTapUpAt = 0;
                    k.HandsFree = true;
                    fire = Fire.HandsFreeToggle;
                }
                else
                {
                    // Not yet a hold, not a double-tap: arm the hold-promotion timer.
                    armTimer = true;
                }
            }
            else if (isUp && k.Down)
            {
                k.Down = false;

                if (k.HandsFree)
                {
                    k.HandsFree = false;
                    fire = Fire.HandsFreeToggle; // stop
                }
                else if (k.HoldActive)
                {
                    k.HoldActive = false;
                    fire = Fire.HoldEnd;         // real hold finalized
                    k.LastTapUpAt = 0;
                }
                else
                {
                    // Released before the hold threshold -> a tap; remember it for a double-tap.
                    k.LastTapUpAt = now;
                }
            }
        }

        if (isUp) DisarmHoldTimer(isEnglish);
        if (armTimer) ArmHoldTimer(isEnglish);
        Dispatch(fire, isEnglish);
    }

    private void Dispatch(Fire fire, bool isEnglish)
    {
        switch (fire)
        {
            case Fire.HoldStart: if (isEnglish) EnglishHoldStarted?.Invoke(); else HoldStarted?.Invoke(); break;
            case Fire.HoldEnd: if (isEnglish) EnglishHoldEnded?.Invoke(); else HoldEnded?.Invoke(); break;
            case Fire.HandsFreeToggle: HandsFreeToggled?.Invoke(isEnglish); break;
        }
    }

    private void ArmHoldTimer(bool isEnglish)
    {
        var timer = new System.Threading.Timer(_ => PromoteToHold(isEnglish), null, TapMaxMs, System.Threading.Timeout.Infinite);
        if (isEnglish) { _englishHoldTimer?.Dispose(); _englishHoldTimer = timer; }
        else { _primaryHoldTimer?.Dispose(); _primaryHoldTimer = timer; }
    }

    private void DisarmHoldTimer(bool isEnglish)
    {
        if (isEnglish) { _englishHoldTimer?.Dispose(); _englishHoldTimer = null; }
        else { _primaryHoldTimer?.Dispose(); _primaryHoldTimer = null; }
    }

    // Fires on a threadpool thread TapMaxMs after key-down; if the key is still genuinely held
    // (and nothing else took over), the press becomes a real hold and dictation begins.
    private void PromoteToHold(bool isEnglish)
    {
        var fire = Fire.None;
        lock (_sync)
        {
            ref var k = ref (isEnglish ? ref _english : ref _primary);
            if (k.Down && !k.HoldActive && !k.HandsFree)
            {
                k.HoldActive = true;
                fire = Fire.HoldStart;
            }
        }
        Dispatch(fire, isEnglish);
    }
}
