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
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kivi.App.Drawing;
using Kivi.App.Themes;

namespace Kivi.App.Controls.Shell;

/// <summary>
/// The shell's detail background — ported from Background.tsx: the Canon canvas plane
/// (owner sets it) + a tiled paper-grain layer (reused P4 tile) + a 24px-pitch 2px dot
/// constellation grid in inkTertiary. Honors reduce-transparency (drops the grain).
/// </summary>
public sealed class ShellBackground : FrameworkElement
{
    private ImageSource? _grainTile;
    private bool _grainDark;

    public ShellBackground()
    {
        ThemeManager.Instance.MoodChanged += (_, _) => { _grainTile = null; InvalidateVisual(); };
    }

    protected override void OnRender(DrawingContext dc)
    {
        bool dark = ThemeManager.Instance.IsDark;
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        // Paper grain (skip under reduce-transparency / reduce-motion is a separate flag).
        if (!SystemParametersHelper.ReduceTransparency)
        {
            var tile = GrainTile(dark);
            if (tile != null)
            {
                double scale = dark ? 1.5 : 1.0;
                double tileSize = PaperGrain.Tile * scale;
                var tb = new ImageBrush(tile)
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, tileSize, tileSize),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Stretch = Stretch.Fill,
                    Opacity = PaperGrain.Opacity(dark),
                };
                tb.Freeze();
                dc.DrawRectangle(tb, null, rect);
            }
        }

        // Constellation dots: 24px pitch, 2px dots, inkTertiary, opacity 0.06 light / 0.08 dark.
        var dotColor = (Color)FindResource("InkTertiaryColor");
        var dotBrush = new SolidColorBrush(dotColor) { Opacity = dark ? 0.08 : 0.06 };
        dotBrush.Freeze();
        var dotGeo = new EllipseGeometry(new Point(17, 17), 1.0, 1.0); // ~2px dot
        var tileBrush = new DrawingBrush(new GeometryDrawing(dotBrush, null, dotGeo))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 24, 24),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        tileBrush.Freeze();
        dc.DrawRectangle(tileBrush, null, rect);
    }

    private object FindResource(string key) => Application.Current.TryFindResource(key) ?? Colors.Gray;

    private ImageSource? GrainTile(bool dark)
    {
        if (_grainTile != null && _grainDark == dark) return _grainTile;
        var bmp = PaperGrain.TileBitmap(dark);
        var hbmp = bmp.GetHbitmap();
        try
        {
            _grainTile = Imaging.CreateBitmapSourceFromHBitmap(
                hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            _grainTile.Freeze();
        }
        finally { NativeDelete(hbmp); }
        _grainDark = dark;
        return _grainTile;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
    private static void NativeDelete(IntPtr h) { try { DeleteObject(h); } catch { } }
}

internal static class SystemParametersHelper
{
    public static bool ReduceTransparency
    {
        get
        {
            // Windows "Transparency effects" off → treat as reduce-transparency.
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var v = k?.GetValue("EnableTransparency");
                if (v is int i) return i == 0;
            }
            catch { }
            return false;
        }
    }

    public static bool ReduceMotion => !System.Windows.SystemParameters.ClientAreaAnimation;
}
