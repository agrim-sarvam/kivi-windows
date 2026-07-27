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
using System.Windows.Controls;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

public partial class StubPage : UserControl
{
    public StubPage()
    {
        InitializeComponent();
    }

    public StubPage(string title, string subtitle, RailIconName icon, string note) : this()
    {
        Header.Title = title;
        Header.Subtitle = subtitle;
        Glyph.Icon = icon;
        Note.Text = note;
    }

    public static StubPage ClipboardStub() => new(
        "clipboard",
        "your opted-in clipboard history.",
        RailIconName.Layers,
        "everything you've copied through kivi, one click to paste again. landing soon.");
}
