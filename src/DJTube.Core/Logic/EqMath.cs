namespace DJTube.Core.Logic;

/// <summary>
/// Posición de los mandos de canal de un plato, todos de -1 a +1 con 0 en el centro (sin efecto):
/// ganancia, agudos, medios, graves y filtro (izquierda paso bajo, derecha paso alto).
/// </summary>
public readonly record struct EqSettings(double Gain = 0, double High = 0, double Mid = 0, double Low = 0, double Filter = 0)
{
    public static EqSettings Flat => default;
}

public enum FilterKind
{
    Off,
    LowPass,
    HighPass,
}

public readonly record struct FilterSettings(FilterKind Kind, double CutoffHz);

/// <summary>
/// Conversión de los mandos del mezclador a parámetros de audio, como en un mezclador DJ:
/// las bandas van de corte total (kill) a +6 dB, la ganancia ±12 dB y el filtro barre de 20 kHz a
/// 80 Hz (paso bajo) o de 20 Hz a 8 kHz (paso alto), con una zona muerta en el centro.
/// </summary>
public static class EqMath
{
    /// <summary>Atenuación que se usa como "corte total" de una banda.</summary>
    public const double KillDb = -60;

    public const double MaxBoostDb = 6;

    public const double GainRangeDb = 12;

    /// <summary>Por debajo de esta posición la banda se corta del todo.</summary>
    public const double KillThreshold = -0.98;

    /// <summary>Alrededor del centro el filtro está apagado.</summary>
    public const double FilterDeadZone = 0.04;

    public const double LowPassMaxHz = 20000;
    public const double LowPassMinHz = 80;
    public const double HighPassMinHz = 20;
    public const double HighPassMaxHz = 8000;

    // Frecuencias de las bandas: shelf de graves, campana de medios y shelf de agudos.
    public const double LowShelfHz = 220;
    public const double MidCenterHz = 1200;
    public const double MidBandwidthOctaves = 2.5;
    public const double HighShelfHz = 4000;

    /// <summary>
    /// Ganancia de una banda en dB. A la izquierda la amplitud baja de forma lineal con el giro
    /// (a medio recorrido, -6 dB), hasta el corte total; a la derecha sube hasta +6 dB.
    /// </summary>
    public static double BandDb(double knob)
    {
        knob = Clamp(knob);
        if (knob >= 0)
            return MaxBoostDb * knob;
        if (knob <= KillThreshold)
            return KillDb;
        return Math.Max(KillDb, 20 * Math.Log10(1 + knob));
    }

    public static bool IsKill(double knob) => Clamp(knob) <= KillThreshold;

    /// <summary>Ganancia de entrada (trim) como factor lineal: ±12 dB.</summary>
    public static double GainFactor(double knob) => Math.Pow(10, GainRangeDb * Clamp(knob) / 20);

    public static double GainDb(double knob) => GainRangeDb * Clamp(knob);

    /// <summary>Tipo y frecuencia de corte del filtro; la frecuencia se mueve en escala logarítmica.</summary>
    public static FilterSettings Filter(double knob)
    {
        knob = Clamp(knob);
        double amount = Math.Abs(knob);
        if (amount < FilterDeadZone)
            return new FilterSettings(FilterKind.Off, 0);

        double t = (amount - FilterDeadZone) / (1 - FilterDeadZone);
        return knob < 0
            ? new FilterSettings(FilterKind.LowPass, LowPassMaxHz * Math.Pow(LowPassMinHz / LowPassMaxHz, t))
            : new FilterSettings(FilterKind.HighPass, HighPassMinHz * Math.Pow(HighPassMaxHz / HighPassMinHz, t));
    }

    /// <summary>Nivel (0..1, amplitud) a posición de un medidor VU en dB: -48 dB abajo, 0 dB arriba.</summary>
    public static double MeterFraction(double amplitude, double floorDb = -48)
    {
        if (!(amplitude > 0) || !double.IsFinite(amplitude))
            return 0;
        double db = 20 * Math.Log10(amplitude);
        return Math.Clamp(1 - db / floorDb, 0, 1);
    }

    /// <summary>Texto corto para la ayuda de un mando de banda.</summary>
    public static string FormatBand(double knob) =>
        IsKill(knob) ? "KILL" : $"{BandDb(knob):+0.0;-0.0;0.0} dB";

    private static double Clamp(double knob) => double.IsFinite(knob) ? Math.Clamp(knob, -1, 1) : 0;
}
