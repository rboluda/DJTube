using DJTube.Core.Models;

namespace DJTube.Core.Logic;

/// <summary>
/// Detección de tempo constante y fase del primer beat a partir de una curva de "ataques" (onset strength).
/// </summary>
/// <remarks>
/// 1. Barrido: se pliegan los ataques sobre el periodo de cada BPM del rango (cada 0,05); el BPM correcto
///    concentra los golpes en una sola fase (el pico del histograma), uno erróneo los dispersa.
/// 2. Hay pulso si el mejor BPM destaca sobre el resto del barrido (comparado con la mediana y la
///    dispersión del propio barrido: el nivel de fondo depende de la duración y del estilo).
/// 3. Afinado de los máximos más altos del barrido; gana la mejor puntuación. Solo para elegir entre
///    ese BPM, su doble y su mitad se aplica una preferencia suave por tempos de baile (≈125).
/// 4. Si un BPM entero está a menos de 0,05 y puntúa casi igual, se redondea (lo habitual en música de baile).
/// </remarks>
public static class BeatDetector
{
    public const double MinBpm = 70;
    public const double MaxBpm = 180;

    private const double PriorCenterBpm = 125;
    private const double PriorSigmaOctaves = 0.6;
    private const double RefineSpan = 0.015;   // ±1,5 % alrededor de cada candidato
    private const double CoarseStep = 0.05;     // BPM, barrido y primera pasada del afinado
    private const double RefineStep = 0.01;     // BPM, pasada fina
    private const int MaxCandidates = 5;        // máximos del barrido a afinar
    // (máximo − mediana) / (percentil 90 − mediana) del barrido. Ruido: hasta ≈4,6; música: 6,5 o más.
    private const double MinContrast = 5.5;

    /// <param name="onset">Fuerza de ataque por muestra de la curva.</param>
    /// <param name="rate">Muestras de la curva por segundo.</param>
    /// <returns>La rejilla, o null si no hay un pulso claro (o la pista es muy corta).</returns>
    public static BeatGrid? Detect(ReadOnlySpan<float> onset, double rate)
    {
        if (rate <= 0 || onset.Length < rate * 8)
            return null;

        float[] novelty = RemoveLocalMean(onset, (int)Math.Round(rate * 0.15));
        if (novelty.All(v => v == 0))
            return null;

        double[] sweep = Sweep(novelty, rate);
        if (Contrast(sweep) < MinContrast)
            return null;

        // Se afinan varios máximos del barrido: el más alto no siempre es el bueno a 0,05 de precisión.
        // Gana la mejor puntuación sin preferencia de tempo: con ella, en ritmos con síncopas o
        // puntillos ganaban 4/3 o 3/2 del tempo real (116 en vez de 87).
        double bestBpm = 0, bestScore = double.MinValue;
        foreach (double candidate in SweepPeaks(sweep, MaxCandidates))
        {
            var (bpm, score) = Refine(novelty, rate, candidate);
            if (score > bestScore)
                (bestBpm, bestScore) = (bpm, score);
        }

        // Doble o mitad: plegar a medio tempo suele puntuar más (64 frente a 128); ahí decide la preferencia.
        double bestWeighted = bestScore * TempoPrior(bestBpm);
        foreach (double octave in new[] { bestBpm * 2, bestBpm / 2 })
        {
            if (octave < MinBpm * (1 - RefineSpan) || octave > MaxBpm * (1 + RefineSpan))
                continue;
            var (bpm, score) = Refine(novelty, rate, octave);
            double weighted = score * TempoPrior(bpm);
            if (bpm >= MinBpm && bpm <= MaxBpm && weighted > bestWeighted)
                (bestWeighted, bestBpm, bestScore) = (weighted, bpm, score);
        }

        double rounded = Math.Round(bestBpm);
        if (Math.Abs(bestBpm - rounded) <= 0.05 && CombScore(novelty, rate, rounded, out _) >= bestScore * 0.95)
            bestBpm = rounded;

        CombScore(novelty, rate, bestBpm, out double phase);
        return new BeatGrid(bestBpm, phase);
    }

