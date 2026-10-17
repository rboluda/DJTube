using DJTube.Core.Abstractions;
using DJTube.Core.Localization;
using DJTube.Core.Logic;
using DJTube.Core.Models;
using ManagedBass;

namespace DJTube.Audio.Bass;

using Bass = ManagedBass.Bass;

/// <summary>
/// Analiza una pista con un decodificador propio (independiente del que suena) en un hilo de fondo:
/// forma de onda (picos y graves) y curva de energía para detectar el BPM.
/// </summary>
public sealed class BassTrackAnalyzer : ITrackAnalyzer
{
    /// <summary>Resolución de la forma de onda (bins por segundo, aproximada).</summary>
    public const int WaveformResolution = 200;

    /// <summary>Resolución de la curva de energía para el BPM (bloques por segundo, aproximada).</summary>
    public const double EnvelopeResolution = 172.27; // 256 muestras a 44,1 kHz

    /// <summary>Corte del filtro de graves (bombo y bajo).</summary>
    private const double LowCutoffHz = 150;

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
            var lowEnergy = new List<float>(expectedHops);
            var fullEnergy = new List<float>(expectedHops);

            // Dos filtros paso bajo de un polo en cascada (12 dB/oct) para quedarse con los graves.
            double a = 1 - Math.Exp(-2 * Math.PI * LowCutoffHz / sampleRate);
            double lp1 = 0, lp2 = 0;

            float binPeak = 0, binLow = 0;
            int binCount = 0;
            double hopLow = 0, hopFull = 0;
            int hopCount = 0;
            long framesDone = 0;
            int lastReported = -1;

            var buffer = new float[16384 * channels];
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int bytes = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                if (bytes <= 0)
                    break;

                int frames = bytes / (sizeof(float) * channels);
                for (int f = 0; f < frames; f++)
                {
                    double mono = 0;
                    int offset = f * channels;
                    for (int c = 0; c < channels; c++)
                        mono += buffer[offset + c];
                    mono /= channels;

                    lp1 += a * (mono - lp1);
                    lp2 += a * (lp1 - lp2);

                    float absMono = (float)Math.Abs(mono);
                    float absLow = (float)Math.Abs(lp2);
                    if (absMono > binPeak) binPeak = absMono;
                    if (absLow > binLow) binLow = absLow;
                    if (++binCount == binFrames)
                    {
                        peaks.Add(binPeak);
                        lows.Add(binLow);
                        binPeak = binLow = 0;
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
            }

            float[] peakArray = Normalize(peaks);
            float[] lowArray = lows.ToArray();
            // Los graves se escalan con el mismo factor que los picos para que se vean en proporción.
            float scale = peaks.Count > 0 ? peaks.Max() : 0;
            if (scale > 0)
            {
                for (int i = 0; i < lowArray.Length; i++)
                    lowArray[i] = Math.Min(1, lowArray[i] / scale);
            }

            var waveform = new WaveformData(sampleRate / (double)binFrames, peakArray, lowArray);
            float[] onset = OnsetStrength.FromBandEnergies(lowEnergy.ToArray(), fullEnergy.ToArray());
            var grid = BeatDetector.Detect(onset, sampleRate / (double)hopFrames);

            progress?.Report(1);
            return new TrackAnalysis(waveform, grid);
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }

    private static float[] Normalize(List<float> values)
    {
        float max = values.Count > 0 ? values.Max() : 0;
        var result = values.ToArray();
        if (max > 0)
        {
            for (int i = 0; i < result.Length; i++)
                result[i] /= max;
        }
        return result;
    }
}
