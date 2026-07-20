using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;

namespace Kivi.App.Views;

public sealed partial class TrayWindow : Window
{
    public TrayViewModel ViewModel { get; }

    public TrayWindow(TrayViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
