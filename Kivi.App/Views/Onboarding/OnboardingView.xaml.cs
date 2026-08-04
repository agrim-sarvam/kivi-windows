using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Kivi.Core.Hotkey;
using Kivi.App.Services;

// WPF/WinForms disambiguation.
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// First-run welcome + hotkey chooser, hosted IN-WINDOW (over the main shell) rather than as a
/// separate window. The two general questions are purely cosmetic (held in memory, never sent). The
/// hotkey section is LIVE: selecting a preset card or recording a custom chord fires
/// <see cref="ChordChosen"/> so the host rebinds the real global hotkey immediately;
/// <see cref="Completed"/> fires when the user clicks "Start dictating".
/// </summary>
public partial class OnboardingView : UserControl
{
    /// <summary>Fired continuously as the user changes their pick — host rebinds the live hotkey.</summary>
    public event EventHandler<HotkeyChord>? ChordChosen;

    /// <summary>Fired once when the user finishes onboarding, carrying the final chord.</summary>
    public event EventHandler<HotkeyChord>? Completed;

    private readonly List<ToggleButton> _presetCards = new();
    private HotkeyChord _selected = HotkeyCatalog.Default;

    public OnboardingView(HotkeyChord? initial = null)
    {
        InitializeComponent();
        _selected = initial ?? HotkeyCatalog.Default;

        BuildPresetCards();
        Capture.Chord = _selected.ToStorageString();
        Capture.ChordChanged += (_, storage) =>
        {
            if (HotkeyChord.TryParse(storage, out var c) && c is not null) Select(c, fromCapture: true);
        };
        Capture.Verdict += (_, verdict) => ShowVerdict(verdict);

        StartButton.Click += (_, _) => Completed?.Invoke(this, _selected);

        SyncUi();
    }

    private void BuildPresetCards()
    {
        foreach (var preset in HotkeyCatalog.Presets)
        {
            var card = new ToggleButton
            {
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 10),
                FocusVisualStyle = null,
                Tag = preset.Chord,
                Template = CardTemplate(),
                Content = CardContent(preset),
            };
            card.Checked += (s, _) =>
            {
                if (((ToggleButton)s).Tag is HotkeyChord c) Select(c, fromCapture: false);
            };
            _presetCards.Add(card);
            PresetGrid.Items.Add(card);
        }
    }

    private object CardContent(HotkeyPreset preset)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };

        panel.Children.Add(new TextBlock
        {
            Text = preset.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#E9F2E3"),
            Margin = new Thickness(0, 0, 0, 8),
        });

        var caps = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var cap in HotkeyCatalog.Keycaps(preset.Chord))
            caps.Children.Add(Keycap(cap));
        panel.Children.Add(caps);

        panel.Children.Add(new TextBlock
        {
            Text = preset.Subtitle,
            FontSize = 11.5,
            Foreground = Brush("#6F7A6A"),
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    private Border Keycap(string text) => new()
    {
        Background = Brush("#232A24"),
        BorderBrush = Brush("#33402F"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(8, 3, 8, 3),
        Margin = new Thickness(0, 0, 5, 0),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Brush("#CBE0BE"),
        },
    };

    private ControlTemplate CardTemplate()
    {
        var t = new ControlTemplate(typeof(ToggleButton));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "b";
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        bd.SetValue(Border.BackgroundProperty, Brush("#171B18"));
        bd.SetValue(Border.BorderBrushProperty, Brush("#2A2F2A"));
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        bd.AppendChild(cp);
        t.VisualTree = bd;

        var hover = new Trigger { Property = ToggleButton.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#1E241F"), "b"));
        t.Triggers.Add(hover);

        var checkedTrig = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrig.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#1F8FD06A"), "b"));
        checkedTrig.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#8FD06A"), "b"));
        t.Triggers.Add(checkedTrig);
        return t;
    }

    private void Select(HotkeyChord chord, bool fromCapture)
    {
        _selected = chord;
        if (!fromCapture) Capture.Chord = chord.ToStorageString();
        SyncUi();
        ChordChosen?.Invoke(this, chord);
    }

    private void SyncUi()
    {
        foreach (var card in _presetCards)
            card.IsChecked = card.Tag is HotkeyChord c && c.Equals(_selected);

        CurrentChord.Text = HotkeyCatalog.Describe(_selected);
        ShowVerdict(HotkeyCatalog.Assess(_selected));
    }

    private void ShowVerdict(HotkeyVerdict verdict)
    {
        if (verdict.Risk == HotkeyRisk.Ok || verdict.Message is null)
        {
            VerdictText.Visibility = Visibility.Collapsed;
            return;
        }
        VerdictText.Visibility = Visibility.Visible;
        VerdictText.Text = (verdict.Risk == HotkeyRisk.Blocked ? "⛔  " : "⚠  ") + verdict.Message;
        VerdictText.Foreground = Brush(verdict.Risk == HotkeyRisk.Blocked ? "#F0716F" : "#E6A11B");
    }

    private static Brush Brush(string hex) =>
        new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
}
