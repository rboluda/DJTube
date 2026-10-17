namespace DJTube.Core.Logic;

/// <summary>Curva del fader de canal (posición 0..1 → ganancia).</summary>
public static class FaderTaper
{
    /// <summary>
    /// Curva cuadrática: se parece más a cómo percibimos el volumen que una lineal
    /// (a mitad de recorrido, unos -12 dB).
    /// </summary>
    public static double ToGain(double position)
    {
        double p = Math.Clamp(double.IsNaN(position) ? 0 : position, 0, 1);
        return p * p;
    }
}
