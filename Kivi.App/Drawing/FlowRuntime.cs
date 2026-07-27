using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Kivi.Core.KiwiMark;
using Kivi.Core.Orb;
using Kivi.Platform.Overlay;
using Application = System.Windows.Application;
using SystemParameters = System.Windows.SystemParameters;

namespace Kivi.App.Drawing;

/// <summary>
/// The render-loop glue — the .NET port of src/renderer/src/orb/FlowRuntimeWeb.ts (macOS
/// FlowRuntime.swift). Drives the pure FlowEngine off a per-frame clock, steps the kiwi-mark
/// engine, renders a FlowFrame to a bitmap, and pushes it to the layered orb window.
///
/// Reproduces: the 3-tier fps band (rest 24 / steady 30 / morph 60 via a geometry-signature diff),
/// the 0-fps rest-park + 1 Hz heartbeat, and Nudge() (render in the same pass as an input edge).
/// dt-correction lives inside the engine (Step reads now-prev), so this runtime only decides WHEN
/// to step — it never double-corrects.
/// </summary>
public sealed class FlowRuntime : IDisposable
{
    private const double RestHz = 24, SteadyHz = 30, MorphHz = 60;
    private const int ParkAfterSettledTicks = 48;
    private const double HeartbeatMs = 1000;

    private enum Tier { Rest, Steady, Morph }

    public FlowEngine Engine { get; }
    private readonly KiwiMarkEngine _markEngine;
    private readonly KiwiMarkRenderer _markRenderer;
    private readonly OrbRenderer _orbRenderer;
    private readonly LayeredOrbHost _host;
    private readonly SpeechPace _pace = new();

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _heartbeat;

    private double _lastStepMs = -1;
    private bool _parked;
    private int _settledTicks;
    private Tier _tier = Tier.Rest;
    private double[] _prevSig = Array.Empty<double>();
    private Action<double>? _driver;

    private double _dpiScale = 1.0;

    public FlowFrame Frame { get; private set; } = new();
    public long TickCount { get; private set; }

