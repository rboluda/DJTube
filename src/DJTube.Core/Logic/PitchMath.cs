namespace DJTube.Core.Logic;

/// <summary>Ajustes para el canal de tempo: cambio de tempo en % y frecuencia de muestreo de reproducción.</summary>
public readonly record struct TempoSettings(double TempoPercent, double Frequency);

/// <summary>
/// Conversión del pitch fader (porcentaje de velocidad) a parámetros de reproducción.
/// </summary>
public static class PitchMath
{
    public const double DefaultRange = 8;
    public const double WideRange = 16;

    /// <summary>Limita el pitch al rango ±<paramref name="range"/> %.</summary>
    public static double Clamp(double percent, double range)
    {
        range = Math.Abs(range);
        return double.IsNaN(percent) ? 0 : Math.Clamp(percent, -range, range);
    }

    /// <summary>Factor de velocidad: +8 % → 1,08.</summary>
    public static double ToRate(double percent) => 1 + percent / 100.0;

    /// <summary>Inverso de <see cref="ToRate"/>: 1,08 → +8 %.</summary>
    public static double FromRate(double rate) => (rate - 1) * 100.0;

    /// <summary>BPM que suena con el pitch aplicado.</summary>
    public static double EffectiveBpm(double bpm, double percent) => bpm * ToRate(percent);

    /// <summary>Desplazamiento de tono (semitonos) al cambiar la velocidad sin key lock.</summary>
    public static double Semitones(double percent) => 12 * Math.Log2(ToRate(percent));

    /// <summary>
    /// Con key lock se cambia el tempo (time-stretch) y se conserva la frecuencia;
    /// sin key lock se cambia la frecuencia de reproducción (como un vinilo) y el tempo queda a 0.
    /// </summary>
    public static TempoSettings ToTempoSettings(double percent, bool keyLock, double sourceFrequency) =>
        keyLock
            ? new TempoSettings(percent, sourceFrequency)
            : new TempoSettings(0, sourceFrequency * ToRate(percent));

    /// <summary>Texto para mostrar el pitch: "+1.25 %", "0.00 %", "-8.00 %".</summary>
    public static string Format(double percent)
    {
        double rounded = Math.Round(percent, 2);
        if (rounded == 0)
            return "0.00 %";
        return rounded.ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture) + " %";
    }
}
