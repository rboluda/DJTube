using DJTube.Core.Models;
using DJTube.Sources.Local;

namespace DJTube.Core.Tests;

public sealed class LocalFileTrackSourceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DJTubeTests_" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileTrackSource _source = new();

    public LocalFileTrackSourceTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("pista.mp3", true)]
    [InlineData("PISTA.MP3", true)]
    [InlineData("pista.flac", true)]
    [InlineData("pista.wav", true)]
    [InlineData("pista.aac", true)]
    [InlineData("pista.m4a", true)]
    [InlineData("pista.ogg", false)]
    [InlineData("pista.txt", false)]
    [InlineData("pista", false)]
    [InlineData("", false)]
    public void IsSupported_ChecksExtension(string path, bool expected)
    {
        Assert.Equal(expected, LocalFileTrackSource.IsSupported(path));
    }

    [Fact]
    public void CreateTrackInfo_UsesFullPathAsIdAndFileNameAsTitle()
    {
        string file = Path.Combine(_folder, "Mi canción.mp3");

        TrackInfo info = LocalFileTrackSource.CreateTrackInfo(file);

        Assert.Equal(LocalFileTrackSource.SourceName, info.SourceName);
        Assert.Equal(Path.GetFullPath(file), info.Id);
        Assert.Equal("Mi canción", info.Title);
    }

    [Fact]
    public async Task BrowseAsync_ReturnsOnlySupportedFilesSortedByName()
    {
        Touch("b.flac");
        Touch("a.mp3");
        Touch("notas.txt");
        Touch("C.wav");

        var titles = new List<string>();
        await foreach (var track in _source.BrowseAsync(_folder))
            titles.Add(track.Title);

        Assert.Equal(["a", "b", "C"], titles);
    }

    [Fact]
    public async Task OpenAsync_ReturnsFileMedia()
    {
        string file = Touch("pista.mp3");
        var track = LocalFileTrackSource.CreateTrackInfo(file);

        using var media = await _source.OpenAsync(track);

        Assert.True(media.IsFile);
        Assert.Equal(Path.GetFullPath(file), media.FilePath);
    }

    [Fact]
    public async Task OpenAsync_MissingFileThrows()
    {
        var track = LocalFileTrackSource.CreateTrackInfo(Path.Combine(_folder, "no-existe.mp3"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => _source.OpenAsync(track));
    }

    [Fact]
    public async Task OpenAsync_RejectsTracksFromOtherSources()
    {
        var track = new TrackInfo("Otra fuente", Touch("pista.mp3"), "pista");

        await Assert.ThrowsAsync<ArgumentException>(() => _source.OpenAsync(track));
    }

    private string Touch(string name)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, []);
        return path;
    }
}
