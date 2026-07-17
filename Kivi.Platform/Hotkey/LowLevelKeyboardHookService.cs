using System.Runtime.InteropServices;
using Kivi.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Kivi.Platform.Hotkey;

public sealed class LowLevelKeyboardHookService : IHotkeyService, IDisposable
{
    private const uint VK_RCONTROL = 0xA3;
    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;

    public event Action? HoldStarted;
    public event Action? HoldEnded;

    private HOOKPROC? _proc;   // keep alive to avoid GC of the delegate while the hook is installed
    private UnhookWindowsHookExSafeHandle? _hook;
    private bool _held;

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

    private LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (data.vkCode == VK_RCONTROL)
            {
                uint msg = (uint)wParam.Value;
                if (msg == WM_KEYDOWN && !_held)
                {
                    _held = true;
                    HoldStarted?.Invoke();
                }
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    if (_held)
                    {
                        _held = false;
                        HoldEnded?.Invoke();
                    }
                }
            }
        }

        return PInvoke.CallNextHookEx(_hook, nCode, wParam, lParam); // non-suppressing
    }
}
