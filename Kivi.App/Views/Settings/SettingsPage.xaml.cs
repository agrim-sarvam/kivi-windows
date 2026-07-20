using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kivi.App.Views.Settings;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // Pre-populate the masked API key field without ever binding/logging the raw value.
        ApiKeyBox.Password = ViewModel.ApiKey;
    }

    // Explicit handler (rather than x:Bind Mode=TwoWay on Password) keeps the secret-bearing
    // control's data path simple and easy to audit: no generated binding code, no logging.
    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.ApiKey = ApiKeyBox.Password;
    }
}
