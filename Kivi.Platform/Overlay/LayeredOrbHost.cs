using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Kivi.Core.Contracts;

namespace Kivi.Platform.Overlay;

/// <summary>
/// The REAL native Win32 layered orb window (MASTER-PLAN §2.1 + the orb-is-a-chip memo).
///
/// A top-level window with WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
/// drawn per-tick via UpdateLayeredWindow with a premultiplied-ARGB 32-bit DIB (true per-pixel
/// alpha). Always-on-top, no taskbar button, never takes focus. Click-through by default
/// (WS_EX_TRANSPARENT); SetClickThrough(false) makes it hit-testable for the interactive regions.
///
/// A WPF transparent window can NOT give true non-activation + per-pixel alpha, hence this native
/// window. An invisible WPF host window (created by the app) owns process lifetime; this window is
/// a plain Win32 popup created on the UI thread.
///
/// The render layer (Kivi.App/Drawing) produces a premultiplied-ARGB System.Drawing.Bitmap each
/// frame and calls <see cref="PushFrame"/> with the desired screen position.
/// </summary>
public sealed class LayeredOrbHost : IOverlayHost, IDisposable
{
    private IntPtr _hwnd = IntPtr.Zero;
    private WndProcDelegate? _wndProc; // keep alive against GC
    private bool _clickThrough = true;
    private bool _disposed;

    // Current window screen rect (physical px), tracked so drag math + coordinate conversion don't
    // need a GetWindowRect round-trip on every mouse message. Updated by PushFrame (render) and by
    // drag moves.
    private int _screenX, _screenY, _width, _height;

    // Drag-anywhere-on-the-orb state (user's explicit requirement: click-and-hold directly on the
    // orb body, drag, release to place it anywhere — no separate handle, no double-click-to-dock).
    private bool _dragging;
    private bool _mouseDownPending; // true between WM_LBUTTONDOWN and either drag-start or click-resolve
    private int _dragStartCursorX, _dragStartCursorY;
    private int _dragStartWinX, _dragStartWinY;
    private const int DragThresholdPx = 4; // click-vs-drag: judgment call (see report) — a few px of
                                            // slop absorbs sub-pixel jitter on a press without
                                            // requiring the user to hold perfectly still to "click".

    private const string ClassName = "KiviLayeredOrb";

    public IntPtr Handle => _hwnd;

    /// <summary>Raised on WM_LBUTTONDOWN, with the cursor position in physical screen px. The
    /// caller (FlowRuntime) converts to flow-space and decides what was pressed; this class only
    /// tracks whether that press turns into a drag vs. resolves as a plain click.</summary>
    public event Action<int, int>? MouseDown;

    /// <summary>Raised on WM_LBUTTONUP when the press did NOT turn into a drag (moved less than
    /// <see cref="DragThresholdPx"/>) — i.e. a real click, in physical screen px.</summary>
    public event Action<int, int>? Click;

    /// <summary>Raised continuously while free-dragging the orb (cursor moved past the threshold
    /// after a down on the orb body) — the new window top-left in physical screen px. FlowRuntime
    /// uses this to know the render loop should stop overwriting the position with the bottom-center
    /// auto-layout for the rest of the session.</summary>
    public event Action<int, int>? DragMoved;

    /// <summary>Raised once when a press turns into a drag (i.e. right after MouseDown, before the
    /// first DragMoved) so the caller can flip its "user positioned the orb" flag exactly once.</summary>
    public event Action? DragStarted;

    public bool IsDragging => _dragging;

    public void EnsureCreated()
    {
        if (_hwnd != IntPtr.Zero) return;

        _wndProc = WndProc;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName,
            hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
        };
        RegisterClassEx(ref wc);

        int exStyle = WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if (_clickThrough) exStyle |= WS_EX_TRANSPARENT;

        _hwnd = CreateWindowEx(
            exStyle,
            ClassName,
            "Kivi",
            WS_POPUP,
            0, 0, 10, 10,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        // Show without activating so the host app keeps focus (dictated text lands there).
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
    }

