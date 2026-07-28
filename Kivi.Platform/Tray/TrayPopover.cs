using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using MessageBox = System.Windows.Forms.MessageBox;

namespace Kivi.Platform.Tray;

/// <summary>
/// Minimal frameless always-on-top popover shown on tray-icon click (P6 Part A scope: no
/// orchestrator wiring for dictate/open — see TODOs below). Full MenuBarContent (history,
/// transcript box, settings) is out of scope for this phase; see
/// docs/maps/menubar-onboarding-auth.md §1.4.
/// </summary>
public sealed class TrayPopover : Window
{
    public TrayPopover()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 200;
        Height = 140;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1c, 0x1e, 0x1a));
        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x3c, 0x38));
        BorderThickness = new Thickness(1);

        var border = new Border { BorderBrush = BorderBrush, BorderThickness = new Thickness(1) };
        var stack = new StackPanel { Margin = new Thickness(10) };

        stack.Children.Add(MakeRow("dictate", OnDictateClick));
        stack.Children.Add(MakeRow("open kivi", OnOpenClick));
        stack.Children.Add(MakeRow("quit", OnQuitClick));

        border.Child = stack;
        Content = border;

        Deactivated += (_, _) => Hide();
    }

    private static System.Windows.Controls.Button MakeRow(string text, RoutedEventHandler onClick)
    {
        var btn = new System.Windows.Controls.Button
        {
            Content = text,
            Margin = new Thickness(0, 4, 0, 4),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
        };
        btn.Click += onClick;
        return btn;
    }

    public void ShowNearCursor()
    {
        var pos = System.Windows.Forms.Cursor.Position;
        var source = PresentationSource.FromVisual(this);
        double dpiScale = source is null ? 1.0 : 1.0; // NotifyIcon click coords are already device px.
        Left = pos.X - Width;
        Top = pos.Y - Height - 8;
        Show();
        Activate();
    }

    private void OnDictateClick(object sender, RoutedEventArgs e)
    {
        // TODO(P6 Part B): wire to DictationOrchestrator once tray has orchestrator access
        // (would need a shared IDictationTrigger seam — out of scope for local-persistence phase).
        Hide();
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        // TODO(P6 Part B): show/activate MainWindow. Requires a reference to the app's MainWindow
        // instance; deferred until the tray is composed alongside the main window in App.xaml.cs.
        foreach (System.Windows.Window w in Application.Current.Windows)
        {
            if (w.GetType().Name == "MainWindow")
            {
                w.Show();
                w.Activate();
                break;
            }
        }
        Hide();
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        Hide();
        Application.Current.Shutdown();
    }
}
