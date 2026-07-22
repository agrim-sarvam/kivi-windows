// Kivi.App/Views/MainApp/SidebarNavItem.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// One sidebar row: icon + label, with an active-state green accent bar and light-green
/// background. IsStub items (personas/presets/memory/analytics/leaderboard - no backend exists
/// for them yet) render identically but never raise Click and show a dimmer, non-interactive look.
/// </summary>
public sealed partial class SidebarNavItem : UserControl
{
    public event RoutedEventHandler? Click;

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SidebarNavItem),
            new PropertyMetadata("", (d, e) => ((SidebarNavItem)d).Icon.Glyph = (string)e.NewValue));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SidebarNavItem),
            new PropertyMetadata("", (d, e) => ((SidebarNavItem)d).LabelText.Text = (string)e.NewValue));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(SidebarNavItem),
            new PropertyMetadata(false, OnIsActiveChanged));

    public static readonly DependencyProperty IsStubProperty =
        DependencyProperty.Register(nameof(IsStub), typeof(bool), typeof(SidebarNavItem),
            new PropertyMetadata(false, OnIsStubChanged));

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public bool IsStub { get => (bool)GetValue(IsStubProperty); set => SetValue(IsStubProperty, value); }

    public SidebarNavItem()
    {
        InitializeComponent();
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (IsStub) return; // honest stub: renders, but does nothing -- no backend exists yet
        Click?.Invoke(this, e);
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (SidebarNavItem)d;
        bool active = (bool)e.NewValue;
        self.AccentBar.Opacity = active ? 1 : 0;
        self.Root.Background = active
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviWarmTintBrush"]
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        self.Icon.Foreground = active
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviAccentBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextSecondaryBrush"];
    }

    private static void OnIsStubChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (SidebarNavItem)d;
        self.Root.Opacity = (bool)e.NewValue ? 0.72 : 1.0;
    }
}
