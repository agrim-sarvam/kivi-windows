using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Settings;

public sealed partial class ComingSoonPage : Page
{
    public ComingSoonPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        TitleText.Text = e.Parameter as string ?? "Coming soon";
    }
}
