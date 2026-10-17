using DJTube.Core.Logic;
using DJTube.Core.Models;

namespace DJTube.Core.Tests;

public class WaveformAndGridTests
{
    private static readonly WaveformData Wave = new(
        binsPerSecond: 10,
        peak: [0.1f, 0.5f, 0.3f, 0.9f, 0.2f],
        low: [0.0f, 0.4f, 0.1f, 0.2f, 0.8f],
        mid: [0.1f, 0.1f, 0.3f, 0.6f, 0.1f],
        high: [0.0f, 0.2f, 0.0f, 0.1f, 0.05f]);

    [Fact]
    public void Waveform_DurationFromBins()
    {
        Assert.Equal(0.5, Wave.DurationSeconds, 1e-9);
    }

    [Theory]
    [InlineData(0.0, 0.2, 0.5f, 0.4f)]
    [InlineData(0.1, 0.4, 0.9f, 0.4f)]
    [InlineData(0.31, 0.32, 0.9f, 0.2f)]   // más corto que un bin: el bin que lo contiene
    [InlineData(0.0, 10, 0.9f, 0.8f)]
    [InlineData(-1, -0.5, 0f, 0f)]          // antes del principio
    [InlineData(2, 3, 0f, 0f)]              // después del final
    public void Waveform_MaxInRange(double from, double to, float peak, float low)
    {
        var sample = Wave.MaxInRange(from, to);

        Assert.Equal(peak, sample.Peak);
        Assert.Equal(low, sample.Low);
    }

    [Fact]
    public void Waveform_MaxInRangeIncludesMidAndHigh()
    {
        var sample = Wave.MaxInRange(0.0, 0.5);

        Assert.Equal(0.6f, sample.Mid);
        Assert.Equal(0.2f, sample.High);
    }

    [Fact]
    public void Waveform_RejectsMismatchedArrays()
    {
        Assert.Throws<ArgumentException>(() => new WaveformData(10, [1f], [1f, 2f], [1f], [1f]));
        Assert.Throws<ArgumentException>(() => new WaveformData(10, [1f], [1f], [1f], []));
    }

    [Fact]
    public void BeatGrid_NormalizesFirstBeatIntoFirstPeriod()
    {
        var grid = new BeatGrid(120, 10.2); // periodo 0,5 s

        Assert.Equal(0.2, grid.FirstBeatSeconds, 1e-9);
        Assert.Equal(0.5, grid.BeatPeriod, 1e-9);
    }

    [Theory]
    [InlineData(0.2, 0.0)]
    [InlineData(0.45, 0.5)]
    [InlineData(0.7, 0.0)]
    [InlineData(0.1, 0.8)]
    public void BeatGrid_PhaseAt(double seconds, double expectedPhase)
    {
        var grid = new BeatGrid(120, 0.2);

        // La fase es circular: 0,99999… y 0 son el mismo punto.
        double diff = Math.Abs(grid.PhaseAt(seconds) - expectedPhase);
        Assert.InRange(Math.Min(diff, 1 - diff), 0, 1e-9);
    }

    [Fact]
    public void BeatGrid_TimeOfBeat()
    {
        var grid = new BeatGrid(120, 0.2);

        Assert.Equal(0.2, grid.TimeOfBeat(0), 1e-9);
        Assert.Equal(2.2, grid.TimeOfBeat(4), 1e-9);
        Assert.Equal(4.0, grid.BeatAt(2.2), 1e-9);
    }

    [Fact]
    public void Smoother_PausedReturnsReportedPosition()
    {
        var smoother = new PlayheadSmoother();

        Assert.Equal(12.3, smoother.Update(12.3, 1.0, 1.0, playing: false));
        Assert.Equal(15.0, smoother.Update(15.0, 1.1, 1.0, playing: false));
    }

    [Fact]
    public void Smoother_SmoothsBlockyReportsWhilePlaying()
    {
        // El motor avanza a saltos de 10 ms; los fotogramas llegan cada 16,7 ms.
        var smoother = new PlayheadSmoother();
        double previous = double.MinValue;
        double maxError = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            double now = frame / 60.0;
            double truePosition = 5 + now;
            double reported = 5 + Math.Floor(now / 0.01) * 0.01;
            double shown = smoother.Update(reported, now, 1.0, playing: true);

            Assert.True(shown >= previous, $"retrocede en el fotograma {frame}");
            previous = shown;
            if (frame > 30)
                maxError = Math.Max(maxError, Math.Abs(shown - truePosition));
        }
        Assert.InRange(maxError, 0, 0.012);
    }

    [Fact]
    public void Smoother_FollowsPitchedRate()
    {
        var smoother = new PlayheadSmoother();
        double shown = 0;
        for (int frame = 0; frame < 300; frame++)
        {
            double now = frame / 60.0;
            shown = smoother.Update(now * 1.08, now, 1.08, playing: true);
        }
        Assert.Equal(299 / 60.0 * 1.08, shown, 0.002);
    }

    [Fact]
    public void Smoother_SnapsOnSeek()
    {
        var smoother = new PlayheadSmoother();
        smoother.Update(10, 0, 1, playing: true);
        smoother.Update(10.0167, 1 / 60.0, 1, playing: true);

        Assert.Equal(60, smoother.Update(60, 2 / 60.0, 1, playing: true));
    }
}
