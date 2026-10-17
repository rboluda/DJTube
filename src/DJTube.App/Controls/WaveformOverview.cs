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
/// Forma de onda completa de la pista con lo ya reproducido oscurecido, marca de cue y cabezal.
/// Clic o arrastre para saltar. La onda se dibuja una vez en un bitmap y en cada fotograma solo
/// se componen el bitmap y las marcas.
/// </summary>
public sealed class WaveformOverview : SKElement
{
    public static readonly DependencyProperty WaveformProperty = DependencyProperty.Register(
        nameof(Waveform), typeof(WaveformData), typeof(WaveformOverview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(WaveformOverview),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CueFractionProperty = DependencyProperty.Register(
        nameof(CueFraction), typeof(double), typeof(WaveformOverview),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(WaveformOverview),
        new FrameworkPropertyMetadata(Colors.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(WaveformOverview));

    private SKBitmap? _cache;
    private WaveformData? _cacheData;
    private Color _cacheAccent;

    public WaveformOverview()
    {
        Cursor = Cursors.Hand;
    }

    public WaveformData? Waveform
    {
        get => (WaveformData?)GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double CueFraction
    {
        get => (double)GetValue(CueFractionProperty);
        set => SetValue(CueFractionProperty, value);
    }

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Recibe la fracción (0..1) de la pista a la que saltar.</summary>
    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        var canvas = e.Surface.Canvas;
        int w = e.Info.Width, h = e.Info.Height;
        canvas.Clear(WaveformPaint.Background);

        if (Waveform is { } waveform)
        {
            canvas.DrawBitmap(GetCache(waveform, w, h), 0, 0, SKSamplingOptions.Default, null);

            // Lo ya reproducido, oscurecido.
            float played = (float)(Math.Clamp(Progress, 0, 1) * w);
            using var shade = new SKPaint { Color = new SKColor(0, 0, 0, 120) };
            canvas.DrawRect(0, 0, played, h, shade);
        }
        else
        {
            using var line = new SKPaint { Color = WaveformPaint.Grid, StrokeWidth = 1 };
            canvas.DrawLine(0, h / 2f, w, h / 2f, line);
        }

        float scale = w / (float)Math.Max(1, ActualWidth);
        WaveformPaint.DrawCueMarker(canvas, (float)(Math.Clamp(CueFraction, 0, 1) * w), h, scale);
        WaveformPaint.DrawPlayhead(canvas, (float)(Math.Clamp(Progress, 0, 1) * w), h, scale);
    }

    private SKBitmap GetCache(WaveformData waveform, int w, int h)
    {
        if (_cache is not null && _cache.Width == w && _cache.Height == h && ReferenceEquals(_cacheData, waveform)
            && _cacheAccent == Accent)
            return _cache;

        _cache?.Dispose();
        _cache = new SKBitmap(w, h);
        _cacheData = waveform;
        _cacheAccent = Accent;

        using var canvas = new SKCanvas(_cache);
        canvas.Clear(WaveformPaint.Background);
        double secondsPerPixel = waveform.DurationSeconds / w;
        WaveformPaint.DrawColumns(canvas, waveform, w, h, x => x * secondsPerPixel, secondsPerPixel);
        return _cache;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        RequestSeek(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured)
            RequestSeek(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void RequestSeek(Point p)
    {
        if (ActualWidth <= 0)
            return;
        double fraction = Math.Clamp(p.X / ActualWidth, 0, 1);
        if (SeekCommand?.CanExecute(fraction) == true)
            SeekCommand.Execute(fraction);
    }
}

/// <summary>Colores y primitivas de dibujo compartidas por las vistas de onda.</summary>
internal static class WaveformPaint
{
    // Paleta de marca (ver Themes/Dark.xaml).
    public static readonly SKColor Background = new(0x16, 0x0D, 0x27);
    public static readonly SKColor Grid = new(0x2B, 0x1D, 0x4A);
    public static readonly SKColor Cue = new(0xFD, 0xCD, 0x21);

    public static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);

    /// <summary>
    /// Dibuja una columna por píxel con la altura del pico y el color de sus frecuencias
    /// (graves rojo, medios verde, agudos azul); los graves se marcan además con un núcleo
    /// más intenso para que los bombos destaquen.
    /// </summary>
    public static void DrawColumns(
        SKCanvas canvas, WaveformData waveform, int width, int height,
        Func<int, double> timeAtPixel, double secondsPerPixel)
    {
        using var paint = new SKPaint { StrokeWidth = 1 };
        float center = height / 2f, half = height / 2f - 1;
        var (lr, lg, lb) = WaveformColors.LowColor;
        var lowCore = new SKColor(lr, lg, lb, 110);

        for (int x = 0; x < width; x++)
        {
            double t = timeAtPixel(x);
            if (t + secondsPerPixel < 0 || t > waveform.DurationSeconds)
                continue;
            var s = waveform.MaxInRange(t, t + secondsPerPixel);
            if (s.Peak <= 0)
                continue;
            float px = x + 0.5f;
            var (r, g, b) = WaveformColors.Mix(s.Low, s.Mid, s.High);
            paint.Color = new SKColor(r, g, b, 235);
            canvas.DrawLine(px, center - s.Peak * half, px, center + s.Peak * half, paint);
            if (s.Low > 0)
            {
                paint.Color = lowCore;
                canvas.DrawLine(px, center - s.Low * half * 0.6f, px, center + s.Low * half * 0.6f, paint);
            }
        }
    }

    public static void DrawPlayhead(SKCanvas canvas, float x, int height, float scale)
    {
        using var paint = new SKPaint { Color = SKColors.White, StrokeWidth = 2 * scale, IsAntialias = true };
        canvas.DrawLine(x, 0, x, height, paint);
    }

    public static void DrawCueMarker(SKCanvas canvas, float x, int height, float scale)
    {
        using var paint = new SKPaint { Color = Cue, IsAntialias = true, Style = SKPaintStyle.Fill };
        string svg = FormattableString.Invariant($"M{x - 6 * scale},0 L{x + 6 * scale},0 L{x},{9 * scale} Z");
        using var path = SKPath.ParseSvgPathData(svg);
        canvas.DrawPath(path, paint);
        using var line = new SKPaint { Color = Cue.WithAlpha(140), StrokeWidth = 1 * scale };
        canvas.DrawLine(x, 0, x, height, line);
    }
}
