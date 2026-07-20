using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Kivi.App.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _settingsVm;

    public SettingsWindow(SettingsViewModel settingsVm)
    {
        InitializeComponent();
        _settingsVm = settingsVm;
        Nav_SelectionChanged(Nav, null!);
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs? e)
    {
        var tag = (Nav.SelectedItem as NavigationViewItem)?.Tag as string ?? "record";
        switch (tag)
        {
            case "record":
                ContentFrame.Navigate(typeof(Settings.RecordPage), null, new EntranceNavigationTransitionInfo());
                break;
            case "settings":
                ContentFrame.Content = new Settings.SettingsPage(_settingsVm);
                break;
            case "history":
                ContentFrame.Navigate(typeof(Settings.ComingSoonPage), "History", new EntranceNavigationTransitionInfo());
                break;
            case "personas":
                ContentFrame.Navigate(typeof(Settings.ComingSoonPage), "Personas", new EntranceNavigationTransitionInfo());
                break;
            case "presets":
                ContentFrame.Navigate(typeof(Settings.ComingSoonPage), "Presets", new EntranceNavigationTransitionInfo());
                break;
            case "memory":
                ContentFrame.Navigate(typeof(Settings.ComingSoonPage), "Memory", new EntranceNavigationTransitionInfo());
                break;
            case "analytics":
                ContentFrame.Navigate(typeof(Settings.ComingSoonPage), "Analytics", new EntranceNavigationTransitionInfo());
                break;
        }
    }
}
