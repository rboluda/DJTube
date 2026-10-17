using System.Text;
using DJTube.Core.Analysis;
using DJTube.Core.Logic;
using DJTube.Core.Models;

namespace DJTube.Core.Tests;

public sealed class LoudnessAndKeyTests
{
    private const int Rate = 48000;

    private static float[] Sine(double hz, double amplitude, double seconds, int channels, int rate = Rate)
    {
        int frames = (int)(rate * seconds);
        var data = new float[frames * channels];
        for (int f = 0; f < frames; f++)
        {
            float v = (float)(amplitude * Math.Sin(2 * Math.PI * hz * f / rate));
            for (int c = 0; c < channels; c++)
                data[f * channels + c] = v;
        }
        return data;
    }

    [Fact]
    public void Loudness_FullScaleSineInBothChannelsIsZeroLufs()
    {
        // Referencia de la norma: un seno de 1 kHz a 0 dBFS en un canal da −3,01 LUFS; en dos, 0.
        var meter = new LoudnessMeter(Rate, 2);
        meter.Process(Sine(997, 1.0, 5, 2));
        Assert.Equal(0.0, meter.IntegratedLufs()!.Value, 1);
    }

    [Fact]
    public void Loudness_OneChannelIsMinusThreeLufs()
    {
        var data = Sine(997, 1.0, 5, 2);
        for (int i = 1; i < data.Length; i += 2)
            data[i] = 0;
        var meter = new LoudnessMeter(Rate, 2);
        meter.Process(data);
        Assert.Equal(-3.01, meter.IntegratedLufs()!.Value, 1);
    }

    [Fact]
    public void Loudness_TwentyDecibelsDownIsMinusTwentyLufs_AtAnyRate()
    {
        var meter = new LoudnessMeter(44100, 2);
        meter.Process(Sine(997, 0.1, 5, 2, 44100));
        Assert.Equal(-20.0, meter.IntegratedLufs()!.Value, 1);
    }

    [Fact]
    public void Loudness_SilenceIsGatedOut()
    {
        var meter = new LoudnessMeter(Rate, 2);
        meter.Process(Sine(997, 0.1, 5, 2));
        meter.Process(new float[Rate * 2 * 10]); // 10 s de silencio: la puerta lo ignora
        // (solo cuentan algo los bloques que solapan el paso a silencio)
        Assert.InRange(meter.IntegratedLufs()!.Value, -20.2, -19.9);

        var silent = new LoudnessMeter(Rate, 2);
        silent.Process(new float[Rate * 2 * 3]);
        Assert.Null(silent.IntegratedLufs());
    }

    [Fact]
    public void AutoGain_BringsTracksToTheTarget()
    {
        Assert.Equal(-3, GainMath.AutoGainDb(-8, -11), 6);
        Assert.Equal(5, GainMath.AutoGainDb(-16, -11), 6);
        Assert.Equal(GainMath.MaxBoostDb, GainMath.AutoGainDb(-40, -11), 6);
        Assert.Equal(0, GainMath.AutoGainDb(null, -11));
    }

    /// <summary>Acordes sintéticos (fundamental + octava) de 1 s cada uno.</summary>
    private static float[] Chords(params int[][] chords)
    {
        const int rate = 44100;
        var result = new List<float>();
        foreach (var chord in chords)
        {
            for (int f = 0; f < rate; f++)
            {
                double v = 0;
                foreach (int midi in chord)
                {
                    double hz = 440 * Math.Pow(2, (midi - 69) / 12.0);
                    v += Math.Sin(2 * Math.PI * hz * f / rate) + 0.4 * Math.Sin(2 * Math.PI * 2 * hz * f / rate);
                }
                result.Add((float)(v / (chord.Length * 2)));
            }
        }
        return result.ToArray();
    }

    [Fact]
    public void Key_CMajorProgressionIsC()
    {
        int[] c = [48, 52, 55, 60], f = [53, 57, 60, 65], g = [55, 59, 62, 67];
        var detector = new KeyDetector(44100);
        for (int i = 0; i < 4; i++)
            detector.Process(Chords(c, f, g, c));

        var key = detector.Result();
        Assert.Equal(new MusicalKey(0, Minor: false), key);
        Assert.Equal("8B", key!.Value.Camelot);
        Assert.Equal("C", key.Value.Name);
    }

    [Fact]
    public void Key_AMinorProgressionIsAm()
    {
        int[] am = [45, 57, 60, 64], dm = [50, 62, 65, 69], e = [52, 56, 59, 64];
        var detector = new KeyDetector(44100);
        for (int i = 0; i < 4; i++)
            detector.Process(Chords(am, dm, am, e, am));

        var key = detector.Result();
        Assert.Equal(new MusicalKey(9, Minor: true), key);
        Assert.Equal("8A", key!.Value.Camelot);
        Assert.Equal("Am", key.Value.Name);
    }

    [Fact]
    public void Key_SilenceHasNoKey()
    {
        var detector = new KeyDetector(44100);
        detector.Process(new float[44100 * 3]);
        Assert.Null(detector.Result());
    }

    [Theory]
    [InlineData("8A", "8A", true)]
    [InlineData("8A", "7A", true)]
    [InlineData("8A", "9A", true)]
    [InlineData("8A", "8B", true)]
    [InlineData("12A", "1A", true)]
    [InlineData("8A", "10A", false)]
    [InlineData("8A", "9B", false)]
    [InlineData("3B", "8A", false)]
    public void Camelot_Compatibility(string a, string b, bool compatible)
    {
        Assert.True(MusicalKey.TryParseCamelot(a, out var ka));
        Assert.True(MusicalKey.TryParseCamelot(b, out var kb));
        Assert.Equal(compatible, ka.IsCompatibleWith(kb));
    }

    [Fact]
    public void Camelot_WheelIsComplete()
    {
        var codes = Enumerable.Range(0, 24).Select(MusicalKey.FromCode).Select(k => k.Camelot).ToList();
        Assert.Equal(24, codes.Distinct().Count());
        foreach (var code in codes)
        {
            Assert.True(MusicalKey.TryParseCamelot(code, out var parsed));
            Assert.Equal(code, parsed.Camelot);
        }
        Assert.Equal("9B", new MusicalKey(0, false).Transpose(7).Camelot); // Do → Sol
    }

    [Fact]
    public void Cache_ReadsVersionOneAndStoresLoudnessAndKey()
    {
        // Análisis antiguo (v1): sin sonoridad ni tonalidad.
        using var v1 = new MemoryStream();
        using (var w = new BinaryWriter(v1, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(0x41544A44);
            w.Write(1);
            w.Write(true);
            w.Write(128.0);
            w.Write(0.25);
            w.Write(100.0);
            w.Write(1);
            w.Write(0.5f); w.Write(0.1f); w.Write(0.2f); w.Write(0.3f);
        }
        v1.Position = 0;
        var old = CachingTrackAnalyzer.Read(v1);
        Assert.NotNull(old);
        Assert.Equal(128, old.Grid!.Bpm);
        Assert.Null(old.LoudnessLufs);
        Assert.Null(old.Key);

        using var v2 = new MemoryStream();
        CachingTrackAnalyzer.Write(v2, old with { LoudnessLufs = -9.5, Key = new MusicalKey(9, true) });
        v2.Position = 0;
        var read = CachingTrackAnalyzer.Read(v2)!;
        Assert.Equal(-9.5, read.LoudnessLufs);
        Assert.Equal("8A", read.Key!.Value.Camelot);
        Assert.Equal(128, read.Grid!.Bpm);
    }
}
