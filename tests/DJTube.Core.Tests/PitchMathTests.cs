using DJTube.Core.Logic;

namespace DJTube.Core.Tests;

public class PitchMathTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(8, 1.08)]
    [InlineData(-8, 0.92)]
    [InlineData(16, 1.16)]
    [InlineData(-16, 0.84)]
    public void ToRate_ConvertsPercent(double percent, double expected)
    {
        Assert.Equal(expected, PitchMath.ToRate(percent), Tolerance);
    }

    [Theory]
    [InlineData(-12.5)]
    [InlineData(0)]
    [InlineData(3.33)]
    public void FromRate_IsInverseOfToRate(double percent)
    {
        Assert.Equal(percent, PitchMath.FromRate(PitchMath.ToRate(percent)), Tolerance);
    }

    [Theory]
    [InlineData(5, 8, 5)]
    [InlineData(12, 8, 8)]
    [InlineData(-12, 8, -8)]
    [InlineData(12, 16, 12)]
    [InlineData(-20, 16, -16)]
    [InlineData(3, -8, 3)]
    public void Clamp_LimitsToRange(double percent, double range, double expected)
    {
        Assert.Equal(expected, PitchMath.Clamp(percent, range));
    }

    [Fact]
    public void Clamp_NaNBecomesZero()
    {
        Assert.Equal(0, PitchMath.Clamp(double.NaN, 8));
    }

    [Theory]
    [InlineData(128, 0, 128)]
    [InlineData(125, 2.4, 128)]
    [InlineData(140, -5, 133)]
    public void EffectiveBpm_AppliesPitch(double bpm, double percent, double expected)
    {
        Assert.Equal(expected, PitchMath.EffectiveBpm(bpm, percent), 1e-6);
    }

    [Fact]
    public void Semitones_DoublingSpeedIsOneOctave()
    {
        Assert.Equal(12, PitchMath.Semitones(100), Tolerance);
        Assert.Equal(0, PitchMath.Semitones(0), Tolerance);
    }

    [Fact]
    public void Semitones_EightPercentIsAboutOneAndAThirdSemitones()
    {
        Assert.Equal(1.3324, PitchMath.Semitones(8), 1e-3);
        Assert.True(PitchMath.Semitones(-8) < 0);
    }

    [Fact]
    public void ToTempoSettings_WithKeyLock_ChangesTempoOnly()
    {
        var settings = PitchMath.ToTempoSettings(6, keyLock: true, sourceFrequency: 44100);

        Assert.Equal(6, settings.TempoPercent);
        Assert.Equal(44100, settings.Frequency);
    }

    [Fact]
    public void ToTempoSettings_WithoutKeyLock_ChangesFrequencyOnly()
    {
        var settings = PitchMath.ToTempoSettings(-8, keyLock: false, sourceFrequency: 44100);

        Assert.Equal(0, settings.TempoPercent);
        Assert.Equal(40572, settings.Frequency, 1e-6);
    }

    [Theory]
    [InlineData(0, "0.00 %")]
    [InlineData(-0.001, "0.00 %")]
    [InlineData(1.25, "+1.25 %")]
    [InlineData(-8, "-8.00 %")]
    [InlineData(16, "+16.00 %")]
    public void Format_ShowsSignAndTwoDecimals(double percent, string expected)
    {
        Assert.Equal(expected, PitchMath.Format(percent));
    }
}
