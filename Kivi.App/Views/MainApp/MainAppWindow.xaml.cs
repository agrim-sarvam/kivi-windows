// Kivi.App/Views/MainApp/MainAppWindow.xaml.cs
using Microsoft.UI.Xaml;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// Hosts the sidebar + content Frame. Opened from the orb's hover "expand" icon
/// (LayeredOrb.SettingsRequested's sibling, wired in OverlayWindow) or re-focused if already
/// open. Record, History, Analytics, and Settings are fully real; Personas, Presets, and
/// Memory are real, navigable pages backed by in-memory-only mock data (WorkspaceMockData).
/// Only "leaderboard" remains a non-interactive stub.
/// </summary>
public sealed partial class MainAppWindow : Window
{
    public MainAppWindow()
    {
        InitializeComponent();
        Title = "Kivi";
        NavRecord.IsActive = true;
        ContentFrame.Navigate(typeof(RecordPage));
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
