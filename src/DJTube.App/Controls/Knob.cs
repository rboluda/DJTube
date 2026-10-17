using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DJTube.App.Controls;

/// <summary>
/// Mando giratorio de mezclador (de -1 a +1 por defecto, centro = sin efecto). Se gira arrastrando
/// arriba/abajo (Mayús = ajuste fino) o con la rueda; doble clic vuelve al centro.
/// El arco se pinta desde el centro hasta el valor, y en el mínimo (kill) el indicador se pone rojo.
/// </summary>
public sealed class Knob : RangeBase
{
    private const double StartAngle = -135;
    private const double SweepAngle = 270;

    /// <summary>Píxeles de arrastre vertical para recorrer todo el mando.</summary>
    private const double DragPixels = 180;

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(Knob),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(Knob), new PropertyMetadata(0.0));

    /// <summary>Marca el mínimo como "corte total" (indicador rojo).</summary>
    public static readonly DependencyProperty ShowKillProperty = DependencyProperty.Register(
        nameof(ShowKill), typeof(bool), typeof(Knob),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Con false, el arco sale del mínimo (volumen) en vez de del valor por defecto.</summary>
    public static readonly DependencyProperty IsBipolarProperty = DependencyProperty.Register(
        nameof(IsBipolar), typeof(bool), typeof(Knob),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private Point _dragStart;
    private double _valueAtDragStart;

    static Knob()
    {
        MinimumProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(-1.0));
        MaximumProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(1.0));
        ValueProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(0.0,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        SmallChangeProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(0.05));
        FocusableProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(false));
        CursorProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(Cursors.SizeNS));
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public double DefaultValue
    {
        get => (double)GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    public bool ShowKill
    {
        get => (bool)GetValue(ShowKillProperty);
        set => SetValue(ShowKillProperty, value);
    }

    public bool IsBipolar
    {
        get => (bool)GetValue(IsBipolarProperty);
        set => SetValue(IsBipolarProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new KnobAutomationPeer(this);

    /// <summary>Se expone como un deslizador (valor por rango) para lectores de pantalla y automatización.</summary>
    private sealed class KnobAutomationPeer(Knob owner) : RangeBaseAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(Knob);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            Value = DefaultValue;
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
        _valueAtDragStart = Value;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured)
            return;
        double fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.2 : 1;
        double delta = (_dragStart.Y - e.GetPosition(this).Y) / DragPixels * (Maximum - Minimum) * fine;
        Value = Math.Clamp(_valueAtDragStart + delta, Minimum, Maximum);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.2 : 1;
        double before = Value;
        Value = Math.Clamp(Value + Math.Sign(e.Delta) * SmallChange * fine, Minimum, Maximum);
        // Con el clic pulsado (arrastrando), el giro de la rueda se suma al arrastre en vez de perderse.
        if (IsMouseCaptured)
            _valueAtDragStart += Value - before;
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8)
            return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = size / 2 - 3;
        double range = Maximum - Minimum;
        double fraction = range > 0 ? (Value - Minimum) / range : 0;
        double defaultFraction = !IsBipolar || range <= 0 ? 0 : (Math.Clamp(DefaultValue, Minimum, Maximum) - Minimum) / range;
        double angle = StartAngle + SweepAngle * fraction;
        double defaultAngle = StartAngle + SweepAngle * defaultFraction;

        var track = (Brush)FindResource("ControlHoverBrush");
        var body = (Brush)FindResource("ControlBrush");
        var text = (Brush)FindResource("TextBrush");
        bool killed = ShowKill && fraction <= 0.01;

        // Pista del recorrido y arco desde el centro hasta el valor.
        dc.DrawGeometry(null, new Pen(track, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            Arc(center, radius, StartAngle, StartAngle + SweepAngle));
        if (Math.Abs(angle - defaultAngle) > 0.5)
        {
            var accent = killed ? (Brush)FindResource("ErrorBrush") : Accent;
            dc.DrawGeometry(null, new Pen(accent, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                Arc(center, radius, Math.Min(angle, defaultAngle), Math.Max(angle, defaultAngle)));
        }

        // Cuerpo e indicador.
        double inner = radius - 5;
        dc.DrawEllipse(body, new Pen(track, 1), center, inner, inner);
        var tip = PointAt(center, inner - 2, angle);
        var tail = PointAt(center, inner * 0.35, angle);
        dc.DrawLine(new Pen(killed ? (Brush)FindResource("ErrorBrush") : text, 2.2)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, tail, tip);
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        double rad = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Sin(rad), center.Y - radius * Math.Cos(rad));
    }

    private static Geometry Arc(Point center, double radius, double from, double to)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(PointAt(center, radius, from), false, false);
            ctx.ArcTo(PointAt(center, radius, to), new Size(radius, radius), 0, to - from > 180,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
