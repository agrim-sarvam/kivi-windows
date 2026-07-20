using Kivi.Core.Orchestration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace Kivi.App.Controls;

/// <summary>
/// The four named orb postures from the design's mockups page, each with its own
/// rendered size: RestPill 39x15, Woken 61x61, Satellites 23x23, Box 322x108.
/// </summary>
public enum KiviOrbPosture
{
    RestPill,
    Woken,
    Satellites,
    Box
}

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

    public static readonly DependencyProperty PostureProperty =
        DependencyProperty.Register(nameof(Posture), typeof(KiviOrbPosture), typeof(KiviOrbControl),
            new PropertyMetadata(KiviOrbPosture.RestPill, OnPostureChanged));

    public KiviOrbPosture Posture
    {
        get => (KiviOrbPosture)GetValue(PostureProperty);
        set => SetValue(PostureProperty, value);
    }

    private const int Columns = 24;
    private SoftwareBitmap? _mask;
    private readonly List<Ellipse> _dots = new();

    public KiviOrbControl()
    {
        Loaded += async (_, _) => await LoadMaskAndBuildDotsAsync();
    }

    private static (double Width, double Height) SizeFor(KiviOrbPosture posture) => posture switch
    {
        KiviOrbPosture.RestPill    => (39, 15),
        KiviOrbPosture.Woken       => (61, 61),
        KiviOrbPosture.Satellites  => (23, 23),
        KiviOrbPosture.Box         => (322, 108),
        _                          => (39, 15)
    };

    private async Task LoadMaskAndBuildDotsAsync()
    {
        var uri = new Uri("ms-appx:///Assets/Icons/kivi-mask.png");
        var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(uri);
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        _mask = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
        ApplySize();
        BuildDots();
        ApplyStateColor();
    }

    private void ApplySize()
    {
        var (w, h) = SizeFor(Posture);
        Width = w;
        Height = h;
    }

    private void BuildDots()
    {
        if (_mask is null) return;
        Children.Clear();
        _dots.Clear();

        // Row count is derived from the CONTROL's own rendered aspect ratio, not the
        // mask's native aspect ratio. Postures (e.g. the 322x108 "Box") can have a very
        // different aspect ratio than the 120x162-ish mask; deriving rows from the mask's
        // own dimensions here caused the dot grid's cells to be sampled at the wrong
        // effective angle, streaking the silhouette into unrecognizable vertical bars.
        // Sampling relative to the control's actual width/height keeps each dot's (col,
        // row) position mapped proportionally into the mask regardless of target shape.
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

    private static void OnPostureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (KiviOrbControl)d;
        control.ApplySize();
        if (control._mask is not null) control.BuildDots();
    }

    private void ApplyStateColor()
    {
        var key = State switch
        {
            RecordingState.Idle       => "OverlayIdleBrush",
            RecordingState.Listening  => "OverlayListeningBrush",
            RecordingState.Processing => "OverlayProcessingBrush",
            RecordingState.Speaking   => "OverlaySpeakingBrush",
            RecordingState.Waiting    => "OverlayWaitingBrush",
            RecordingState.Done       => "OverlayDoneBrush",
            RecordingState.Error      => "OverlayErrorBrush",
            _                         => "OverlayIdleBrush"
        };
        if (Application.Current.Resources.TryGetValue(key, out var brushObj) && brushObj is Brush brush)
        {
            foreach (var dot in _dots) dot.Fill = brush;
        }
    }
}
