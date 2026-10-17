using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DJTube.App.Controls;

/// <summary>
/// Barra de progreso de la pista con marca de cue. Clic o arrastre para saltar.
/// (Provisional: en la fase 3 la sustituye la forma de onda.)
/// </summary>
public sealed class TrackProgressBar : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(TrackProgressBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CueFractionProperty = DependencyProperty.Register(
        nameof(CueFraction), typeof(double), typeof(TrackProgressBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(TrackProgressBar),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(TrackProgressBar));

    private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x37)));
    private static readonly Pen PlayheadPen = Freeze(new Pen(Brushes.White, 2));
    private static readonly Brush CueBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x1A)));

    public TrackProgressBar()
    {
        Cursor = Cursors.Hand;
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

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Recibe la fracción (0..1) de la pista a la que saltar.</summary>
    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), 4, 4);

        double x = Math.Clamp(Progress, 0, 1) * w;
        dc.PushOpacity(0.45);
        dc.DrawRoundedRectangle(Accent, null, new Rect(0, 0, x, h), 4, 4);
        dc.Pop();

        double cx = Math.Clamp(CueFraction, 0, 1) * w;
        var triangle = new StreamGeometry();
        using (var ctx = triangle.Open())
        {
            ctx.BeginFigure(new Point(cx - 6, 0), true, true);
            ctx.LineTo(new Point(cx + 6, 0), false, false);
            ctx.LineTo(new Point(cx, 9), false, false);
        }
        dc.DrawGeometry(CueBrush, null, triangle);

        dc.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, h));
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

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
