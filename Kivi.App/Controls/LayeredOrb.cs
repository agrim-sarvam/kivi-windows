using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Kivi.App.Interop;
using Kivi.Core.Orchestration;
using Microsoft.UI.Dispatching;

namespace Kivi.App.Controls;

/// <summary>
/// The persistent desktop overlay, drawn as a genuinely transparent, click-through, always-on-top
/// Win32 <b>layered window</b> (UpdateLayeredWindow + a premultiplied-ARGB GDI+ bitmap) - WinUI 3
/// composites its own windows opaquely, so it can't float a soft-glowing free-form shape.
///
/// One object, growing from a fixed bottom-center anchor (brand "kivi on the desktop"):
///  - <b>Idle</b>  -> the woken orb: a forest-green disc with the dot-matrix kiwi + satellite dots.
///  - <b>dictating / processing / …</b> -> the disc grows into a white transcript box with a live
///    waveform (listening) or a working indicator, plus a coloured state chip.
///
/// The state signal is the accent/glow colour and the chip; every state has a distinct fixed
/// colour so transitions read clearly. Everything is animated on a 60fps-capable render loop with
/// short easings so it feels instant and snappy.
/// </summary>
public sealed class LayeredOrb : IDisposable
{
    private const string ClassName = "KiviOrbLayered";
    private static NativeMethods.WndProc? _wndProcKeepAlive;
    private static ushort _classAtom;

    // Design sizes in effective (96-dpi) px; scaled by the monitor DPI when drawn.
    private const double CanvasW = 352, CanvasH = 156;
    private const double Baseline = CanvasH - 30;       // shared bottom edge; postures grow upward
    private const double OrbDiameter = 62;
    private const double BoxW = 300, BoxH = 76, BoxRadius = 18;

    private static readonly Color Forest    = Color.FromArgb(255, 0x1E, 0x3D, 0x18);
    private static readonly Color Rim        = Color.FromArgb(255, 0x37, 0x63, 0x30);
    private static readonly Color BirdDots   = Color.FromArgb(255, 0xCF, 0xE0, 0xB0);
    private static readonly Color Satellite  = Color.FromArgb(235, 0xFF, 0xFF, 0xFF);
    private static readonly Color BoxFill     = Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
    private static readonly Color TextPrimary = Color.FromArgb(255, 0x14, 0x18, 0x0E);
    private static readonly Color TextMuted   = Color.FromArgb(255, 0x5C, 0x64, 0x54);

    // Fixed, distinct per-state colours (foundation palette) so transitions are unmistakable.
    private static readonly Color CIdle       = Color.FromArgb(0x6E, 0xA3, 0x35);
    private static readonly Color CListening  = Color.FromArgb(0xE9, 0x6C, 0x2F);
    private static readonly Color CProcessing = Color.FromArgb(0x42, 0x50, 0xD5);
    private static readonly Color CSpeaking   = Color.FromArgb(0x4B, 0x7D, 0x28);
    private static readonly Color CWaiting    = Color.FromArgb(0xD2, 0x96, 0x2D);
    private static readonly Color CDone       = Color.FromArgb(0x6E, 0xA3, 0x35);
    private static readonly Color CError      = Color.FromArgb(0xB8, 0x15, 0x14);

    private readonly nint _hwnd;
    private readonly Color _accent;
    private readonly DispatcherQueueTimer _timer;
    private readonly double _scale;

    private byte[]? _mask;
    private int _maskW, _maskH, _maskStride;
    private PrivateFontCollection? _fonts;
    private FontFamily? _family;

    private RecordingState _state = RecordingState.Idle;
    private double _boxAnim;                 // 0 = orb, 1 = box (eased)
    private ColorF _glow;                    // current glow colour (lerped)
    private long _lastTicks;
    private double _phase;                   // seconds, drives breathing + waveform
    private bool _disposed;