    public void ApplyNonActivating()
    {
        // The extended styles are set at creation; this ensures the window exists + is topmost.
        EnsureCreated();
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void SetClickThrough(bool clickThrough)
    {
        if (_clickThrough == clickThrough && _hwnd != IntPtr.Zero) return;
        _clickThrough = clickThrough;
        if (_hwnd == IntPtr.Zero) return;
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        if (clickThrough) ex |= WS_EX_TRANSPARENT;
        else ex &= ~WS_EX_TRANSPARENT;
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
    }

    /// <summary>
    /// Blit a premultiplied-ARGB bitmap to the layered window, positioned so its top-left sits at
    /// (screenX, screenY) in physical pixels. The bitmap's own size becomes the window size.
    /// </summary>
    public void PushFrame(Bitmap premultipliedArgb, int screenX, int screenY)
    {
        if (_disposed) return;
        EnsureCreated();

        int w = premultipliedArgb.Width;
        int h = premultipliedArgb.Height;
        _screenX = screenX;
        _screenY = screenY;
        _width = w;
        _height = h;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            hBitmap = premultipliedArgb.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memDc, hBitmap);

            var size = new SIZE { cx = w, cy = h };
            var srcPos = new POINT { x = 0, y = 0 };
            var dstPos = new POINT { x = screenX, y = screenY };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA,
            };

            UpdateLayeredWindow(_hwnd, screenDc, ref dstPos, ref size, memDc, ref srcPos,
                0, ref blend, ULW_ALPHA);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Hide()
    {
        if (_hwnd != IntPtr.Zero) ShowWindow(_hwnd, SW_HIDE);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // NCHITTEST: when not click-through, the whole window is client so the app hit-test / hover
        // classifier (FlowFrame.InteractiveTarget) governs interactivity; we simply let it through.
        if (msg == WM_MOUSEACTIVATE)
            return (IntPtr)MA_NOACTIVATE; // never steal activation on click

        // Mouse messages only arrive here when SetClickThrough(false) is currently in effect (the
        // window is WS_EX_TRANSPARENT otherwise, so Windows routes clicks to whatever is behind it).
        // FlowRuntime toggles click-through every tick from the same hit-test that decides what a
        // click here should do, so by the time WM_LBUTTONDOWN arrives we know the cursor is over
        // *some* interactive region — GetCursorPos gives the exact point for the caller to classify.
        switch (msg)
        {
            case WM_LBUTTONDOWN:
            {
                GetCursorPos(out var pt);
                _mouseDownPending = true;
                _dragging = false;
                _dragStartCursorX = pt.x;
                _dragStartCursorY = pt.y;
                _dragStartWinX = _screenX;
                _dragStartWinY = _screenY;
                SetCapture(hWnd); // keep receiving WM_MOUSEMOVE/WM_LBUTTONUP even if the cursor
                                   // leaves the (tiny) orb window mid-drag
                MouseDown?.Invoke(pt.x, pt.y);
                return IntPtr.Zero;
            }
            case WM_MOUSEMOVE:
            {
                if (!_mouseDownPending) break;
                GetCursorPos(out var pt);
                int dx = pt.x - _dragStartCursorX;
                int dy = pt.y - _dragStartCursorY;
                if (!_dragging && (Math.Abs(dx) > DragThresholdPx || Math.Abs(dy) > DragThresholdPx))
                {
                    _dragging = true;
                    DragStarted?.Invoke();
                }
                if (_dragging)
                {
                    int newX = _dragStartWinX + dx;
                    int newY = _dragStartWinY + dy;
                    SetWindowPos(hWnd, IntPtr.Zero, newX, newY, 0, 0,
                        SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
                    _screenX = newX;
                    _screenY = newY;
                    DragMoved?.Invoke(newX, newY);
                }
                return IntPtr.Zero;
            }
            case WM_LBUTTONUP:
            {
                if (!_mouseDownPending) break;
                _mouseDownPending = false;
                ReleaseCapture();
                GetCursorPos(out var pt);
                bool wasDragging = _dragging;
                _dragging = false;
                if (!wasDragging) Click?.Invoke(pt.x, pt.y);
                return IntPtr.Zero;
            }
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    // ---------------------------------------------------------------- P/Invoke

    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_POPUP = 0x80000000;

    private const int GWL_EXSTYLE = -20;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    private const int ULW_ALPHA = 0x02;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;

    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_MOUSEMOVE = 0x0200;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOZORDER = 0x0004;

    private const int IDC_ARROW = 32512;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName,
        uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr inst, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr h, int index, int val);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size,
        IntPtr srcDc, ref POINT src, int colorKey, ref BLENDFUNCTION blend, int flags);
}
