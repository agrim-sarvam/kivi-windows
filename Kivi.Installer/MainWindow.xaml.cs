using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Kivi.Installer;

public partial class MainWindow : Window
{
    private readonly Installer _installer = new();
    private readonly bool _uninstallMode;

    public MainWindow(bool uninstallMode)
    {
        _uninstallMode = uninstallMode;
        InitializeComponent();

        if (_uninstallMode)
        {
            ShowPanel(UninstallPanel);
        }
        else
        {
            ShowPanel(WelcomePanel);
            if (!Installer.HasPayload)
            {
                // Dev build with no embedded payload: make it clear, don't crash.
                InstallButton.IsEnabled = false;
                WelcomeNote.Text = "no payload bundled — this is a dev build.";
                WelcomeNote.Visibility = Visibility.Visible;
            }
        }
    }

    // ---- window chrome -----------------------------------------------------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ---- install flow ------------------------------------------------------

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        bool desktop = DesktopShortcutCheck.IsChecked == true;
        bool atLogin = LaunchAtLoginCheck.IsChecked == true;

        ProgressHeadline.Text = "installing kivi";
        ShowPanel(ProgressPanel);

        var progress = new Progress<(int percent, string status)>(OnProgress);
        try
        {
            await Task.Run(() => _installer.Install(desktop, atLogin, progress));
            DoneHeadline.Text = "kivi is ready";
            DoneSub.Text = "hold your hotkey and start talking.";
            LaunchButton.Visibility = Visibility.Visible;
            ShowPanel(DonePanel);
        }
        catch (Exception ex)
        {
            ShowFailure("install failed", ex.Message);
        }
    }

    // ---- uninstall flow ----------------------------------------------------

    private async void ConfirmUninstallButton_Click(object sender, RoutedEventArgs e)
    {
        ProgressHeadline.Text = "removing kivi";
        ShowPanel(ProgressPanel);

        var progress = new Progress<(int percent, string status)>(OnProgress);
        try
        {
            await Task.Run(() => _installer.Uninstall(progress));

            DoneHeadline.Text = "kivi removed";
            DoneSub.Text = "thanks for trying kivi.";
            LaunchButton.Visibility = Visibility.Collapsed; // nothing to launch
            ShowPanel(DonePanel);

            // The running uninstall.exe cleans itself + the Kivi dir after we exit.
            Installer.ScheduleSelfDelete();
        }
        catch (Exception ex)
        {
            ShowFailure("uninstall failed", ex.Message);
        }
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        try { Installer.LaunchApp(); } catch { /* ignore — user can start it from Start Menu */ }
        Close();
    }

    // ---- helpers -----------------------------------------------------------

    private void OnProgress((int percent, string status) p)
    {
        ProgressStatus.Text = p.status;
        // Animate the fill width across the track (track is content width minus padding).
        double trackWidth = 520 - 40 - 40 - 2; // window - left/right margin - border
        double target = Math.Clamp(p.percent, 0, 100) / 100.0 * trackWidth;
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ProgressFill.BeginAnimation(WidthProperty, anim);
    }

    private void ShowFailure(string headline, string detail)
    {
        DoneHeadline.Text = headline;
        DoneSub.Text = detail;
        LaunchButton.Visibility = Visibility.Collapsed;
        ShowPanel(DonePanel);
    }

    private void ShowPanel(FrameworkElement panel)
    {
        WelcomePanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Collapsed;
        UninstallPanel.Visibility = Visibility.Collapsed;

        panel.Visibility = Visibility.Visible;

        // Canon EaseSoft fade-in on state change.
        panel.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        panel.BeginAnimation(OpacityProperty, fade);
    }
}
