using DJTube.Core.Abstractions;
using DJTube.Core.Analysis;
using DJTube.Core.Localization;
using DJTube.Core.Logic;
using DJTube.Core.Models;
using ManagedBass;

namespace DJTube.Audio.Bass;

using Bass = ManagedBass.Bass;

/// <summary>
/// Analiza una pista con un decodificador propio (independiente del que suena) en un hilo de fondo:
/// forma de onda (por bandas), curva de energía para detectar el BPM, sonoridad (LUFS) y tonalidad.
/// </summary>
public sealed class BassTrackAnalyzer : ITrackAnalyzer
{
    /// <summary>Resolución de la forma de onda (bins por segundo, aproximada).</summary>
    public const int WaveformResolution = 200;

    /// <summary>Resolución de la curva de energía para el BPM (bloques por segundo, aproximada).</summary>
    public const double EnvelopeResolution = 172.27; // 256 muestras a 44,1 kHz

    /// <summary>Corte del filtro de graves (bombo y bajo).</summary>
    private const double LowCutoffHz = 150;

    /// <summary>Por encima de este corte se consideran agudos (para colorear la onda).</summary>
    private const double HighCutoffHz = 2500;

    public BassTrackAnalyzer()
    {
        BassRuntime.EnsureInitialized();
    }

    public Task<TrackAnalysis> AnalyzeAsync(string filePath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(() => Analyze(filePath, progress, ct), ct);
    }

    private static TrackAnalysis Analyze(string path, IProgress<double>? progress, CancellationToken ct)
    {
        int stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (stream == 0)
            throw new BassException(Messages.AnalyzeFailed(Path.GetFileName(path)));

        try
        {
            var info = Bass.ChannelGetInfo(stream);
            int channels = Math.Max(1, info.Channels);
            int sampleRate = info.Frequency;
            long totalFrames = Math.Max(1, Bass.ChannelGetLength(stream, PositionFlags.Bytes) / (sizeof(float) * channels));

            int binFrames = Math.Max(1, sampleRate / WaveformResolution);
            int hopFrames = Math.Max(1, (int)Math.Round(sampleRate / EnvelopeResolution));
            int expectedBins = (int)(totalFrames / binFrames) + 2;
            int expectedHops = (int)(totalFrames / hopFrames) + 2;

            var peaks = new List<float>(expectedBins);
            var lows = new List<float>(expectedBins);
            var mids = new List<float>(expectedBins);
            var highs = new List<float>(expectedBins);
            var lowEnergy = new List<float>(expectedHops);
            var fullEnergy = new List<float>(expectedHops);

            // Filtros paso bajo de un polo en cascada (12 dB/oct): graves (< 150 Hz) y "no agudos" (< 2,5 kHz).
            // Medios = no agudos − graves; agudos = señal − no agudos.
            double a = 1 - Math.Exp(-2 * Math.PI * LowCutoffHz / sampleRate);
            double b = 1 - Math.Exp(-2 * Math.PI * HighCutoffHz / sampleRate);
            double lp1 = 0, lp2 = 0, hp1 = 0, hp2 = 0;

            float binPeak = 0, binLow = 0, binMid = 0, binHigh = 0;
            int binCount = 0;
            double hopLow = 0, hopFull = 0;
            int hopCount = 0;
            long framesDone = 0;
            int lastReported = -1;
            var loudness = new LoudnessMeter(sampleRate, channels);
            var keys = new KeyDetector(sampleRate);
            var monoBuffer = new float[16384];

            var buffer = new float[16384 * channels];
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int bytes = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                if (bytes <= 0)
                    break;

                int frames = bytes / (sizeof(float) * channels);
                loudness.Process(buffer.AsSpan(0, frames * channels));
                for (int f = 0; f < frames; f++)
                {
                    double mono = 0;
                    int offset = f * channels;
                    for (int c = 0; c < channels; c++)
                        mono += buffer[offset + c];
                    mono /= channels;
                    monoBuffer[f] = (float)mono;

                    lp1 += a * (mono - lp1);
                    lp2 += a * (lp1 - lp2);
                    hp1 += b * (mono - hp1);
                    hp2 += b * (hp1 - hp2);

                    float absMono = (float)Math.Abs(mono);
                    float absLow = (float)Math.Abs(lp2);
                    float absMid = (float)Math.Abs(hp2 - lp2);
                    float absHigh = (float)Math.Abs(mono - hp2);
                    if (absMono > binPeak) binPeak = absMono;
                    if (absLow > binLow) binLow = absLow;
                    if (absMid > binMid) binMid = absMid;
                    if (absHigh > binHigh) binHigh = absHigh;
                    if (++binCount == binFrames)
                    {
                        peaks.Add(binPeak);
                        lows.Add(binLow);
                        mids.Add(binMid);
                        highs.Add(binHigh);
                        binPeak = binLow = binMid = binHigh = 0;
                        binCount = 0;
                    }

                    hopLow += lp2 * lp2;
                    hopFull += mono * mono;
                    if (++hopCount == hopFrames)
                    {
                        lowEnergy.Add((float)hopLow);
                        fullEnergy.Add((float)hopFull);
                        hopLow = hopFull = 0;
                        hopCount = 0;
                    }
                }

                keys.Process(monoBuffer.AsSpan(0, frames));
                framesDone += frames;
                int percent = (int)(100 * framesDone / totalFrames);
                if (percent != lastReported)
                {
                    lastReported = percent;
                    progress?.Report(Math.Min(1, framesDone / (double)totalFrames));
                }
            }

            if (binCount > 0)
            {
                peaks.Add(binPeak);
                lows.Add(binLow);
                mids.Add(binMid);
                highs.Add(binHigh);
            }

            // Todas las bandas se escalan con el mismo factor que los picos, para que se vean en proporción.
            float scale = peaks.Count > 0 ? peaks.Max() : 0;
            var waveform = new WaveformData(
                sampleRate / (double)binFrames, Scale(peaks, scale), Scale(lows, scale), Scale(mids, scale), Scale(highs, scale));
            float[] onset = OnsetStrength.FromBandEnergies(lowEnergy.ToArray(), fullEnergy.ToArray());
            var grid = BeatDetector.Detect(onset, sampleRate / (double)hopFrames);

            progress?.Report(1);
            return new TrackAnalysis(waveform, grid, loudness.IntegratedLufs(), keys.Result());
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }

    private static float[] Scale(List<float> values, float max)
    {
        var result = values.ToArray();
        if (max > 0)
        {
            for (int i = 0; i < result.Length; i++)
                result[i] = Math.Min(1, result[i] / max);
        }
        return result;
    }
}
