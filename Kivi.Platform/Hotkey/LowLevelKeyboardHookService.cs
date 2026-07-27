using System.Diagnostics;
using System.Runtime.InteropServices;
using Kivi.Core.Contracts;

namespace Kivi.Platform.Hotkey;

/// <summary>
/// REAL global hold-to-talk hotkey. Installs a WH_KEYBOARD_LL hook on a DEDICATED native thread with
/// its own message pump (GetMessage/TranslateMessage/DispatchMessage). This is the #1 gotcha (R9/R5):
/// a busy thread — especially the UI thread — makes Windows silently drop the hook, so the hook must
/// own an otherwise-idle thread whose only job is pumping messages.
///
/// Emits raw key-down / key-up <see cref="GestureEdge"/>s for the configured trigger key (default
/// Right-Ctrl). The pure GestureClassifier (Kivi.Core, the 420/450/600 ms logic) turns these edges
/// into tap/hold/double in the orchestrator — this layer only reports the raw physical edges.
///
/// Rebuilt from scratch to the Electron/OpenWhispr Windows pattern (windows-key-listener.c). Not lifted.
/// </summary>
public sealed class LowLevelKeyboardHookService : IHotkeyService, IDisposable
{
    // --- Win32 constants ---
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;

    // Virtual key codes. Default trigger = Right-Ctrl (VK_RCONTROL). NOT fn (no fn on Windows),
    // NOT AltGr (VK_RMENU) which collides with paste chords, NOT Left-Ctrl (collides with Ctrl+V).
    private const int VK_RCONTROL = 0xA3;

    public event Action<GestureEdge>? Edge;

    private readonly int _triggerVk;
    private readonly object _gate = new();

    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hookHandle = IntPtr.Zero;

    // Keep the delegate alive for the lifetime of the hook — if it is GC'd the hook crashes the app.
    private LowLevelKeyboardProc? _proc;

    // Whether to swallow the trigger key so the host app never sees it. Read on the hook thread,
    // written from any thread — a plain volatile bool is sufficient (single writer semantics fine here).
    private volatile bool _consume;

    // Debounces auto-repeat: WH_KEYBOARD_LL delivers repeated WM_KEYDOWN while a key is physically held.
    // We only emit ONE Down edge per physical press and ONE Up edge per release.
    private volatile bool _triggerDown;

    private volatile bool _started;
    private volatile bool _disposed;

    public LowLevelKeyboardHookService() : this(VK_RCONTROL) { }

    /// <summary>Construct with a custom trigger virtual-key (for rebinding / tests).</summary>
    public LowLevelKeyboardHookService(int triggerVirtualKey) => _triggerVk = triggerVirtualKey;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;

            var ready = new ManualResetEventSlim(false);
            _hookThread = new Thread(() => HookThreadMain(ready))
            {
                IsBackground = true,
                Name = "Kivi.Hotkey.WH_KEYBOARD_LL",
                // A high priority keeps the hook responsive; the thread is otherwise idle (only pumps).
                Priority = ThreadPriority.AboveNormal,
            };
            // MTA is fine — the hook needs a message pump, not an STA apartment.
            _hookThread.Start();
            ready.Wait(); // block until SetWindowsHookEx has run (success or fail) on the dedicated thread
        }
    }

    public void Consume(bool on) => _consume = on;

    private void HookThreadMain(ManualResetEventSlim ready)
    {
        _hookThreadId = GetCurrentThreadId();
        _proc = HookCallback;

        // WH_KEYBOARD_LL is a GLOBAL hook — hMod can be the module handle of any loaded module and
        // dwThreadId must be 0 (all threads on the desktop). It does NOT require a DLL, unlike most hooks.
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var hMod = GetModuleHandle(curModule.ModuleName);

        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
        ready.Set();

        if (_hookHandle == IntPtr.Zero)
        {
            // Install failed — nothing to pump. Leave the thread; Start() has already unblocked.
            return;
        }

        // Dedicated message pump. The hook is only serviced while this thread pumps messages; keep it
        // doing nothing else. GetMessage blocks (0 CPU) until a message or WM_QUIT arrives.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)data.vkCode;

            if (vk == _triggerVk)
            {
                bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;
                long ts = Environment.TickCount64;

                if (isDown && !_triggerDown)
                {
                    _triggerDown = true;
                    RaiseEdge(new GestureEdge(GestureEdgeKind.Down, ts));
                }
                else if (isUp && _triggerDown)
                {
                    _triggerDown = false;
                    RaiseEdge(new GestureEdge(GestureEdgeKind.Up, ts));
                }

                // Swallow the key when consuming so the host never sees the trigger keystroke.
                if (_consume)
                    return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void RaiseEdge(GestureEdge edge)
    {
        // Marshal off the hook thread: subscribers must NOT run on the hook's message-pump thread
        // (any blocking work there would stall the pump and make Windows drop the hook). The classifier
        // and orchestrator consume these on a normal thread-pool thread.
        var handler = Edge;
        if (handler is null) return;
        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (h, e) = state;
            h(e);
        }, (handler, edge), preferLocal: false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            if (_hookThreadId != 0)
                PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }

        _hookThread?.Join(1000);
    }

    // --- P/Invoke ---

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
}
