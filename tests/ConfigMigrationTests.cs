using System.Linq;
using Eikon.Config;
using Eikon.UI.Theme;
using Xunit;

namespace Eikon.Tests;

// The release-only Version 1->2 migration resets a persisted loopback dev URL to production, while
// leaving a self-hoster's custom URL alone. ServerUrl.ResetLoopbackIfNeeded holds that pure logic.
// The Version 2->3 migration carries a pre-catalog theme choice onto a catalog id; ThemeMigration
// holds that one, and every id it can produce has to exist in the catalog.
public class ConfigMigrationTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://[::1]:8080")]
    public void ResetLoopbackIfNeeded_moves_loopback_to_production(string url)
        => Assert.Equal("https://api.eikon.chat", ServerUrl.ResetLoopbackIfNeeded(url));

    [Theory]
    [InlineData("https://api.eikon.chat")]
    [InlineData("https://selfhosted.example.com")]
    [InlineData("not-a-url")]
    public void ResetLoopbackIfNeeded_leaves_non_loopback_untouched(string url)
        => Assert.Equal(url, ServerUrl.ResetLoopbackIfNeeded(url));

    // The two flag ids that were shortened in the old build have to land on their catalog names, or the
    // member is dropped onto the default with no way to tell their theme went missing.
    [Theory]
    [InlineData("bi", "bisexual")]
    [InlineData("nb", "nonbinary")]
    public void Resolve_renames_the_shortened_flag_ids(string saved, string expected)
        => Assert.Equal(expected, ThemeMigration.Resolve(saved, 0));

    [Theory]
    [InlineData("pride")]
    [InlineData("mlm")]
    [InlineData("trans")]
    [InlineData("ace")]
    public void Resolve_keeps_the_flag_ids_that_did_not_change(string saved)
        => Assert.Equal(saved, ThemeMigration.Resolve(saved, 0));

    // No saved flag means the old build was on a solid accent, so the index decides. Index 0 was the
    // old default and cannot be told from an untouched install, so it takes the current default.
    [Theory]
    [InlineData(0, "editorial-dark")]
    [InlineData(1, "sky")]
    [InlineData(3, "violet")]
    [InlineData(5, "pink")]
    [InlineData(11, "lime")]
    public void Resolve_maps_a_solid_accent_index_onto_a_catalog_theme(int index, string expected)
        => Assert.Equal(expected, ThemeMigration.Resolve(null, index));

    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    [InlineData(9999)]
    public void Resolve_falls_back_when_the_accent_index_is_out_of_range(int index)
        => Assert.Equal(ThemeMigration.DefaultTheme, ThemeMigration.Resolve(null, index));

    // Every id the migration can hand back must resolve in the catalog, otherwise ById quietly falls
    // back to the default and the carry-over is a no-op.
    [Fact]
    public void Resolve_only_produces_ids_that_exist_in_the_catalog()
    {
        var produced = new List<string> { ThemeMigration.Resolve("bi", 0), ThemeMigration.Resolve("nb", 0) };
        for (var i = 0; i < 12; i++)
            produced.Add(ThemeMigration.Resolve(null, i));

        foreach (var id in produced)
            Assert.Equal(id, Themes.ById(id).Id);
    }
}
