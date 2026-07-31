using System.Linq;
using Eikon.UI;
using Xunit;

namespace Eikon.Tests;

// FontSets is the pure typeface catalog behind the Font picker: it resolves a saved id (or null) to a set,
// defaults safely, and names the embedded font each role loads. These guard the resolution rules and that
// every referenced font file is actually bundled with the plugin assembly.
public class FontSetsTests
{
    [Fact]
    public void Default_id_resolves_to_a_set_in_the_catalog()
    {
        Assert.Equal(FontSets.Default, FontSets.ById(FontSets.Default).Id);
        Assert.Contains(FontSets.All, s => s.Id == FontSets.Default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-set")]
    public void Unknown_or_missing_id_falls_back_to_the_default(string? id)
        => Assert.Equal(FontSets.Default, FontSets.ById(id).Id);

    [Fact]
    public void Every_known_id_round_trips()
    {
        foreach (var set in FontSets.All)
            Assert.Equal(set.Id, FontSets.ById(set.Id).Id);
    }

    [Fact]
    public void Ids_are_unique()
        => Assert.Equal(FontSets.All.Count, FontSets.All.Select(s => s.Id).Distinct().Count());

    [Fact]
    public void Every_set_names_a_face_for_each_role_and_a_positive_scale()
    {
        foreach (var set in FontSets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(set.Name));
            Assert.False(string.IsNullOrWhiteSpace(set.Tag));
            foreach (var res in new[] { set.Display, set.DisplayItalic, set.Body, set.Mono })
            {
                Assert.StartsWith("Eikon.Fonts.", res);
                Assert.EndsWith(".ttf", res);
            }

            Assert.True(set.Scale > 0f);
        }
    }

    // The catalog names embedded resources by hand; this fails loudly if a set references a font the csproj
    // does not actually bundle (a typo, or a face added to the catalog but never embedded).
    [Fact]
    public void Every_referenced_font_is_embedded_in_the_assembly()
    {
        var embedded = typeof(FontSet).Assembly.GetManifestResourceNames().ToHashSet();
        var referenced = FontSets.All
            .SelectMany(s => new[] { s.Display, s.DisplayItalic, s.Body, s.Mono })
            .Distinct();

        foreach (var res in referenced)
            Assert.Contains(res, embedded);
    }
}
