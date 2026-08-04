
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
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kivi.Core.Hotkey;
using Kivi.App.Services;

namespace Kivi.App.Controls.Shell;

/// <summary>
/// A rebind field for the global hotkey. Click to arm, then press-and-hold your chord and release —
/// whatever was held at the peak of the press is captured. Unlike the old version it captures
/// MODIFIER-ONLY combos (Ctrl+Win, both Ctrls, both Alts) and distinguishes left/right, mapping WPF
/// keys to Windows virtual-key codes so the captured <see cref="HotkeyChord"/> matches exactly what
/// the low-level hook will see. A live verdict (ok / warn / blocked) is surfaced via
/// <see cref="Verdict"/> using <see cref="HotkeyCatalog.Assess"/>; a Blocked chord is not committed.
/// </summary>
public sealed class HotkeyCaptureField : Button
{
    private bool _capturing;
    private readonly TextBlock _label = new();

    // Keys currently held during a capture, and the richest chord seen this press (so a
    // press-then-release of Ctrl+Space commits Ctrl+Space, not the empty set on release).
    private readonly HashSet<int> _held = new();
    private HotkeyChord? _peak;

    public static readonly DependencyProperty ChordProperty = DependencyProperty.Register(
        nameof(Chord), typeof(string), typeof(HotkeyCaptureField),
        new PropertyMetadata("A3", (d, e) => ((HotkeyCaptureField)d).Render()));

    /// <summary>The committed chord in HotkeyChord storage form (e.g. "A3", "11-20").</summary>
    public string Chord { get => (string)GetValue(ChordProperty); set => SetValue(ChordProperty, value); }

    /// <summary>Raised with the committed chord's storage string when the user captures a new chord.</summary>
    public event System.EventHandler<string>? ChordChanged;

    /// <summary>Raised (possibly repeatedly during capture) with the live verdict, so the host can show
    /// a warning line. Fires with an Ok/null verdict when capture resets.</summary>
    public event System.EventHandler<HotkeyVerdict>? Verdict;

    public HotkeyCaptureField()
    {
        Height = 40;
        Padding = new Thickness(14, 0, 14, 0);
        Cursor = Cursors.Hand;
        Focusable = true;
        _label.FontFamily = (FontFamily?)TryFindResource("FontBody");
        _label.FontSize = 13;
        _label.FontWeight = FontWeights.Medium;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        Content = _label;
        Template = BuildTemplate();
        Click += (_, _) => Arm();
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        LostKeyboardFocus += (_, _) => { CancelCapture(); };
        Loaded += (_, _) => Render();
    }

    private static ControlTemplate BuildTemplate()
    {
        var t = new ControlTemplate(typeof(HotkeyCaptureField));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "bd";
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bd.SetResourceReference(Border.BorderBrushProperty, "Hairline");
        bd.SetResourceReference(Border.BackgroundProperty, "Surface2");
        bd.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        // Remove the dotted focus rectangle artifact.
        return t;
    }

    private void Arm()
    {
        _capturing = true;
        _held.Clear();
        _peak = null;
        Focus();
        Keyboard.Focus(this);
        Render();
    }

    private void CancelCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _held.Clear();
        _peak = null;
        Render();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;

        var wpfKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (wpfKey == Key.Escape) { CancelCapture(); return; }

        int vk = ToVk(wpfKey);
        if (vk == 0) return; // unmappable key — ignore

        _held.Add(vk);
        // The richest set held so far this press is the candidate.
        _peak = new HotkeyChord(_held);
        RaiseVerdict();
        Render();
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;

        var wpfKey = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = ToVk(wpfKey);
        if (vk != 0) _held.Remove(vk);

        // Commit when everything is released and we captured something.
        if (_held.Count == 0 && _peak is { } chord)
            Commit(chord);
    }

    private void Commit(HotkeyChord chord)
    {
        var verdict = HotkeyCatalog.Assess(chord);
        Verdict?.Invoke(this, verdict);
        if (verdict.Risk == HotkeyRisk.Blocked)
        {
            // Don't accept an impossible chord; stay armed so the user can try again.
            _peak = null;
            Render();
            return;
        }

        _capturing = false;
        _peak = null;
        Chord = chord.ToStorageString();
        Render();
        ChordChanged?.Invoke(this, Chord);
    }

    private void RaiseVerdict()
    {
        if (_peak is { } c) Verdict?.Invoke(this, HotkeyCatalog.Assess(c));
    }

    private void Render()
    {
        string text;
        if (_capturing)
            text = _peak is { } c ? HotkeyCatalog.Describe(c) + " …" : "press your keys…";
        else
            text = HotkeyChord.TryParse(Chord, out var chord) && chord is not null
                ? HotkeyCatalog.Describe(chord)
                : Chord;

        _label.Text = text;
        _label.Foreground = (Brush)(_capturing
            ? TryFindResource("Accent") : TryFindResource("InkSecondary")) ?? Brushes.Gray;
    }

    /// <summary>Map a WPF <see cref="Key"/> to a Windows virtual-key code, preserving left/right for
    /// modifiers (so "both Ctrls" is distinguishable). Returns 0 for keys we don't bind.</summary>
    private static int ToVk(Key key) => key switch
    {
        Key.LeftCtrl => Vk.LControl,
        Key.RightCtrl => Vk.RControl,
        Key.LeftAlt => Vk.LMenu,
        Key.RightAlt => Vk.RMenu,
        Key.LeftShift => Vk.LShift,
        Key.RightShift => Vk.RShift,
        Key.LWin => Vk.LWin,
        Key.RWin => Vk.RWin,
        Key.Space => Vk.Space,
        Key.Tab => Vk.Tab,
        Key.Delete => Vk.Delete,
        _ => KeyInterop.VirtualKeyFromKey(key) is var v && v is > 0 and <= 0xFF ? v : 0,
    };
}
