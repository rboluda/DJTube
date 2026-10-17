using System.Windows;
using System.Windows.Media;

namespace DJTube.App.Controls;

/// <summary>
/// Medidor VU estéreo vertical por segmentos: verde, amarillo cerca de 0 dB y rojo arriba del todo.
/// <see cref="Left"/> y <see cref="Right"/> van de 0 a 1 (posición ya en escala de dB).
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    private const int Segments = 16;

    public static readonly DependencyProperty LeftProperty = DependencyProperty.Register(
        nameof(Left), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RightProperty = DependencyProperty.Register(
        nameof(Right), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Off = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Green = Frozen(Color.FromRgb(0x3D, 0xDC, 0x84));
    private static readonly Brush Yellow = Frozen(Color.FromRgb(0xFD, 0xCD, 0x21));
    private static readonly Brush Red = Frozen(Color.FromRgb(0xFF, 0x5C, 0x5C));

    public double Left
    {
        get => (double)GetValue(LeftProperty);
        set => SetValue(LeftProperty, value);
    }

    public double Right
    {
        get => (double)GetValue(RightProperty);
        set => SetValue(RightProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double gap = 2;
        double barWidth = (ActualWidth - gap) / 2;
        if (barWidth <= 0 || ActualHeight <= 0)
            return;
        DrawBar(dc, 0, barWidth, Left);
        DrawBar(dc, barWidth + gap, barWidth, Right);
    }

    private void DrawBar(DrawingContext dc, double x, double width, double level)
    {
        double segment = ActualHeight / Segments;
        int lit = (int)Math.Round(Math.Clamp(level, 0, 1) * Segments);
        for (int i = 0; i < Segments; i++)
        {
            Brush brush = i >= lit ? Off : i >= Segments - 2 ? Red : i >= Segments - 5 ? Yellow : Green;
            double y = ActualHeight - (i + 1) * segment;
            dc.DrawRectangle(brush, null, new Rect(x, y + 0.5, width, Math.Max(1, segment - 1.5)));
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
