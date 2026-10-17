using DJTube.Core.Logic;

namespace DJTube.Core.Tests;

public sealed class EffectMathTests
{
    [Fact]
    public void BeatSeconds_FollowsBpmAndPitch()
    {
        Assert.Equal(0.5, EffectMath.BeatSeconds(120, 1), 6);
        Assert.Equal(0.5 / 1.08, EffectMath.BeatSeconds(120, 1.08), 6); // +8 %: tiempos más cortos
        Assert.Equal(0.5, EffectMath.BeatSeconds(null, 1), 6);         // sin BPM: 120 por defecto
    }

    [Fact]
    public void Echo_DelayIsTheBeatFraction()
    {
        var s = new EffectSettings(EffectKind.Echo, true, 0.5, 0.5, EffectMath.BeatSeconds(128, 1));
        Assert.Equal(60 / 128.0 / 2, EffectMath.EchoDelay(s), 6);
        Assert.Equal(6, EffectMath.EchoDelay(s with { Beats = 64 }), 6); // límite de BASS_FX
        Assert.InRange(EffectMath.EchoFeedback(1), 0.7, 0.8);
    }

    [Fact]
    public void Flanger_SweepsOncePerPeriod()
    {
        var s = new EffectSettings(EffectKind.Flanger, true, 0.5, 8, 0.5); // 8 tiempos de 0,5 s = 4 s
        Assert.Equal(4, EffectMath.SweepPeriod(s), 6);
        // De 1 a 4 ms y vuelta (6 ms de recorrido) en 4 s = 1,5 ms/s.
        Assert.Equal(1.5, EffectMath.FlangerRate(s, 1, 4), 6);
    }

    [Fact]
    public void Phaser_RateIsInverseOfPeriod()
    {
        var s = new EffectSettings(EffectKind.Phaser, true, 0.5, 4, 0.5);
        Assert.Equal(0.5, EffectMath.PhaserRate(s), 6);
    }

    [Theory]
    [InlineData(0.125, "1/8")]
    [InlineData(0.75, "3/4")]
    [InlineData(2, "2")]
    public void Beats_AreFormatted(double beats, string text)
    {
        Assert.Equal(text, EffectMath.FormatBeats(beats));
    }

    [Fact]
    public void Reverb_DoesNotUseBeats()
    {
        Assert.False(EffectMath.UsesBeats(EffectKind.Reverb));
        Assert.True(EffectMath.UsesBeats(EffectKind.Echo));
        Assert.Equal(0.5, EffectMath.DefaultBeats(EffectKind.Echo));
    }
}
