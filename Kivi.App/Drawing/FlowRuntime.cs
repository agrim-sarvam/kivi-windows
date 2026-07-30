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
    // While parked (render idle), this cheap ~60Hz watch polls the cursor and wakes the moment it
    // enters an interactive region — so hover feels instant instead of waiting up to 1s for the next
    // 1Hz heartbeat. It does NOT render; it only unparks on a hit, then the normal loop takes over.
    private readonly DispatcherTimer _watch;
    private const double WatchHz = 60;

    private double _lastStepMs = -1;
    private bool _parked;
    private int _settledTicks;
    private Tier _tier = Tier.Rest;
    private double[] _prevSig = Array.Empty<double>();
    private Action<double>? _driver;

    // Last transcript content "fit key" — see Step(): when it changes we remeasure + refit the box.
    private string _lastFitKey = "";

    private double _dpiScale = 1.0;

    // Drag-anywhere-on-the-orb (user requirement: click-and-hold directly on the orb body, drag,
    // release to place it — no separate handle UI, no double-click-to-dock). Once the user drags the
    // orb, the bottom-center auto-layout in ScreenTopLeft() is permanently replaced for the rest of
    // the session by the last dragged-to position — this flag + stored point are that override.
    private bool _userPositioned;
    private int _manualScreenX, _manualScreenY;
    private bool _clickThroughState = true; // mirrors the host's current WS_EX_TRANSPARENT state so
                                             // we only call SetClickThrough on an actual change

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

        _host.MouseDown += OnHostMouseDown;
        _host.Click += OnHostClick;
        _host.DragStarted += OnHostDragStarted;
        _host.DragMoved += OnHostDragMoved;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / MorphHz) };
        _timer.Tick += (_, _) => Loop();
        _heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(HeartbeatMs) };
        _heartbeat.Tick += (_, _) => Heartbeat();
        _watch = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(1000.0 / WatchHz) };
        _watch.Tick += (_, _) => WatchForWake();
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
        _watch.Stop();
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

        // Content-driven box sizing — port of OrbApp.tsx's fitKey-gated fitBoxToContent() effect.
        // When the transcript text / stage / surface mode changes, remeasure the display text and ask
        // the engine to grow the box to fit (it clamps + eases over subsequent frames). Gated on a
        // cheap change-key so we don't remeasure every frame. Without this call the box stayed frozen
        // at BOX_DEFAULT (108 px) and long transcripts spilled past the bottom edge into the footer.
        string fitKey = BoxContentFit.FitKey(f);
        if (fitKey != _lastFitKey)
        {
            _lastFitKey = fitKey;
            var (fitW, fitH) = BoxContentFit.Request(f);
            Engine.FitBoxToContent(fitW, fitH);
        }

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
        PollPointer(f);
    }

    private void Present(FlowFrame f)
    {
        try
        {
            using var bmp = _orbRenderer.Render(f, _dpiScale);
            var (sx, sy) = ScreenTopLeft(bmp.Width, bmp.Height);
            _lastScreenX = sx;
            _lastScreenY = sy;
            _host.PushFrame(bmp, sx, sy);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[FlowRuntime] present failed: " + ex);
        }
    }

    private int _lastScreenX, _lastScreenY;

    /// Per-tick cursor poll — orb-engine-behavior.md §7: "the shell polls the live cursor every tick
    /// (GetCursorPos) and calls UpdateHover() — race-free, no fragile mouse-move events. This same
    /// function drives the layered window's click-through toggle." No .OnHover fallback: this IS the
    /// hover + click-through mechanism, called once per render tick (24-60Hz depending on the
    /// current fps tier — see Loop()/TierHz above), never event-driven.
    private void PollPointer(FlowFrame f)
    {
        if (!GetCursorPos(out int cx, out int cy)) return;
        var (flowX, flowY) = ScreenToFlow(cx, cy);
        Engine.SetPointer(flowX, flowY, f);
        bool interactive = FlowEngine.IsInteractiveAt(f, flowX, flowY);
        SetClickThroughIfChanged(!interactive);
        PollOutsideClickToCollapse(f, interactive);
    }

    private bool _leftButtonWasDown;

    /// <summary>
    /// Click-outside-to-collapse: while the box is expanded, a left-click anywhere that is NOT an
    /// interactive orb/box/satellite region collapses it — matching the common "popover" convention
    /// (per user request). The orb window is click-through outside its interactive regions (that's
    /// the whole point of the per-tick hit-test), so a click on the desktop or another app never
    /// reaches OnHostClick/WM_LBUTTONDOWN at all; global button-state polling (already ticking here
    /// every frame for the hover/click-through mechanism) is the only way to observe it without a
    /// separate low-level mouse hook. Edge-detected (down-transition only) so a held button doesn't
    /// fire repeatedly.
    /// </summary>
    private void PollOutsideClickToCollapse(FlowFrame f, bool cursorIsInteractive)
    {
        bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        bool justPressed = leftDown && !_leftButtonWasDown;
        _leftButtonWasDown = leftDown;

        if (justPressed && f.Expanded && !cursorIsInteractive)
            Engine.CollapseClick();
    }

    private void SetClickThroughIfChanged(bool clickThrough)
    {
        if (_clickThroughState == clickThrough) return;
        _clickThroughState = clickThrough;
        _host.SetClickThrough(clickThrough);
    }

    /// Converts a physical screen point to flow-space (origin at the orb's (CenterX, OrbCenterY)
    /// anchor, in logical/CSS px) — the inverse of how OrbRenderer/SatellitesRenderer/
    /// TranscriptBoxRenderer place things onto the canvas, and of how ScreenTopLeft places the
    /// canvas onto the screen.
    private (double flowX, double flowY) ScreenToFlow(int screenCx, int screenCy)
    {
        double localX = (screenCx - _lastScreenX) / _dpiScale;
        double localY = (screenCy - _lastScreenY) / _dpiScale;
        return (localX - OrbRenderer.CenterX, localY - OrbRenderer.OrbCenterY);
    }

    // --- mouse input from the layered window (drag-anywhere-on-the-orb + satellite clicks) ---

    private void OnHostMouseDown(int screenX, int screenY)
    {
        // Nothing to do here beyond what LayeredOrbHost already tracks (drag-start detection lives
        // there, gated on the DragThresholdPx it owns) — a plain down is not itself a gesture; we act
        // on Click (no drag happened) or DragStarted/DragMoved (drag did happen).
    }

    private void OnHostDragStarted()
    {
        // The user grabbed the orb body and started moving it. Per the spec: dragging must not
        // trigger FnDown/FnUp or the OrbPointerDown/PointerUp talk gesture — it is handled entirely
        // here, never touching the hotkey path or the engine's press state.
        _userPositioned = true;
    }

    private void OnHostDragMoved(int newScreenX, int newScreenY)
    {
        // The host already moved the actual window (SetWindowPos) for immediate visual feedback;
        // record where so ScreenTopLeft() keeps returning this position on every subsequent tick
        // instead of fighting it back to bottom-center.
        _manualScreenX = newScreenX;
        _manualScreenY = newScreenY;
        _lastScreenX = newScreenX;
        _lastScreenY = newScreenY;
    }

    private void OnHostClick(int screenX, int screenY)
    {
        var (flowX, flowY) = ScreenToFlow(screenX, screenY);
        var target = Frame.InteractiveTarget(flowX, flowY);
        switch (target)
        {
            case HoverTarget.SatCancel:
                // Cancel while a take/edit is live, OR the copy chip when SatManualCopy is showing in
                // its slot (copy vs. cancel is the SAME bubble, tri-mode per orb-visual-and-box.md
                // §4) — CopyClick() only returns text for the shell to place on the clipboard; do
                // that here since LayeredOrbHost/FlowRuntime is the natural place to own clipboard
                // access (Kivi.Core stays OS-free).
                if (Frame.SatManualCopy && !Frame.SatCancelInteractive)
                {
                    var text = Engine.CopyClick();
                    TrySetClipboard(text);
                }
                else
                {
                    Engine.CancelClick();
                }
                Nudge();
                break;
            case HoverTarget.SatEdit:
                Engine.EditClick();
                Nudge();
                break;
            case HoverTarget.SatExpand:
                if (Frame.Expanded) Engine.CollapseClick();
                else Engine.ExpandClick();
                Nudge();
                break;
            case HoverTarget.SatSettings:
                // SettingsClick() invokes OnOpenKivi (opens the full Kivi window/settings surface) —
                // that host callback isn't wired to any Kivi.App window yet (out of scope for this
                // hover/click pass; MainWindow has no settings UI hook today), so we still call
                // through the engine (keeps its internal hint/toast feedback correct) but the actual
                // window-opening is a no-op until OnOpenKivi is wired elsewhere.
                Engine.SettingsClick();
                Nudge();
                break;
            case HoverTarget.CopyChip:
                // Box copy chip (§8b/§8c) — now a dedicated hit region (FlowFrame.InteractiveTarget)
                // separate from the general Box fallback below. CopyClick() returns the plain text;
                // the clipboard write + copyFlash visual timing both live where they already did for
                // the satellite copy path.
                TrySetClipboard(Engine.CopyClick());
                Nudge();
                break;
            case HoverTarget.ThumbUp:
                Engine.RateTake(up: true);
                Nudge();
                break;
            case HoverTarget.ThumbDown:
                Engine.RateTake(up: false);
                Nudge();
                break;
            case HoverTarget.NewSession:
                Engine.NewSessionClick();
                Nudge();
                break;
            case HoverTarget.Box:
                // Clicked inside the box body but not on any of its dedicated sub-regions (copy
                // chip / footer thumbs / new-session — all checked first and would have matched
                // above) — this is just "focus the box", which the box already gets from being
                // expanded/interactive; no separate action needed here.
                break;
            case HoverTarget.Orb:
                // A plain click on the orb body itself, with no drag. CONFIRMED DECISION (not an
                // oversight): investigated whether the reference wires a mouse click on the orb to
                // anything — it does not. OrbPointerDown()/PointerUp() are the actual talk-gesture
                // pair (timed around HOLD_MS=420ms) but this port never calls them from the mouse
                // path (see OnHostMouseDown above and its comment): dictation is PTT-only, driven
                // exclusively by the global hotkey (FnDown/FnUp in FlowEngine, wired from
                // Kivi.Platform's low-level keyboard hook), exactly like the reference where a mouse
                // does not drive push-to-talk. So a bare orb-body click intentionally does nothing
                // beyond the hover/wake hysteresis it already gets every tick. The orb remains fully
                // draggable (handled entirely via DragStarted/DragMoved, never reaching this switch)
                // and fully hoverable.
                break;
        }
    }

    private static void TrySetClipboard(string text)
    {
        try
        {
            if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            {
                d.BeginInvoke(new Action(() => TrySetClipboard(text)));
                return;
            }
            System.Windows.Clipboard.SetText(text ?? "");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[FlowRuntime] clipboard copy failed: " + ex);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point pt);

    private static bool GetCursorPos(out int x, out int y)
    {
        if (GetCursorPos(out System.Drawing.Point pt)) { x = pt.X; y = pt.Y; return true; }
        x = 0; y = 0; return false;
    }

    private const int VK_LBUTTON = 0x01;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // Bottom-center of the primary work area, orb center anchored per the reference
    // (docs/maps/orb-visual-and-box.md: "Orb sits at window horizontal centre"; orbEdgeInset near
    // the bottom edge). We want the ORB'S VISUAL CENTER (OrbCenterY below the canvas top) to land at
    // (centerX, bottomDip) on screen — so the canvas top-left is offset by -OrbCenterY, not by
    // -(CanvasH - OrbCenterY) (that inverted offset was pulling the whole canvas ~380px too high,
    // landing near screen-center instead of near the bottom edge).
    private (int x, int y) ScreenTopLeft(int bmpW, int bmpH)
    {
        // Once the user has free-dragged the orb, that placement wins for the rest of the session —
        // the bottom-center auto-layout below never runs again (per the task's explicit drag spec).
        if (_userPositioned) return (_manualScreenX, _manualScreenY);

        var wa = SystemParameters.WorkArea; // logical (DIP)
        // center horizontally on the work area; sit near the bottom.
        double centerXDip = wa.Left + wa.Width / 2.0;
        double bottomDip = wa.Top + wa.Height - 120; // orb visual-center Y; sit clearly above the taskbar edge
        double topLeftXDip = centerXDip - OrbRenderer.CanvasW / 2.0;
        double topLeftYDip = bottomDip - OrbRenderer.OrbCenterY;
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
        _watch.Start(); // keep watching the cursor cheaply so hover wakes instantly
    }

    private void Unpark()
    {
        _parked = false;
        _settledTicks = 0;
        _heartbeat.Stop();
        _watch.Stop();
    }

    private void Heartbeat()
    {
        Step(NowMs());
        if (_parked) { /* heartbeat timer keeps firing */ }
        else { _heartbeat.Stop(); EnsureLoop(); }
    }

    /// While parked, cheaply poll the cursor (no render) and wake the instant it enters an
    /// interactive region — this is what makes hover feel snappy instead of lagging up to a full
    /// heartbeat second. On a hit we Unpark + Step immediately; the normal per-tick PollPointer then
    /// owns hover/click-through from there.
    private void WatchForWake()
    {
        if (!_parked) return;
        if (!GetCursorPos(out int cx, out int cy)) return;
        var (flowX, flowY) = ScreenToFlow(cx, cy);
        if (FlowEngine.IsInteractiveAt(Frame, flowX, flowY))
        {
            Unpark();
            Step(NowMs());
            EnsureLoop();
        }
    }

    public bool IsParked => _parked;

    public void Dispose()
    {
        Stop();
        _host.MouseDown -= OnHostMouseDown;
        _host.Click -= OnHostClick;
        _host.DragStarted -= OnHostDragStarted;
        _host.DragMoved -= OnHostDragMoved;
        _host.Dispose();
    }
}
