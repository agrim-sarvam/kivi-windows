// Kivi.App/Views/MainApp/MainAppWindow.xaml.cs
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// Hosts the sidebar + content Frame. Opened from the orb's hover "expand" icon or the tray
/// icon's "Open Kivi"/"Settings" commands, or re-focused if already open. Closing the window
/// (the titlebar X) hides it instead of destroying it -- Kivi keeps running via the tray icon
/// and orb -- so the window can be reopened without losing its Frame/nav state. Only an
/// explicit Kivi-wide quit (tray "Quit Kivi") actually destroys it, as part of process exit.
/// </summary>
public sealed partial class MainAppWindow : Window
{
    private readonly AppWindow _appWindow;

    public MainAppWindow()
    {
        InitializeComponent();
        Title = "Kivi";
        NavRecord.IsActive = true;
        ContentFrame.Navigate(typeof(RecordPage));

        nint hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.Closing += OnAppWindowClosing;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        _appWindow.Hide();
    }

    /// <summary>Reshows the window if it was hidden via the titlebar close button.</summary>
    public new void Activate()
    {
        _appWindow.Show();
        base.Activate();
    }

    private void OnNavRecord(object sender, RoutedEventArgs e)
    {
        NavRecord.IsActive = true;
        NavHistory.IsActive = false;
        ContentFrame.Navigate(typeof(RecordPage));
    }

    private void OnNavHistory(object sender, RoutedEventArgs e)
    {
        NavRecord.IsActive = false;
        NavHistory.IsActive = true;
        ContentFrame.Navigate(typeof(HistoryPage));
    }
}
