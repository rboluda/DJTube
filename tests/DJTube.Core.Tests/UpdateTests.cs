using DJTube.Core.Updates;

namespace DJTube.Core.Tests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("0.7.0", 0, 7, 0, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("2", 2, 0, 0, null)]
    [InlineData("0.7.0-beta.2", 0, 7, 0, "beta.2")]
    [InlineData("0.6.1+94b2c0a5", 0, 6, 1, null)]
    public void TryParse_ReadsVersions(string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(SemanticVersion.TryParse(text, out var v));
        Assert.Equal(new SemanticVersion(major, minor, patch, pre), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.0")]
    [InlineData("1.0.0-")]
    [InlineData("-1.0.0")]
    public void TryParse_RejectsInvalid(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("0.7.0", "0.6.1")]
    [InlineData("0.10.0", "0.9.9")]
    [InlineData("1.0.0", "0.99.99")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.10", "1.0.0-rc.9")]
    [InlineData("1.0.0-beta", "1.0.0-alpha")]
    [InlineData("1.0.0-beta.1", "1.0.0-beta")]
    public void Compare_NewerIsGreater(string newer, string older)
    {
        Assert.True(SemanticVersion.Parse(newer) > SemanticVersion.Parse(older));
        Assert.True(SemanticVersion.Parse(older) < SemanticVersion.Parse(newer));
    }

    [Fact]
    public void Compare_EqualVersions()
    {
        Assert.Equal(0, SemanticVersion.Parse("v0.7.0").CompareTo(SemanticVersion.Parse("0.7.0+abc")));
    }
}

public class UpdatePlannerTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string ReleaseJson(string tag, bool draft = false, string? installerUrl = null) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "DJTube {{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": false,
          "html_url": "https://github.com/rboluda/DJTube/releases/tag/{{tag}}",
          "body": "- Novedad",
          "assets": [
            {
              "name": "DJTube-{{tag.TrimStart('v')}}-win-x64-portable.zip",
              "browser_download_url": "https://github.com/rboluda/DJTube/releases/download/{{tag}}/DJTube-{{tag.TrimStart('v')}}-win-x64-portable.zip",
              "size": 150000000,
              "digest": "sha256:{{Sha}}"
            },
            {
              "name": "DJTube-Setup-{{tag.TrimStart('v')}}.exe",
              "browser_download_url": "{{installerUrl ?? $"https://github.com/rboluda/DJTube/releases/download/{tag}/DJTube-Setup-{tag.TrimStart('v')}.exe"}}",
              "size": 110000000,
              "digest": "sha256:{{Sha.ToUpperInvariant()}}"
            }
          ]
        }
        """;

    [Fact]
    public void ParseRelease_ReadsVersionPageNotesAndAssets()
    {
        var release = UpdatePlanner.ParseRelease(ReleaseJson("v0.7.0"));

        Assert.NotNull(release);
        Assert.Equal(SemanticVersion.Parse("0.7.0"), release.Version);
        Assert.Equal("https://github.com/rboluda/DJTube/releases/tag/v0.7.0", release.PageUrl.AbsoluteUri);
        Assert.Equal("- Novedad", release.Notes);
        Assert.Equal(2, release.Assets.Count);
        Assert.All(release.Assets, a => Assert.Equal(Sha, a.Sha256));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("no es json")]
    [InlineData("""{"tag_name": "latest", "html_url": "https://github.com/x"}""")]
    public void ParseRelease_InvalidGivesNull(string json)
    {
        Assert.Null(UpdatePlanner.ParseRelease(json));
    }

    [Fact]
    public void ParseRelease_IgnoresDrafts()
    {
        Assert.Null(UpdatePlanner.ParseRelease(ReleaseJson("v0.7.0", draft: true)));
    }

    [Fact]
    public void FindUpdate_NewerReleaseOffersInstaller()
    {
        var update = UpdatePlanner.FindUpdate(SemanticVersion.Parse("0.6.1"), UpdatePlanner.ParseRelease(ReleaseJson("v0.7.0")));

        Assert.NotNull(update);
        Assert.Equal("0.7.0", update.Version.ToString());
        Assert.NotNull(update.Installer);
        Assert.Equal("DJTube-Setup-0.7.0.exe", update.Installer.Name);
        Assert.Equal(Sha, update.Installer.Sha256);
    }

    [Theory]
    [InlineData("0.7.0")]
    [InlineData("0.8.0")]
    public void FindUpdate_SameOrOlderReleaseIsNoUpdate(string current)
    {
        Assert.Null(UpdatePlanner.FindUpdate(SemanticVersion.Parse(current), UpdatePlanner.ParseRelease(ReleaseJson("v0.7.0"))));
    }

    [Theory]
    [InlineData("https://evil.example.com/DJTube-Setup-0.7.0.exe")]
    [InlineData("http://github.com/rboluda/DJTube/releases/download/v0.7.0/DJTube-Setup-0.7.0.exe")]
    [InlineData("https://github.com/otro/DJTube/releases/download/v0.7.0/DJTube-Setup-0.7.0.exe")]
    public void FindUpdate_RejectsInstallersFromElsewhere(string url)
    {
        var update = UpdatePlanner.FindUpdate(SemanticVersion.Parse("0.6.1"), UpdatePlanner.ParseRelease(ReleaseJson("v0.7.0", installerUrl: url)));

        Assert.NotNull(update);
        Assert.Null(update.Installer);
    }

    [Theory]
    [InlineData("sha256:" + Sha, Sha)]
    [InlineData("SHA256:" + Sha, Sha)]
    [InlineData("sha1:abcdef", null)]
    [InlineData("sha256:1234", null)]
    [InlineData(null, null)]
    public void ParseSha256(string? digest, string? expected)
    {
        Assert.Equal(expected, UpdatePlanner.ParseSha256(digest));
    }
}
