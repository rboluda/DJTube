namespace DJTube.Core.Logic;

/// <summary>Cálculos de la pre-escucha por auriculares.</summary>
public static class HeadphoneMath
{
    /// <summary>Colchón de audio que se mantiene en la ruta de auriculares cuando tiene su propio reloj.</summary>
    public const double TargetBufferSeconds = 0.03;

    /// <summary>Si la ruta de auriculares acumula más que esto, se descarta y vuelve a empezar.</summary>
    public const double ResetBufferSeconds = 0.25;

    /// <summary>Corrección máxima de velocidad (±1 % ≈ 17 cents: inaudible en la práctica).</summary>
    public const double MaxCorrection = 0.01;

    private const double CorrectionGain = 0.5;

    /// <summary>
    /// Ganancia de un plato en los auriculares. Con <paramref name="cueMix"/> = 0 solo suenan los platos
    /// con CUE (a tope, sin importar faders ni crossfader); con 1, lo mismo que en el master.
    /// </summary>
    public static double CueGain(bool cueEnabled, double cueMix, double masterGain, double headphoneVolume)
    {
        double mix = Math.Clamp(double.IsFinite(cueMix) ? cueMix : 0, 0, 1);
        double volume = Math.Clamp(double.IsFinite(headphoneVolume) ? headphoneVolume : 0, 0, 1);
        double master = Math.Max(0, double.IsFinite(masterGain) ? masterGain : 0);
        return volume * ((cueEnabled ? 1 - mix : 0) + mix * master);
    }

    /// <summary>
    /// Factor de velocidad para la ruta de auriculares según el audio que tiene acumulado: si hay más
    /// colchón del deseado consume un poco más rápido, y al revés (control proporcional, limitado).
    /// </summary>
    public static double DriftCorrection(double bufferedSeconds) =>
        Math.Clamp(1 + CorrectionGain * (bufferedSeconds - TargetBufferSeconds), 1 - MaxCorrection, 1 + MaxCorrection);

    /// <summary>¿Hay que vaciar la ruta de auriculares? (sin salida de auriculares o con demasiado retraso).</summary>
    public static bool ShouldReset(bool headphonesActive, double bufferedSeconds) =>
        !headphonesActive || bufferedSeconds > ResetBufferSeconds;
}
