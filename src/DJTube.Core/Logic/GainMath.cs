namespace DJTube.Core.Logic;

/// <summary>
/// Ganancia automática: lleva cada canción a la misma sonoridad (LUFS) para que al mezclar no haya saltos de
/// volumen entre un tema muy comprimido y otro más suave.
/// </summary>
public static class GainMath
{
    /// <summary>Objetivo por defecto: alto, como la música de baile, para que casi todo se atenúe un poco.</summary>
    public const double DefaultTargetLufs = -11;

    public const double MinTargetLufs = -20;
    public const double MaxTargetLufs = -6;

    /// <summary>Lo máximo que se sube un tema suave (el limitador del master evita la saturación).</summary>
    public const double MaxBoostDb = 9;

    public const double MaxCutDb = 15;

    /// <summary>Ajuste en dB para llevar <paramref name="lufs"/> al objetivo (0 si no se conoce).</summary>
    public static double AutoGainDb(double? lufs, double targetLufs) =>
        lufs is { } l && double.IsFinite(l) ? Math.Clamp(targetLufs - l, -MaxCutDb, MaxBoostDb) : 0;

    public static double DbToFactor(double db) => Math.Pow(10, db / 20);

    public static string FormatDb(double db) => $"{db:+0.0;-0.0;0.0} dB";
}
