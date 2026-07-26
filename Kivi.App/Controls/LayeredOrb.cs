using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Kivi.App.Interop;
using Kivi.App.ViewModels;
using Kivi.Core.Orchestration;
using Microsoft.UI.Dispatching;

namespace Kivi.App.Controls;

/// <summary>
/// The persistent desktop overlay, drawn as a genuinely transparent, always-on-top Win32
/// <b>layered window</b> (UpdateLayeredWindow + a premultiplied-ARGB GDI+ bitmap) - WinUI 3
/// composites its own windows opaquely, so it can't float a soft-glowing free-form shape.
///
/// Four postures, growing from a fixed bottom-center anchor (brand "kivi on the desktop"):
///  - <b>Rest</b>  -> a small breathing pill.
///  - <b>Woken</b> -> a brief transitional round orb (dot-matrix kiwi + satellites) shown right
///    after leaving rest, before the box appears.
///  - <b>Dictating</b> -> a text-layout box (header/body/footer) for Listening and every
///    subsequent pipeline state. Its geometry (not just opacity) grows from the orb's footprint
///    up to full size, so the transition reads as "the orb grows into the box" rather than a
///    full-size card materializing above a still-solid orb.
///
/// Click-through everywhere except a handful of small hotspots: hovering the rest pill (detected
/// by polling the cursor position each frame, independent of window messages) morphs the pill
/// into the same round "woken" orb used during real dictation, and rings it with five small
/// quick-action icons (edit/expand/settings/theme/dismiss) plus a "hold to talk" tooltip. Settings
/// reopens the Config page; Expand opens the main app window; the rest are honest visual stubs.
/// The orb body itself is also draggable while hovering (grab it and move it anywhere on screen -
/// it is not fixed bottom-center once dragged; this position is in-memory only, not persisted
/// across restarts yet). This requires the window to answer WM_NCHITTEST itself (rather than
/// relying on WS_EX_TRANSPARENT, which would make the whole window click-through
/// unconditionally) - HTTRANSPARENT everywhere except inside a hotspot circle, so the rest of
/// the screen stays exactly as click-through as before.
///
/// Reads live state directly off <see cref="OverlayViewModel"/> every frame instead of being
/// pushed updates, since the viewmodel already marshals orchestrator events onto this same UI
/// thread's DispatcherQueue.
/// </summary>
public sealed class LayeredOrb : IDisposable
{
    private const string ClassName = "KiviOrbLayered";
    private static NativeMethods.WndProc? _wndProcKeepAlive;
    private static ushort _classAtom;

    // Only one LayeredOrb exists for the app's lifetime; the static WndProc callback (required
    // by RegisterClassExW, which needs a plain function pointer, not an instance method) reaches
    // back into that single instance via this reference.
    private static LayeredOrb? _current;

    private const uint WM_MOUSEMOVE      = 0x0200;
    private const uint WM_LBUTTONDOWN    = 0x0201;
    private const uint WM_LBUTTONUP      = 0x0202;
    private const uint WM_CAPTURECHANGED = 0x0215;
    private const uint WM_NCHITTEST      = 0x0084;
    private const nint HTTRANSPARENT     = -1;
    private const nint HTCLIENT          = 1;

    // Design sizes in effective (96-dpi) px; scaled by the monitor DPI when drawn.
    // Uniformly reduced ~20% from the original mockup-derived sizes (520/170/39/15/61/23/
    // 322/108/20/480) so the orb, pill, and review box all read smaller on screen while
    // keeping their relative proportions and hit-region math intact.
    private const double CanvasW = 416, CanvasH = 136;
    private const double Baseline = CanvasH - 24;       // shared bottom edge; postures grow upward
    private const double PillW = 31, PillH = 12;
    private const double OrbDiameter = 49;
    private const double SatelliteGap = 18;              // from the orb's edge
    private const double BoxW = 258, BoxH = 86, BoxRadius = 16;
    private const double WokenHoldSeconds = 0.25;        // how long the woken orb holds before growing into a box

    private static readonly Color Forest     = Color.FromArgb(255, 0x18, 0x30, 0x0F); // --brand-orbforest (unused now)
    private static readonly Color Rim        = Color.FromArgb(255, 0x37, 0x63, 0x30);
    // Rest pill: light-green fill with a dark-green outline (per request), minimal glow.
    private static readonly Color PillFill    = Color.FromArgb(255, 0x8F, 0xBE, 0x63); // light leaf green
    private static readonly Color PillOutline = Color.FromArgb(255, 0x41, 0x69, 0x1E); // KiviColorLegGreen (dark green)

    // The woken orb (both the real dictation transition and the hover preview) is a light
    // sage/cream fill, NOT the rest pill's dark forest green - sampled directly from the
    // approved reference mockup, which shows a light-coloured orb with a darker dot-matrix
    // bird silhouette on top, not the other way around.
    private static readonly Color OrbFill      = Color.FromArgb(255, 0xCE, 0xD6, 0xC0);
    private static readonly Color OrbRim       = Color.FromArgb(255, 0xB4, 0xBF, 0xA2);
    private static readonly Color BirdDots     = Color.FromArgb(255, 0x6E, 0x74, 0x66);
    private static readonly Color Satellite    = Color.FromArgb(220, 0x6E, 0x74, 0x66);
    private static readonly Color TooltipBg    = Color.FromArgb(255, 0xF4, 0xF6, 0xEF);
    private static readonly Color FnBadgeBg    = Color.FromArgb(255, 0x3B, 0x5E, 0x1E);
    private static readonly Color Paper2     = Color.FromArgb(255, 0xFF, 0xFF, 0xFF); // --color-paper2
    private static readonly Color Border1    = Color.FromArgb(255, 0xED, 0xF0, 0xE6); // --color-border1
    private static readonly Color Fg1        = Color.FromArgb(255, 0x14, 0x18, 0x0E); // --color-fg1
    private static readonly Color Fg2        = Color.FromArgb(255, 0x5C, 0x64, 0x54); // --color-fg2
    private static readonly Color Fg3        = Color.FromArgb(255, 0x92, 0x9A, 0x8A); // --color-fg3

