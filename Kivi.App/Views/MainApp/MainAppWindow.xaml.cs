// Kivi.App/Views/MainApp/MainAppWindow.xaml.cs
using Kivi.Core.Config;
using Microsoft.Extensions.DependencyInjection;
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
/// Record, History, Analytics, and Settings are fully real; Personas, Presets, and Memory are
/// real, navigable pages backed by in-memory-only mock data (WorkspaceMockData). Only
/// "leaderboard" remains a non-interactive stub.
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
        RenderAccountFooter();

        nint hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.Closing += OnAppWindowClosing;
        Kivi.App.Services.WindowIcon.Apply(this);
    }

    /// <summary>
    /// Shows the real signed-in profile (AppConfig.ProfileName/ProfileEmail, set during
    /// onboarding's Google sign-in or left null if "use work email instead" was chosen)
    /// instead of a hardcoded placeholder account.
    /// </summary>
    private void RenderAccountFooter()
    {
        var config = Kivi.App.App.Services.GetRequiredService<AppConfig>();
        var name = config.ProfileName;
        var email = config.ProfileEmail;

        if (!string.IsNullOrWhiteSpace(name))
        {
            AccountName.Text = name;
            AccountEmail.Text = email ?? "";
            AccountInitial.Text = name.Substring(0, 1).ToUpperInvariant();
        }
        else
        {
            AccountName.Text = "Not signed in";
            AccountEmail.Text = "";
            AccountInitial.Text = "?";
        }
    }

    /// <summary>
    /// Set by the tray's "Quit Kivi" command before it closes every window, so this
    /// window's Closing handler lets the real close through instead of hiding it --
    /// otherwise a hidden-not-closed MainAppWindow would keep the process alive and
    /// silently defeat Quit.
    /// </summary>
    public bool AllowRealClose { get; set; }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (AllowRealClose) return;
        args.Cancel = true;
        _appWindow.Hide();
    }

    /// <summary>Reshows the window if it was hidden via the titlebar close button.</summary>
    public new void Activate()
    {
        _appWindow.Show();
        base.Activate();
    }

    private void DeactivateAll()
    {
        NavRecord.IsActive = false;
        NavHistory.IsActive = false;
        NavAnalytics.IsActive = false;
        NavSettings.IsActive = false;
        NavPersonas.IsActive = false;
        NavPresets.IsActive = false;
        NavMemory.IsActive = false;
    }

    private void OnNavRecord(object sender, RoutedEventArgs e)
    {
        DeactivateAll();
        NavRecord.IsActive = true;
        ContentFrame.Navigate(typeof(RecordPage));
    }

    private void OnNavHistory(object sender, RoutedEventArgs e)
    {
        DeactivateAll();
        NavHistory.IsActive = true;
        ContentFrame.Navigate(typeof(HistoryPage));
    }

    private void OnNavAnalytics(object sender, RoutedEventArgs e)
    {
        DeactivateAll();
        NavAnalytics.IsActive = true;
        ContentFrame.Navigate(typeof(AnalyticsPage));
    }

    private void OnNavSettings(object sender, RoutedEventArgs e)
    {
        DeactivateAll();
        NavSettings.IsActive = true;
        ContentFrame.Navigate(typeof(SettingsPage));
    }

    private void OnNavPersonas(object sender, RoutedEventArgs e)
    {
        DeactivateAll();
        NavPersonas.IsActive = true;
        ContentFrame.Navigate(typeof(PersonasPage));
    }

    private void OnNavPresets(object sender, RoutedEventArgs e)
    {
        DeactivateAll();
        NavPresets.IsActive = true;
        ContentFrame.Navigate(typeof(PresetsPage));
    }

    private void OnNavMemory(object sender, RoutedEventArgs e)
    {
        DeactivateAll();
        NavMemory.IsActive = true;
        ContentFrame.Navigate(typeof(MemoryPage));
    }
}
