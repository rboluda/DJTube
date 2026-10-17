using DJTube.Core.Logic;

namespace DJTube.Core.Tests;

public class TimeFormatTests
{
    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(5.25, "0:05.2")]
    [InlineData(59.99, "0:59.9")]
    [InlineData(60, "1:00.0")]
    [InlineData(61.5, "1:01.5")]
    [InlineData(3599.9, "59:59.9")]
    [InlineData(3600, "60:00.0")]
    [InlineData(0.3, "0:00.3")]
    public void Format_ProducesMinutesSecondsTenths(double seconds, string expected)
    {
        Assert.Equal(expected, TimeFormat.Format(seconds));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Format_InvalidValuesShowZero(double seconds)
    {
        Assert.Equal("0:00.0", TimeFormat.Format(seconds));
    }

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(59.9, "00:00:59")]
    [InlineData(61, "00:01:01")]
    [InlineData(3600 + 23 * 60 + 5.5, "01:23:05")]
    [InlineData(-3, "00:00:00")]
    public void Clock_ShowsHoursMinutesSeconds(double seconds, string expected)
    {
        Assert.Equal(expected, TimeFormat.Clock(TimeSpan.FromSeconds(seconds)));
    }
}
