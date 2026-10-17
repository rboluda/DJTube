using DJTube.Core.Logic;
using DJTube.Core.Models;

namespace DJTube.Core.Tests;

public sealed class AutomixTests
{
    [Fact]
    public void Remaining_AccountsForPitch()
    {
        Assert.Equal(100, AutomixMath.RemainingSeconds(300, 200, 1), 6);
        Assert.Equal(100 / 1.08, AutomixMath.RemainingSeconds(300, 200, 1.08), 6);
        Assert.Equal(0, AutomixMath.RemainingSeconds(300, 310, 1), 6);
    }

    [Fact]
    public void PrepareBeforeFade()
    {
        Assert.False(AutomixMath.ShouldPrepare(60, 10));
        Assert.True(AutomixMath.ShouldPrepare(40, 10));
        Assert.False(AutomixMath.ShouldStartFade(11, 10));
        Assert.True(AutomixMath.ShouldStartFade(10, 10));
    }

    [Fact]
    public void Crossfader_MovesFromOneSideToTheOther()
    {
        Assert.Equal(-1, AutomixMath.Crossfader(0, towardsB: true), 6);
        Assert.Equal(0, AutomixMath.Crossfader(0.5, towardsB: true), 6);
        Assert.Equal(1, AutomixMath.Crossfader(1, towardsB: true), 6);
        Assert.Equal(1, AutomixMath.Crossfader(0, towardsB: false), 6);
        Assert.Equal(-1, AutomixMath.Crossfader(2, towardsB: false), 6);
    }

    [Fact]
    public void StartsOnTheFirstBeat()
    {
        Assert.Equal(0.42, AutomixMath.StartPosition(new BeatGrid(128, 0.42)), 6);
        Assert.Equal(0, AutomixMath.StartPosition(null));
        Assert.Equal(0, AutomixMath.StartPosition(new BeatGrid(128, 45))); // un "primer golpe" tan tardío no se usa
    }

    [Fact]
    public void MatchPitch_UsesTheClosestTempo()
    {
        Assert.Equal(128.0 / 125 * 100 - 100, AutomixMath.MatchPitch(128, 125, 8)!.Value, 6);
        Assert.Equal(140.0 / 2 / 70 * 100 - 100, AutomixMath.MatchPitch(140, 70, 8)!.Value, 6) ; // mitad: 0 %
        Assert.Null(AutomixMath.MatchPitch(128, 100, 8)); // +28 %: no cabe
        Assert.NotNull(AutomixMath.MatchPitch(128, 115, 16));
    }
}
