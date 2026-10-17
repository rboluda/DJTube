using DJTube.Core.Library;
using DJTube.Core.Logic;
using DJTube.Core.Models;

namespace DJTube.Core.Tests;

public sealed class LoopTests : IDisposable
{
    // 120 BPM: un tiempo cada 0,5 s, primer golpe en 0,1 s.
    private static readonly BeatGrid Grid = new(120, 0.1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DJTubeLoopTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void AutoLoop_StartsOnPreviousBeatAndLastsTheBeats()
    {
        var loop = LoopMath.AutoLoop(10.33, Grid, 4, duration: 200);

        Assert.NotNull(loop);
        Assert.Equal(10.1, loop.Value.Start, 6); // tiempo anterior a 10,33
        Assert.Equal(12.1, loop.Value.End, 6);   // 4 tiempos de 0,5 s
    }

    [Fact]
    public void AutoLoop_ShortLoopsSnapToTheFraction()
    {
        var loop = LoopMath.AutoLoop(10.33, Grid, 0.25, duration: 200)!.Value;

        Assert.Equal(10.225, loop.Start, 6); // cuartos de tiempo: 10,1 + 0,125·1
        Assert.Equal(0.125, loop.Length, 6);
    }

    [Fact]
    public void AutoLoop_WithoutQuantizeStartsHere()
    {
        var loop = LoopMath.AutoLoop(10.33, Grid, 1, duration: 200, quantize: false)!.Value;
        Assert.Equal(10.33, loop.Start, 6);
        Assert.Equal(10.83, loop.End, 6);
    }

    [Fact]
    public void AutoLoop_NeedsBpm()
    {
        Assert.Null(LoopMath.AutoLoop(10, null, 4, 200));
    }

    [Fact]
    public void AutoLoop_IsCutAtTheEndOfTheTrack()
    {
        var loop = LoopMath.AutoLoop(198.2, Grid, 16, duration: 200)!.Value;
        Assert.Equal(200, loop.End, 6);
    }

    [Fact]
    public void HalveAndDouble_KeepTheStart()
    {
        var loop = new LoopRegion(10, 12);
        Assert.Equal(new LoopRegion(10, 11), LoopMath.Halve(loop));
        Assert.Equal(new LoopRegion(10, 14), LoopMath.Double(loop, 200));
        var tiny = new LoopRegion(10, 10.015);
        Assert.Equal(tiny, LoopMath.Halve(tiny)); // por debajo del mínimo no se reduce
    }

    [Fact]
    public void Manual_NeedsOutAfterIn()
    {
        Assert.Equal(new LoopRegion(5, 7), LoopMath.Manual(5, 7, 200));
        Assert.Null(LoopMath.Manual(7, 5, 200));
    }

    [Fact]
    public void Quantize_SnapsOnlyWhenClose()
    {
        Assert.Equal(10.1, LoopMath.Quantize(10.15, Grid), 6);  // cerca del golpe
        Assert.Equal(10.35, LoopMath.Quantize(10.35, Grid), 6); // entre golpes: se respeta
        Assert.Equal(10.35, LoopMath.Quantize(10.35, null), 6);
    }

    [Fact]
    public void Roll_ContinuesWhereTheTrackWouldHaveBeen()
    {
        Assert.Equal(13, LoopMath.RollExitPosition(10, 2.5, 1.2, 200), 6);
        Assert.Equal(199.99, LoopMath.RollExitPosition(199, 5, 1, 200), 6);
    }

    [Fact]
    public void Contains_IncludesStartExcludesEnd()
    {
        var loop = new LoopRegion(5, 7);
        Assert.True(loop.Contains(5));
        Assert.True(loop.Contains(6.99));
        Assert.False(loop.Contains(7));
        Assert.False(loop.Contains(4.9));
    }

    [Theory]
    [InlineData(0.125, "1/8")]
    [InlineData(0.5, "1/2")]
    [InlineData(4, "4")]
    [InlineData(32, "32")]
    public void Size_IsFormatted(double beats, string expected)
    {
        Assert.Equal(expected, LoopMath.FormatSize(beats));
    }

    [Fact]
    public void HotCues_AreSavedPerTrack()
    {
        string path = Path.Combine(_dir, "hotcues.json");
        var store = new TrackMarksStore(path);
        string key = TrackMarksStore.Key("YouTube", "abcdefghijk");
        var cues = new double?[HotCueMath.Count];
        cues[0] = 12.5;
        cues[3] = 60;
        store.SetHotCues(key, cues);

        var reloaded = new TrackMarksStore(path).GetHotCues(key);

        Assert.Equal(HotCueMath.Count, reloaded.Length);
        Assert.Equal(12.5, reloaded[0]);
        Assert.Equal(60, reloaded[3]);
        Assert.Null(reloaded[1]);
        Assert.All(new TrackMarksStore(path).GetHotCues("otra:pista"), c => Assert.Null(c));
    }

    [Fact]
    public void HotCues_ForgetAndEmptyRemoveTheEntry()
    {
        string path = Path.Combine(_dir, "hotcues.json");
        var store = new TrackMarksStore(path);
        store.SetHotCues("a", [1.0, null, null, null, null, null, null, null]);
        store.SetHotCues("b", [2.0, null, null, null, null, null, null, null]);

        store.Forget("a");
        store.SetHotCues("b", new double?[8]);

        var reloaded = new TrackMarksStore(path);
        Assert.Null(reloaded.GetHotCues("a")[0]);
        Assert.Null(reloaded.GetHotCues("b")[0]);
    }
}
