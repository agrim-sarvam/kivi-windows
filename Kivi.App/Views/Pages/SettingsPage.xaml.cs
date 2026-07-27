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
using Appearance = Kivi.App.Themes.Appearance;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Application = System.Windows.Application;
using Orientation = System.Windows.Controls.Orientation;
using ComboBox = System.Windows.Controls.ComboBox;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Kivi.App.Controls.Shell;
using Kivi.App.Themes;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

public partial class SettingsPage : UserControl
{
    private string _selected = SettingsModel.DefaultPane;

    public SettingsPage()
    {
        InitializeComponent();
        BuildRail(string.Empty);
        ShowPane(_selected);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed;
        BuildRail(Search.Text);
    }

    private void BuildRail(string query)
    {
        PaneRail.Children.Clear();
        var groups = SettingsModel.Filter(query);
        if (groups.Length == 0)
        {
            PaneRail.Children.Add(new TextBlock
            {
                Text = $"no settings match “{query.Trim()}”", Margin = new Thickness(12, 4, 12, 4),
                FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 12, Foreground = (Brush)FindResource("InkTertiary"),
            });
            return;
        }
        foreach (var g in groups)
        {
            PaneRail.Children.Add(new TextBlock
            {
                Text = g.Title, Margin = new Thickness(12, 8, 12, 2),
                FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 11, Foreground = (Brush)FindResource("InkTertiary"),
            });
            foreach (var p in g.Panes)
                PaneRail.Children.Add(BuildPaneRow(p));
        }
    }

