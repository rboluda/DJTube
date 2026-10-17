using DJTube.Core.Models;

namespace DJTube.Core.Logic;

/// <summary>Decisiones del automix: cuándo preparar y lanzar la siguiente canción, y el fundido.</summary>
public static class AutomixMath
{
    public const double DefaultFadeSeconds = 10;
    public const double MinFadeSeconds = 2;
    public const double MaxFadeSeconds = 30;

    /// <summary>Margen extra para descargar y analizar la siguiente antes del fundido.</summary>
    public const double PrepareMarginSeconds = 30;

    /// <summary>Tiempo (real) que le queda a la canción, con el pitch aplicado.</summary>
    public static double RemainingSeconds(double duration, double position, double playbackRate) =>
        Math.Max(0, duration - position) / Math.Max(0.1, playbackRate);

    /// <summary>¿Toca tener ya cargada la siguiente?</summary>
    public static bool ShouldPrepare(double remainingSeconds, double fadeSeconds) =>
        remainingSeconds <= fadeSeconds + PrepareMarginSeconds;

    /// <summary>¿Toca empezar el fundido?</summary>
    public static bool ShouldStartFade(double remainingSeconds, double fadeSeconds) => remainingSeconds <= fadeSeconds;

    /// <summary>Posición del crossfader (−1 = A, +1 = B) a mitad del fundido de un plato a otro.</summary>
    public static double Crossfader(double progress, bool towardsB)
    {
        double p = Math.Clamp(progress, 0, 1);
        return towardsB ? -1 + 2 * p : 1 - 2 * p;
    }

    /// <summary>Dónde empieza la siguiente: en su primer golpe (sin el silencio del principio).</summary>
    public static double StartPosition(BeatGrid? grid) => grid is { FirstBeatSeconds: > 0 and < 30 } g ? g.FirstBeatSeconds : 0;

    /// <summary>
    /// Pitch para que la siguiente suene al tempo de la que está sonando, o null si no cabe en el rango
    /// (también probando a medio y doble tempo, como el SYNC).
    /// </summary>
    public static double? MatchPitch(double currentEffectiveBpm, double nextBpm, double range)
    {
        if (currentEffectiveBpm <= 0 || nextBpm <= 0)
            return null;
        double? best = null;
        foreach (double factor in new[] { 1.0, 0.5, 2.0 })
        {
            double pitch = (currentEffectiveBpm * factor / nextBpm - 1) * 100;
            if (Math.Abs(pitch) <= range && (best is null || Math.Abs(pitch) < Math.Abs(best.Value)))
                best = pitch;
        }
        return best;
    }
}
