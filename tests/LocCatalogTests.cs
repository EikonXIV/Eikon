using Eikon.Contracts;
using Eikon.I18n;
using Eikon.UI;
using Eikon.UI.Theme;
using Xunit;

namespace Eikon.Tests;

public sealed class LocCatalogTests
{
    private static readonly IReadOnlyDictionary<string, string> English = LocTable.Load("en")!.Strings;

    [Fact]
    public void Every_theme_has_a_name_and_tag_matching_the_catalog()
    {
        Assert.All(Themes.All, theme =>
        {
            Assert.Equal(theme.Name, English[Themes.NameKey(theme)]);
            Assert.Equal(theme.Tag, English[Themes.TagKey(theme)]);
            Assert.True(English.ContainsKey(Themes.GroupKey(theme.Group)), theme.Group);
        });
    }

    [Fact]
    public void Every_typeface_set_has_a_name_and_tag_matching_the_catalog()
    {
        Assert.All(FontSets.All, set =>
        {
            Assert.Equal(set.Name, English[$"font.{set.Id}.name"]);
            Assert.Equal(set.Tag, English[$"font.{set.Id}.tag"]);
        });
    }

    [Fact]
    public void Profile_option_labels_read_as_their_english_values()
    {
        var groups = new[]
        {
            Options.Pronouns, Options.Genders, Options.Tribes, Options.Races, Options.LookingFor, Options.Interests,
            Options.Positions, Options.Roles, Options.Sizes, Options.Meet, Options.Kinks,
        };

        Assert.All(groups, values => Assert.Equal(values, Options.Labels(values)));
        Assert.Equal(groups.Sum(g => g.Length), English.Keys.Count(k => k.StartsWith("option.", StringComparison.Ordinal)));
    }

    [Fact]
    public void Every_event_kind_and_scope_has_a_label()
    {
        Assert.All(Enum.GetNames<EventKindElement>(), kind =>
        {
            Assert.True(English.ContainsKey("eventCreate.kind." + kind.ToLowerInvariant()), kind);
            Assert.True(English.ContainsKey("eventsBoard.kind." + kind.ToLowerInvariant()), kind);
        });
        Assert.All(Enum.GetNames<EventScopeEnum>(), scope =>
            Assert.True(English.ContainsKey("tier." + scope.ToLowerInvariant()), scope));
    }
}