    public LayeredOrb(Color accent)
    {
        _accent = accent;
        _glow = ColorF.From(CIdle);
        EnsureClassRegistered();

        _hwnd = NativeMethods.CreateWindowExW(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE,
            ClassName, "kivi", NativeMethods.WS_POPUP,
            0, 0, 10, 10, 0, 0, NativeMethods.GetModuleHandleW(null), 0);

        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        _scale = dpi == 0 ? 1.0 : dpi / 96.0;

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

    public void SetState(RecordingState state)
    {
        _state = state;
        // Kick the loop to full speed so the very next frame reflects the change instantly.
        if (!_disposed) _timer.Interval = TimeSpan.FromMilliseconds(16);
    }

    // ---- animation loop ----
    private void Frame()
    {
        if (_disposed) return;
        long now = Environment.TickCount64;
        double dt = Math.Clamp((now - _lastTicks) / 1000.0, 0, 0.1);
        _lastTicks = now;
        _phase += dt;

        // Snappy easing: reach the target in ~120ms.
        double target = _state == RecordingState.Idle ? 0.0 : 1.0;
        _boxAnim = Approach(_boxAnim, target, dt / 0.12);

        // Crossfade the glow colour toward the current state colour (~120ms).
        var gTarget = ColorF.From(StateColor(_state));
        _glow = ColorF.Lerp(_glow, gTarget, Math.Clamp(dt / 0.12, 0, 1));

        Render();

        // Throttle to a gentle breathing rate once fully settled at idle (saves battery).
        bool settled = _state == RecordingState.Idle && _boxAnim < 0.001 && _glow.Near(gTarget);
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

            double t = Smooth(_boxAnim);
            float cx = w / 2f;
            float baseline = (float)(Baseline * _scale);

            if (t < 0.999) DrawOrb(g, cx, baseline, (float)(1 - t));
            if (t > 0.001) DrawBox(g, cx, baseline, (float)t);
        }
        PushLayered(bmp, w, h);
    }

    // ---- orb posture ----
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
        float satX = (float)(r + 9 * s);
        FillCircle(g, cx - satX, cy, satR, Mul(Satellite, alpha));
        FillCircle(g, cx + satX, cy, satR, Mul(Satellite, alpha));

