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
        // NotifyIcon MouseUp fires with the cursor at the icon, in physical (device) pixels; WPF
        // window Left/Top are DIPs. Convert via the screen's DPI so the popover lands exactly at
        // the tray icon regardless of monitor scaling — a raw physical->DIP 1:1 assumption (the
        // previous version) drifted the popover off-screen/behind other windows on any DPI != 100%.
        var cursorPx = System.Windows.Forms.Cursor.Position;
        double dpiScale = GetDpiScaleForPoint(cursorPx);
        double cursorDipX = cursorPx.X / dpiScale;
        double cursorDipY = cursorPx.Y / dpiScale;

        double left = cursorDipX - Width;
        double top = cursorDipY - Height - 8;

        // Clamp to the containing screen's work area (DIPs) so the popover never renders off-screen
        // (e.g. a taskbar pinned to the top, or a tray icon near a monitor edge).
        var screen = System.Windows.Forms.Screen.FromPoint(cursorPx);
        var wa = screen.WorkingArea; // physical px
        double waLeft = wa.Left / dpiScale, waTop = wa.Top / dpiScale;
        double waRight = wa.Right / dpiScale, waBottom = wa.Bottom / dpiScale;
        left = Math.Max(waLeft + 4, Math.Min(left, waRight - Width - 4));
        top = Math.Max(waTop + 4, Math.Min(top, waBottom - Height - 4));

        Left = left;
        Top = top;

        // Show + force to the foreground reliably: a plain Show()/Activate() can lose the race
        // against the OS's own focus rules for a just-clicked tray icon (the previous version's
        // popover could show but land BEHIND the always-on-top orb window or simply never receive
        // activation). Topmost is already set; toggling it re-asserts z-order, and
        // ShowActivated + a Win32-level foreground nudge make this deterministic.
        Topmost = false;
        Topmost = true;
        Show();
        Activate();
        Focus();
    }

    private static double GetDpiScaleForPoint(System.Drawing.Point p)
    {
        try
        {
            var screen = System.Windows.Forms.Screen.FromPoint(p);
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
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
