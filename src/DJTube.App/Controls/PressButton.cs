using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DJTube.App.Controls;

/// <summary>
/// Botón con órdenes separadas al pulsar y al soltar (para CUE).
/// </summary>
public class PressButton : Button
{
    public static readonly DependencyProperty PressCommandProperty = DependencyProperty.Register(
        nameof(PressCommand), typeof(ICommand), typeof(PressButton));

    public static readonly DependencyProperty ReleaseCommandProperty = DependencyProperty.Register(
        nameof(ReleaseCommand), typeof(ICommand), typeof(PressButton));

    private bool _held;

    public ICommand? PressCommand
    {
        get => (ICommand?)GetValue(PressCommandProperty);
        set => SetValue(PressCommandProperty, value);
    }

    public ICommand? ReleaseCommand
    {
        get => (ICommand?)GetValue(ReleaseCommandProperty);
        set => SetValue(ReleaseCommandProperty, value);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_held)
            return;
        _held = true;
        CaptureMouse();
        PressCommand?.Execute(null);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        Release();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        Release();
    }

    private void Release()
    {
        if (!_held)
            return;
        _held = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        ReleaseCommand?.Execute(null);
    }
}
