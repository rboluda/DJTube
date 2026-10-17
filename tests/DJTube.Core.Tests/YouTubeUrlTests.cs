using DJTube.Core.Logic;

namespace DJTube.Core.Tests;

public class YouTubeUrlTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ&feature=share")]
    [InlineData("https://www.youtube.com/watch?feature=shared&v=dQw4w9WgXcQ&t=42s")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL1234567890&index=3")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abcDEF123&t=10")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ?feature=share")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("youtu.be/dQw4w9WgXcQ")]
    [InlineData("  https://YOUTU.BE/dQw4w9WgXcQ  ")]
    public void TryGetVideoId_AcceptsVideoLinks(string url)
    {
        Assert.True(YouTubeUrl.TryGetVideoId(url, out string id));
        Assert.Equal("dQw4w9WgXcQ", id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hola")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://www.youtube.com/playlist?list=PL1234567890")]
    [InlineData("https://www.youtube.com/@canal")]
    [InlineData("https://www.youtube.com/watch?v=corto")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQextra")]
    [InlineData("https://vimeo.com/123456789")]
    [InlineData("https://www.youtube.com.evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("ftp://youtu.be/dQw4w9WgXcQ")]
    [InlineData(@"C:\musica\pista.mp3")]
    public void TryGetVideoId_RejectsOtherText(string? text)
    {
        Assert.False(YouTubeUrl.TryGetVideoId(text, out _));
    }

    [Theory]
    [InlineData("dQw4w9WgXcQ", true)]
    [InlineData("aqz-KE-bpKQ", true)]
    [InlineData("abc_DEF-123", true)]
    [InlineData("short", false)]
    [InlineData("dQw4w9WgXcQ1", false)]
    [InlineData("dQw4w9WgX!Q", false)]
    [InlineData(null, false)]
    public void IsValidVideoId_ChecksFormat(string? id, bool expected)
    {
        Assert.Equal(expected, YouTubeUrl.IsValidVideoId(id));
    }

    [Fact]
    public void ToWatchUrl_RoundTrips()
    {
        string url = YouTubeUrl.ToWatchUrl("aqz-KE-bpKQ");

        Assert.True(YouTubeUrl.TryGetVideoId(url, out string id));
        Assert.Equal("aqz-KE-bpKQ", id);
    }
}
