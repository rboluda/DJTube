using DJTube.Core.Logic;

namespace DJTube.Core.Tests;

public sealed class PeakLimiterTests
{
    private static float[] Sine(double amplitude, int frames, int channels = 2, double hz = 440, int rate = 48000)
    {
        var data = new float[frames * channels];
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < channels; c++)
                data[f * channels + c] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * f / rate));
        return data;
    }

    [Fact]
    public void LoudSignalNeverExceedsTheCeiling()
    {
        var limiter = new PeakLimiter(48000, 2);
        var data = Sine(4.0, 48000); // +12 dBFS
        limiter.Process(data);
        Assert.True(data.Max(Math.Abs) <= limiter.Ceiling + 1e-6);
    }

    [Fact]
    public void QuietSignalPassesUnchangedButDelayed()
    {
        var limiter = new PeakLimiter(48000, 2);
        var input = Sine(0.5, 4800);
        var output = (float[])input.Clone();
        limiter.Process(output);

        int delay = limiter.LatencyFrames * 2;
        for (int i = delay; i < input.Length; i++)
            Assert.Equal(input[i - delay], output[i], 5);
        Assert.Equal(1, limiter.CurrentGain, 6);
    }

    [Fact]
    public void GainComesBackAfterAPeak()
    {
        var limiter = new PeakLimiter(48000, 1, releaseMs: 50);
        var burst = Sine(3.0, 4800, channels: 1);
        limiter.Process(burst);
        Assert.True(limiter.CurrentGain < 0.5);

        var quiet = Sine(0.2, 48000, channels: 1); // 1 s tranquilo
        limiter.Process(quiet);
        Assert.True(limiter.CurrentGain > 0.99);
    }

    [Fact]
    public void ReductionStartsBeforeThePeak()
    {
        // Un único pico aislado: gracias a la anticipación sale ya limitado (sin depender del recorte final).
        var limiter = new PeakLimiter(48000, 1);
        var data = new float[2000];
        data[1000] = 3f;
        limiter.Process(data);
        int outIndex = 1000 + limiter.LatencyFrames;
        Assert.InRange(data[outIndex], 0.9f, limiter.Ceiling + 1e-6f);
    }
}
