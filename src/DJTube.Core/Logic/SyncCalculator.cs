using DJTube.Core.Models;

namespace DJTube.Core.Logic;

/// <summary>Qué hacer en el plato esclavo al pulsar SYNC.</summary>
/// <param name="PitchPercent">Pitch que iguala su tempo al del maestro.</param>
/// <param name="SeekSeconds">Posición (de decodificación) que alinea los golpes, o null si no se alinea la fase.</param>
/// <param name="Multiplier">1, 2 (el esclavo se cuenta a doble tempo) o 0,5 (a medio tempo).</param>
public sealed record SyncPlan(double PitchPercent, double? SeekSeconds, double Multiplier);

public static class SyncCalculator
{
    private static readonly double[] Multipliers = [1, 2, 0.5];

    /// <summary>
    /// Calcula el pitch que iguala el tempo del esclavo al del maestro y, si los dos suenan, el salto que
    /// alinea sus golpes. Admite medio y doble tempo (p. ej. 87 con 174). Devuelve null si el pitch
    /// necesario se sale de ±<paramref name="pitchRange"/> %.
    /// </summary>
    /// <param name="masterPosition">Posición que se oye del maestro.</param>
    /// <param name="slavePosition">Posición que se oye del esclavo.</param>
    /// <param name="outputLatencySeconds">
    /// Tiempo entre que se decodifica el audio y se oye. El salto actúa sobre la decodificación,
    /// así que las dos posiciones se adelantan esa latencia antes de comparar fases.
    /// </param>
    public static SyncPlan? Plan(
        BeatGrid master, double masterPosition, double masterPitch, bool masterPlaying,
        BeatGrid slave, double slavePosition, double slavePitch, bool slavePlaying,
        double pitchRange, double outputLatencySeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(slave);

        double targetBpm = PitchMath.EffectiveBpm(master.Bpm, masterPitch);
        double multiplier = 1;
        double pitch = double.MaxValue;
        foreach (double m in Multipliers)
        {
            double candidate = PitchMath.FromRate(targetBpm / (slave.Bpm * m));
            if (Math.Abs(candidate) < Math.Abs(pitch) - 1e-9)
                (pitch, multiplier) = (candidate, m);
        }

        if (Math.Abs(pitch) > Math.Abs(pitchRange) + 1e-9)
            return null;

        double? seek = null;
        if (masterPlaying && slavePlaying)
        {
            double latency = Math.Max(0, outputLatencySeconds);
            double masterDecode = masterPosition + latency * PitchMath.ToRate(masterPitch);
            double slaveDecode = slavePosition + latency * PitchMath.ToRate(slavePitch);

            double masterPhase = master.PhaseAt(masterDecode);
            double slaveBeat = slave.BeatPeriod / multiplier;
            double slavePhase = Fraction((slaveDecode - slave.FirstBeatSeconds) / slaveBeat);
            double delta = WrapHalf(masterPhase - slavePhase);
            seek = Math.Max(0, slaveDecode + delta * slaveBeat);
        }

        return new SyncPlan(pitch, seek, multiplier);
    }

    private static double Fraction(double x) => x - Math.Floor(x);

    /// <summary>Lleva una diferencia de fase a [-0,5, 0,5): el salto más corto.</summary>
    private static double WrapHalf(double x) => x - Math.Floor(x + 0.5);
}
