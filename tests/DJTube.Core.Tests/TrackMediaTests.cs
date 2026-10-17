using DJTube.Core.Models;

namespace DJTube.Core.Tests;

public class TrackMediaTests
{
    private static readonly TrackInfo Track = new("test", "1", "Pista");

    [Fact]
    public void FromFile_SetsPath()
    {
        using var media = TrackMedia.FromFile(Track, @"C:\musica\pista.mp3");

        Assert.True(media.IsFile);
        Assert.Equal(@"C:\musica\pista.mp3", media.FilePath);
        Assert.Null(media.Stream);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromFile_RejectsEmptyPath(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => TrackMedia.FromFile(Track, path));
    }

    [Fact]
    public void FromStream_RejectsNonSeekableStream()
    {
        using var stream = new NonSeekableStream();

        Assert.Throws<ArgumentException>(() => TrackMedia.FromStream(Track, stream));
    }

    [Fact]
    public void Dispose_DisposesStream()
    {
        var stream = new MemoryStream([1, 2, 3]);
        var media = TrackMedia.FromStream(Track, stream);

        media.Dispose();

        Assert.False(stream.CanRead);
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
