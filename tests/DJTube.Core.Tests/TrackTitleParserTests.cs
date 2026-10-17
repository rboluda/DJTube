using DJTube.Core.Logic;

namespace DJTube.Core.Tests;

public class TrackTitleParserTests
{
    [Theory]
    [InlineData("Apollo - Dance", "Apollo", "Dance")]
    [InlineData("Ian Van Dahl - Castles In The Sky (OFFICAL VIDEO)", "Ian Van Dahl", "Castles In The Sky")]
    [InlineData("Lasgo - Something   (OFFICIAL VIDEO)", "Lasgo", "Something")]
    [InlineData("Daft Punk - One More Time (Official Video)", "Daft Punk", "One More Time")]
    [InlineData("Avicii - Levels (Official Music Video) [HD]", "Avicii", "Levels")]
    [InlineData("Rosalía – MALAMENTE (Cap.1: Augurio) (Video Oficial)", "Rosalía", "MALAMENTE (Cap.1: Augurio)")]
    [InlineData("Bad Bunny - Tití Me Preguntó (Video Oficial) | Un Verano Sin Ti", "Bad Bunny", "Tití Me Preguntó")]
    [InlineData("3 Ways - Real love (Original version) [320kbps]", "3 Ways", "Real love (Original version)")]
    [InlineData("Calvin Harris ft. Rihanna - This Is What You Came For (Lyrics)", "Calvin Harris ft. Rihanna", "This Is What You Came For")]
    [InlineData("Fred again.. — Delilah (pull me out of this) [Official Audio]", "Fred again..", "Delilah (pull me out of this)")]
    [InlineData("Eric Prydz - Pjanoo (Club Mix) [4K]", "Eric Prydz", "Pjanoo (Club Mix)")]
    [InlineData("Shakira | Bzrp Music Sessions, Vol. 53", "Shakira", "Bzrp Music Sessions, Vol. 53")]
    [InlineData("KAROL G - TQG (Lyric Video)", "KAROL G", "TQG")]
    [InlineData("Artista - Canción (Videoclip Oficial) HD", "Artista", "Canción")]
    public void Parse_SplitsArtistAndTitle(string videoTitle, string artist, string title)
    {
        var (a, t) = TrackTitleParser.Parse(videoTitle);

        Assert.Equal(artist, a);
        Assert.Equal(title, t);
    }

    [Theory]
    [InlineData("I'm The 1 You're Looking 4", "I'm The 1 You're Looking 4")]
    [InlineData("Beat JJ Juramento x Trap Type Beat x Julio Jaramillo", "Beat JJ Juramento x Trap Type Beat x Julio Jaramillo")]
    [InlineData("Song Without Artist (Official Video)", "Song Without Artist")]
    public void Parse_WithoutSeparatorKeepsTitle(string videoTitle, string title)
    {
        var (a, t) = TrackTitleParser.Parse(videoTitle);

        Assert.Null(a);
        Assert.Equal(title, t);
    }

    [Fact]
    public void Parse_UsesTopicChannelAsArtist()
    {
        var (a, t) = TrackTitleParser.Parse("Levels", "Avicii - Topic");

        Assert.Equal("Avicii", a);
        Assert.Equal("Levels", t);
    }

    [Fact]
    public void Parse_KeepsHyphenatedWords()
    {
        var (a, t) = TrackTitleParser.Parse("Jay-Z - Run-DMC Tribute");

        Assert.Equal("Jay-Z", a);
        Assert.Equal("Run-DMC Tribute", t);
    }

    [Theory]
    [InlineData(null, "Canción", "Canción")]
    [InlineData("", "Canción", "Canción")]
    [InlineData("Artista", "Canción", "Artista - Canción")]
    public void Display_JoinsArtistAndTitle(string? artist, string title, string expected)
    {
        Assert.Equal(expected, TrackTitleParser.Display(artist, title));
    }
}
