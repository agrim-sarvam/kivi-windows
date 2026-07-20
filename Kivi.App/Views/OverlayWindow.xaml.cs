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
        _vm = vm;
        Orb.State = vm.State;

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

        MakeClickThrough();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OverlayViewModel.State) or nameof(OverlayViewModel.IsVisible))
                ApplyState();
        };
        ApplyState();
    }

    private static KiviOrbPosture PostureFor(RecordingState state) => state switch
    {
        RecordingState.Idle       => KiviOrbPosture.RestPill,
        RecordingState.Listening  => KiviOrbPosture.Woken,
        RecordingState.Processing => KiviOrbPosture.Woken,
        RecordingState.Waiting    => KiviOrbPosture.Satellites,
        RecordingState.Speaking   => KiviOrbPosture.Box,
        RecordingState.Done       => KiviOrbPosture.Woken,
        RecordingState.Error      => KiviOrbPosture.Woken,
        _                         => KiviOrbPosture.RestPill
    };

    private void ApplyState()
    {
        Orb.State = _vm.State;
        Orb.Posture = PostureFor(_vm.State);

        var (w, h) = _vm.State switch
        {
            RecordingState.Idle       => (39, 15),   // rest pill
            RecordingState.Listening  => (61, 61),   // woken
            RecordingState.Processing => (61, 61),
            RecordingState.Waiting    => (23, 23),   // satellites
            RecordingState.Speaking   => (322, 108), // box
            RecordingState.Done       => (61, 61),
            RecordingState.Error      => (61, 61),
            _                         => (39, 15)
        };
        _appWindow.Resize(new SizeInt32(w, h));

        if (_vm.IsVisible) ShowAnchoredBottomCenter();
        else _appWindow.Hide();
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