    private Button BuildPaneRow(SettingsModel.Pane p)
    {
        bool sel = p.Id == _selected;
        var bd = new Border
        {
            Height = 34, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 0, 12, 0),
            Background = sel ? (Brush)FindResource("AccentWash") : Brushes.Transparent,
            Child = new TextBlock
            {
                Text = p.Title, VerticalAlignment = VerticalAlignment.Center,
                FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 15,
                Foreground = sel ? (Brush)FindResource("InkPrimary") : (Brush)FindResource("InkSecondary"),
            },
        };
        var btn = new Button { Content = bd, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 1, 0, 1) };
        btn.Template = Passthrough();
        btn.Click += (_, _) => { _selected = p.Id; BuildRail(Search.Text); ShowPane(p.Id); };
        return btn;
    }

    private static ControlTemplate Passthrough()
    {
        var t = new ControlTemplate(typeof(Button));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        t.VisualTree = cp;
        return t;
    }

    private void ShowPane(string id)
    {
        Detail.Children.Clear();
        var found = SettingsModel.Find(id);
        if (found == null) return;
        var (pane, showsReset) = found.Value;

        // head
        var head = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headText = new StackPanel();
        headText.Children.Add(new TextBlock { Text = pane.Title, Style = (Style)FindResource("SectionTitleDisplay") });
        headText.Children.Add(new TextBlock { Text = pane.Subtitle, Style = (Style)FindResource("Subtitle"), Margin = new Thickness(0, 6, 0, 0) });
        head.Children.Add(headText);
        if (showsReset)
        {
            var reset = new Button { Content = "↺ reset", Style = (Style)FindResource("TextButton"), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(reset, 1);
            head.Children.Add(reset);
        }
        Detail.Children.Add(head);

        foreach (var sec in PaneContent(id)) Detail.Children.Add(sec);
    }

    // ---- pane content ----
    private IEnumerable<UIElement> PaneContent(string id) => id switch
    {
        "general" => new[]
        {
            Section("general", Row("appearance", AppearanceControl(), true), Row("language", MenuPill("auto-detect"), false), Row("welcome demo", Ghost("replay"), false)),
            Section("shortcuts", Row("kivi key", HotkeyField("fn"), true), Row("cancel take", Keycap("esc"), false), Row("paste last", Keycap("⌃⇧v"), false)),
            Section("privacy", Row("code-mix (hinglish)", Switch(true), true), Row("retry failed dictations", Switch(true), false), Row("personalization", Value("on"), false)),
        },
        "orb" => new[]
        {
            Section("the orb", Row("theme", Value("forest"), true), Row("size", Value("medium"), false), Row("placement", Value("top · docked"), false)),
            Section("do not disturb", Row("mute cues", Switch(false), true), Row("sounds", Switch(true), false)),
        },
        "system" => new[]
        {
            Section("microphone", Row("input device", Value("default"), true), Row("microphone access", Granted(), false)),
            Section("permissions", Row("accessibility", Granted(), true), Row("screen context", Ghost("grant…"), false)),
            Section("dictation", Row("press enter to send", Switch(false), true), Row("keep in orb", Switch(false), false)),
        },
        "plan" => new[]
        {
            Section("plan & billing", Row("current plan", Value("free"), true), Row("words this month", Value("1,240 / 10,000"), false), Row("upgrade", Ghost("see plans"), false)),
        },
        "invite" => new[] { Placeholder("invite friends is on the way", "soon you'll be able to give friends a head start on kivi and grow your flock. we're building it now.") },
        "org" => new[] { Placeholder("you're a member of this workspace", "workspace and member management is available to owners and admins.") },
        "account" => new[]
        {
            Section("account", Row("name", Value("abhigyan"), true), Row("email", Value("abhigyan@sarvam.ai"), false), Row("sign out", Ghost("sign out"), false)),
        },
        "advanced" => new[]
        {
            Section("advanced", Row("endpoint", Value("qa"), true), Row("software update", Ghost("check…"), false), Row("calm motion", Value("follow system"), false)),
        },
        _ => Array.Empty<UIElement>(),
    };

    private UIElement Section(string title, params UIElement[] rows)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        sp.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("SectionTitleDisplay"), FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
        var card = new Border { Background = (Brush)FindResource("Surface1"), BorderBrush = (Brush)FindResource("Hairline"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        var inner = new StackPanel();
        card.Child = inner;
        foreach (var r in rows) inner.Children.Add(r);
        sp.Children.Add(card);
        return sp;
    }

    private UIElement Row(string label, UIElement control, bool first)
    {
        var grid = new Grid { MinHeight = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 15, Foreground = (Brush)FindResource("InkPrimary"), Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(lbl, 0);
        var host = new ContentControl { Content = control, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(host, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(host);
        var bd = new Border { Padding = new Thickness(0, 8, 0, 8), Child = grid };
        if (!first) bd.BorderBrush = (Brush)FindResource("Hairline");
        if (!first) bd.BorderThickness = new Thickness(0, 1, 0, 0);
        return bd;
    }

    private UIElement Value(string s) => new TextBlock { Text = s, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 13, Foreground = (Brush)FindResource("InkTertiary") };
    private UIElement Granted()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = "✓ ", FontSize = 12, Foreground = (Brush)FindResource("Accent") });
        sp.Children.Add(new TextBlock { Text = "granted", FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 12, Foreground = (Brush)FindResource("Accent") });
        return sp;
    }
    private UIElement Ghost(string s) => new Button { Content = s, Style = (Style)FindResource("GhostButton") };
    private UIElement Keycap(string s) => new Border
    {
        Background = (Brush)FindResource("Surface2"), BorderBrush = (Brush)FindResource("Hairline"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1),
        Child = new TextBlock { Text = s, FontFamily = (FontFamily)FindResource("FontBody"), FontWeight = FontWeights.Medium, FontSize = 11, Foreground = (Brush)FindResource("InkSecondary") },
    };
    private UIElement HotkeyField(string chord) => new HotkeyCaptureField { Chord = chord };

    private UIElement Switch(bool on)
    {
        var toggle = new ToggleButton { IsChecked = on, Width = 30, Height = 18, Cursor = System.Windows.Input.Cursors.Hand };
        toggle.Template = SwitchTemplate();
        return toggle;
    }

    private ControlTemplate SwitchTemplate()
    {
        var t = new ControlTemplate(typeof(ToggleButton));
        var track = new FrameworkElementFactory(typeof(Border));
        track.Name = "track";
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        track.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        track.SetResourceReference(Border.BorderBrushProperty, "Hairline");
        track.SetResourceReference(Border.BackgroundProperty, "Surface2");
        var knob = new FrameworkElementFactory(typeof(Border));
        knob.Name = "knob";
        knob.SetValue(Border.WidthProperty, 14.0);
        knob.SetValue(Border.HeightProperty, 14.0);
        knob.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        knob.SetResourceReference(Border.BackgroundProperty, "Canvas");
        knob.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        knob.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
        knob.SetValue(Border.MarginProperty, new Thickness(2, 0, 0, 0));
        track.AppendChild(knob);
        t.VisualTree = track;

        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Accent"), "track"));
        on.Setters.Add(new Setter(Border.MarginProperty, new Thickness(14, 0, 0, 0), "knob"));
        t.Triggers.Add(on);
        return t;
    }

    private UIElement MenuPill(string current)
    {
        var bd = new Border { Background = (Brush)FindResource("Surface1"), BorderBrush = (Brush)FindResource("Hairline"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 0, 10, 0), Height = 28 };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = current, FontFamily = (FontFamily)FindResource("FontBody"), FontWeight = FontWeights.Medium, FontSize = 13, Foreground = (Brush)FindResource("InkPrimary") });
        sp.Children.Add(new TextBlock { Text = " ⌄", FontSize = 12, Foreground = (Brush)FindResource("InkTertiary") });
        bd.Child = sp;
        return bd;
    }

    // Appearance is the only live-wired control (theme authority).
    private UIElement AppearanceControl()
    {
        var combo = new ComboBox { Width = 120, Height = 28 };
        combo.Items.Add("system");
        combo.Items.Add("light");
        combo.Items.Add("dark");
        combo.SelectedItem = ThemeManager.Instance.Appearance.ToString().ToLowerInvariant();
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string s && Enum.TryParse<Appearance>(s, true, out var a))
                ThemeManager.Instance.Appearance = a;
        };
        return combo;
    }

    private UIElement Placeholder(string head, string detail)
    {
        var card = new Border { Background = (Brush)FindResource("Surface1"), BorderBrush = (Brush)FindResource("Hairline"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(24, 48, 24, 48) };
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = head, Style = (Style)FindResource("SectionTitleDisplay"), HorizontalAlignment = HorizontalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("Subtitle"), MaxWidth = 360, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        card.Child = sp;
        return card;
    }
}
