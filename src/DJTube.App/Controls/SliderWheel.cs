using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DJTube.App.Controls;

/// <summary>
/// Rueda del ratón en todos los deslizadores (faders, crossfader…): con el puntero encima o
/// manteniendo pulsado el clic sobre el control. Cada paso mueve un 2 % del recorrido (Mayús: 0,4 %).
/// Un deslizador con su propio manejo de la rueda (el pitch) lo marca como gestionado y este no actúa.
/// </summary>
public static class SliderWheel
{
    private const double StepFraction = 0.02;
    private const double FineFactor = 0.2;

    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(Slider), UIElement.MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheel));

    private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not Slider { IsEnabled: true } slider)
            return;
        double step = (slider.Maximum - slider.Minimum) * StepFraction;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            step *= FineFactor;
        double direction = Math.Sign(e.Delta) * (slider.IsDirectionReversed ? -1 : 1);
        slider.Value = Math.Clamp(slider.Value + direction * step, slider.Minimum, slider.Maximum);
        e.Handled = true;
    }
}