        FillCircle(g, cx, cy, r, Mul(Forest, alpha));
        using (var pen = new Pen(Mul(Rim, alpha), (float)(1.2 * s)))
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);

        DrawBird(g, cx, cy - (float)(1.5 * s), (float)(OrbDiameter * 0.74 * s), Mul(BirdDots, alpha));
    }

    // ---- transcript-box postures ----
    private void DrawBox(Graphics g, float cx, float baseline, float alpha)
    {
        double s = _scale;
        float bw = (float)(BoxW * s);
        float bh = (float)(BoxH * s);
        float rad = (float)(BoxRadius * s);
        // Pop-in: scale from 96% -> 100% as it appears.
        float sc = 0.96f + 0.04f * alpha;
        bw *= sc; bh *= sc;
        float left = cx - bw / 2f;
        float top = baseline - bh;

        Color state = _glow.ToColor();

        // soft drop shadow
        for (int i = 3; i >= 1; i--)
        {
            float e = i * (float)(3 * s);
            using var sh = RoundedRect(left - e * 0.3f, top + e * 0.6f, bw + e * 0.6f, bh + e, rad + e);
            using var sb = new SolidBrush(Color.FromArgb((int)(10 * alpha), 0, 0, 0));
            g.FillPath(sb, sh);
        }

        using (var path = RoundedRect(left, top, bw, bh, rad))
        {
            using var fill = new SolidBrush(Mul(BoxFill, alpha));
            g.FillPath(fill, path);
            using var edge = new Pen(Color.FromArgb((int)(18 * alpha), 0, 0, 0), (float)s);
            g.DrawPath(edge, path);
        }

        float padX = (float)(18 * s);
        float contentY = top + bh * 0.40f;
        DrawStateContent(g, cx, contentY, state, alpha);

        // state chip, bottom-left
        DrawChip(g, left + padX, top + bh - (float)(16 * s), ChipLabel(_state), state, alpha);
    }

    private void DrawStateContent(Graphics g, float cx, float cy, Color color, float alpha)
    {
        switch (_state)
        {
            case RecordingState.Listening:
                DrawWaveform(g, cx, cy, color, alpha);
                break;
            case RecordingState.Done:
                DrawCheck(g, cx, cy, color, alpha);
                break;
            case RecordingState.Error:
                DrawBang(g, cx, cy, color, alpha);
                break;
            default: // Processing / Speaking / Waiting -> working dots
                DrawDots(g, cx, cy, color, alpha);
                break;
        }
    }

    private void DrawWaveform(Graphics g, float cx, float cy, Color color, float alpha)
    {
        double s = _scale;
        const int n = 15;
        float bw = (float)(3.5 * s);
        float gap = (float)(4 * s);
        float total = n * bw + (n - 1) * gap;
        float x = cx - total / 2f;
        float maxH = (float)(26 * s);
        using var brush = new SolidBrush(Mul(color, alpha));
        for (int i = 0; i < n; i++)
        {
            double env = 0.35 + 0.65 * Math.Abs(Math.Sin(i * 0.7 + 1.3));       // static shape
            double mod = 0.5 + 0.5 * Math.Sin(_phase * 9 + i * 0.9);            // animation
            float hh = (float)(maxH * (0.25 + 0.75 * env * mod));
            var rect = new RectangleF(x, cy - hh / 2f, bw, hh);
            using var p = RoundedRect(rect.X, rect.Y, rect.Width, rect.Height, bw / 2f);
            g.FillPath(brush, p);
            x += bw + gap;
        }
    }

    private void DrawDots(Graphics g, float cx, float cy, Color color, float alpha)
    {
        double s = _scale;
        float r = (float)(4 * s);
        float gap = (float)(16 * s);
        for (int i = -1; i <= 1; i++)
        {
            double pulse = 0.4 + 0.6 * (0.5 + 0.5 * Math.Sin(_phase * 6 - i * 0.9));
            FillCircle(g, cx + i * gap, cy, r, Mul(color, (float)(alpha * pulse)));
        }
    }

    private void DrawCheck(Graphics g, float cx, float cy, Color color, float alpha)
    {
        double s = _scale;
        using var pen = new Pen(Mul(color, alpha), (float)(3 * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float u = (float)(6 * s);
        g.DrawLines(pen, new[] { new PointF(cx - 2 * u, cy), new PointF(cx - 0.4f * u, cy + 1.5f * u), new PointF(cx + 2.2f * u, cy - 1.8f * u) });
    }

    private void DrawBang(Graphics g, float cx, float cy, Color color, float alpha)
    {
        double s = _scale;
        using var b = new SolidBrush(Mul(color, alpha));
        float w = (float)(3.5 * s);
        g.FillPath(b, RoundedRect(cx - w / 2, cy - (float)(11 * s), w, (float)(14 * s), w / 2));
        FillCircle(g, cx, cy + (float)(8 * s), w / 1.6f, Mul(color, alpha));
    }

    private void DrawChip(Graphics g, float x, float baselineY, string label, Color color, float alpha)
    {
        double s = _scale;
        var font = MakeFont(11.5f);
        var size = g.MeasureString(label, font);
        float dot = (float)(6 * s);
        float padH = (float)(9 * s);
        float chipH = (float)(21 * s);
        float chipW = padH + dot + (float)(6 * s) + size.Width + padH;
        float y = baselineY - chipH / 2f;
        using (var bg = new SolidBrush(Mul(color, alpha * 0.14f)))
        using (var path = RoundedRect(x, y, chipW, chipH, chipH / 2f))
            g.FillPath(bg, path);
        FillCircle(g, x + padH + dot / 2f, y + chipH / 2f, dot / 2f, Mul(color, alpha));
        using var tb = new SolidBrush(Mul(Darken(color, 0.82f), alpha));
        g.DrawString(label, font, tb, x + padH + dot + (float)(6 * s), y + (chipH - size.Height) / 2f);
        font.Dispose();
    }

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

    private string ChipLabel(RecordingState s) => s switch
    {
        RecordingState.Listening  => "listening",
        RecordingState.Processing => "polishing",
        RecordingState.Speaking   => "inserting",
        RecordingState.Waiting    => "cooling down",
        RecordingState.Done       => "done",
        RecordingState.Error      => "couldn't catch that",
        _                         => "kivi",
    };

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

    private Font MakeFont(float px)
    {
        float size = px * (float)_scale;
        try { if (_family != null) return new Font(_family, size, FontStyle.Regular, GraphicsUnit.Pixel); }
        catch { }
        return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private static Color Mul(Color c, double a) => Color.FromArgb((int)Math.Clamp(c.A * a, 0, 255), c.R, c.G, c.B);
    private static Color Darken(Color c, float f) => Color.FromArgb(c.A, (int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
    private static double Approach(double v, double t, double step) => v < t ? Math.Min(t, v + step) : Math.Max(t, v - step);
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

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
            foreach (var f in new[] { "Inter-Medium.ttf", "Inter-Regular.ttf" })
            {
                var p = System.IO.Path.Combine(dir, f);
                if (System.IO.File.Exists(p)) _fonts.AddFontFile(p);
            }
            if (_fonts.Families.Length > 0) _family = _fonts.Families[0];
        }
        catch { _family = null; }
    }

    private static void EnsureClassRegistered()
    {
        if (_classAtom != 0) return;
        _wndProcKeepAlive = NativeMethods.DefWindowProcW;
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
        nint mon = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        int x, y;
        if (NativeMethods.GetMonitorInfoW(mon, ref mi))
        {
            x = mi.rcWork.Left + ((mi.rcWork.Right - mi.rcWork.Left) - w) / 2;
            y = mi.rcWork.Bottom - h - (int)Math.Round(14 * _scale);
        }
        else { x = 0; y = 0; }

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
        if (_hwnd != 0) NativeMethods.DestroyWindow(_hwnd);
    }
}
