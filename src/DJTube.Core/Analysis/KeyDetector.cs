using System.Numerics;
using DJTube.Core.Models;

namespace DJTube.Core.Analysis;

/// <summary>
/// Detecta la tonalidad: suma un cromagrama (energía de cada una de las 12 notas, de Do2 a Si6) de toda la
/// canción y lo compara con los perfiles de tonalidad de Krumhansl-Kessler (24 tonalidades). La señal se
/// reduce a ~11 kHz y se analiza con FFT de 4096 puntos (resolución de ~2,7 Hz).
/// </summary>
public sealed class KeyDetector
{
    private const int FftSize = 4096;
    private const int Hop = 2048;
    private const double MinHz = 65.4;   // Do2
    private const double MaxHz = 2000;

    // Perfiles de Krumhansl-Kessler (Do mayor / Do menor).
    private static readonly double[] MajorProfile = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] MinorProfile = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    private readonly int _decimation;
    private readonly double _rate;
    private readonly float[] _frame = new float[FftSize];
    private readonly double[] _window = new double[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly double[] _chroma = new double[12];
    private readonly int[] _binNote;
    private int _filled;
    private double _acc;
    private int _accCount;

    public KeyDetector(int sampleRate)
    {
        _decimation = Math.Max(1, (int)Math.Round(sampleRate / 11025.0));
        _rate = sampleRate / (double)_decimation;
        for (int i = 0; i < FftSize; i++)
            _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));

        // Nota (0 = Do) de cada bin dentro del rango; −1 fuera.
        _binNote = new int[FftSize / 2];
        for (int k = 0; k < _binNote.Length; k++)
        {
            double hz = k * _rate / FftSize;
            _binNote[k] = hz < MinHz || hz > MaxHz ? -1 : (int)(((Math.Round(12 * Math.Log2(hz / 440)) + 9) % 12 + 12) % 12);
        }
    }

    /// <summary>Añade muestras mono (en la frecuencia original).</summary>
    public void Process(ReadOnlySpan<float> mono)
    {
        foreach (float s in mono)
        {
            // Diezmado con media (filtro paso bajo sencillo): suficiente para notas por debajo de 2 kHz.
            _acc += s;
            if (++_accCount < _decimation)
                continue;
            _frame[_filled++] = (float)(_acc / _accCount);
            _acc = 0;
            _accCount = 0;
            if (_filled == FftSize)
            {
                AnalyzeFrame();
                Array.Copy(_frame, Hop, _frame, 0, FftSize - Hop);
                _filled = FftSize - Hop;
            }
        }
    }

    /// <summary>La tonalidad más probable, o null si no hay suficiente señal tonal.</summary>
    public MusicalKey? Result()
    {
        double total = _chroma.Sum();
        if (total <= 1e-9)
            return null;

        MusicalKey? best = null;
        double bestScore = double.NegativeInfinity;
        for (int tonic = 0; tonic < 12; tonic++)
        {
            foreach (bool minor in new[] { false, true })
            {
                double score = Correlation(_chroma, minor ? MinorProfile : MajorProfile, tonic);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new MusicalKey(tonic, minor);
                }
            }
        }
        return bestScore > 0.3 ? best : null;
    }

    /// <summary>Energía de cada nota (para los tests).</summary>
    public IReadOnlyList<double> Chroma => _chroma;

    private void AnalyzeFrame()
    {
        for (int i = 0; i < FftSize; i++)
            _fft[i] = new Complex(_frame[i] * _window[i], 0);
        Fft(_fft);

        // Magnitud con compresión logarítmica: que una nota fuerte no tape a las demás.
        Span<double> frameChroma = stackalloc double[12];
        for (int k = 1; k < _binNote.Length; k++)
        {
            int note = _binNote[k];
            if (note >= 0)
                frameChroma[note] += Math.Log(1 + _fft[k].Magnitude);
        }
        for (int n = 0; n < 12; n++)
            _chroma[n] += frameChroma[n];
    }

    /// <summary>Correlación de Pearson entre el cromagrama y el perfil girado a la tónica.</summary>
    private static double Correlation(double[] chroma, double[] profile, int tonic)
    {
        double meanC = chroma.Average(), meanP = profile.Average();
        double num = 0, dc = 0, dp = 0;
        for (int i = 0; i < 12; i++)
        {
            double c = chroma[(i + tonic) % 12] - meanC;
            double p = profile[i] - meanP;
            num += c * p;
            dc += c * c;
            dp += p * p;
        }
        return dc > 0 && dp > 0 ? num / Math.Sqrt(dc * dp) : 0;
    }

    /// <summary>FFT radix-2 iterativa, en el sitio.</summary>
    private static void Fft(Complex[] data)
    {
        int n = data.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
                (data[i], data[j]) = (data[j], data[i]);
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            var wLen = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int j = 0; j < len / 2; j++)
                {
                    var u = data[i + j];
                    var v = data[i + j + len / 2] * w;
                    data[i + j] = u + v;
                    data[i + j + len / 2] = u - v;
                    w *= wLen;
                }
            }
        }
    }
}
