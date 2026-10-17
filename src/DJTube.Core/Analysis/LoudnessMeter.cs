namespace DJTube.Core.Analysis;

/// <summary>
/// Sonoridad integrada (LUFS) según ITU-R BS.1770-4 / EBU R128: filtro K (shelving + paso alto), bloques
/// de 400 ms solapados al 75 %, puerta absoluta de −70 LUFS y relativa de −10 LU. Es la medida que usan
/// las plataformas de streaming para igualar volúmenes.
/// </summary>
public sealed class LoudnessMeter
{
    private const double AbsoluteGate = -70;
    private const double RelativeGate = -10;

    private readonly int _channels;
    private readonly int _subBlockFrames;
    private readonly Biquad[] _shelf;
    private readonly Biquad[] _highPass;
    private readonly List<double> _subBlocks = []; // energía (suma de cuadrados de todos los canales) cada 100 ms
    private double _current;
    private int _currentFrames;

    public LoudnessMeter(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        _channels = channels;
        _subBlockFrames = Math.Max(1, sampleRate / 10);

        // Coeficientes para cualquier frecuencia de muestreo (mismas fórmulas que libebur128).
        double f0 = 1681.974450955533, gain = 3.999843853973347, q = 0.7071752369554196;
        double k = Math.Tan(Math.PI * f0 / sampleRate);
        double vh = Math.Pow(10, gain / 20), vb = Math.Pow(vh, 0.4996667741545416);
        double a0 = 1 + k / q + k * k;
        var shelf = new Biquad(
            (vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0,
            2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);

        f0 = 38.13547087602444;
        q = 0.5003270373238773;
        k = Math.Tan(Math.PI * f0 / sampleRate);
        a0 = 1 + k / q + k * k;
        var highPass = new Biquad(1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);

        _shelf = Enumerable.Range(0, channels).Select(_ => shelf with { }).ToArray();
        _highPass = Enumerable.Range(0, channels).Select(_ => highPass with { }).ToArray();
    }

    /// <summary>Añade muestras intercaladas (float, todos los canales).</summary>
    public void Process(ReadOnlySpan<float> interleaved)
    {
        int frames = interleaved.Length / _channels;
        for (int f = 0; f < frames; f++)
        {
            int offset = f * _channels;
            for (int c = 0; c < _channels; c++)
            {
                double y = _highPass[c].Next(_shelf[c].Next(interleaved[offset + c]));
                _current += y * y;
            }
            if (++_currentFrames == _subBlockFrames)
            {
                _subBlocks.Add(_current / _subBlockFrames);
                _current = 0;
                _currentFrames = 0;
            }
        }
    }

    /// <summary>Sonoridad integrada en LUFS, o null si es silencio o dura menos de 400 ms.</summary>
    public double? IntegratedLufs()
    {
        // Bloques de 400 ms (4 sub-bloques de 100 ms) cada 100 ms.
        var blocks = new List<double>(Math.Max(0, _subBlocks.Count - 3));
        for (int i = 0; i + 3 < _subBlocks.Count; i++)
            blocks.Add((_subBlocks[i] + _subBlocks[i + 1] + _subBlocks[i + 2] + _subBlocks[i + 3]) / 4);

        var aboveAbsolute = blocks.Where(z => Lufs(z) > AbsoluteGate).ToList();
        if (aboveAbsolute.Count == 0)
            return null;
        double relativeGate = Lufs(aboveAbsolute.Average()) + RelativeGate;
        var gated = aboveAbsolute.Where(z => Lufs(z) > relativeGate).ToList();
        return gated.Count == 0 ? null : Lufs(gated.Average());
    }

    private static double Lufs(double meanSquare) => meanSquare > 0 ? -0.691 + 10 * Math.Log10(meanSquare) : double.NegativeInfinity;

    /// <summary>Filtro bicuadrático (forma directa I).</summary>
    private sealed record Biquad(double B0, double B1, double B2, double A1, double A2)
    {
        private double _x1, _x2, _y1, _y2;

        public double Next(double x)
        {
            double y = B0 * x + B1 * _x1 + B2 * _x2 - A1 * _y1 - A2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }
    }
}
