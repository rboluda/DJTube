using DJTube.Core.Logic;

namespace DJTube.Core.Tests;

public class HeadphoneMathTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(true, 0.0)]
    [InlineData(true, 0.3)]
    [InlineData(true, 1.0)]
    public void CueOnly_IgnoresFadersAndCrossfader(bool cue, double masterGain)
    {
        Assert.Equal(0.8, HeadphoneMath.CueGain(cue, cueMix: 0, masterGain, headphoneVolume: 0.8), Tolerance);
    }

    [Fact]
    public void CueOnly_DeckWithoutCueIsSilent()
    {
        Assert.Equal(0, HeadphoneMath.CueGain(false, cueMix: 0, masterGain: 1, headphoneVolume: 1), Tolerance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullMix_FollowsMaster(bool cue)
    {
        Assert.Equal(0.25, HeadphoneMath.CueGain(cue, cueMix: 1, masterGain: 0.5, headphoneVolume: 0.5), Tolerance);
    }

    [Fact]
    public void HalfMix_BlendsCueAndMaster()
    {
        // 0,5 × CUE (1) + 0,5 × master (0,2) = 0,6
        Assert.Equal(0.6, HeadphoneMath.CueGain(true, cueMix: 0.5, masterGain: 0.2, headphoneVolume: 1), Tolerance);
        Assert.Equal(0.1, HeadphoneMath.CueGain(false, cueMix: 0.5, masterGain: 0.2, headphoneVolume: 1), Tolerance);
    }

    [Fact]
    public void CueGain_ClampsInvalidInput()
    {
        Assert.Equal(1, HeadphoneMath.CueGain(true, cueMix: -3, masterGain: 0, headphoneVolume: 7), Tolerance);
        Assert.Equal(0, HeadphoneMath.CueGain(true, cueMix: double.NaN, masterGain: double.NaN, headphoneVolume: double.NaN), Tolerance);
    }

    [Fact]
    public void DriftCorrection_IsNeutralAtTarget()
    {
        Assert.Equal(1, HeadphoneMath.DriftCorrection(HeadphoneMath.TargetBufferSeconds), Tolerance);
    }

    [Fact]
    public void DriftCorrection_SpeedsUpWithTooMuchBufferAndSlowsDownWithTooLittle()
    {
        Assert.True(HeadphoneMath.DriftCorrection(0.05) > 1);
        Assert.True(HeadphoneMath.DriftCorrection(0.01) < 1);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(10.0)]
    public void DriftCorrection_IsLimited(double buffered)
    {
        double ratio = HeadphoneMath.DriftCorrection(buffered);
        Assert.InRange(ratio, 1 - HeadphoneMath.MaxCorrection, 1 + HeadphoneMath.MaxCorrection);
    }

    [Fact]
    public void DriftCorrection_ConvergesTowardsTarget()
    {
        // Simulación: el master produce a 1x, los auriculares consumen a "ratio" con un reloj un 0,01 % más rápido.
        double buffered = 0;
        const double step = 0.01; // 10 ms
        for (int i = 0; i < 6000; i++) // 60 s
        {
            double ratio = HeadphoneMath.DriftCorrection(buffered);
            buffered = Math.Max(0, buffered + step - step * ratio * 1.0001);
        }
        Assert.InRange(buffered, HeadphoneMath.TargetBufferSeconds - 0.005, HeadphoneMath.TargetBufferSeconds + 0.005);
    }

    [Theory]
    [InlineData(false, 0.0, true)]
    [InlineData(true, 0.03, false)]
    [InlineData(true, 0.3, true)]
    public void ShouldReset(bool active, double buffered, bool expected)
    {
        Assert.Equal(expected, HeadphoneMath.ShouldReset(active, buffered));
    }
}
