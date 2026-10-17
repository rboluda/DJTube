using DJTube.Core.Logic;
using DJTube.Core.Models;

namespace DJTube.Core.Tests;

public class BeatDetectorTests
{
    // Igual que el analizador real: bloques de 256 muestras a 44,1 kHz.
    private const double Rate = 44100.0 / 256;

    [Theory]
    [InlineData(128, 0.30)]
    [InlineData(124, 0.05)]
    [InlineData(120, 0.42)]
    [InlineData(140, 0.10)]
    [InlineData(95, 0.25)]
    [InlineData(100, 0.0)]
    public void Detect_FindsIntegerTempoAndFirstBeat(double bpm, double firstBeat)
    {
        var grid = Detect(SynthesizeDanceTrack(bpm, firstBeat, seconds: 90));

        Assert.NotNull(grid);
        Assert.Equal(bpm, grid.Bpm, 0.001);
        Assert.InRange(PhaseError(grid, firstBeat), 0, 0.012);
    }

    [Fact]
    public void Detect_FindsFractionalTempo()
    {
        var grid = Detect(SynthesizeDanceTrack(122.5, 0.2, seconds: 120));

        Assert.NotNull(grid);
        Assert.Equal(122.5, grid.Bpm, 0.03);
        Assert.InRange(PhaseError(grid, 0.2), 0, 0.015);
    }

    [Fact]
    public void Detect_DrumAndBassIsHalfOrFullTempo()
    {
        var grid = Detect(SynthesizeDanceTrack(174, 0.1, seconds: 90));

        Assert.NotNull(grid);
        Assert.True(Math.Abs(grid.Bpm - 174) < 0.05 || Math.Abs(grid.Bpm - 87) < 0.05, $"BPM {grid.Bpm}");
    }

    [Fact]
    public void Detect_IntroWithoutBeatDoesNotBreakIt()
    {
        var (low, full) = SynthesizeDanceTrack(126, 0.15, seconds: 90);
        // Primeros 20 s sin bombo ni charles: solo ruido de fondo.
        int introEnd = (int)(20 * Rate);
        var rng = new Random(7);
        for (int i = 0; i < introEnd; i++)
        {
            low[i] = 0.01f + (float)rng.NextDouble() * 0.01f;
            full[i] = 0.02f + (float)rng.NextDouble() * 0.02f;
        }

        var grid = Detect((low, full));

        Assert.NotNull(grid);
        Assert.Equal(126, grid.Bpm, 0.001);
        Assert.InRange(PhaseError(grid, 0.15), 0, 0.012);
    }

    [Fact]
    public void Detect_SilenceOrNoiseHasNoTempo()
    {
        int n = (int)(60 * Rate);
        Assert.Null(BeatDetector.Detect(new float[n], Rate));

        var rng = new Random(3);
        var low = new float[n];
        var full = new float[n];
        for (int i = 0; i < n; i++)
        {
            low[i] = (float)rng.NextDouble();
            full[i] = (float)rng.NextDouble();
        }
        Assert.Null(Detect((low, full)));
    }

    [Fact]
    public void Detect_TooShortReturnsNull()
    {
        Assert.Null(Detect(SynthesizeDanceTrack(128, 0, seconds: 5)));
    }

    private static BeatGrid? Detect((float[] Low, float[] Full) energies) =>
        BeatDetector.Detect(OnsetStrength.FromBandEnergies(energies.Low, energies.Full), Rate);

    /// <summary>Distancia (s) entre la rejilla detectada y el beat real, módulo un beat.</summary>
    private static double PhaseError(BeatGrid grid, double expectedFirstBeat)
    {
        double diff = Math.Abs(grid.FirstBeatSeconds - expectedFirstBeat % grid.BeatPeriod);
        return Math.Min(diff, grid.BeatPeriod - diff);
    }

    /// <summary>
    /// Energía por bloque de un patrón "four on the floor": bombo en cada beat (graves y completa),
    /// charles a contratiempo y en semicorcheas (solo completa) y ruido de fondo.
    /// </summary>
    private static (float[] Low, float[] Full) SynthesizeDanceTrack(double bpm, double firstBeat, double seconds)
    {
        int n = (int)(seconds * Rate);
        var low = new float[n];
        var full = new float[n];
        var rng = new Random(42);
        double period = 60 / bpm;

        for (int i = 0; i < n; i++)
        {
            double t = i / Rate;
            double sinceBeat = Mod(t - firstBeat, period);
            double sinceOffbeat = Mod(t - firstBeat - period / 2, period);
            double sinceSixteenth = Mod(t - firstBeat - period / 4, period / 2);

            double kick = t >= firstBeat ? Math.Exp(-sinceBeat / 0.07) : 0;
            double hat = 0.35 * Math.Exp(-sinceOffbeat / 0.03) + 0.15 * Math.Exp(-sinceSixteenth / 0.02);
            double noise = 0.02 * rng.NextDouble();

            low[i] = (float)(kick + noise);
            full[i] = (float)(kick + hat + noise);
        }
        return (low, full);
    }

    private static double Mod(double x, double m) => x - m * Math.Floor(x / m);
}
