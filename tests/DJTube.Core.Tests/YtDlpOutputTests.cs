using DJTube.Sources.YouTube;

namespace DJTube.Core.Tests;

public class YtDlpOutputTests
{
    [Theory]
    [InlineData("DJTUBE-PROGRESS 0 1000 NA", 0.0)]
    [InlineData("DJTUBE-PROGRESS 250 1000 NA", 0.25)]
    [InlineData("DJTUBE-PROGRESS 1000 1000 NA", 1.0)]
    [InlineData("DJTUBE-PROGRESS 500 NA 2000", 0.25)]
    [InlineData("DJTUBE-PROGRESS 5000 NA 2000", 1.0)]
    [InlineData("DJTUBE-PROGRESS 10271496 10271496 NA", 1.0)]
    public void TryParseProgress_ComputesFraction(string line, double expected)
    {
        Assert.Equal(expected, YtDlpOutput.TryParseProgress(line)!.Value, 1e-9);
    }

    [Theory]
    [InlineData("DJTUBE-PROGRESS 100 NA NA")]
    [InlineData("DJTUBE-PROGRESS NA NA NA")]
    [InlineData("DJTUBE-PROGRESS 100 0 NA")]
    [InlineData("[download] 45.3% of 9.80MiB")]
    [InlineData("")]
    public void TryParseProgress_UnknownOrOtherLinesGiveNull(string line)
    {
        Assert.Null(YtDlpOutput.TryParseProgress(line));
    }

    [Fact]
    public void TryParseMeta_RegularVideo_KeepsTitleWithoutArtist()
    {
        var info = YtDlpOutput.TryParseMeta(
            """DJTUBE-META {"id": "aqz-KE-bpKQ", "title": "Big Buck Bunny 60fps 4K", "uploader": "Blender", "channel": "Blender", "duration": 635}""");

        Assert.NotNull(info);
        Assert.Equal("aqz-KE-bpKQ", info.Id);
        Assert.Equal("Big Buck Bunny 60fps 4K", info.Title);
        Assert.Null(info.Artist);
        Assert.Equal(635, info.DurationSeconds);
    }

    [Fact]
    public void TryParseMeta_MusicVideo_UsesTrackAndArtist()
    {
        var info = YtDlpOutput.TryParseMeta(
            """DJTUBE-META {"id": "abcdefghijk", "title": "Artista - Canción (Official Audio)", "artist": "Artista", "track": "Canción", "channel": "Artista - Topic", "duration": 215.5}""");

        Assert.NotNull(info);
        Assert.Equal("Canción", info.Title);
        Assert.Equal("Artista", info.Artist);
        Assert.Equal(215.5, info.DurationSeconds);
    }

    [Fact]
    public void TryParseMeta_TopicChannel_IsArtist()
    {
        var info = YtDlpOutput.TryParseMeta(
            """DJTUBE-META {"id": "abcdefghijk", "title": "Canción", "channel": "Artista - Topic"}""");

        Assert.NotNull(info);
        Assert.Equal("Canción", info.Title);
        Assert.Equal("Artista", info.Artist);
        Assert.Null(info.DurationSeconds);
    }

    [Theory]
    [InlineData("DJTUBE-META {no es json")]
    [InlineData("DJTUBE-META {\"title\": \"sin id\"}")]
    [InlineData("otra línea")]
    public void TryParseMeta_InvalidGivesNull(string line)
    {
        Assert.Null(YtDlpOutput.TryParseMeta(line));
    }

    [Fact]
    public void TryParseError_ExtractsMessage()
    {
        Assert.Equal("[youtube] abc: Video unavailable",
            YtDlpOutput.TryParseError("ERROR: [youtube] abc: Video unavailable"));
        Assert.Null(YtDlpOutput.TryParseError("WARNING: algo"));
    }

    [Fact]
    public void DownloadArguments_AskOnlyForTheVideoAsM4a()
    {
        var args = YtDlpOutput.DownloadArguments("https://youtu.be/abcdefghijk", @"C:\cache\%(id)s.%(ext)s");

        Assert.Contains("--no-playlist", args);
        Assert.Equal(YtDlpOutput.AudioFormat, args[args.ToList().IndexOf("-f") + 1]);
        Assert.Equal(@"C:\cache\%(id)s.%(ext)s", args[args.ToList().IndexOf("-o") + 1]);
        Assert.Equal("https://youtu.be/abcdefghijk", args[^1]);
    }
}
