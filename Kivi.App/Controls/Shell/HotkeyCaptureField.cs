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
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Application = System.Windows.Application;
using Orientation = System.Windows.Controls.Orientation;
using ComboBox = System.Windows.Controls.ComboBox;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Kivi.App.Controls.Shell;

/// <summary>
/// A rebind field for the global hotkey (map §5.10). Click to arm, then press a chord;
/// the captured chord is displayed and raised via ChordChanged. This records the chord only
/// (the actual global rebind wiring into the hotkey service is a later platform concern).
/// </summary>
public sealed class HotkeyCaptureField : Button
{
    private bool _capturing;
    private readonly TextBlock _label = new();

    public static readonly DependencyProperty ChordProperty = DependencyProperty.Register(
        nameof(Chord), typeof(string), typeof(HotkeyCaptureField),
        new PropertyMetadata("fn", (d, e) => ((HotkeyCaptureField)d).Render()));

    public string Chord { get => (string)GetValue(ChordProperty); set => SetValue(ChordProperty, value); }

    public event System.EventHandler<string>? ChordChanged;

    public HotkeyCaptureField()
    {
        Height = 28;
        Padding = new Thickness(12, 0, 12, 0);
        Cursor = Cursors.Hand;
        Focusable = true;
        _label.FontFamily = (FontFamily?)TryFindResource("FontBody");
        _label.FontSize = 12;
        _label.FontWeight = FontWeights.Medium;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        Content = _label;
        Template = BuildTemplate();
        Click += (_, _) => Arm();
        PreviewKeyDown += OnKey;
        LostKeyboardFocus += (_, _) => { _capturing = false; Render(); };
        Loaded += (_, _) => Render();
    }

    private static ControlTemplate BuildTemplate()
    {
        var t = new ControlTemplate(typeof(HotkeyCaptureField));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "bd";
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bd.SetResourceReference(Border.BorderBrushProperty, "Hairline");
        bd.SetResourceReference(Border.BackgroundProperty, "Surface2");
        bd.SetValue(Border.PaddingProperty, new Thickness(6, 2, 6, 2));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        return t;
    }

    private void Arm()
    {
        _capturing = true;
        Focus();
        Keyboard.Focus(this);
        Render();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return; // wait for the non-modifier

        if (key == Key.Escape) { _capturing = false; Render(); return; }

        var parts = new List<string>();
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("win");
        parts.Add(key.ToString().ToLowerInvariant());

        Chord = string.Join("+", parts);
        _capturing = false;
        Render();
        ChordChanged?.Invoke(this, Chord);
    }

    private void Render()
    {
        _label.Text = _capturing ? "press a key…" : Chord;
        _label.Foreground = (Brush)(_capturing
            ? TryFindResource("Accent") : TryFindResource("InkSecondary")) ?? Brushes.Gray;
    }
}
