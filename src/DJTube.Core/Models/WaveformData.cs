namespace DJTube.Core.Models;

/// <summary>Picos de un tramo de onda: señal completa y por bandas (0..1).</summary>
public readonly record struct WaveformSample(float Peak, float Low, float Mid, float High);

/// <summary>
/// Forma de onda resumida de una pista: por cada "bin" (unos 5 ms) el pico de la señal completa
/// y el de graves, medios y agudos, normalizados a 0..1 respecto al pico de la pista.
/// </summary>
public sealed class WaveformData
{
    private readonly float[] _peak;
    private readonly float[] _low;
    private readonly float[] _mid;
    private readonly float[] _high;

    public WaveformData(double binsPerSecond, float[] peak, float[] low, float[] mid, float[] high)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binsPerSecond);
        ArgumentNullException.ThrowIfNull(peak);
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(mid);
        ArgumentNullException.ThrowIfNull(high);
        if (peak.Length != low.Length || peak.Length != mid.Length || peak.Length != high.Length)
            throw new ArgumentException("Todas las bandas deben tener la misma longitud.");

        BinsPerSecond = binsPerSecond;
        _peak = peak;
        _low = low;
        _mid = mid;
        _high = high;
    }

    public double BinsPerSecond { get; }

    public int Length => _peak.Length;

    public double DurationSeconds => Length / BinsPerSecond;

    public ReadOnlySpan<float> Peak => _peak;

    public ReadOnlySpan<float> Low => _low;

    public ReadOnlySpan<float> Mid => _mid;

    public ReadOnlySpan<float> High => _high;

    /// <summary>
    /// Máximos de los bins que cubren [<paramref name="from"/>, <paramref name="to"/>) segundos.
    /// Si el intervalo es más corto que un bin, devuelve el bin que contiene <paramref name="from"/>.
    /// Fuera de la pista devuelve ceros.
    /// </summary>
    public WaveformSample MaxInRange(double from, double to)
    {
        int first = (int)Math.Floor(from * BinsPerSecond);
        int last = Math.Max(first + 1, (int)Math.Ceiling(to * BinsPerSecond));
        first = Math.Max(first, 0);
        last = Math.Min(last, Length);

        float peak = 0, low = 0, mid = 0, high = 0;
        for (int i = first; i < last; i++)
        {
            if (_peak[i] > peak) peak = _peak[i];
            if (_low[i] > low) low = _low[i];
            if (_mid[i] > mid) mid = _mid[i];
            if (_high[i] > high) high = _high[i];
        }
        return new WaveformSample(peak, low, mid, high);
    }
}
