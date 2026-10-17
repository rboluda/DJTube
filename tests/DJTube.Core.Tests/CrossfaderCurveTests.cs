using DJTube.Core.Logic;

namespace DJTube.Core.Tests;

public class CrossfaderCurveTests
{
    private const double Tolerance = 1e-9;

    public static TheoryData<CrossfaderCurveType> AllCurves =>
        new(Enum.GetValues<CrossfaderCurveType>());

    [Theory]
    [MemberData(nameof(AllCurves))]
    public void Extremes_OnlyOneDeckSounds(CrossfaderCurveType curve)
    {
        var left = CrossfaderCurve.Gains(-1, curve);
        var right = CrossfaderCurve.Gains(1, curve);

        Assert.Equal(1, left.A, Tolerance);
        Assert.Equal(0, left.B, Tolerance);
        Assert.Equal(0, right.A, Tolerance);
        Assert.Equal(1, right.B, Tolerance);
    }

    [Theory]
    [MemberData(nameof(AllCurves))]
    public void Curves_AreSymmetric(CrossfaderCurveType curve)
    {
        for (double p = -1; p <= 1; p += 0.05)
        {
            var gains = CrossfaderCurve.Gains(p, curve);
            var mirrored = CrossfaderCurve.Gains(-p, curve);
            Assert.Equal(gains.A, mirrored.B, 1e-9);
            Assert.Equal(gains.B, mirrored.A, 1e-9);
        }
    }

    [Theory]
    [MemberData(nameof(AllCurves))]
    public void Curves_AreMonotonic(CrossfaderCurveType curve)
    {
        var previous = CrossfaderCurve.Gains(-1, curve);
        for (double p = -0.99; p <= 1; p += 0.01)
        {
            var gains = CrossfaderCurve.Gains(p, curve);
            Assert.True(gains.A <= previous.A + Tolerance, $"A sube en {p}");
            Assert.True(gains.B >= previous.B - Tolerance, $"B baja en {p}");
            previous = gains;
        }
    }

    [Theory]
    [MemberData(nameof(AllCurves))]
    public void OutOfRangeAndNaN_AreClamped(CrossfaderCurveType curve)
    {
        Assert.Equal(CrossfaderCurve.Gains(-1, curve), CrossfaderCurve.Gains(-5, curve));
        Assert.Equal(CrossfaderCurve.Gains(1, curve), CrossfaderCurve.Gains(3, curve));
        Assert.Equal(CrossfaderCurve.Gains(0, curve), CrossfaderCurve.Gains(double.NaN, curve));
    }

    [Fact]
    public void Linear_CenterIsHalfEach()
    {
        var gains = CrossfaderCurve.Gains(0, CrossfaderCurveType.Linear);

        Assert.Equal(0.5, gains.A, Tolerance);
        Assert.Equal(0.5, gains.B, Tolerance);
    }

    [Fact]
    public void Linear_GainsAlwaysSumToOne()
    {
        for (double p = -1; p <= 1; p += 0.1)
        {
            var gains = CrossfaderCurve.Gains(p, CrossfaderCurveType.Linear);
            Assert.Equal(1, gains.A + gains.B, Tolerance);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.85)]
    [InlineData(0.85)]
    public void FastCut_BothFullAwayFromEdges(double position)
    {
        var gains = CrossfaderCurve.Gains(position, CrossfaderCurveType.FastCut);

        Assert.Equal(1, gains.A, Tolerance);
        Assert.Equal(1, gains.B, Tolerance);
    }

    [Fact]
    public void FastCut_CutsWithinEdgeWidth()
    {
        // A punto de llegar al extremo B, A ya está atenuado pero no en silencio.
        double position = 1 - CrossfaderCurve.FastCutWidth; // x = 1 - width/2
        var gains = CrossfaderCurve.Gains(position, CrossfaderCurveType.FastCut);

        Assert.Equal(0.5, gains.A, Tolerance);
        Assert.Equal(1, gains.B, Tolerance);
    }

    [Fact]
    public void ConstantPower_KeepsTotalPower()
    {
        for (double p = -1; p <= 1; p += 0.1)
        {
            var gains = CrossfaderCurve.Gains(p, CrossfaderCurveType.ConstantPower);
            Assert.Equal(1, gains.A * gains.A + gains.B * gains.B, Tolerance);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(0.5, 0.25)]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    public void FaderTaper_IsQuadratic(double position, double expected)
    {
        Assert.Equal(expected, FaderTaper.ToGain(position), Tolerance);
    }
}
