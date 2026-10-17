using DJTube.Core.Models;

namespace DJTube.Core.Logic;

/// <summary>Un loop activo: de <see cref="Start"/> a <see cref="End"/> (segundos de pista).</summary>
public readonly record struct LoopRegion(double Start, double End)
{
    public double Length => End - Start;

    public bool Contains(double seconds) => seconds >= Start && seconds < End;
}

/// <summary>Hot cues y loop de un plato, para dibujarlos sobre la onda.</summary>
public sealed record DeckMarkers(IReadOnlyList<double?> HotCues, LoopRegion? Loop)
{
    public static DeckMarkers Empty { get; } = new(new double?[HotCueMath.Count], null);
}

/// <summary>Cálculos de loops (en tiempos de compás) y de cuantización.</summary>
public static class LoopMath
{
    /// <summary>Tamaños de loop que se ofrecen, en tiempos.</summary>
    public static IReadOnlyList<double> Sizes { get; } = [1.0 / 8, 1.0 / 4, 1.0 / 2, 1, 2, 4, 8, 16, 32];

    public const double DefaultSize = 4;

    /// <summary>Loop más corto (o más largo) que se permite, en segundos.</summary>
    public const double MinLength = 0.01;
    public const double MaxLength = 120;

    /// <summary>
    /// El tiempo de compás más cercano a <paramref name="seconds"/> si está a menos de un cuarto de tiempo;
    /// si no (o sin rejilla), la posición tal cual. Sirve para que loops y hot cues caigan en el golpe.
    /// </summary>
    public static double Quantize(double seconds, BeatGrid? grid)
    {
        if (grid is null)
            return seconds;
        long beat = (long)Math.Round(grid.BeatAt(seconds));
        double snapped = grid.TimeOfBeat(beat);
        return Math.Abs(snapped - seconds) <= grid.BeatPeriod / 4 && snapped >= 0 ? snapped : seconds;
    }

    /// <summary>
    /// Loop automático de <paramref name="beats"/> tiempos que empieza en la posición (cuantizada al tiempo
    /// anterior si hay rejilla, para no perder el golpe). Null sin BPM.
    /// </summary>
    public static LoopRegion? AutoLoop(double position, BeatGrid? grid, double beats, double duration, bool quantize = true)
    {
        if (grid is null || beats <= 0)
            return null;
        double start = position;
        if (quantize)
        {
            // Al tiempo (o fracción de tiempo, en loops cortos) anterior.
            double unit = grid.BeatPeriod * Math.Min(1, beats);
            double k = Math.Floor((position - grid.FirstBeatSeconds) / unit + 1e-6);
            start = grid.FirstBeatSeconds + k * unit;
            if (start < 0)
                start = position;
        }
        double end = start + beats * grid.BeatPeriod;
        return Clip(new LoopRegion(start, end), duration);
    }

    /// <summary>Loop a la mitad (conserva el inicio).</summary>
    public static LoopRegion Halve(LoopRegion loop) =>
        loop.Length / 2 < MinLength ? loop : loop with { End = loop.Start + loop.Length / 2 };

    /// <summary>Loop al doble (conserva el inicio).</summary>
    public static LoopRegion Double(LoopRegion loop, double duration)
    {
        if (loop.Length * 2 > MaxLength)
            return loop;
        return Clip(loop with { End = loop.Start + loop.Length * 2 }, duration) ?? loop;
    }

    /// <summary>Loop manual con IN y OUT (null si el final no está después del principio).</summary>
    public static LoopRegion? Manual(double loopIn, double loopOut, double duration) =>
        loopOut - loopIn >= MinLength ? Clip(new LoopRegion(loopIn, loopOut), duration) : null;

    /// <summary>
    /// Dónde sigue la pista al soltar un loop roll: donde estaría si no se hubiera hecho el loop
    /// (posición al empezar + tiempo de pista transcurrido).
    /// </summary>
    public static double RollExitPosition(double positionAtStart, double wallSecondsElapsed, double playbackRate, double duration) =>
        Math.Clamp(positionAtStart + wallSecondsElapsed * playbackRate, 0, Math.Max(0, duration - 0.01));

    /// <summary>Texto del tamaño: "1/4", "1", "16".</summary>
    public static string FormatSize(double beats) => beats >= 1 ? beats.ToString("0") : $"1/{Math.Round(1 / beats):0}";

    private static LoopRegion? Clip(LoopRegion loop, double duration)
    {
        if (duration > 0 && loop.End > duration)
            loop = loop with { End = duration };
        return loop.Length >= MinLength ? loop : null;
    }
}

/// <summary>Hot cues: puntos de la pista a los que se salta con un botón (8 por plato).</summary>
public static class HotCueMath
{
    public const int Count = 8;

    /// <summary>Colores de los botones (como en los reproductores de club).</summary>
    public static IReadOnlyList<(byte R, byte G, byte B)> Colors { get; } =
    [
        (0xFF, 0x3B, 0x3B), (0xFF, 0x8C, 0x1A), (0xFF, 0xD3, 0x2A), (0x3D, 0xDC, 0x84),
        (0x22, 0xC7, 0xE8), (0x3D, 0x7B, 0xFF), (0x9B, 0x5C, 0xFF), (0xFF, 0x5C, 0xC8),
    ];
}