    // Satellite quick-action styling: a warm-white chip with a tinted ring + glyph, one green
    // (open app), one muted red (dismiss), each on a soft shadow so it reads as a floating button.
    private static readonly Color ChipFill    = Color.FromArgb(255, 0xFB, 0xFC, 0xF8);
    private static readonly Color ChipShadow  = Color.FromArgb(70, 0x14, 0x18, 0x0E);
    private static readonly Color ExpandRing  = Color.FromArgb(255, 0x9C, 0xC0, 0x6E);
    private static readonly Color ExpandGlyph = Color.FromArgb(255, 0x3B, 0x5E, 0x1E);
    private static readonly Color DismissRing = Color.FromArgb(255, 0xE2, 0xB4, 0xAE);
    private static readonly Color DismissGlyph= Color.FromArgb(255, 0xB0, 0x4A, 0x42);

    // Fixed, distinct per-state colours (foundation palette) so transitions are unmistakable.
    private static readonly Color CIdle       = Color.FromArgb(0x6E, 0xA3, 0x35);
    private static readonly Color CListening  = Color.FromArgb(0xE9, 0x6C, 0x2F);
    private static readonly Color CProcessing = Color.FromArgb(0x42, 0x50, 0xD5);
    private static readonly Color CSpeaking   = Color.FromArgb(0x4B, 0x7D, 0x28);
    private static readonly Color CWaiting    = Color.FromArgb(0xD2, 0x96, 0x2D);
    private static readonly Color CDone       = Color.FromArgb(0x6E, 0xA3, 0x35);
    private static readonly Color CError      = Color.FromArgb(0xB8, 0x15, 0x14);

    // Expand is a placeholder for a future "open the main Kivi app" action (per the approved
    // reference design) - the main app doesn't exist yet, so it stays an honest visual stub
    // alongside Edit/Theme/Dismiss until that's built.
    private enum HoverIcon { Expand, Dismiss }

    private readonly nint _hwnd;
    private readonly OverlayViewModel _vm;
    private readonly Color _accent;
    private readonly string _languageLabel;
    private readonly string _hotkeyLabel;
    private readonly DispatcherQueueTimer _timer;
    private readonly double _scale;

    // Hover hotspots, in the same scaled canvas-pixel space Render() draws in. Computed once
    // (they never change - canvas size and DPI scale are both fixed after construction).
    private readonly float _hoverLeft, _hoverTop, _hoverRight, _hoverBottom;
    private readonly (HoverIcon Icon, float Cx, float Cy, float R)[] _iconHotspots;
    private readonly float _orbBodyCx, _orbBodyCy, _orbBodyR; // draggable hit-region for the orb itself

    private byte[]? _mask;
    private int _maskW, _maskH, _maskStride;
    private PrivateFontCollection? _fonts;
    private FontFamily? _interFamily;
    private FontFamily? _monoFamily;

    private RecordingState _prevState = RecordingState.Idle;
    private double _activeSeconds;
    private double _orbAmount;               // 0 = pill, 1 = orb
    private double _boxAmount;                // 0 = orb, 1 = box (eased)
    private ColorF _glow;                    // current glow colour (lerped)
    private long _lastTicks;
    private long _lastTopmostReassert;       // throttles the periodic HWND_TOPMOST re-assert
    private double _phase;                   // seconds, drives breathing + waveform

    // Typewriter animation for the live transcript: the VM's PartialTranscript updates in ~500ms
    // jumps (each a full re-transcription), so we reveal it character-by-character here at frame
    // rate for a smooth "typing" feel instead of whole-line swaps.
    private string _typeTarget = "";          // the latest full partial from the VM
    private double _typeRevealed;             // how many chars of _typeTarget are currently shown (fractional, eased)
    private const double CharsPerSecond = 45; // typewriter reveal speed
    private bool _disposed;

    private int _windowX, _windowY;          // last screen position pushed via UpdateLayeredWindow
    private bool _hovering;                  // cursor within the pill+icon-row hover rect, and State == Idle

    // Dragging: lets the orb be moved anywhere on screen instead of staying fixed bottom-center.
    // In-memory only for this pass -- resets to bottom-center on the next app launch; persisting
    // the position is a "backend" concern for a later pass.
    private bool _dragging;
    private int _dragOffsetX, _dragOffsetY;  // cursor position relative to the window's origin, at drag start
    private bool _hasCustomPosition;
    private int _customX, _customY;

    /// <summary>Raised when the hover gear icon is clicked. Always raised on the UI thread.</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the hover expand icon is clicked. Always raised on the UI thread.</summary>
    public event Action? MainAppRequested;

    /// <summary>Raised when the hover dismiss (X) icon is clicked -- quits the whole app.
    /// Always raised on the UI thread.</summary>
    public event Action? QuitRequested;

