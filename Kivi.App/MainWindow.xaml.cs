// WPF/WinForms type disambiguation (project enables both UseWPF and UseWindowsForms).
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Application = System.Windows.Application;
using Orientation = System.Windows.Controls.Orientation;
using ComboBox = System.Windows.Controls.ComboBox;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Kivi.App.Themes;
using Kivi.App.ViewModels;
using Kivi.App.Views.Pages;

namespace Kivi.App;

/// <summary>
/// The Canon shell (M5): frameless 1180x760 window, rail + detail with a hard-cut page host.
/// Port of main-window/App.tsx. Theme is applied on load; Ctrl+\ toggles the rail.
/// </summary>
public partial class MainWindow : Window
{
    public AppNavigation Nav { get; } = new();

    public MainWindow()
    {
        // Apply the resolved Canon theme before first paint.
        ThemeManager.Instance.Apply();

        InitializeComponent();
        DataContext = this;

        Nav.PropertyChanged += OnNavChanged;
        ThemeManager.Instance.MoodChanged += (_, _) => CrossfadePage();

        Loaded += (_, _) => SwapPage(Nav.Section);

        var toggle = new RoutedCommand();
        InputBindings.Add(new KeyBinding(new RelayCommandShim(() => Nav.ToggleCollapse()),
            new KeyGesture(Key.OemBackslash, ModifierKeys.Control)));
    }

    private void OnNavChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppNavigation.Section)) SwapPage(Nav.Section);
    }

    /// <summary>Hard-cut section switch (App.tsx keyed remount — no cross-page transition).</summary>
    private void SwapPage(AppSection section)
    {
        PageHost.Content = CreatePage(section);
    }

    private void CrossfadePage()
    {
        // 240ms crossfade on theme change (map §0). Re-render background too.
        Bg.InvalidateVisual();
        if (PageHost.Content is UIElement el)
        {
            var anim = new DoubleAnimation(0.0, 1.0, System.TimeSpan.FromMilliseconds(240));
            el.BeginAnimation(OpacityProperty, anim);
        }
    }

    private static UserControl CreatePage(AppSection section) => section switch
    {
        AppSection.Record => new RecordPage(),
        AppSection.History => new HistoryPage(),
        AppSection.Settings => new SettingsPage(),
        AppSection.Memory => new MemoryPage(),
        AppSection.Shortcuts => new ShortcutsPage(),
        AppSection.Styles or AppSection.Presets => new PersonasPage(),
        AppSection.Analytics => new AnalyticsPage(),
        AppSection.SharedTerms => new SharedTermsPage(),
        AppSection.Leaderboard => new LeaderboardPage(),
        AppSection.Clipboard => StubPage.ClipboardStub(),
        _ => new RecordPage(),
    };

    /// <summary>
    /// Show the first-run onboarding IN-WINDOW as a full-cover overlay (not a separate window). The
    /// host wires the live-rebind callback; when the user finishes we hide the overlay, invoke
    /// <paramref name="onDone"/> (persist the flag/chord), and reveal the normal shell underneath.
    /// </summary>
    public void ShowOnboarding(
        Kivi.Core.Hotkey.HotkeyChord? initial,
        System.Action<Kivi.Core.Hotkey.HotkeyChord> onChordChosen,
        System.Action<Kivi.Core.Hotkey.HotkeyChord> onDone)
    {
        var view = new Views.Onboarding.OnboardingView(initial);
        view.ChordChosen += (_, chord) => onChordChosen(chord);
        view.Completed += (_, chord) =>
        {
            OnboardingHost.Visibility = Visibility.Collapsed;
            OnboardingHost.Content = null;
            onDone(chord);
        };
        OnboardingHost.Content = view;
        OnboardingHost.Visibility = Visibility.Visible;
    }

    private void CollapseToggle_Click(object sender, RoutedEventArgs e) => Nav.ToggleCollapse();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaxRestore_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Closing drops to resident agent (orb+tray stay alive); for now just hide the window.
        Hide();
    }
}

/// <summary>Minimal ICommand for a KeyBinding without pulling in a full command object.</summary>
internal sealed class RelayCommandShim : ICommand
{
    private readonly System.Action _run;
    public RelayCommandShim(System.Action run) => _run = run;
    public event System.EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _run();
}
