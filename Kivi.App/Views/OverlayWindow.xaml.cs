using System.Drawing;
using CommunityToolkit.Mvvm.Input;
using Kivi.App.Controls;
using Kivi.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
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
    private Views.Onboarding.OnboardingWindow? _settingsWindow;
    private Views.MainApp.MainAppWindow? _mainAppWindow;
    private bool _dictationPaused;

    public OverlayWindow(OverlayViewModel vm, Color accent, string languageLabel, string hotkeyLabel)
    {
        InitializeComponent();
        _vm = vm;

        // H.NotifyIcon.WinUI 2.3.2's TaskbarIcon exposes no click *events* (no
        // TrayLeftMouseUp) -- left-click is wired via the LeftClickCommand ICommand
        // property instead. Right-click (MenuActivation default) opens the menu set via
        // the standard FrameworkElement.ContextFlyout property in OverlayWindow.xaml.
        TrayIcon.LeftClickCommand = new RelayCommand(() => OnTrayOpenKivi(this, new RoutedEventArgs()));

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

        // The visible orb lives here, on the UI thread (its render timer needs the
        // DispatcherQueue) and reads state straight off _vm every frame.
        _orb = new LayeredOrb(vm, accent, languageLabel, hotkeyLabel);
        _orb.SettingsRequested += OnSettingsRequested;
        _orb.MainAppRequested += OnMainAppRequested;

        Closed += (_, _) =>
        {
            _orb.SettingsRequested -= OnSettingsRequested;
            _orb.MainAppRequested -= OnMainAppRequested;
            _orb.Dispose();
            TrayIcon.Dispose();
        };

        // Activate (still off-screen, so invisible) so the window counts as "open" and keeps
        // the app running after onboarding closes.
        Activate();
    }

    // Raised (on this same UI thread) when the orb's hover gear icon is clicked. Reopens the
    // existing Config page as a standalone settings window, or refocuses it if already open.
    private void OnSettingsRequested()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var win = Views.Onboarding.OnboardingWindow.ForSettingsReentry();
        win.Completed += () => win.Close();
        win.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = win;
        win.Activate();
    }

    /// <summary>
    /// Opens (or refocuses) the main app window. Public so App.xaml.cs's startup gate can
    /// call it directly once onboarding finishes, in addition to the orb/tray triggers below.
    /// </summary>
    public void ShowMainApp() => OnMainAppRequested();

    // Raised (on this same UI thread) when the orb's hover expand icon is clicked. Opens the
    // main app window (sidebar + record/history), or refocuses it if already open.
    private void OnMainAppRequested()
    {
        if (_mainAppWindow is not null)
        {
            _mainAppWindow.Activate();
            return;
        }

        var win = new Views.MainApp.MainAppWindow();
        win.Closed += (_, _) => _mainAppWindow = null;
        _mainAppWindow = win;
        win.Activate();
    }

    // --- Tray icon handlers ---

    private void OnTrayOpenKivi(object sender, RoutedEventArgs e) => OnMainAppRequested();

    private void OnTrayPauseToggle(object sender, RoutedEventArgs e)
    {
        _dictationPaused = !_dictationPaused;
        var hotkey = Kivi.App.App.Services.GetRequiredService<Kivi.Core.Abstractions.IHotkeyService>();
        hotkey.SetEnabled(!_dictationPaused);
        TrayPauseItem.Text = _dictationPaused ? "Resume dictation" : "Pause dictation";
    }

    private void OnTraySettings(object sender, RoutedEventArgs e) => OnSettingsRequested();

    private void OnTrayQuit(object sender, RoutedEventArgs e)
    {
        // MainAppWindow normally hides instead of closing (so the titlebar X doesn't quit
        // the whole app) -- if it's open, that same Closing handler would otherwise cancel
        // this close too and silently defeat Quit. Let it through, then close everything
        // and force the process to end: Application.Current.Exit() alone isn't reliably
        // enough to terminate a WinUI3 process that has an off-screen anchor window plus a
        // native (non-WinUI) tray icon and layered orb still holding native resources open.
        if (_mainAppWindow is not null)
        {
            _mainAppWindow.AllowRealClose = true;
            _mainAppWindow.Close();
        }

        _orb.Dispose();
        TrayIcon.Dispose();
        Application.Current.Exit();
        Environment.Exit(0);
    }
}