    public LayeredOrb(OverlayViewModel vm, Color accent, string languageLabel, string hotkeyLabel)
    {
        _current = this;
        _vm = vm;
        _accent = accent;
        _languageLabel = languageLabel;
        _hotkeyLabel = hotkeyLabel;
        _glow = ColorF.From(CIdle);
        EnsureClassRegistered();

        _hwnd = NativeMethods.CreateWindowExW(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE,
            ClassName, "kivi", NativeMethods.WS_POPUP,
            0, 0, 10, 10, 0, 0, NativeMethods.GetModuleHandleW(null), 0);

        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        _scale = dpi == 0 ? 1.0 : dpi / 96.0;
        (_hoverLeft, _hoverTop, _hoverRight, _hoverBottom, _iconHotspots, _orbBodyCx, _orbBodyCy, _orbBodyR) = ComputeHotspots();

        LoadMask();
        LoadFonts();

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);

        _lastTicks = Environment.TickCount64;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) => Frame();
        _timer.Start();

        Frame();
    }

    // ---- hover hotspots ----
    // The hover-catch region is a RECTANGLE spanning the orb and the ring of icons around it
    // (not a small circle around the pill/orb center alone) - a circle sized just to catch the
    // orb's own footprint doesn't reach the icons overlapping its rim, so moving the cursor from
    // the orb toward an icon would dismiss the menu before the cursor ever got there. The
    // rectangle is the padded bounding box of the orb plus every icon (plus a fixed allowance
    // for the tooltip drawn above them), guaranteeing continuous coverage with no gap.
    private (float Left, float Top, float Right, float Bottom, (HoverIcon, float, float, float)[] Icons,
        float OrbCx, float OrbCy, float OrbR) ComputeHotspots()
    {
        double s = _scale;
        int w = (int)Math.Round(CanvasW * s);
        float cx = w / 2f;
        float baseline = (float)(Baseline * s);

        float r = (float)(OrbDiameter * s / 2);
        float orbCy = baseline - r;

        float iconR = (float)(9 * s);
        // The two actions flank the orb: pushed well clear horizontally (~1.75x the orb radius)
        // and lifted only slightly, so they sit beside the orb (not hugging it, not stacked into
        // the tooltip above it). Dismiss left, open-app right, matched size for balance.
        float scatterX = r * 1.75f;
        float lift = r * 0.55f;

        var icons = new (HoverIcon, float, float, float)[]
        {
            (HoverIcon.Dismiss, cx - scatterX, orbCy - lift, iconR),
            (HoverIcon.Expand,  cx + scatterX, orbCy - lift, iconR),
        };

        float left = cx - r, right = cx + r, top = orbCy - r, bottom = orbCy + r;
        foreach (var (_, ix, iy, ir) in icons)
        {
            left = Math.Min(left, ix - ir);
            right = Math.Max(right, ix + ir);
            top = Math.Min(top, iy - ir);
            bottom = Math.Max(bottom, iy + ir);
        }

        float pad = (float)(8 * s);
        float tooltipAllowance = (float)(34 * s); // fixed allowance for the tooltip drawn above the ring
        return (left - pad, top - tooltipAllowance - pad, right + pad, bottom + pad, icons, cx, orbCy, r);
    }

    // ---- Win32 message handling (hover hit-testing, icon clicks, orb dragging) ----
    private static nint WndProcCallback(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        var self = _current;
        if (self is not null)
        {
            if (msg == WM_NCHITTEST) return self.HitTest(lParam);
            if (msg == WM_LBUTTONDOWN) self.HandleMouseDown(lParam);
            if (msg == WM_MOUSEMOVE) self.HandleMouseMove();
            if (msg == WM_LBUTTONUP) self.HandleMouseUp(lParam);
            // Windows can revoke mouse capture involuntarily mid-drag (Alt+Tab, another app
            // grabbing capture, a display change, ...) without ever delivering WM_LBUTTONUP.
            // Without this, _dragging would stay stuck true forever: HitTest would keep
            // unconditionally claiming the whole window, and the orb would keep following the
            // cursor whenever it later passed over that rectangle, with no in-app recovery short
            // of a restart. Capture is already gone by the time this arrives, so just clear the
            // flag - do not call ReleaseCapture (there is nothing to release).
            if (msg == WM_CAPTURECHANGED) self._dragging = false;
        }
        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // WM_NCHITTEST's lParam is the cursor position in SCREEN coordinates. The orb body itself
    // claims clicks too (not just the icons) so it can be picked up and dragged; while
    // _dragging, every point claims the click so a fast drag never slips back to click-through
    // mid-gesture.
    private nint HitTest(nint lParam)
    {
        if (_dragging) return HTCLIENT;
        if (!_hovering) return HTTRANSPARENT;
        (int screenX, int screenY) = DecodePoint(lParam);
        float localX = screenX - _windowX, localY = screenY - _windowY;
        float odx = localX - _orbBodyCx, ody = localY - _orbBodyCy;
        if (odx * odx + ody * ody <= _orbBodyR * _orbBodyR) return HTCLIENT;
        foreach (var (_, hx, hy, hr) in _iconHotspots)
        {
            float dx = localX - hx, dy = localY - hy;
            if (dx * dx + dy * dy <= hr * hr) return HTCLIENT;
        }
        return HTTRANSPARENT;
    }

    // WM_LBUTTONDOWN's lParam is client-area relative. Starting a drag requires the down-click
    // to land on the orb BODY specifically (not an icon, so icon clicks keep working normally).
    private void HandleMouseDown(nint lParam)
    {
        if (!_hovering) return;
        (int localX, int localY) = DecodePoint(lParam);
        float dx = localX - _orbBodyCx, dy = localY - _orbBodyCy;
        if (dx * dx + dy * dy > _orbBodyR * _orbBodyR) return;

        NativeMethods.GetCursorPos(out var cursor);
        _dragOffsetX = cursor.X - _windowX;
        _dragOffsetY = cursor.Y - _windowY;
        _dragging = true;
        NativeMethods.SetCapture(_hwnd);
    }

    // Reposition immediately via SetWindowPos (cheap - no re-render needed for a plain move) and
    // remember the new origin so the NEXT normal Render()/PushLayered call keeps using it instead
    // of snapping back to the bottom-center default.
    private void HandleMouseMove()
    {
        if (!_dragging) return;
        NativeMethods.GetCursorPos(out var cursor);
        int newX = cursor.X - _dragOffsetX;
        int newY = cursor.Y - _dragOffsetY;

        int w = (int)Math.Round(CanvasW * _scale);
        int h = (int)Math.Round(CanvasH * _scale);
        ClampToWorkArea(ref newX, ref newY, w, h);

        NativeMethods.SetWindowPos(_hwnd, 0, newX, newY, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        _windowX = newX; _windowY = newY;
        _hasCustomPosition = true;
        _customX = newX; _customY = newY;
    }

    // WM_LBUTTONUP's lParam is client-area relative, the same coordinate space as our drawing
    // (this window's client area IS the whole popup, no non-client borders). If this mouse-up
    // ends a drag, it is NOT also treated as an icon click (the down-click already landed on the
    // orb body, not an icon, so this is naturally already the case - the early return here is
    // just to skip the icon-hit-test loop, not to resolve an ambiguity).
    private void HandleMouseUp(nint lParam)
    {
        if (_dragging)
        {
            _dragging = false;
            NativeMethods.ReleaseCapture();
            return;
        }

        if (!_hovering) return;
        (int localX, int localY) = DecodePoint(lParam);
        foreach (var (icon, hx, hy, hr) in _iconHotspots)
        {
            float dx = localX - hx, dy = localY - hy;
            if (dx * dx + dy * dy > hr * hr) continue;
            if (icon == HoverIcon.Expand) MainAppRequested?.Invoke();
            else if (icon == HoverIcon.Dismiss) QuitRequested?.Invoke();
            return;
        }
    }

    private void ClampToWorkArea(ref int x, ref int y, int w, int h)
    {
        nint mon = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(mon, ref mi)) return;
        x = Math.Clamp(x, mi.rcWork.Left, Math.Max(mi.rcWork.Left, mi.rcWork.Right - w));
        y = Math.Clamp(y, mi.rcWork.Top, Math.Max(mi.rcWork.Top, mi.rcWork.Bottom - h));
    }

    private static (int X, int Y) DecodePoint(nint lParam)
    {
        long l = lParam.ToInt64();
        int x = unchecked((short)(l & 0xFFFF));
        int y = unchecked((short)((l >> 16) & 0xFFFF));
        return (x, y);
    }

    // ---- animation loop ----
    private void Frame()
    {
        if (_disposed) return;
        long now = Environment.TickCount64;
        double dt = Math.Clamp((now - _lastTicks) / 1000.0, 0, 0.1);
        _lastTicks = now;
        _phase += dt;

        // Re-assert HWND_TOPMOST roughly once a second. WS_EX_TOPMOST set at creation isn't
        // permanent -- other apps that also go topmost (or briefly cover the screen) can end up
        // above the orb and it stays there. Periodically re-inserting at HWND_TOPMOST (without
        // moving/resizing/activating it) keeps the pill reliably above everything.
        if (now - _lastTopmostReassert > 1000)
        {
            _lastTopmostReassert = now;
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        var state = _vm.State;
        bool isIdle = state == RecordingState.Idle;
        if (_prevState == RecordingState.Idle && !isIdle) _activeSeconds = 0;
        if (!isIdle) _activeSeconds += dt;
        _prevState = state;

        UpdateTypewriter(state, dt);

        NativeMethods.GetCursorPos(out var cursor);
        float localX = cursor.X - _windowX, localY = cursor.Y - _windowY;
        _hovering = isIdle && localX >= _hoverLeft && localX <= _hoverRight && localY >= _hoverTop && localY <= _hoverBottom;

        // Hovering at rest pulls the SAME pill->orb crossfade real dictation uses (reusing its
        // easing and DrawOrb rendering verbatim) - never the box, which stays gated on !isIdle
        // alone so hovering can never accidentally grow into the dictating box.
        double orbTarget = (!isIdle || _hovering) ? 1.0 : 0.0;
        double boxTarget = (!isIdle && _activeSeconds > WokenHoldSeconds) ? 1.0 : 0.0;
        _orbAmount = Approach(_orbAmount, orbTarget, dt / 0.12);
        _boxAmount = Approach(_boxAmount, boxTarget, dt / 0.12);

        var gTarget = ColorF.From(StateColor(state));
        _glow = ColorF.Lerp(_glow, gTarget, Math.Clamp(dt / 0.12, 0, 1));

        Render();

        bool settled = isIdle && !_hovering && _orbAmount < 0.001 && _glow.Near(gTarget);
        var want = TimeSpan.FromMilliseconds(settled ? 50 : 16);
        if (Math.Abs(_timer.Interval.TotalMilliseconds - want.TotalMilliseconds) > 1) _timer.Interval = want;
    }

    private Color StateColor(RecordingState s) => s switch
    {
        RecordingState.Listening  => CListening,
        RecordingState.Processing => CProcessing,
        RecordingState.Speaking   => CSpeaking,
        RecordingState.Waiting    => CWaiting,
        RecordingState.Done       => CDone,
        RecordingState.Error      => CError,
        _                         => IdleGlow(),
    };

    // At rest, honour the user's accent colour if it's bright enough to read as a glow.
    private Color IdleGlow()
    {
        double lum = (0.299 * _accent.R + 0.587 * _accent.G + 0.114 * _accent.B) / 255.0;
        return lum > 0.28 ? _accent : CIdle;
    }

    private void Render()
    {
        int w = (int)Math.Round(CanvasW * _scale);
        int h = (int)Math.Round(CanvasH * _scale);
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            double orbT = Smooth(_orbAmount);
            double boxT = Smooth(_boxAmount);
            float cx = w / 2f;
            float baseline = (float)(Baseline * _scale);

            float pillAlpha = (float)(1 - orbT);
            float orbAlpha = (float)(orbT * (1 - boxT));
            float boxAlpha = (float)boxT;

            if (pillAlpha > 0.001f) DrawPill(g, cx, baseline, pillAlpha);
            if (orbAlpha > 0.001f) DrawOrb(g, cx, baseline, orbAlpha);
            if (orbAlpha > 0.001f) DrawHoverMenu(g, cx, orbAlpha);
            if (boxAlpha > 0.001f) DrawBox(g, cx, baseline, boxAlpha);
        }
        PushLayered(bmp, w, h);
    }

    // ---- rest posture ----
    private void DrawPill(Graphics g, float cx, float baseline, float alpha)
    {
        double s = _scale;
        float w = (float)(PillW * s), h = (float)(PillH * s);
        float left = cx - w / 2f, top = baseline - h;
        double breath = 0.5 + 0.5 * Math.Sin(_phase * 1.6);

        // Very subtle glow — a faint light-green halo, much less than before.
        float glowR = (float)(w * 0.75 + (3 + 2 * breath) * s);
        DrawGlow(g, cx, top + h / 2f, glowR, Mul(PillFill, (float)(0.08 + 0.05 * breath) * alpha));

        using var path = RoundedRect(left, top, w, h, h / 2f);
        using var fill = new SolidBrush(Mul(PillFill, alpha));
        g.FillPath(fill, path);
        using var pen = new Pen(Mul(PillOutline, alpha), (float)(1.4 * s));
        g.DrawPath(pen, path);
    }

    // ---- hover-revealed quick-action menu (rings the woken orb once hover pulls it in) ----
    private void DrawHoverMenu(Graphics g, float cx, float alpha)
    {
        if (!_hovering) return;

        foreach (var (icon, hx, hy, hr) in _iconHotspots)
            DrawHoverIcon(g, icon, hx, hy, hr, alpha);

        // Tooltip sits above whatever reaches highest - the orb body or the flanking icons -
        // so it never overlaps either, wherever the layout puts them.
        float topMost = _orbBodyCy - _orbBodyR;
        foreach (var (_, _, hy, hr) in _iconHotspots) topMost = Math.Min(topMost, hy - hr);

        // Matches the reference: a light pill with dark text, ending in a small dark-green
        // badge holding the hotkey name (not the earlier dark-pill/white-text version).
        string prefix = "hold ", suffix = " to talk";
        using var font = MakeFont(11f, mono: true);
        var prefixSize = g.MeasureString(prefix, font);
        var suffixSize = g.MeasureString(suffix, font);
        var badgeTextSize = g.MeasureString(_hotkeyLabel, font);

        float padH = (float)(10 * _scale), padV = (float)(6 * _scale);
        float badgePadH = (float)(6 * _scale);
        float badgeGap = (float)(6 * _scale);
        float badgeW = badgeTextSize.Width + badgePadH * 2;
        float rowH = Math.Max(prefixSize.Height, badgeTextSize.Height);

        float tipW = padH + prefixSize.Width + badgeGap + badgeW + badgeGap + suffixSize.Width + padH;
        float tipH = rowH + padV * 2;
        float gap = (float)(10 * _scale);
        float tipY = topMost - gap - tipH;
        float tipX = cx - tipW / 2f;

        // Soft shadow beneath the tooltip so it lifts off the desktop instead of sitting flat.
        DrawGlow(g, cx, tipY + tipH * 0.75f, tipW * 0.62f, Mul(ChipShadow, 0.9f * alpha));

        using (var path = RoundedRect(tipX, tipY, tipW, tipH, tipH / 2f))
        using (var fill = new SolidBrush(Mul(TooltipBg, 0.97f * alpha)))
        using (var stroke = new Pen(Mul(Border1, 0.9f * alpha), (float)(1 * _scale)))
        {
            g.FillPath(fill, path);
            g.DrawPath(stroke, path);
        }

        float textY = tipY + padV;
        float x = tipX + padH;
        using (var tb = new SolidBrush(Mul(Fg1, alpha)))
            g.DrawString(prefix, font, tb, x, textY);
        x += prefixSize.Width + badgeGap;

        float badgeY = tipY + (tipH - rowH) / 2f - padV / 2f;
        using (var badgePath = RoundedRect(x, badgeY, badgeW, rowH + padV, (rowH + padV) / 2f))
        using (var badgeFill = new SolidBrush(Mul(FnBadgeBg, alpha)))
            g.FillPath(badgeFill, badgePath);
        using (var badgeText = new SolidBrush(Mul(Color.White, alpha)))
            g.DrawString(_hotkeyLabel, font, badgeText, x + badgePadH, textY + (padV / 2f));
        x += badgeW + badgeGap;

        using (var tb2 = new SolidBrush(Mul(Fg1, alpha)))
            g.DrawString(suffix, font, tb2, x, textY);
    }

    // Simple line-art glyphs (no image assets): pencil (edit), outward corner ticks (expand),
    // Two clearly-distinct quick actions: a green "open app" (diagonal expand arrows) and a muted
    // red "dismiss" (cross). Each is a warm-white chip on a soft drop-shadow with a tinted ring,
    // so they read as real floating buttons rather than two identical dots.
    private void DrawHoverIcon(Graphics g, HoverIcon icon, float cx, float cy, float r, float alpha)
    {
        (Color ring, Color glyph) = icon == HoverIcon.Expand
            ? (ExpandRing, ExpandGlyph)
            : (DismissRing, DismissGlyph);

        // Soft shadow: a faint dark halo offset slightly downward gives the chip depth.
        DrawGlow(g, cx, cy + (float)(1.5 * _scale), r * 1.55f, Mul(ChipShadow, alpha));

        FillCircle(g, cx, cy, r, Mul(ChipFill, 0.98f * alpha));
        using (var edge = new Pen(Mul(ring, alpha), (float)(1.5 * _scale)))
            g.DrawEllipse(edge, cx - r, cy - r, r * 2, r * 2);

        using var pen = new Pen(Mul(glyph, alpha), (float)(1.6 * _scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float u = r * 0.42f;
        switch (icon)
        {
            case HoverIcon.Expand:
                // Two diagonal arrows pointing apart (top-right & bottom-left) = "open / expand".
                DrawExpandArrow(g, pen, cx, cy, u, +1); // toward top-right
                DrawExpandArrow(g, pen, cx, cy, u, -1); // toward bottom-left
                break;
            case HoverIcon.Dismiss:
                g.DrawLine(pen, cx - u, cy - u, cx + u, cy + u);
                g.DrawLine(pen, cx - u, cy + u, cx + u, cy - u);
                break;
        }
    }

    // One arm of the expand glyph: a short diagonal shaft with a little arrowhead at its outer
    // tip. dir=+1 draws the top-right arm, dir=-1 the bottom-left arm.
    private static void DrawExpandArrow(Graphics g, Pen pen, float cx, float cy, float u, int dir)
    {
        // Outer tip and inner end of the shaft (top-right when dir=+1).
        float tx = cx + dir * u, ty = cy - dir * u;
        float ix = cx - dir * u * 0.55f, iy = cy + dir * u * 0.55f;
        g.DrawLine(pen, ix, iy, tx, ty);
        // Arrowhead: two short ticks back from the tip.
        float head = u * 0.7f;
        g.DrawLine(pen, tx, ty, tx - dir * head, ty);       // horizontal barb
        g.DrawLine(pen, tx, ty, tx, ty + dir * head);       // vertical barb
    }

    // ---- woken posture ----
    private void DrawOrb(Graphics g, float cx, float baseline, float alpha)
    {
        double s = _scale;
        float r = (float)(OrbDiameter * s / 2);
        float cy = baseline - r;
        double breath = 0.5 + 0.5 * Math.Sin(_phase * 2.4);

        Color gc = _glow.ToColor();
        float glowR = (float)(r + (18 + 8 * breath) * s);
        DrawGlow(g, cx, cy, glowR, Mul(gc, (0.32 + 0.30 * breath) * alpha));

        float satR = (float)(4 * s);
        float satX = (float)(r + SatelliteGap * s);
        FillCircle(g, cx - satX, cy, satR, Mul(Satellite, alpha));
        FillCircle(g, cx + satX, cy, satR, Mul(Satellite, alpha));

        FillCircle(g, cx, cy, r, Mul(OrbFill, alpha));
        using (var pen = new Pen(Mul(OrbRim, alpha), (float)(1.2 * s)))
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);

        DrawBird(g, cx, cy - (float)(1.5 * s), (float)(OrbDiameter * 0.74 * s), Mul(BirdDots, alpha));
    }

    // ---- dictating posture ----
    private void DrawBox(Graphics g, float cx, float baseline, float alpha)
    {
        var state = _vm.State;
        double s = _scale;

        string header = HeaderLabel(state);
        float desiredW = (float)(BoxW * s);

        // Grow the box's actual geometry from the orb's small footprint up to full size (not
        // just its opacity), so the transition reads as "the orb grows into the box" rather than
        // a full-size translucent card materializing above the still-solid orb.
        float growT = Math.Clamp(alpha, 0f, 1f);
        float startSize = (float)(OrbDiameter * s);
        float sc = 0.96f + 0.04f * alpha;
        float targetW = desiredW * sc;
        float targetH = (float)(BoxH * s) * sc;
        float targetRad = (float)(BoxRadius * s);

        float bw = Lerp(startSize, targetW, growT);
        float bh = Lerp(startSize, targetH, growT);
        float rad = Lerp(startSize / 2f, targetRad, growT);
        float left = cx - bw / 2f;
        float top = baseline - bh;

        for (int i = 3; i >= 1; i--)
        {
            float e = i * (float)(3 * s);
            using var sh = RoundedRect(left - e * 0.3f, top + e * 0.6f, bw + e * 0.6f, bh + e, rad + e);
            using var sb = new SolidBrush(Color.FromArgb((int)(12 * alpha), 20, 20, 20));
            g.FillPath(sb, sh);
        }

        using (var path = RoundedRect(left, top, bw, bh, rad))
        {
            using var fill = new SolidBrush(Mul(Paper2, alpha));
            g.FillPath(fill, path);
            using var edge = new Pen(Mul(Border1, alpha), (float)s);
            g.DrawPath(edge, path);
        }

        // Content (text/diff) only starts appearing once the box has mostly finished growing --
        // avoids rendering full-size text squeezed into a still-small box.
        float contentAlpha = Math.Clamp((growT - 0.6f) / 0.4f, 0f, 1f) * alpha;
        if (contentAlpha <= 0.001f) return;

        float padX = (float)(20 * s);
        float headerY = top + (float)(16 * s);

        using (var headerFont = MakeFont(11f, mono: true))
        {
            using var hb = new SolidBrush(Mul(Fg3, contentAlpha));
            g.DrawString(header, headerFont, hb, left + padX, headerY);

            using var chipFont = MakeFont(12f, mono: true);
            var chipSize = g.MeasureString(_languageLabel, chipFont);
            using var cb = new SolidBrush(Mul(Fg2, contentAlpha));
            g.DrawString(_languageLabel, chipFont, cb, left + bw - padX - chipSize.Width, headerY);
        }

        float bodyTop = headerY + (float)(22 * s);
        float bodyBottom = top + bh - (float)(12 * s) - (float)(18 * s);
        var bodyRect = new RectangleF(left + padX, bodyTop, bw - padX * 2, Math.Max(0, bodyBottom - bodyTop));

        var body = BodyText(state);
        if (body.Length > 0)
        {
            bool placeholder = state == RecordingState.Listening && string.IsNullOrEmpty(_vm.PartialTranscript);
            using var bodyFont = MakeFont(15f);
            using var bb = new SolidBrush(Mul(placeholder ? Fg3 : Fg1, contentAlpha));

            // While the live transcript is growing, keep the NEWEST words in view: draw a
            // single line pinned to the right edge of the body box so the sentence scrolls
            // leftward as more is spoken (older text runs off the left), instead of the whole
            // line getting ellipsis-truncated and freezing. Every other state is short static
            // copy that fits, so it's drawn normally, left-aligned.
            bool liveTranscript = state == RecordingState.Listening && !placeholder;
            if (liveTranscript)
            {
                using var noWrap = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.None };
                var textSize = g.MeasureString(body, bodyFont, PointF.Empty, noWrap);
                // Pin the right end of the text to the right edge of the body box; negative x
                // pushes the already-typed start off the left. GDI+ clips to bodyRect.
                float drawX = bodyRect.Right - textSize.Width;
                if (drawX > bodyRect.Left) drawX = bodyRect.Left; // short enough to fit -> left-align
                var saved = g.Save(); // clip to the body box so scrolled-off text doesn't spill outside
                g.SetClip(bodyRect);
                g.DrawString(body, bodyFont, bb, drawX, bodyRect.Top, noWrap);
                g.Restore(saved);
            }
            else
            {
                using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(body, bodyFont, bb, bodyRect, fmt);
            }
        }

        var footer = FooterText(state);
        if (footer.Length > 0)
        {
            using var footerFont = MakeFont(12f, mono: true);
            using var fb = new SolidBrush(Mul(Fg2, contentAlpha));
            g.DrawString(footer, footerFont, fb, left + padX, top + bh - (float)(12 * s) - (float)(14 * s));
        }
    }

    private string HeaderLabel(RecordingState s) => s switch
    {
        RecordingState.Listening  => "LIVE",
        RecordingState.Processing => "POLISHING",
        RecordingState.Speaking   => "INSERTING",
        RecordingState.Waiting    => "COOLING DOWN",
        RecordingState.Done       => "DONE",
        RecordingState.Error      => "ERROR",
        _                         => "KIVI",
    };

    // Advances the character-by-character reveal of the live transcript. Called every frame.
    private void UpdateTypewriter(RecordingState state, double dt)
    {
        if (state != RecordingState.Listening)
        {
            // Not dictating: reset so the next capture types from scratch.
            _typeTarget = "";
            _typeRevealed = 0;
            return;
        }

        var target = _vm.PartialTranscript ?? "";
        if (!ReferenceEquals(target, _typeTarget) && target != _typeTarget)
        {
            // A new partial arrived. Sarvam re-transcribes the whole clip and can revise earlier
            // words, so keep the reveal position only up to where old and new still agree; if the
            // revision changed text we'd already revealed, back the cursor up to the divergence
            // point and re-type forward from there.
            int commonPrefix = 0;
            int max = Math.Min(target.Length, _typeTarget.Length);
            while (commonPrefix < max && target[commonPrefix] == _typeTarget[commonPrefix]) commonPrefix++;
            if (_typeRevealed > commonPrefix) _typeRevealed = commonPrefix;
            _typeTarget = target;
        }

        if (_typeRevealed < _typeTarget.Length)
        {
            _typeRevealed = Math.Min(_typeTarget.Length, _typeRevealed + CharsPerSecond * dt);
        }
    }

    // The portion of the live transcript revealed so far by the typewriter animation.
    private string RevealedTranscript()
    {
        int n = Math.Clamp((int)_typeRevealed, 0, _typeTarget.Length);
        return _typeTarget[..n];
    }

    private string BodyText(RecordingState s) => s switch
    {
        RecordingState.Listening      => string.IsNullOrEmpty(_vm.PartialTranscript)
            ? "Press a dictation key and speak — finished text appears here, in your style…"
            : RevealedTranscript(),
        RecordingState.Processing     => "Cleaning up your text…",
        RecordingState.Speaking       => "Pasting…",
        RecordingState.Waiting        => "Rate limited — retrying shortly…",
        RecordingState.Error          => _vm.LastErrorMessage ?? "Couldn't catch that.",
        _                              => "",
    };

    private string FooterText(RecordingState s) => s switch
    {
        RecordingState.Listening     => "release to stop",
        _                             => "",
    };

    private void DrawBird(Graphics g, float cx, float cy, float boxH, Color color)
    {
        if (_mask is null || _maskW == 0) return;
        const int cols = 14;
        float aspect = (float)_maskW / _maskH;
        float boxW = boxH * aspect;
        int rows = Math.Max(1, (int)Math.Round(boxH / boxW * cols));
        float cellW = boxW / cols, cellH = boxH / rows;
        float dot = Math.Min(cellW, cellH) * 0.82f;
        float left = cx - boxW / 2f, top = cy - boxH / 2f;
        using var brush = new SolidBrush(color);
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                int px = (int)((col + 0.5) / cols * _maskW);
                int py = (int)((row + 0.5) / rows * _maskH);
                int off = py * _maskStride + px * 4;
                byte a = (off + 3 < _mask.Length) ? _mask[off + 3] : (byte)0;
                if (a < 40) continue;
                g.FillEllipse(brush, left + col * cellW + (cellW - dot) / 2f, top + row * cellH + (cellH - dot) / 2f, dot, dot);
            }
    }

    // ---- primitives ----
    private static void DrawGlow(Graphics g, float cx, float cy, float radius, Color center)
    {
        if (radius <= 0 || center.A == 0) return;
        using var path = new GraphicsPath();
        path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(cx, cy),
            CenterColor = center,
            SurroundColors = new[] { Color.FromArgb(0, center) },
        };
        g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
    }

    private static void FillCircle(Graphics g, float cx, float cy, float r, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2f);
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private Font MakeFont(float px, bool mono = false)
    {
        float size = px * (float)_scale;
        var family = mono ? _monoFamily : _interFamily;
        try { if (family != null) return new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel); }
        catch { }
        return new Font(mono ? "Consolas" : "Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private static Color Mul(Color c, double a) => Color.FromArgb((int)Math.Clamp(c.A * a, 0, 255), c.R, c.G, c.B);
    private static double Approach(double v, double t, double step) => v < t ? Math.Min(t, v + step) : Math.Max(t, v - step);
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private readonly struct ColorF
    {
        public readonly double R, G, B;
        private ColorF(double r, double g, double b) { R = r; G = g; B = b; }
        public static ColorF From(Color c) => new(c.R, c.G, c.B);
        public static ColorF Lerp(ColorF a, ColorF b, double t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        public Color ToColor() => Color.FromArgb(255, (int)Math.Clamp(R, 0, 255), (int)Math.Clamp(G, 0, 255), (int)Math.Clamp(B, 0, 255));
        public bool Near(ColorF o) => Math.Abs(R - o.R) + Math.Abs(G - o.G) + Math.Abs(B - o.B) < 3;
    }

    // ---- infra ----
    private void LoadMask()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "kivi-mask.png");
            using var src = new Bitmap(path);
            _maskW = src.Width; _maskH = src.Height;
            var data = src.LockBits(new Rectangle(0, 0, _maskW, _maskH), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            _mask = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, _mask, 0, _mask.Length);
            _maskStride = data.Stride;
            src.UnlockBits(data);
        }
        catch { _mask = null; }
    }

    private void LoadFonts()
    {
        try
        {
            _fonts = new PrivateFontCollection();
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            foreach (var f in new[] { "Inter-Medium.ttf", "Inter-Regular.ttf", "SpaceMono-Regular.ttf" })
            {
                var p = System.IO.Path.Combine(dir, f);
                if (System.IO.File.Exists(p)) _fonts.AddFontFile(p);
            }
            foreach (var fam in _fonts.Families)
            {
                if (fam.Name.Contains("Mono", StringComparison.OrdinalIgnoreCase)) _monoFamily = fam;
                else _interFamily ??= fam;
            }
        }
        catch { _interFamily = null; _monoFamily = null; }
    }

    private static void EnsureClassRegistered()
    {
        if (_classAtom != 0) return;
        _wndProcKeepAlive = WndProcCallback;
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _wndProcKeepAlive,
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = ClassName,
        };
        _classAtom = NativeMethods.RegisterClassExW(ref wc);
    }

    private void PushLayered(Bitmap bmp, int w, int h)
    {
        int x, y;
        if (_hasCustomPosition)
        {
            // The orb was dragged at least once -- keep it there instead of snapping back to
            // the bottom-center default on every subsequent render.
            x = _customX; y = _customY;
        }
        else
        {
            nint mon = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfoW(mon, ref mi))
            {
                x = mi.rcWork.Left + ((mi.rcWork.Right - mi.rcWork.Left) - w) / 2;
                y = mi.rcWork.Bottom - h - (int)Math.Round(14 * _scale);
            }
            else { x = 0; y = 0; }
        }
        _windowX = x; _windowY = y;

        nint screenDC = NativeMethods.GetDC(0);
        nint memDC = NativeMethods.CreateCompatibleDC(screenDC);
        nint hbm = bmp.GetHbitmap(Color.FromArgb(0));
        nint old = NativeMethods.SelectObject(memDC, hbm);
        try
        {
            var ptDst = new NativeMethods.POINT(x, y);
            var size = new NativeMethods.SIZE(w, h);
            var ptSrc = new NativeMethods.POINT(0, 0);
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA,
            };
            NativeMethods.UpdateLayeredWindow(_hwnd, screenDC, ref ptDst, ref size, memDC, ref ptSrc, 0, ref blend, NativeMethods.ULW_ALPHA);
        }
        finally
        {
            NativeMethods.SelectObject(memDC, old);
            NativeMethods.DeleteObject(hbm);
            NativeMethods.DeleteDC(memDC);
            NativeMethods.ReleaseDC(0, screenDC);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _fonts?.Dispose();
        if (_current == this) _current = null;
        if (_hwnd != 0) NativeMethods.DestroyWindow(_hwnd);
    }
}
