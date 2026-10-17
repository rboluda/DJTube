using DJTube.Core.Logic;

namespace DJTube.Core.Tests;

public sealed class EqMathTests
{
    [Fact]
    public void Band_CenterIsFlat()
    {
        Assert.Equal(0, EqMath.BandDb(0));
    }

    [Fact]
    public void Band_RightBoostsUpToSixDb()
    {
        Assert.Equal(3, EqMath.BandDb(0.5), 6);
        Assert.Equal(EqMath.MaxBoostDb, EqMath.BandDb(1), 6);
        Assert.Equal(EqMath.MaxBoostDb, EqMath.BandDb(5), 6);
    }

    [Fact]
    public void Band_LeftHalfwayIsMinusSixDb()
    {
        Assert.Equal(-6.02, EqMath.BandDb(-0.5), 2);
    }

    [Fact]
    public void Band_FullyLeftKills()
    {
        Assert.Equal(EqMath.KillDb, EqMath.BandDb(-1));
        Assert.True(EqMath.IsKill(-1));
        Assert.False(EqMath.IsKill(-0.5));
        Assert.Equal("KILL", EqMath.FormatBand(-1));
    }

    [Fact]
    public void Band_IsMonotonic()
    {
        double previous = double.NegativeInfinity;
        for (double k = -1; k <= 1.0001; k += 0.01)
        {
            double db = EqMath.BandDb(k);
            Assert.True(db >= previous, $"k={k}");
            previous = db;
        }
    }

    [Fact]
    public void Band_InvalidValuesAreFlat()
    {
        Assert.Equal(0, EqMath.BandDb(double.NaN));
    }

    [Fact]
    public void Gain_IsPlusMinusTwelveDb()
    {
        Assert.Equal(1, EqMath.GainFactor(0), 6);
        Assert.Equal(Math.Pow(10, 12 / 20.0), EqMath.GainFactor(1), 6);
        Assert.Equal(Math.Pow(10, -12 / 20.0), EqMath.GainFactor(-1), 6);
        Assert.Equal(-6, EqMath.GainDb(-0.5), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.03)]
    [InlineData(-0.03)]
    public void Filter_CenterIsOff(double knob)
    {
        Assert.Equal(FilterKind.Off, EqMath.Filter(knob).Kind);
    }

    [Fact]
    public void Filter_LeftIsLowPassSweepingDown()
    {
        var start = EqMath.Filter(-0.05);
        var end = EqMath.Filter(-1);

        Assert.Equal(FilterKind.LowPass, start.Kind);
        Assert.InRange(start.CutoffHz, 15000, EqMath.LowPassMaxHz);
        Assert.Equal(EqMath.LowPassMinHz, end.CutoffHz, 3);
        Assert.True(EqMath.Filter(-0.5).CutoffHz < start.CutoffHz);
    }

    [Fact]
    public void Filter_RightIsHighPassSweepingUp()
    {
        var start = EqMath.Filter(0.05);
        var end = EqMath.Filter(1);

        Assert.Equal(FilterKind.HighPass, start.Kind);
        Assert.InRange(start.CutoffHz, EqMath.HighPassMinHz, 25);
        Assert.Equal(EqMath.HighPassMaxHz, end.CutoffHz, 3);
        Assert.True(EqMath.Filter(0.5).CutoffHz > start.CutoffHz);
    }

    [Fact]
    public void Meter_MapsDecibelsToFraction()
    {
        Assert.Equal(1, EqMath.MeterFraction(1), 6);
        Assert.Equal(0.5, EqMath.MeterFraction(Math.Pow(10, -24 / 20.0)), 6);
        Assert.Equal(0, EqMath.MeterFraction(0));
        Assert.Equal(0, EqMath.MeterFraction(1e-6));
        Assert.Equal(1, EqMath.MeterFraction(2)); // por encima de 0 dB se queda arriba
    }
}
