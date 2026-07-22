// Kivi.App/Views/MainApp/MainAppWindow.xaml.cs
using Microsoft.UI.Xaml;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// Hosts the sidebar + content Frame. Opened from the orb's hover "expand" icon
/// (LayeredOrb.SettingsRequested's sibling, wired in OverlayWindow) or re-focused if already
/// open. Only Record and History are real pages today.
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
