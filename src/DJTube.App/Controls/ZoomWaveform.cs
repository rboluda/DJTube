using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DJTube.Core.Logic;
using DJTube.Core.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace DJTube.App.Controls;

/// <summary>
/// Vista ampliada que se desplaza (estilo AtomixMP3): el cabezal fijo en el centro y la onda corriendo,
/// con las líneas de beat de la pista. La escala es de tiempo real: con el pitch subido los beats se
/// juntan, así que dos platos al mismo tempo muestran sus líneas a la misma distancia.
/// Rueda = zoom · arrastrar = mover la posición.
/// </summary>
public sealed class ZoomWaveform : SKElement
{
    public const double MinSecondsVisible = 1;
    public const double MaxSecondsVisible = 30;

    public static readonly DependencyProperty WaveformProperty = Register(nameof(Waveform), typeof(WaveformData), null);
    public static readonly DependencyProperty PositionProperty = Register(nameof(Position), typeof(double), 0.0);
    public static readonly DependencyProperty RateProperty = Register(nameof(Rate), typeof(double), 1.0);
    public static readonly DependencyProperty GridProperty = Register(nameof(Grid), typeof(BeatGrid), null);
    public static readonly DependencyProperty CuePointProperty = Register(nameof(CuePoint), typeof(double), 0.0);
    public static readonly DependencyProperty AccentProperty = Register(nameof(Accent), typeof(Color), Colors.DodgerBlue);
    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), null);
    public static readonly DependencyProperty MarkersProperty = Register(nameof(Markers), typeof(DeckMarkers), null);

    public static readonly DependencyProperty SecondsVisibleProperty = DependencyProperty.Register(
        nameof(SecondsVisible), typeof(double), typeof(ZoomWaveform),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender
            | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Recibe la posición en segundos de pista a la que saltar.</summary>
    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(ZoomWaveform));

    private static readonly SKTypeface LabelTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);

    private Point _dragStart;
    private double _dragStartPosition;

    public ZoomWaveform()
    {
        Cursor = Cursors.SizeWE;
        Services.ThemeManager.Changed += (_, _) => InvalidateVisual();
    }

    public WaveformData? Waveform { get => (WaveformData?)GetValue(WaveformProperty); set => SetValue(WaveformProperty, value); }

    /// <summary>Posición (segundos de pista) bajo el cabezal central.</summary>
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    /// <summary>Velocidad de reproducción (1 + pitch).</summary>
    public double Rate { get => (double)GetValue(RateProperty); set => SetValue(RateProperty, value); }

    public BeatGrid? Grid { get => (BeatGrid?)GetValue(GridProperty); set => SetValue(GridProperty, value); }

    public double CuePoint { get => (double)GetValue(CuePointProperty); set => SetValue(CuePointProperty, value); }

    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>Hot cues y loop del plato.</summary>
    public DeckMarkers? Markers { get => (DeckMarkers?)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }

    /// <summary>Segundos de tiempo real que caben de lado a lado (zoom).</summary>
    public double SecondsVisible { get => (double)GetValue(SecondsVisibleProperty); set => SetValue(SecondsVisibleProperty, value); }

    public ICommand? SeekCommand { get => (ICommand?)GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }

    /// <summary>Segundos de pista por píxel físico.</summary>
    private double TrackSecondsPerPixel(int pixelWidth) =>
        Math.Clamp(SecondsVisible, MinSecondsVisible, MaxSecondsVisible) * Math.Max(0.01, Rate) / Math.Max(1, pixelWidth);

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        var canvas = e.Surface.Canvas;
        int w = e.Info.Width, h = e.Info.Height;
        float scale = w / (float)Math.Max(1, ActualWidth);
        canvas.Clear(WaveformPaint.Background);

        double spp = TrackSecondsPerPixel(w);
        double center = w / 2.0;
        double position = Position;
        double TimeAt(int x) => position + (x - center) * spp;
        float XAt(double t) => (float)(center + (t - position) / spp);

        using (var axis = new SKPaint { Color = WaveformPaint.Grid, StrokeWidth = 1 })
            canvas.DrawLine(0, h / 2f, w, h / 2f, axis);

        if (Markers?.Loop is { } loop && Waveform is not null)
            WaveformPaint.DrawLoop(canvas, XAt(loop.Start), XAt(loop.End), h, scale);

        if (Waveform is { } waveform)
            WaveformPaint.DrawColumns(canvas, waveform, w, h, TimeAt, spp);

        if (Grid is { } grid && Waveform is { } wf)
        {
            double tLeft = Math.Max(0, TimeAt(0)), tRight = Math.Min(wf.DurationSeconds, TimeAt(w));
            long first = (long)Math.Ceiling(grid.BeatAt(tLeft));
            long last = (long)Math.Floor(grid.BeatAt(tRight));
            using var beat = new SKPaint { Color = SKColors.White.WithAlpha(70), StrokeWidth = 1 * scale };
            using var bar = new SKPaint { Color = SKColors.White.WithAlpha(170), StrokeWidth = 2 * scale };
            for (long k = Math.Max(first, 0); k <= last; k++)
            {
                float x = XAt(grid.TimeOfBeat(k));
                canvas.DrawLine(x, 0, x, h, k % 4 == 0 ? bar : beat);
            }
        }

        if (Markers is { } markers && Waveform is not null)
        {
            for (int i = 0; i < markers.HotCues.Count; i++)
            {
                if (markers.HotCues[i] is not { } t)
                    continue;
                float x = XAt(t);
                if (x >= -20 && x <= w + 20)
                    WaveformPaint.DrawHotCue(canvas, x, h, scale, i, compact: false);
            }
        }

        if (Waveform is not null)
        {
            float cueX = XAt(CuePoint);
            if (cueX >= -10 && cueX <= w + 10)
                WaveformPaint.DrawCueMarker(canvas, cueX, h, scale);
        }

        WaveformPaint.DrawPlayhead(canvas, (float)center, h, scale);

        if (!string.IsNullOrEmpty(Label))
        {
            using var font = new SKFont(LabelTypeface, 13 * scale);
            using var labelPaint = new SKPaint { Color = WaveformPaint.ToSk(Accent), IsAntialias = true };
            canvas.DrawText(Label, 6 * scale, 16 * scale, SKTextAlign.Left, font, labelPaint);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Waveform is null)
            return;
        CaptureMouse();
        _dragStart = e.GetPosition(this);
        _dragStartPosition = Position;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured || ActualWidth <= 0)
            return;
        double secondsPerDip = Math.Clamp(SecondsVisible, MinSecondsVisible, MaxSecondsVisible) * Math.Max(0.01, Rate) / ActualWidth;
        double target = _dragStartPosition - (e.GetPosition(this).X - _dragStart.X) * secondsPerDip;
        if (SeekCommand?.CanExecute(target) == true)
            SeekCommand.Execute(target);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1 / 1.25 : 1.25;
        SecondsVisible = Math.Clamp(SecondsVisible * factor, MinSecondsVisible, MaxSecondsVisible);
        e.Handled = true;
    }

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(ZoomWaveform),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}
