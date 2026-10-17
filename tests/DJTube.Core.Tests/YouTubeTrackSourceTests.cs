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