    public FlowRuntime(FlowEngine engine, LayeredOrbHost host, double markCssWidth = 38)
    {
        Engine = engine;
        _host = host;
        _markEngine = new KiwiMarkEngine();
        _markRenderer = new KiwiMarkRenderer(_markEngine, markCssWidth);
        _orbRenderer = new OrbRenderer(_markRenderer);
        Engine.OnServiceWorkEnqueued = Nudge;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / MorphHz) };
        _timer.Tick += (_, _) => Loop();
        _heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(HeartbeatMs) };
        _heartbeat.Tick += (_, _) => Heartbeat();
    }

    public void SetDriver(Action<double>? driver) => _driver = driver;

    private double NowMs() => _clock.Elapsed.TotalMilliseconds;

    public void Start()
    {
        _host.EnsureCreated();
        _host.ApplyNonActivating();
        _dpiScale = ResolveDpiScale();
        _lastStepMs = -1;
        Step(NowMs());
        EnsureLoop();
    }

    public void Stop()
    {
        _timer.Stop();
        _heartbeat.Stop();
    }

    /// Render NOW in the same pass as an input edge (unpark + step). Marshalled to the UI thread.
    public void Nudge()
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.BeginInvoke(new Action(Nudge));
            return;
        }
        if (_parked) Unpark();
        Step(NowMs());
        EnsureLoop();
    }

    private void EnsureLoop()
    {
        if (!_parked && !_timer.IsEnabled) _timer.Start();
    }

    private void Loop()
    {
        double now = NowMs();
        double interval = 1000.0 / TierHz(_tier);
        if (_lastStepMs >= 0 && now - _lastStepMs < interval - 1) return; // band-gate
        Step(now);
    }

    private static double TierHz(Tier t) => t == Tier.Morph ? MorphHz : t == Tier.Steady ? SteadyHz : RestHz;

    private void Step(double now)
    {
        double dt = _lastStepMs < 0 ? 1.0 / 60 : Math.Min(0.05, Math.Max(0, (now - _lastStepMs) / 1000.0));
        _lastStepMs = now;

        _driver?.Invoke(now);

        var f = Engine.Step(now);

        if (f.MarkOpacity > 0.001)
        {
            _markEngine.ReduceMotion = f.Settings.ReduceMotion;
            _markEngine.FreezeWalk = Engine.OrbShowcase;
            _markRenderer.Inverted = f.Inverted;
            var target = f.MarkState;
            if (target == KiwiMarkState.Listening || target == KiwiMarkState.Speaking)
            {
                double raw = Math.Min(1, Math.Max(0, Engine.MicLevel));
                double level = Math.Min(1, Math.Max(0, (raw - 0.12) / 0.55));
                _pace.Feed(level, dt);
                double pace = _pace.Eased;
                _markEngine.WalkDrive = 0.45 + 1.3 * pace;
                _markEngine.SpeechGlow = pace;
                _markEngine.ListenLevel = 0.16 + 1.55 * raw;
            }
            else
            {
                _pace.Reset();
                _markEngine.WalkDrive = 1;
                _markEngine.SpeechGlow = 0;
                _markEngine.ListenLevel = 1;
            }
            _markEngine.Step(dt, target, f.Inverted);
        }

        Frame = f;
        TickCount++;
        Retune(f);
        UpdateRestPark(f);
        Present(f);
    }

    private void Present(FlowFrame f)
    {
        try
        {
            using var bmp = _orbRenderer.Render(f, _dpiScale);
            var (sx, sy) = ScreenTopLeft(bmp.Width, bmp.Height);
            _host.PushFrame(bmp, sx, sy);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[FlowRuntime] present failed: " + ex);
        }
    }

    // Bottom-center of the primary work area, orb center anchored per the reference.
    private (int x, int y) ScreenTopLeft(int bmpW, int bmpH)
    {
        var wa = SystemParameters.WorkArea; // logical (DIP)
        double screenLeft = SystemParameters.VirtualScreenLeft;
        // center horizontally on the work area; sit near the bottom.
        double centerXDip = wa.Left + wa.Width / 2.0;
        double bottomDip = wa.Top + wa.Height - 120; // sit clearly above the taskbar edge
        // orb center within the canvas
        double canvasCenterXDip = centerXDip;
        double orbCenterYDip = bottomDip - (OrbRenderer.CanvasH - OrbRenderer.OrbCenterY);
        double topLeftXDip = canvasCenterXDip - OrbRenderer.CanvasW / 2.0;
        double topLeftYDip = orbCenterYDip;
        return ((int)Math.Round(topLeftXDip * _dpiScale), (int)Math.Round(topLeftYDip * _dpiScale));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private static double ResolveDpiScale()
    {
        // The app is per-monitor DPI-aware (app.manifest); GetDpiForSystem is reliable even before a
        // window is shown. PresentationSource is unreliable on a minimized/off-screen host window.
        try
        {
            uint dpi = GetDpiForSystem();
            if (dpi >= 48) return dpi / 96.0;
        }
        catch { }
        try
        {
            foreach (System.Windows.Window w in Application.Current.Windows)
            {
                var src = System.Windows.PresentationSource.FromVisual(w);
                if (src?.CompositionTarget != null)
                {
                    double m = src.CompositionTarget.TransformToDevice.M11;
                    if (m > 0.1) return m;
                }
            }
        }
        catch { }
        return 1.0;
    }

    // --- 3-tier band (geometry-signature diff) ---
    private double[] GeoSig(FlowFrame f) => new[]
    {
        f.Open, f.Exp, f.FlowShiftX, f.BoxW, f.BoxH, f.BoxGrowUp, f.TxWrapWidth,
        f.OrbWidth, f.OrbHeight, f.Drop, f.Press,
        f.SatEditShakeX, f.OrbShakeX, f.BoxShakeX,
        f.DiffProgress == null ? 0 : 1, f.ToastVisible ? 1 : 0, f.CopyFlash ? 1 : 0,
    };

    private void Retune(FlowFrame f)
    {
        if (f.Phase == FlowPhase.Rest && f.Open < 0.01 && !f.Expanded)
        {
            _tier = Tier.Rest;
            _prevSig = GeoSig(f);
            return;
        }
        var sig = GeoSig(f);
        Tier tier = Tier.Steady;
        if (_prevSig.Length != sig.Length) tier = Tier.Morph;
        else
            for (int i = 0; i < sig.Length; i++)
                if (Math.Abs(sig[i] - _prevSig[i]) > 0.01) { tier = Tier.Morph; break; }
        _prevSig = sig;
        _tier = tier;
    }

    // --- 0-fps rest park + heartbeat ---
    private void UpdateRestPark(FlowFrame f)
    {
        bool restingStill = f.Phase == FlowPhase.Rest && f.Open < 0.01 && !f.Expanded
            && f.HintOpacity <= 0.001 && !Engine.NeedsRuntimeTicks;
        if (!restingStill)
        {
            _settledTicks = 0;
            if (_parked) Unpark();
            return;
        }
        _settledTicks++;
        if (_settledTicks >= ParkAfterSettledTicks && !_parked) Park();
    }

    private void Park()
    {
        _parked = true;
        _timer.Stop();
        _heartbeat.Start();
    }

    private void Unpark()
    {
        _parked = false;
        _settledTicks = 0;
        _heartbeat.Stop();
    }

    private void Heartbeat()
    {
        Step(NowMs());
        if (_parked) { /* heartbeat timer keeps firing */ }
        else { _heartbeat.Stop(); EnsureLoop(); }
    }

    public bool IsParked => _parked;

    public void Dispose()
    {
        Stop();
        _host.Dispose();
    }
}