    /// <summary>
    /// Afina un BPM aproximado: pasada gruesa (0,05) en ±1,5 % y fina (0,01) alrededor del mejor.
    /// Devuelve el centro de la meseta de máxima puntuación (más estable que el máximo aislado).
    /// </summary>
    private static (double Bpm, double Score) Refine(float[] novelty, double rate, double bpm0)
    {
        double coarseBest = bpm0, coarseScore = double.MinValue;
        for (double bpm = bpm0 * (1 - RefineSpan); bpm <= bpm0 * (1 + RefineSpan); bpm += CoarseStep)
        {
            double score = CombScore(novelty, rate, bpm, out _);
            if (score > coarseScore)
                (coarseBest, coarseScore) = (bpm, score);
        }

        int steps = (int)Math.Round(2 * CoarseStep / RefineStep);
        double start = Math.Round(coarseBest - CoarseStep, 2);
        var candidates = new double[steps + 1];
        var scores = new double[steps + 1];
        int best = 0;
        for (int k = 0; k <= steps; k++)
        {
            candidates[k] = Math.Round(start + k * RefineStep, 2);
            scores[k] = CombScore(novelty, rate, candidates[k], out _);
            if (scores[k] > scores[best])
                best = k;
        }

        double bestScore = scores[best];
        int from = best, to = best;
        while (from > 0 && scores[from - 1] >= bestScore * 0.97) from--;
        while (to < steps && scores[to + 1] >= bestScore * 0.97) to++;
        double weightSum = 0, centroid = 0;
        for (int k = from; k <= to; k++)
        {
            weightSum += scores[k];
            centroid += scores[k] * candidates[k];
        }
        return (Math.Round(centroid / weightSum, 2), bestScore);
    }

    private static double TempoPrior(double bpm)
    {
        double octaves = Math.Log2(bpm / PriorCenterBpm) / PriorSigmaOctaves;
        return Math.Exp(-0.5 * octaves * octaves);
    }

    /// <summary>
    /// Pliega la curva sobre el periodo del beat. Devuelve pico/media del histograma de fase
    /// y la fase del pico en segundos.
    /// </summary>
    internal static double CombScore(float[] novelty, double rate, double bpm, out double phaseSeconds)
    {
        double period = 60.0 * rate / bpm; // en muestras de la curva
        // ~2 muestras por bin: un golpe no se reparte entre bins por el redondeo de su posición.
        int bins = Math.Max(16, (int)Math.Round(period / 2));
        var histogram = new double[bins];

        for (int i = 0; i < novelty.Length; i++)
        {
            float v = novelty[i];
            if (v == 0)
                continue;
            // Reparto lineal entre los dos bins vecinos: sin saltos por redondeo de la posición.
            double cycles = i / period;
            double position = (cycles - Math.Floor(cycles)) * bins;
            int bin = Math.Min((int)position, bins - 1);
            double weight = position - bin;
            histogram[bin] += v * (1 - weight);
            histogram[(bin + 1) % bins] += v * weight;
        }

        // Suavizado circular binomial [1 4 6 4 1]: la posición de cada golpe tiene un error de
        // ±1 muestra (bloques de ~6 ms); sin suavizar, eso pesa más que la propia deriva del tempo.
        var smooth = new double[bins];
        double sum = 0;
        for (int b = 0; b < bins; b++)
        {
            smooth[b] = histogram[(b + bins - 2) % bins] + 4 * histogram[(b + bins - 1) % bins]
                + 6 * histogram[b] + 4 * histogram[(b + 1) % bins] + histogram[(b + 2) % bins];
            sum += smooth[b];
        }

        int peak = 0;
        for (int b = 1; b < bins; b++)
        {
            if (smooth[b] > smooth[peak])
                peak = b;
        }

        // Interpolación parabólica del pico para afinar la fase.
        double left = smooth[(peak + bins - 1) % bins], center = smooth[peak], right = smooth[(peak + 1) % bins];
        double denominator = left - 2 * center + right;
        double offset = denominator != 0 ? 0.5 * (left - right) / denominator : 0;
        double phaseFraction = (peak + offset) / bins;
        phaseSeconds = phaseFraction * period / rate;

        // Altura del pico de la parábola, no la del bin: si los golpes caen justo entre dos bins,
        // el bin más alto solo tiene la mitad y el tempo exacto puntuaría por debajo de sus vecinos.
        double peakHeight = center - 0.25 * (left - right) * offset;
        double mean = sum / bins;
        return mean > 0 ? peakHeight / mean : 0;
    }

