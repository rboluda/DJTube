namespace DJTube.Core.Models;

/// <summary>
/// Forma de onda resumida de una pista: por cada "bin" (unos 5 ms) el pico de la señal completa
/// y el de los graves, normalizados a 0..1 respecto al pico de la pista.
/// </summary>
public sealed class WaveformData
{
    private readonly float[] _peak;
    private readonly float[] _low;

    public WaveformData(double binsPerSecond, float[] peak, float[] low)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binsPerSecond);
        ArgumentNullException.ThrowIfNull(peak);
        ArgumentNullException.ThrowIfNull(low);
        if (peak.Length != low.Length)
            throw new ArgumentException("Picos y graves deben tener la misma longitud.");

        BinsPerSecond = binsPerSecond;
        _peak = peak;
        _low = low;
    }

    public double BinsPerSecond { get; }

    public int Length => _peak.Length;

    public double DurationSeconds => Length / BinsPerSecond;

    public ReadOnlySpan<float> Peak => _peak;

    public ReadOnlySpan<float> Low => _low;

    /// <summary>
    /// Máximos de los bins que cubren [<paramref name="from"/>, <paramref name="to"/>) segundos.
    /// Si el intervalo es más corto que un bin, devuelve el bin que contiene <paramref name="from"/>.
    /// Fuera de la pista devuelve (0, 0).
    /// </summary>
    public (float Peak, float Low) MaxInRange(double from, double to)
    {
        int first = (int)Math.Floor(from * BinsPerSecond);
        int last = Math.Max(first + 1, (int)Math.Ceiling(to * BinsPerSecond));
        first = Math.Max(first, 0);
        last = Math.Min(last, Length);

        float peak = 0, low = 0;
        for (int i = first; i < last; i++)
        {
            if (_peak[i] > peak) peak = _peak[i];
            if (_low[i] > low) low = _low[i];
        }
        return (peak, low);
    }
}
