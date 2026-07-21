using Kivi.Core.Orchestration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace Kivi.App.Controls;

public sealed class KiviOrbControl : Canvas
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(RecordingState), typeof(KiviOrbControl),
            new PropertyMetadata(RecordingState.Idle, OnStateChanged));

    public RecordingState State
    {
        get => (RecordingState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    // Set once at startup from AppConfig.OrbAccentColor; used for the "your voice" states
    // (Listening/Speaking/Done). Idle/Processing/Waiting/Error keep fixed Overlay*Brush tokens.
    public static Microsoft.UI.Xaml.Media.Brush? AccentBrush { get; set; }

    private const int Columns = 24;
    private SoftwareBitmap? _mask;
    private readonly List<Ellipse> _dots = new();

    public KiviOrbControl()
    {
        Loaded += async (_, _) => await LoadMaskAndBuildDotsAsync();
        SizeChanged += (_, _) => { if (_mask is not null) BuildDots(); };
    }

    private async Task LoadMaskAndBuildDotsAsync()
    {
        var uri = new Uri("ms-appx:///Assets/Icons/kivi-mask.png");
        var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(uri);
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        _mask = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
        BuildDots();
        ApplyStateColor();
    }

    private void BuildDots()
    {
        if (_mask is null) return;
        Children.Clear();
        _dots.Clear();

        // Row count is derived from the CONTROL's own rendered aspect ratio, not the
        // mask's native aspect ratio. The window resizes this control between the small
        // (48x64) idle size and the larger (96x130) active size, both of which preserve
        // the mask's own ~0.74 aspect ratio, but deriving rows from the mask's raw pixel
        // dimensions instead of the control's actual rendered size would still risk
        // sampling at the wrong effective angle if that ever drifts. Sampling relative to
        // the control's actual width/height keeps each dot's (col, row) position mapped
        // proportionally into the mask regardless of target size.
        double renderW = ActualWidth > 0 ? ActualWidth : Width;
        double renderH = ActualHeight > 0 ? ActualHeight : Height;
        int rows = Math.Max(1, (int)Math.Round(renderH / renderW * Columns));
        double cellW = renderW / Columns;
        double cellH = renderH / rows;
        double dotSize = Math.Min(cellW, cellH) * 0.85;

        var buffer = new byte[4 * _mask.PixelWidth * _mask.PixelHeight];
        _mask.CopyToBuffer(buffer.AsBuffer());

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                int px = (int)((double)col / Columns * _mask.PixelWidth);
                int py = (int)((double)row / rows * _mask.PixelHeight);
                int offset = (py * _mask.PixelWidth + px) * 4;
                byte alpha = offset + 3 < buffer.Length ? buffer[offset + 3] : (byte)0;
                if (alpha < 32) continue; // transparent -> no dot here

                var dot = new Ellipse { Width = dotSize, Height = dotSize };
                SetLeft(dot, col * cellW + (cellW - dotSize) / 2);
                SetTop(dot, row * cellH + (cellH - dotSize) / 2);
                Children.Add(dot);
                _dots.Add(dot);
            }
        }
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((KiviOrbControl)d).ApplyStateColor();

    private void ApplyStateColor()
    {
        // Listening/Speaking/Done are the "your voice" states and tint from the
        // user-configurable AppConfig.OrbAccentColor; the rest keep fixed system tokens.
        Brush? brush = State switch
        {
            RecordingState.Listening => AccentBrush ?? TokenBrush("OverlayListeningBrush"),
            RecordingState.Speaking  => AccentBrush ?? TokenBrush("OverlaySpeakingBrush"),
            RecordingState.Done      => AccentBrush ?? TokenBrush("OverlayDoneBrush"),
            RecordingState.Idle       => TokenBrush("OverlayIdleBrush"),
            RecordingState.Processing => TokenBrush("OverlayProcessingBrush"),
            RecordingState.Waiting    => TokenBrush("OverlayWaitingBrush"),
            RecordingState.Error      => TokenBrush("OverlayErrorBrush"),
            _                         => TokenBrush("OverlayIdleBrush")
        };
        if (brush is null) return;
        foreach (var dot in _dots) dot.Fill = brush;
    }

    private static Brush? TokenBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var brushObj) && brushObj is Brush brush
            ? brush
            : null;
}
