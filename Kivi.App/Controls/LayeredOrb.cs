using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Kivi.App.Interop;
using Kivi.App.ViewModels;
using Kivi.Core.Orchestration;
using Kivi.Core.Text;
using Microsoft.UI.Dispatching;

namespace Kivi.App.Controls;

/// <summary>
/// The persistent desktop overlay, drawn as a genuinely transparent, click-through, always-on-top
/// Win32 <b>layered window</b> (UpdateLayeredWindow + a premultiplied-ARGB GDI+ bitmap) - WinUI 3
/// composites its own windows opaquely, so it can't float a soft-glowing free-form shape.
///
/// Four postures, growing from a fixed bottom-center anchor (brand "kivi on the desktop"):
///  - <b>Rest</b>  -> a small breathing pill.
///  - <b>Woken</b> -> a brief transitional round orb (dot-matrix kiwi + satellites) shown right
///    after leaving rest, before the box appears.
///  - <b>Dictating</b> -> a text-layout box (header/body/footer) for Listening and every
///    subsequent pipeline state.
///  - <b>Hey kivi</b> -> the same box, wider, rendering a word diff instead of plain body text,
///    while awaiting an accept (Enter) or reject (Esc).
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

    // Design sizes in effective (96-dpi) px; scaled by the monitor DPI when drawn.
    private const double CanvasW = 520, CanvasH = 170;
    private const double Baseline = CanvasH - 30;       // shared bottom edge; postures grow upward
    private const double PillW = 39, PillH = 15;
    private const double OrbDiameter = 61;
    private const double SatelliteGap = 23;             // from the orb's edge
    private const double BoxW = 322, BoxH = 108, BoxRadius = 20;
    private const double BoxMaxWidthHeyKivi = 480;
    private const double WokenHoldSeconds = 0.25;        // how long the woken orb holds before growing into a box

    private static readonly Color Forest     = Color.FromArgb(255, 0x18, 0x30, 0x0F); // --brand-orbforest
    private static readonly Color Rim        = Color.FromArgb(255, 0x37, 0x63, 0x30);
    private static readonly Color BirdDots   = Color.FromArgb(255, 0xCF, 0xE0, 0xB0);
    private static readonly Color Satellite  = Color.FromArgb(235, 0xFF, 0xFF, 0xFF);
    private static readonly Color Paper2     = Color.FromArgb(255, 0xFF, 0xFF, 0xFF); // --color-paper2
    private static readonly Color Border1    = Color.FromArgb(255, 0xED, 0xF0, 0xE6); // --color-border1
    private static readonly Color Fg1        = Color.FromArgb(255, 0x14, 0x18, 0x0E); // --color-fg1
    private static readonly Color Fg2        = Color.FromArgb(255, 0x5C, 0x64, 0x54); // --color-fg2
    private static readonly Color Fg3        = Color.FromArgb(255, 0x92, 0x9A, 0x8A); // --color-fg3
    private static readonly Color Positive   = Color.FromArgb(255, 0x6E, 0xA3, 0x35); // --color-positive
    private static readonly Color PositiveBg = Color.FromArgb(255, 0xF2, 0xF8, 0xEB); // --color-positivebg

    // Fixed, distinct per-state colours (foundation palette) so transitions are unmistakable.
    private static readonly Color CIdle       = Color.FromArgb(0x6E, 0xA3, 0x35);
    private static readonly Color CListening  = Color.FromArgb(0xE9, 0x6C, 0x2F);
    private static readonly Color CProcessing = Color.FromArgb(0x42, 0x50, 0xD5);
    private static readonly Color CSpeaking   = Color.FromArgb(0x4B, 0x7D, 0x28);
    private static readonly Color CWaiting    = Color.FromArgb(0xD2, 0x96, 0x2D);
    private static readonly Color CDone       = Color.FromArgb(0x6E, 0xA3, 0x35);
    private static readonly Color CError      = Color.FromArgb(0xB8, 0x15, 0x14);

    private readonly nint _hwnd;
    private readonly OverlayViewModel _vm;
    private readonly Color _accent;
    private readonly string _languageLabel;
    private readonly DispatcherQueueTimer _timer;
    private readonly double _scale;

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
    private double _phase;                   // seconds, drives breathing + waveform
    private bool _disposed;

    public LayeredOrb(OverlayViewModel vm, Color accent, string languageLabel)
    {
        _vm = vm;
        _accent = accent;
        _languageLabel = languageLabel;
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

    // ---- animation loop ----
    private void Frame()
    {
        if (_disposed) return;
        long now = Environment.TickCount64;
        double dt = Math.Clamp((now - _lastTicks) / 1000.0, 0, 0.1);
        _lastTicks = now;
        _phase += dt;

        var state = _vm.State;
        bool isIdle = state == RecordingState.Idle;
        if (_prevState == RecordingState.Idle && !isIdle) _activeSeconds = 0;
        if (!isIdle) _activeSeconds += dt;
        _prevState = state;

        double orbTarget = isIdle ? 0.0 : 1.0;
        double boxTarget = (!isIdle && _activeSeconds > WokenHoldSeconds) ? 1.0 : 0.0;
        _orbAmount = Approach(_orbAmount, orbTarget, dt / 0.12);
        _boxAmount = Approach(_boxAmount, boxTarget, dt / 0.12);

        var gTarget = ColorF.From(StateColor(state));
        _glow = ColorF.Lerp(_glow, gTarget, Math.Clamp(dt / 0.12, 0, 1));

        Render();

        bool settled = isIdle && _orbAmount < 0.001 && _glow.Near(gTarget);
        var want = TimeSpan.FromMilliseconds(settled ? 50 : 16);
        if (Math.Abs(_timer.Interval.TotalMilliseconds - want.TotalMilliseconds) > 1) _timer.Interval = want;
    }

    private Color StateColor(RecordingState s) => s switch
    {
        RecordingState.Listening  => _vm.IsRewriteCapture ? CProcessing : CListening,
        RecordingState.Processing => CProcessing,
        RecordingState.Speaking   => CSpeaking,
        RecordingState.Waiting    => CWaiting,
        RecordingState.Done       => CDone,
        RecordingState.Error      => CError,
        RecordingState.RewritePending => CProcessing,
        RecordingState.RewriteReview  => CProcessing,
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

        Color gc = _glow.ToColor();
        float glowR = (float)(w * 0.9 + (6 + 4 * breath) * s);
        DrawGlow(g, cx, top + h / 2f, glowR, Mul(gc, (float)(0.22 + 0.16 * breath) * alpha));

        using var path = RoundedRect(left, top, w, h, h / 2f);
        using var fill = new SolidBrush(Mul(Forest, alpha));
        g.FillPath(fill, path);
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

        FillCircle(g, cx, cy, r, Mul(Forest, alpha));
        using (var pen = new Pen(Mul(Rim, alpha), (float)(1.2 * s)))
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);

        DrawBird(g, cx, cy - (float)(1.5 * s), (float)(OrbDiameter * 0.74 * s), Mul(BirdDots, alpha));
    }

    // ---- dictating / hey-kivi posture ----
    private void DrawBox(Graphics g, float cx, float baseline, float alpha)
    {
        var state = _vm.State;
        bool isHeyKivi = _vm.IsRewriteCapture || state is RecordingState.RewritePending or RecordingState.RewriteReview;
        double s = _scale;

        string header = HeaderLabel(state);
        float desiredW = (float)(BoxW * s);
        if (isHeyKivi)
        {
            using var headerFont = MakeFont(11f, mono: true);
            var headerSize = g.MeasureString(header, headerFont);
            float contentW = headerSize.Width + (float)(40 * s);
            desiredW = Math.Max(desiredW, Math.Min(contentW, (float)(BoxMaxWidthHeyKivi * s)));
        }

        float bh = (float)(BoxH * s);
        float rad = (float)(BoxRadius * s);
        float sc = 0.96f + 0.04f * alpha;
        float bw = desiredW * sc; bh *= sc;
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

        float padX = (float)(20 * s);
        float headerY = top + (float)(16 * s);

        using (var headerFont = MakeFont(11f, mono: true))
        {
            var headerColor = isHeyKivi ? CProcessing : Fg3;
            using var hb = new SolidBrush(Mul(headerColor, alpha));
            g.DrawString(header, headerFont, hb, left + padX, headerY);

            if (!isHeyKivi)
            {
                using var chipFont = MakeFont(12f, mono: true);
                var chipSize = g.MeasureString(_languageLabel, chipFont);
                using var cb = new SolidBrush(Mul(Fg2, alpha));
                g.DrawString(_languageLabel, chipFont, cb, left + bw - padX - chipSize.Width, headerY);
            }
        }

        float bodyTop = headerY + (float)(22 * s);
        float bodyBottom = top + bh - (float)(12 * s) - (float)(18 * s);
        var bodyRect = new RectangleF(left + padX, bodyTop, bw - padX * 2, Math.Max(0, bodyBottom - bodyTop));

        if (state == RecordingState.RewriteReview && _vm.Diff is { Count: > 0 } diff)
        {
            DrawDiffText(g, bodyRect, diff, alpha);
        }
        else
        {
            var body = BodyText(state);
            if (body.Length > 0)
            {
                bool placeholder = state == RecordingState.Listening && !_vm.IsRewriteCapture && string.IsNullOrEmpty(_vm.PartialTranscript);
                using var bodyFont = MakeFont(15f);
                using var bb = new SolidBrush(Mul(placeholder ? Fg3 : Fg1, alpha));
                using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(body, bodyFont, bb, bodyRect, fmt);
            }
        }

        var footer = FooterText(state);
        if (footer.Length > 0)
        {
            using var footerFont = MakeFont(12f, mono: true);
            using var fb = new SolidBrush(Mul(Fg2, alpha));
            g.DrawString(footer, footerFont, fb, left + padX, top + bh - (float)(12 * s) - (float)(14 * s));
        }
    }

    private void DrawDiffText(Graphics g, RectangleF bounds, IReadOnlyList<DiffToken> diff, float alpha)
    {
        using var font = MakeFont(15f);
        float lineHeight = (float)(15 * 1.65 * _scale);
        float x = bounds.Left, y = bounds.Top;

        foreach (var token in diff)
        {
            if (token.Text.Length == 0) continue;
            if (string.IsNullOrWhiteSpace(token.Text))
            {
                if (token.Text.Contains('\n')) { x = bounds.Left; y += lineHeight; }
                else x += g.MeasureString(token.Text, font).Width;
                continue;
            }

            var size = g.MeasureString(token.Text, font);
            if (x + size.Width > bounds.Right) { x = bounds.Left; y += lineHeight; }
            if (y + lineHeight > bounds.Bottom) break; // clip silently past the box's visible height

            switch (token.Op)
            {
                case DiffOp.Insert:
                    using (var bg = new SolidBrush(Mul(PositiveBg, alpha)))
                        g.FillRectangle(bg, x, y, size.Width, lineHeight * 0.82f);
                    using (var fg = new SolidBrush(Mul(Positive, alpha)))
                        g.DrawString(token.Text, font, fg, x, y);
                    break;
                case DiffOp.Delete:
                    using (var fg = new SolidBrush(Mul(Fg2, alpha)))
                        g.DrawString(token.Text, font, fg, x, y);
                    using (var pen = new Pen(Mul(Fg2, alpha), (float)(1 * _scale)))
                        g.DrawLine(pen, x, y + lineHeight * 0.5f, x + size.Width, y + lineHeight * 0.5f);
                    break;
                default:
                    using (var fg = new SolidBrush(Mul(Fg1, alpha)))
                        g.DrawString(token.Text, font, fg, x, y);
                    break;
            }
            x += size.Width;
        }
    }

    private string HeaderLabel(RecordingState s)
    {
        bool heyKivi = _vm.IsRewriteCapture || s is RecordingState.RewritePending or RecordingState.RewriteReview;
        if (heyKivi)
        {
            var instr = s == RecordingState.Listening ? _vm.PartialTranscript : _vm.Instruction;
            return string.IsNullOrWhiteSpace(instr) ? "HEY KIVI" : $"HEY KIVI · \"{instr}\"";
        }
        return s switch
        {
            RecordingState.Listening  => "LIVE",
            RecordingState.Processing => "POLISHING",
            RecordingState.Speaking   => "INSERTING",
            RecordingState.Waiting    => "COOLING DOWN",
            RecordingState.Done       => "DONE",
            RecordingState.Error      => "ERROR",
            _                         => "KIVI",
        };
    }

    private string BodyText(RecordingState s) => s switch
    {
        RecordingState.Listening      => string.IsNullOrEmpty(_vm.PartialTranscript)
            ? "Press right ctrl and speak — finished text appears here, in your style…"
            : _vm.PartialTranscript,
        RecordingState.Processing     => "Cleaning up your text…",
        RecordingState.Speaking       => "Pasting…",
        RecordingState.Waiting        => "Rate limited — retrying shortly…",
        RecordingState.Done           => "Done.",
        RecordingState.Error          => _vm.LastErrorMessage ?? "Couldn't catch that.",
        RecordingState.RewritePending => "Rewriting…",
        _                              => "",
    };

    private string FooterText(RecordingState s) => s switch
    {
        RecordingState.Listening     => _vm.IsRewriteCapture ? "release to rewrite · esc to discard" : "right ctrl to stop · esc to discard",
        RecordingState.RewriteReview => "⏎ paste · esc keep original",
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
