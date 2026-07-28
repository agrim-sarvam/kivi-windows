using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Timers;
using System.Windows;
using Kivi.Core.Contracts;
using Application = System.Windows.Application;

namespace Kivi.Platform.Tray;

/// <summary>
/// PHASE P6 (M7) — notification-area tray icon (WinForms NotifyIcon) with pre-rendered discrete
/// per-state icon frames cycled on a timer (never redrawn from scratch per tick) + a frameless
/// always-on-top WPF popover shown on click.
///
/// Port of _reference/sarvam-kivi-electron/src/main/tray/trayIcon.ts (renderTrayIcon) +
/// TrayController.ts. See docs/maps/menubar-onboarding-auth.md §1.2 for the exact shape/gradient/
/// breathing-alpha spec this reproduces.
/// </summary>
public sealed class NotifyIconTrayHost : ITrayHost, IDisposable
{
    // Pill geometry (logical px @ SCALE=2, matching trayIcon.ts H=18, SCALE=2).
    private const int H = 18;
    private const int Scale = 2;

    // Breathing cycle: pre-render this many discrete frames covering one full period.
    private const int FrameCount = 10;
    private const int TickIntervalMs = 120;

    private static readonly (byte R, byte G, byte B) IdleTint = (104, 106, 100);

    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Timers.Timer _breatheTimer;

    private (byte R, byte G, byte B) _baseColor = IdleTint;
    private string _phaseName = "idle";
    private Icon[] _frames = Array.Empty<Icon>();
    private int _frameIndex;
    private bool _reducedMotion;
    private TrayPopover? _popover;
    private bool _disposed;

    public NotifyIconTrayHost()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Visible = false,
            Text = "Kivi",
        };
        _notifyIcon.Click += OnNotifyIconClick;

        _breatheTimer = new System.Timers.Timer(TickIntervalMs) { AutoReset = true };
        _breatheTimer.Elapsed += OnTick;

        RebuildFrames();
        ApplyFrame(0);
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
        _breatheTimer.Start();
    }

    public void Hide()
    {
        _breatheTimer.Stop();
        _notifyIcon.Visible = false;
    }

    public void UpdateState(string phaseName, (byte R, byte G, byte B) baseColor)
    {
        if (_phaseName == phaseName && _baseColor == baseColor) return;
        _phaseName = phaseName;
        _baseColor = baseColor;
        _frameIndex = 0;
        RebuildFrames();
        ApplyFrame(0);
    }

    /// <summary>Reduced-motion setting: forces steady alpha=1.0 (see docs/maps §1.2).</summary>
    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            if (_reducedMotion == value) return;
            _reducedMotion = value;
            _frameIndex = 0;
            RebuildFrames();
            ApplyFrame(0);
        }
    }

    private static bool IsMotionPhase(string phaseName) => phaseName is
        "processing" or "editing" or "acting" or "editProcess" or "actProcess" or "actListen";

    private double PeriodSeconds => IsMotionPhase(_phaseName) ? 1.1 : 1.6;

    private void RebuildFrames()
    {
        var old = _frames;
        var frames = new Icon[FrameCount];
        bool steady = _reducedMotion || !IsMotionPhase(_phaseName) && _phaseName != "listening" && _phaseName != "connecting";
        for (int i = 0; i < FrameCount; i++)
        {
            double alpha = steady ? 1.0 : BreathingAlpha((double)i / FrameCount);
            using var bmp = RenderPill(_baseColor, alpha);
            frames[i] = Icon.FromHandle(bmp.GetHicon());
        }
        _frames = frames;

        foreach (var icon in old)
            icon.Dispose();
    }

    private static double BreathingAlpha(double phaseFraction)
    {
        // breathingAlpha = 0.55 + 0.45*(sin(2*PI*elapsed/period)+1)/2, sampled evenly over one period.
        double theta = 2 * Math.PI * phaseFraction;
        return 0.55 + 0.45 * (Math.Sin(theta) + 1) / 2;
    }

    private static Bitmap RenderPill((byte R, byte G, byte B) tint, double alpha)
    {
        int w = (int)Math.Round(H * 1.12);
        int width = w * Scale;
        int height = H * Scale;
        float r = (float)(H * 0.28 * Scale);
        float inset = 0.5f * Scale;

        var top = Blend(tint, (255, 255, 255), 0.26);
        var bottom = Blend(tint, (0, 0, 0), 0.18);

        var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = new RectangleF(inset, inset, width - 2 * inset, height - 2 * inset);
        using var path = RoundedRect(rect, r);

        // Vertical gradient [top, base, bottom] at [0, 0.52, 1], -90deg (top to bottom).
        using var brush = new LinearGradientBrush(
            new PointF(rect.Left, rect.Top),
            new PointF(rect.Left, rect.Bottom),
            System.Drawing.Color.White, System.Drawing.Color.White);
        var blend = new ColorBlend(3)
        {
            Colors = new[]
            {
                ToColor(top),
                ToColor(tint),
                ToColor(bottom),
            },
            Positions = new[] { 0f, 0.52f, 1f },
        };
        brush.InterpolationColors = blend;

        int overallAlpha = (int)Math.Round(Math.Clamp(alpha, 0.0, 1.0) * 255);
        using var alphaBmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var gAlpha = Graphics.FromImage(alphaBmp))
        {
            gAlpha.SmoothingMode = SmoothingMode.AntiAlias;
            gAlpha.FillPath(brush, path);

            // Top sheen: white ellipse, alpha 0.14, height ~55% of pill, x 10%-90% width.
            var sheenRect = new RectangleF(
                rect.Left + rect.Width * 0.10f,
                rect.Top,
                rect.Width * 0.80f,
                rect.Height * 0.55f);
            using var sheenBrush = new SolidBrush(System.Drawing.Color.FromArgb((int)Math.Round(0.14 * 255), 255, 255, 255));
            var oldClip = gAlpha.Clip;
            gAlpha.SetClip(path);
            gAlpha.FillEllipse(sheenBrush, sheenRect);
            gAlpha.Clip = oldClip;
        }

        // Apply overall coverage/breathing alpha by multiplying the alpha channel.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var px = alphaBmp.GetPixel(x, y);
                if (px.A == 0) continue;
                int a = px.A * overallAlpha / 255;
                bmp.SetPixel(x, y, System.Drawing.Color.FromArgb(a, px.R, px.G, px.B));
            }
        }

        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static (byte R, byte G, byte B) Blend((byte R, byte G, byte B) baseColor, (int R, int G, int B) other, double t) => (
        (byte)Math.Round(baseColor.R + (other.R - baseColor.R) * t),
        (byte)Math.Round(baseColor.G + (other.G - baseColor.G) * t),
        (byte)Math.Round(baseColor.B + (other.B - baseColor.B) * t));

    private static System.Drawing.Color ToColor((byte R, byte G, byte B) c) => System.Drawing.Color.FromArgb(c.R, c.G, c.B);

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        if (_frames.Length == 0) return;
        _frameIndex = (_frameIndex + 1) % _frames.Length;
        ApplyFrame(_frameIndex);
    }

    private void ApplyFrame(int index)
    {
        if (_frames.Length == 0) return;
        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _notifyIcon.Icon = _frames[index % _frames.Length];
            });
        }
        catch
        {
            // Dispatcher may be shutting down; ignore.
        }
    }

    private void OnNotifyIconClick(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _popover ??= new TrayPopover();
            _popover.ShowNearCursor();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _breatheTimer.Stop();
        _breatheTimer.Dispose();
        _notifyIcon.Click -= OnNotifyIconClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        foreach (var icon in _frames)
            icon.Dispose();
    }
}
