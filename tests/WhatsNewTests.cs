using Eikon.Content;
using Xunit;

namespace Eikon.Tests;

// The pure logic behind the What's new screen: the bundled changelog stays newest-first, the "since the
// member last looked" filter is exclusive of the seen version, and stored version strings normalize to a
// comparable Major.Minor.Build.
public class WhatsNewTests
{
    [Fact]
    public void ReleaseNotes_are_strictly_newest_first()
    {
        var all = ReleaseNotes.All;
        for (var i = 1; i < all.Count; i++)
            Assert.True(all[i - 1].Version > all[i].Version, "Releases must be strictly newest-first.");
    }

    [Fact]
    public void Since_null_returns_the_full_history()
    {
        Assert.Equal(ReleaseNotes.All.Count, ReleaseNotes.Since(null).Count());
    }

    [Fact]
    public void Since_is_exclusive_of_the_seen_version()
    {
        var newest = ReleaseNotes.All[0].Version;
        Assert.Empty(ReleaseNotes.Since(newest));
        Assert.Empty(ReleaseNotes.Since(new Version(99, 0, 0)));
    }

    [Fact]
    public void Since_an_old_version_returns_only_newer_releases()
    {
        var floor = new Version(0, 0, 1);
        var result = ReleaseNotes.Since(floor).ToList();
        Assert.NotEmpty(result);
        Assert.All(result, r => Assert.True(r.Version > floor));
    }

    [Theory]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("2.0", 2, 0, 0)]
    [InlineData("3.4.5", 3, 4, 5)]
    public void Parse_normalizes_to_major_minor_build(string input, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build), PluginVersion.Parse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void Parse_treats_bad_input_as_the_oldest_version(string? input)
    {
        Assert.Equal(new Version(0, 0, 0), PluginVersion.Parse(input));
    }
}