    /// <summary>Puntuación de cada BPM del rango, de <see cref="CoarseStep"/> en <see cref="CoarseStep"/>.</summary>
    private static double[] Sweep(float[] novelty, double rate)
    {
        int count = (int)Math.Round((MaxBpm - MinBpm) / CoarseStep) + 1;
        var scores = new double[count];
        for (int k = 0; k < count; k++)
            scores[k] = CombScore(novelty, rate, SweepBpm(k), out _);
        return scores;
    }

    private static double SweepBpm(int index) => MinBpm + index * CoarseStep;

    /// <summary>
    /// Cuánto destaca el máximo del barrido sobre el resto: (máximo − mediana) / (percentil 90 − mediana).
    /// Con ruido el máximo es solo el extremo de unas fluctuaciones; con pulso, un pico muy por encima.
    /// </summary>
    private static double Contrast(double[] sweep)
    {
        var sorted = (double[])sweep.Clone();
        Array.Sort(sorted);
        double median = sorted[sorted.Length / 2];
        double spread = sorted[(int)(0.9 * (sorted.Length - 1))] - median;
        return spread > 0 ? (sorted[^1] - median) / spread : 0;
    }

    /// <summary>BPM de los máximos locales más altos del barrido.</summary>
    private static IEnumerable<double> SweepPeaks(double[] sweep, int count)
    {
        var peaks = new List<int>();
        for (int k = 0; k < sweep.Length; k++)
        {
            if ((k == 0 || sweep[k] >= sweep[k - 1]) && (k == sweep.Length - 1 || sweep[k] > sweep[k + 1]))
                peaks.Add(k);
        }
        return peaks.OrderByDescending(k => sweep[k]).Take(count).Select(SweepBpm);
    }

    /// <summary>Resta la media local y recorta a 0: deja solo los ataques que destacan sobre su entorno.</summary>
    private static float[] RemoveLocalMean(ReadOnlySpan<float> x, int halfWindow)
    {
        var prefix = new double[x.Length + 1];
        for (int i = 0; i < x.Length; i++)
            prefix[i + 1] = prefix[i] + x[i];

        var result = new float[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            int from = Math.Max(0, i - halfWindow), to = Math.Min(x.Length, i + halfWindow + 1);
            double mean = (prefix[to] - prefix[from]) / (to - from);
            double v = x[i] - mean;
            result[i] = v > 0 ? (float)v : 0;
        }
        return result;
    }
}

/// <summary>Curva de ataques a partir de la energía por bloques (graves y señal completa).</summary>
public static class OnsetStrength
{
    /// <summary>
    /// Subidas de energía (en escala logarítmica) en graves y en la señal completa.
    /// Los graves pesan el doble: el bombo es la mejor referencia de pulso en música de baile.
    /// </summary>
    public static float[] FromBandEnergies(ReadOnlySpan<float> lowEnergy, ReadOnlySpan<float> fullEnergy)
    {
        if (lowEnergy.Length != fullEnergy.Length)
            throw new ArgumentException("Las dos curvas deben tener la misma longitud.");

        float[] low = LogCompress(lowEnergy);
        float[] full = LogCompress(fullEnergy);
        var onset = new float[low.Length];
        for (int i = 1; i < onset.Length; i++)
        {
            float dl = low[i] - low[i - 1];
            float df = full[i] - full[i - 1];
            onset[i] = (dl > 0 ? dl : 0) + 0.5f * (df > 0 ? df : 0);
        }
        return onset;
    }

    private static float[] LogCompress(ReadOnlySpan<float> energy)
    {
        float max = 0;
        foreach (float e in energy)
            max = Math.Max(max, e);

        var result = new float[energy.Length];
        if (max <= 0)
            return result;
        for (int i = 0; i < energy.Length; i++)
            result[i] = MathF.Log(1 + 100 * energy[i] / max);
        return result;
    }
}
