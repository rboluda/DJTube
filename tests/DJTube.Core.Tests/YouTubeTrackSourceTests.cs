using System.Text.Json;
using DJTube.Core.Models;
using DJTube.Sources.YouTube;

namespace DJTube.Core.Tests;

/// <summary>Comportamiento de la caché (sin red ni yt-dlp).</summary>
public sealed class YouTubeTrackSourceTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "DJTubeYtTests_" + Guid.NewGuid().ToString("N"));
    private readonly YouTubeTrackSource _source;

    public YouTubeTrackSourceTests()
    {
        // Sin yt-dlp: cualquier intento de descarga falla.
        _source = new YouTubeTrackSource(_cache, findExecutable: () => null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cache))
            Directory.Delete(_cache, recursive: true);
    }

    [Fact]
    public async Task OpenAsync_CachedTrack_ReturnsFileWithoutDownloading()
    {
        Seed("abcdefghijk", new YouTubeDownloadInfo("abcdefghijk", "Canción", "Artista", 200));
        var progress = new List<double>();

        using var media = await _source.OpenAsync(
            _source.CreateTrackInfo("abcdefghijk"), new SyncProgress(progress.Add));

        Assert.Equal(Path.Combine(_cache, "abcdefghijk.m4a"), media.FilePath);
        Assert.Equal("Canción", media.Track.Title);
        Assert.Equal("Artista", media.Track.Artist);
        Assert.Equal(TimeSpan.FromSeconds(200), media.Track.Duration);
        Assert.Equal([1.0], progress);
    }

    [Fact]
    public void CreateTrackInfo_UsesCachedTitleOrPlaceholder()
    {
        Seed("abcdefghijk", new YouTubeDownloadInfo("abcdefghijk", "Canción", null, null));

        Assert.Equal("Canción", _source.CreateTrackInfo("abcdefghijk").Title);
        Assert.Equal("YouTube zyxwvutsrqp", _source.CreateTrackInfo("zyxwvutsrqp").Title);
    }

    [Fact]
    public async Task OpenAsync_AudioWithoutMetadata_IsNotCached()
    {
        Directory.CreateDirectory(_cache);
        File.WriteAllBytes(Path.Combine(_cache, "abcdefghijk.m4a"), [1, 2, 3]);

        Assert.False(_source.IsCached("abcdefghijk"));
        var ex = await Assert.ThrowsAsync<YouTubeSourceException>(
            () => _source.OpenAsync(_source.CreateTrackInfo("abcdefghijk")));
        Assert.Contains("yt-dlp", ex.Message);
    }

    [Fact]
    public async Task OpenAsync_RejectsTracksFromOtherSources()
    {
        var track = new TrackInfo("Archivos locales", "abcdefghijk", "x");

        await Assert.ThrowsAsync<ArgumentException>(() => _source.OpenAsync(track));
    }

    [Fact]
    public void CreateTrackInfo_RejectsInvalidId()
    {
        Assert.Throws<ArgumentException>(() => _source.CreateTrackInfo("../../hack"));
    }

    [Fact]
    public async Task BrowseAsync_ListsCachedTracks()
    {
        Seed("abcdefghijk", new YouTubeDownloadInfo("abcdefghijk", "Uno", null, null));
        Seed("zyxwvutsrqp", new YouTubeDownloadInfo("zyxwvutsrqp", "Dos", null, null));

        var titles = new List<string>();
        await foreach (var track in _source.BrowseAsync(null))
            titles.Add(track.Title);

        Assert.Equal(["Dos", "Uno"], titles.Order());
    }

    [Fact]
    public async Task GetArtworkAsync_DownloadsThumbnailOnceAndCachesIt()
    {
        var requested = new List<string>();
        var source = new YouTubeTrackSource(_cache, () => null, (url, _) =>
        {
            requested.Add(url);
            return Task.FromResult(new byte[] { 0xFF, 0xD8, 0xFF });
        });
        var track = source.CreateTrackInfo("abcdefghijk");

        string? first = await source.GetArtworkAsync(track);
        string? second = await source.GetArtworkAsync(track);

        Assert.Equal(Path.Combine(_cache, "abcdefghijk.jpg"), first);
        Assert.Equal(first, second);
        Assert.Equal(["https://i.ytimg.com/vi/abcdefghijk/mqdefault.jpg"], requested);
        Assert.Equal([0xFF, 0xD8, 0xFF], File.ReadAllBytes(first!));
    }

    [Fact]
    public async Task GetArtworkAsync_NetworkErrorGivesNull()
    {
        var source = new YouTubeTrackSource(_cache, () => null,
            (_, _) => throw new HttpRequestException("sin conexión"));

        Assert.Null(await source.GetArtworkAsync(source.CreateTrackInfo("abcdefghijk")));
        Assert.False(File.Exists(Path.Combine(_cache, "abcdefghijk.jpg")));
    }

    [Fact]
    public async Task GetArtworkAsync_OtherSourcesHaveNoArtwork()
    {
        Assert.Null(await _source.GetArtworkAsync(new TrackInfo("Archivos locales", @"C:\x.mp3", "x")));
    }

    [Fact]
    public void OldCacheEntries_GetArtistAndTitleSplitOnce()
    {
        Seed("abcdefghijk", new YouTubeDownloadInfo("abcdefghijk", "Apollo - Dance (Official Video)", null, 228));

        var track = _source.CreateTrackInfo("abcdefghijk");

        Assert.Equal("Apollo", track.Artist);
        Assert.Equal("Dance", track.Title);
        string json = File.ReadAllText(Path.Combine(_cache, "abcdefghijk.json"));
        Assert.Contains("Apollo - Dance (Official Video)", json); // título original guardado
    }

    [Fact]
    public void UpdateMetadata_ChangesArtistAndTitleAndSurvivesReload()
    {
        Seed("abcdefghijk", new YouTubeDownloadInfo("abcdefghijk", "Dance", "Apollo", 228, "Apollo - Dance"));

        var updated = _source.UpdateMetadata("abcdefghijk", "Apollo 440", "Dance (Remix)");

        Assert.NotNull(updated);
        var reloaded = new YouTubeTrackSource(_cache, () => null).CreateTrackInfo("abcdefghijk");
        Assert.Equal("Apollo 440", reloaded.Artist);
        Assert.Equal("Dance (Remix)", reloaded.Title);
    }

    [Fact]
    public void UpdateMetadata_RejectsEmptyTitle()
    {
        Seed("abcdefghijk", new YouTubeDownloadInfo("abcdefghijk", "Dance", "Apollo", 228, "Apollo - Dance"));

        Assert.Null(_source.UpdateMetadata("abcdefghijk", "Apollo", "  "));
    }

    [Fact]
    public void Delete_RemovesAllFilesOfTheTrack()
    {
        Seed("abcdefghijk", new YouTubeDownloadInfo("abcdefghijk", "Dance", "Apollo", 228, "Apollo - Dance"));
        File.WriteAllBytes(Path.Combine(_cache, "abcdefghijk.jpg"), [1]);
        Seed("zyxwvutsrqp", new YouTubeDownloadInfo("zyxwvutsrqp", "Otra", null, 100, "Otra"));

        _source.Delete("abcdefghijk");

        Assert.False(_source.IsCached("abcdefghijk"));
        Assert.Empty(Directory.EnumerateFiles(_cache, "abcdefghijk.*"));
        Assert.True(_source.IsCached("zyxwvutsrqp"));
    }

    [Fact]
    public void FreeSpace_DeletesLeastRecentlyUsedButKeepsProtected()
    {
        Seed("aaaaaaaaaaa", new YouTubeDownloadInfo("aaaaaaaaaaa", "Vieja", null, 100, "Vieja"));
        Seed("bbbbbbbbbbb", new YouTubeDownloadInfo("bbbbbbbbbbb", "En una lista", null, 100, "En una lista"));
        Seed("ccccccccccc", new YouTubeDownloadInfo("ccccccccccc", "Reciente", null, 100, "Reciente"));
        File.SetLastAccessTimeUtc(Path.Combine(_cache, "aaaaaaaaaaa.json"), new DateTime(2020, 1, 1));
        File.SetLastAccessTimeUtc(Path.Combine(_cache, "bbbbbbbbbbb.json"), new DateTime(2019, 1, 1));
        File.SetLastAccessTimeUtc(Path.Combine(_cache, "ccccccccccc.json"), new DateTime(2026, 1, 1));
        long sizeOfOld = Directory.EnumerateFiles(_cache, "aaaaaaaaaaa.*").Sum(f => new FileInfo(f).Length);
        long limit = _source.CacheSizeBytes() - sizeOfOld; // basta con quitar la más vieja

        var deleted = _source.FreeSpace(limit, new HashSet<string> { "bbbbbbbbbbb" });

        Assert.Equal(["aaaaaaaaaaa"], deleted);
        Assert.False(_source.IsCached("aaaaaaaaaaa"));
        Assert.True(_source.IsCached("bbbbbbbbbbb"));
        Assert.True(_source.IsCached("ccccccccccc"));
        Assert.Empty(_source.FreeSpace(long.MaxValue, new HashSet<string>()));
    }

    private void Seed(string id, YouTubeDownloadInfo info)
    {
        Directory.CreateDirectory(_cache);
        File.WriteAllBytes(Path.Combine(_cache, id + ".m4a"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(_cache, id + ".json"), JsonSerializer.Serialize(info));
    }

    /// <summary>IProgress síncrono (Progress&lt;T&gt; publica en el contexto y llegaría tarde al assert).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
