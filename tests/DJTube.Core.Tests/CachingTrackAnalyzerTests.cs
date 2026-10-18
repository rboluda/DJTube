using DJTube.Core.Abstractions;
using DJTube.Core.Analysis;
using DJTube.Core.Models;

namespace DJTube.Core.Tests;

public sealed class CachingTrackAnalyzerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DJTubeAnalysisTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _track;
    private readonly FakeAnalyzer _inner = new();
    private readonly CachingTrackAnalyzer _cache;

    public CachingTrackAnalyzerTests()
    {
        Directory.CreateDirectory(_folder);
        _track = Path.Combine(_folder, "pista.m4a");
        File.WriteAllBytes(_track, [1, 2, 3, 4]);
        _cache = new CachingTrackAnalyzer(_inner, Path.Combine(_folder, "cache"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task SecondAnalysisComesFromCache()
    {
        var first = await _cache.AnalyzeAsync(_track);
        var second = await _cache.AnalyzeAsync(_track);

        Assert.Equal(1, _inner.Calls);
        Assert.Equal(first.Grid, second.Grid);
        Assert.Equal(first.Waveform.Peak.ToArray(), second.Waveform.Peak.ToArray());
        Assert.Equal(first.Waveform.High.ToArray(), second.Waveform.High.ToArray());
        Assert.Equal(first.Waveform.BinsPerSecond, second.Waveform.BinsPerSecond);
    }

    [Fact]
    public async Task ChangedFileIsAnalysedAgain()
    {
        await _cache.AnalyzeAsync(_track);
        File.WriteAllBytes(_track, [9, 9, 9, 9, 9]);

        await _cache.AnalyzeAsync(_track);

        Assert.Equal(2, _inner.Calls);
    }

    [Fact]
    public async Task SavedGridIsUsedNextTimeAndListed()
    {
        await _cache.AnalyzeAsync(_track);
        _cache.SaveGrid(_track, new BeatGrid(64, 0.1));

        var again = await _cache.AnalyzeAsync(_track);

        Assert.Equal(64, again.Grid!.Bpm);
        Assert.Equal(64, _cache.TryGetCachedGrid(_track)!.Bpm);
        Assert.Equal(1, _inner.Calls);
    }

    [Fact]
    public async Task OldAnalysisWithoutBpmGetsTheNewOne()
    {
        _inner.Grid = null;
        await _cache.AnalyzeAsync(_track);
        MarkCacheAsVersion(2);
        _inner.Grid = new BeatGrid(130, 0.1);

        Assert.True(_cache.NeedsAnalysis(_track));
        var again = await _cache.AnalyzeAsync(_track);

        Assert.Equal(130, again.Grid!.Bpm);
        Assert.Equal(130, _cache.TryGetCachedGrid(_track)!.Bpm);
        Assert.False(_cache.NeedsAnalysis(_track));
    }

    [Fact]
    public async Task OldAnalysisKeepsAnOctaveCorrection()
    {
        await _cache.AnalyzeAsync(_track);
        _cache.SaveGrid(_track, new BeatGrid(64, 0.1));
        MarkCacheAsVersion(2);

        var again = await _cache.AnalyzeAsync(_track);

        Assert.Equal(2, _inner.Calls);
        Assert.Equal(64, again.Grid!.Bpm);
    }

    [Fact]
    public async Task OldAnalysisWithAnotherTempoIsReplaced()
    {
        _inner.Grid = new BeatGrid(116, 0.1);
        await _cache.AnalyzeAsync(_track);
        MarkCacheAsVersion(2);
        _inner.Grid = new BeatGrid(87, 0.2);

        var again = await _cache.AnalyzeAsync(_track);

        Assert.Equal(87, again.Grid!.Bpm);
    }

    [Fact]
    public async Task ForgetRemovesTheCache()
    {
        await _cache.AnalyzeAsync(_track);
        _cache.Forget(_track);

        Assert.Null(_cache.TryGetCachedGrid(_track));
        await _cache.AnalyzeAsync(_track);
        Assert.Equal(2, _inner.Calls);
    }

    [Fact]
    public void Read_RejectsOtherFiles()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Null(CachingTrackAnalyzer.Read(stream));
    }

    [Fact]
    public void WriteRead_RoundTripWithoutGrid()
    {
        var analysis = new TrackAnalysis(new WaveformData(200, [0.5f], [0.1f], [0.2f], [0.3f]), null);
        using var stream = new MemoryStream();
        CachingTrackAnalyzer.Write(stream, analysis);
        stream.Position = 0;

        var read = CachingTrackAnalyzer.Read(stream);

        Assert.NotNull(read);
        Assert.Null(read.Grid);
        Assert.Equal(0.3f, read.Waveform.High[0]);
    }

    /// <summary>Simula un análisis guardado por una versión anterior (el número va tras la marca "DJTA").</summary>
    private void MarkCacheAsVersion(int version)
    {
        string file = Assert.Single(Directory.GetFiles(_cache.CacheFolder, "*.djta"));
        using var stream = File.OpenWrite(file);
        stream.Position = 4;
        stream.Write(BitConverter.GetBytes(version));
    }

    private sealed class FakeAnalyzer : ITrackAnalyzer
    {
        public int Calls { get; private set; }

        public BeatGrid? Grid { get; set; } = new(128, 0.25);

        public Task<TrackAnalysis> AnalyzeAsync(string filePath, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            Calls++;
            var wave = new WaveformData(200, [0.1f, 0.9f], [0.1f, 0.5f], [0.0f, 0.3f], [0.05f, 0.2f]);
            return Task.FromResult(new TrackAnalysis(wave, Grid));
        }
    }
}
