using System.Drawing;
using Kivi.App.Controls;
using Kivi.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Graphics;

namespace Kivi.App.Views;

/// <summary>
/// Invisible lifetime-anchor window. A WinUI app exits when its last <see cref="Window"/>
/// closes, so this 1x1 off-screen window keeps the process alive while the actual, visible
/// desktop orb is drawn by a separate Win32 layered window (<see cref="LayeredOrb"/>) - WinUI
/// composites its own windows opaquely and cannot float a transparent, glowing orb.
/// </summary>
public sealed partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _vm;
    private readonly LayeredOrb _orb;

    public OverlayWindow(OverlayViewModel vm, Color accent)
    {
        InitializeComponent();
        _vm = vm;

        // Push this anchor window off-screen and shrink it so it is never seen.
        nint hwnd = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        appWindow.SetPresenter(presenter);
        appWindow.IsShownInSwitchers = false;
        appWindow.Resize(new SizeInt32(1, 1));
        appWindow.Move(new PointInt32(-32000, -32000));

        // The visible orb lives here, on the UI thread (its breathing timer needs the
        // DispatcherQueue).
        _orb = new LayeredOrb(accent);
        _orb.SetState(_vm.State);

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.State))
                _orb.SetState(_vm.State);
        };

        Closed += (_, _) => _orb.Dispose();

        // Activate (still off-screen, so invisible) so the window counts as "open" and keeps
        // the app running after onboarding closes.
        Activate();
    }
}
