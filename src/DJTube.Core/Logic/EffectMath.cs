namespace DJTube.Core.Logic;

public enum EffectKind
{
    Echo,
    Reverb,
    Flanger,
    Phaser,
}

/// <summary>
/// Efecto de un plato: tipo, encendido, cantidad (mezcla del efecto, 0..1) y tiempos de compás para los que
/// dependen del tempo (retardo del eco, periodo del barrido). <see cref="BeatSeconds"/> es lo que dura un
/// tiempo en tiempo real (con el pitch aplicado).
/// </summary>
public readonly record struct EffectSettings(EffectKind Kind, bool On, double Amount, double Beats, double BeatSeconds)
{
    public static EffectSettings Off => new(EffectKind.Echo, false, 0.5, 0.5, 0.5);
}

/// <summary>Parámetros de los efectos sincronizados con el tempo.</summary>
public static class EffectMath
{
    /// <summary>Tiempos de compás que se ofrecen.</summary>
    public static IReadOnlyList<double> Beats { get; } = [1.0 / 8, 1.0 / 4, 1.0 / 2, 3.0 / 4, 1, 2, 4, 8, 16];

    /// <summary>Tiempos por defecto de cada efecto (eco corchea, barridos largos).</summary>
    public static double DefaultBeats(EffectKind kind) => kind switch
    {
        EffectKind.Echo => 0.5,
        EffectKind.Flanger => 8,
        EffectKind.Phaser => 4,
        _ => 1,
    };

    /// <summary>¿Usa los tiempos de compás? (La reverb no.)</summary>
    public static bool UsesBeats(EffectKind kind) => kind != EffectKind.Reverb;

    /// <summary>Duración de un tiempo en tiempo real: con el pitch subido los tiempos son más cortos.</summary>
    public static double BeatSeconds(double? bpm, double playbackRate) =>
        bpm is > 0 ? 60 / bpm.Value / Math.Max(0.1, playbackRate) : 0.5;

    /// <summary>Retardo del eco (máximo 6 s, lo que admite BASS_FX).</summary>
    public static double EchoDelay(EffectSettings s) => Math.Clamp(s.Beats * s.BeatSeconds, 0.01, 6);

    /// <summary>Realimentación del eco: más cantidad = más repeticiones.</summary>
    public static double EchoFeedback(double amount) => 0.25 + 0.5 * Math.Clamp(amount, 0, 1);

    /// <summary>Periodo de un barrido (flanger/phaser) en segundos.</summary>
    public static double SweepPeriod(EffectSettings s) => Math.Clamp(s.Beats * s.BeatSeconds, 0.05, 60);

    /// <summary>
    /// Velocidad del chorus de BASS_FX (ms de retardo por segundo) para que el flanger recorra de
    /// <paramref name="minMs"/> a <paramref name="maxMs"/> y vuelva en un periodo.
    /// </summary>
    public static double FlangerRate(EffectSettings s, double minMs, double maxMs) =>
        Math.Clamp(2 * (maxMs - minMs) / SweepPeriod(s), 0.001, 1000);

    /// <summary>Barridos por segundo del phaser.</summary>
    public static double PhaserRate(EffectSettings s) => Math.Clamp(1 / SweepPeriod(s), 0.01, 9.9);

    public static string FormatBeats(double beats) => beats switch
    {
        >= 1 => beats.ToString("0"),
        0.75 => "3/4",
        _ => $"1/{Math.Round(1 / beats):0}",
    };
}
