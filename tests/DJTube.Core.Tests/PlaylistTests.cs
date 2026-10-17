using DJTube.Core.Library;
using DJTube.Core.Logic;
using DJTube.Sources.YouTube;

namespace DJTube.Core.Tests;

public sealed class PlaylistTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DJTubePlTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLabcdefghij123", "PLabcdefghij123")]
    [InlineData("youtube.com/playlist?list=PLx_y-z", "PLx_y-z")]
    [InlineData("https://www.youtube.com/watch?v=BdMvMlxA8yE&list=RDBdMvMlxA8yE&start_radio=1", "RDBdMvMlxA8yE")]
    [InlineData("https://music.youtube.com/playlist?list=OLAK5uy_abc", "OLAK5uy_abc")]
    public void PlaylistId_IsRecognised(string url, string expected)
    {
        Assert.True(YouTubeUrl.TryGetPlaylistId(url, out string id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=BdMvMlxA8yE")]
    [InlineData("https://youtu.be/BdMvMlxA8yE")]
    [InlineData("https://example.com/playlist?list=PLabc")]
    [InlineData("https://www.youtube.com/playlist?list=<script>")]
    [InlineData("")]
    public void PlaylistId_RejectsOtherLinks(string url)
    {
        Assert.False(YouTubeUrl.TryGetPlaylistId(url, out _));
    }

    [Fact]
    public void PlaylistUrl_KeepsStartVideoForMixes()
    {
        Assert.Equal("https://www.youtube.com/playlist?list=PLabc", YouTubeUrl.ToPlaylistUrl("PLabc", "BdMvMlxA8yE"));
        Assert.Equal("https://www.youtube.com/watch?v=BdMvMlxA8yE&list=RDBdMvMlxA8yE",
            YouTubeUrl.ToPlaylistUrl("RDBdMvMlxA8yE", "BdMvMlxA8yE"));
    }

    [Fact]
    public void PlaylistItem_IsParsedWithArtistAndTitle()
    {
        var entry = YtDlpOutput.TryParsePlaylistItem(
            """DJTUBE-ITEM {"id": "8XvQVrNHX4Q", "title": "Dee Dee - Forever (Official Video)", "channel": "DEE DEE Official", "duration": 236}""");

        Assert.NotNull(entry);
        Assert.Equal("8XvQVrNHX4Q", entry.Id);
        Assert.Equal("Dee Dee", entry.Artist);
        Assert.Equal("Forever", entry.Title);
        Assert.Equal(236, entry.DurationSeconds);
    }

    [Theory]
    [InlineData("""DJTUBE-ITEM {"id": "8XvQVrNHX4Q", "title": "[Private video]", "duration": null}""")]
    [InlineData("""DJTUBE-ITEM {"id": "8XvQVrNHX4Q", "title": "[Deleted video]"}""")]
    [InlineData("""DJTUBE-ITEM {"id": "bad", "title": "x"}""")]
    [InlineData("""DJTUBE-ITEM {not json""")]
    [InlineData("[download] 10%")]
    public void PlaylistItem_SkipsUnavailableAndNoise(string line)
    {
        Assert.Null(YtDlpOutput.TryParsePlaylistItem(line));
    }

    [Fact]
    public void PlaylistTitle_IsParsed()
    {
        Assert.Equal("Mix - Apollo - Dance",
            YtDlpOutput.TryParsePlaylistTitle("""DJTUBE-LIST {"id": "RDBdMvMlxA8yE", "title": "Mix - Apollo - Dance"}"""));
        Assert.Null(YtDlpOutput.TryParsePlaylistTitle("DJTUBE-ITEM {}"));
    }

    [Fact]
    public void PlaylistArguments_ListWithoutDownloading()
    {
        var args = YtDlpOutput.PlaylistArguments("https://www.youtube.com/playlist?list=PLabc");
        Assert.Contains("--flat-playlist", args);
        Assert.Equal("https://www.youtube.com/playlist?list=PLabc", args[^1]);
    }

    [Fact]
    public void Store_SurvivesReload()
    {
        string path = Path.Combine(_dir, "playlists.json");
        var store = new PlaylistStore(path);
        store.AddOrUpdate("PL1", "Mi lista", "https://www.youtube.com/playlist?list=PL1", ["aaaaaaaaaaa", "bbbbbbbbbbb", "aaaaaaaaaaa"]);

        var reloaded = new PlaylistStore(path).Playlists;

        var playlist = Assert.Single(reloaded);
        Assert.Equal("Mi lista", playlist.Title);
        Assert.Equal(["aaaaaaaaaaa", "bbbbbbbbbbb"], playlist.VideoIds); // sin repetidos, en orden
    }

    [Fact]
    public void Store_UpdateReplacesContentAndKeepsOneEntry()
    {
        var store = new PlaylistStore(Path.Combine(_dir, "p.json"));
        store.AddOrUpdate("PL1", "Uno", "u", ["aaaaaaaaaaa"]);
        store.AddOrUpdate("PL1", "Uno (nuevo)", "u", ["aaaaaaaaaaa", "ccccccccccc"]);

        var playlist = Assert.Single(store.Playlists);
        Assert.Equal("Uno (nuevo)", playlist.Title);
        Assert.Equal(2, playlist.VideoIds.Count);
    }

    [Fact]
    public void Store_TracksMembershipAndRemovals()
    {
        var store = new PlaylistStore(Path.Combine(_dir, "p.json"));
        store.AddOrUpdate("PL1", "Uno", "u1", ["aaaaaaaaaaa", "bbbbbbbbbbb"]);
        store.AddOrUpdate("PL2", "Dos", "u2", ["bbbbbbbbbbb"]);

        Assert.Equal(2, store.ListsContaining("bbbbbbbbbbb").Count);

        store.RemoveVideo("bbbbbbbbbbb");
        Assert.Empty(store.ListsContaining("bbbbbbbbbbb"));
        Assert.Equal(["aaaaaaaaaaa"], store.Playlists.Single(p => p.Id == "PL1").VideoIds);

        store.Remove("PL1");
        Assert.Equal(["PL2"], store.Playlists.Select(p => p.Id));
        store.Rename("PL2", "  Dos bis ");
        Assert.Equal("Dos bis", store.Playlists[0].Title);
    }

    [Fact]
    public void Store_DamagedFileStartsEmpty()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "p.json");
        File.WriteAllText(path, "{ esto no es json");
        Assert.Empty(new PlaylistStore(path).Playlists);
    }
}
