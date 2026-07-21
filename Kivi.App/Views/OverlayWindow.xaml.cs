using Kivi.App.Controls;
using Kivi.App.Interop;
using Kivi.App.ViewModels;
using Kivi.Core.Orchestration;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Graphics;

namespace Kivi.App.Views;

public sealed partial class OverlayWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;
    private readonly OverlayViewModel _vm;

    public OverlayWindow(OverlayViewModel vm)
    {
        InitializeComponent();

        // No system backdrop: an explicit null backdrop plus a transparent content root
        // (Root's Background="Transparent" in OverlayWindow.xaml) is required in WinAppSDK
        // 1.8 - a Window left to its default backdrop can paint an opaque surface behind
        // transparent XAML content, which is what produced the fullscreen-black window.
        SystemBackdrop = null;

        _vm = vm;

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        _appWindow.SetPresenter(presenter);
        _appWindow.IsShownInSwitchers = false;

        // Resize away from the AppWindow's default (large) size and make the HWND
        // layered+click-through BEFORE the window is ever shown. Showing first and
        // resizing/transparentizing after let a large, not-yet-transparent surface flash
        // opaque black for a frame - that flash is the root cause of the reported
        // fullscreen-black-window bug.
        var (w, h) = SizeFor(_vm.State);
        _appWindow.Resize(new SizeInt32(w, h));
        MakeClickThrough();

        // KiviOrbControl is a bare Canvas with no intrinsic size - it must be told its
        // rendered size explicitly (matching the AppWindow's client area) or it measures
        // to 0x0 and no dots are ever built.
        Orb.Width = w;
        Orb.Height = h;
        Orb.State = _vm.State;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OverlayViewModel.State) or nameof(OverlayViewModel.IsVisible))
                ApplyState();
        };
        ApplyState();
    }

    // Bare dot-matrix bird, no container: a small resting size at Idle, a larger size for
    // all active states. Both preserve the ~0.74 aspect ratio of the 120x162 mask trace
    // (48/64 = 0.75, 96/130 = 0.74).
    private static (int w, int h) SizeFor(RecordingState s) => s switch
    {
        RecordingState.Idle => (48, 64),
        _                   => (96, 130)
    };

    private void ApplyState()
    {
        var (w, h) = SizeFor(_vm.State);
        Orb.Width = w;
        Orb.Height = h;
        Orb.State = _vm.State;

        _appWindow.Resize(new SizeInt32(w, h));

        ShowAnchoredBottomCenter();
    }

    private void ShowAnchoredBottomCenter()
    {
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int w = _appWindow.Size.Width, h = _appWindow.Size.Height;
        var pos = new PointInt32(area.X + (area.Width - w) / 2, area.Y + area.Height - h - 48);
        _appWindow.Move(pos);
        _appWindow.Show(activateWindow: false);
    }

    private void MakeClickThrough()
    {
        nint ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 255, NativeMethods.LWA_ALPHA);
    }
}
