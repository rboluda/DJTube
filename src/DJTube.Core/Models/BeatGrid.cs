namespace DJTube.Core.Models;

/// <summary>
/// Rejilla de beats de tempo constante: un beat cada 60/BPM segundos a partir de <see cref="FirstBeatSeconds"/>.
/// Los tiempos son de la pista original (sin pitch).
/// </summary>
public sealed record BeatGrid
{
    public BeatGrid(double bpm, double firstBeatSeconds)
    {
        if (!double.IsFinite(bpm) || bpm <= 0)
            throw new ArgumentOutOfRangeException(nameof(bpm));
        Bpm = bpm;
        // Primer beat dentro del primer periodo: así la rejilla es única.
        FirstBeatSeconds = Mod(firstBeatSeconds, 60.0 / bpm);
    }

    public double Bpm { get; }

    public double FirstBeatSeconds { get; }

    public double BeatPeriod => 60.0 / Bpm;

    /// <summary>Número de beat (con decimales) en el instante <paramref name="seconds"/>.</summary>
    public double BeatAt(double seconds) => (seconds - FirstBeatSeconds) / BeatPeriod;

    /// <summary>Fase dentro del beat, 0..1 (0 = justo en el golpe).</summary>
    public double PhaseAt(double seconds) => Mod(BeatAt(seconds), 1);

    public double TimeOfBeat(long beat) => FirstBeatSeconds + beat * BeatPeriod;

    private static double Mod(double value, double modulus) => value - modulus * Math.Floor(value / modulus);
}

/// <summary>Resultado del análisis de una pista.</summary>
public sealed record TrackAnalysis(WaveformData Waveform, BeatGrid? Grid);
