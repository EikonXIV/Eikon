using Xunit;
using System.Text.RegularExpressions;
using Eikon.I18n;

namespace Eikon.Tests;

public sealed class LocTests
{
    private static readonly LocTable Source = new(new Dictionary<string, string>
    {
        ["greeting"] = "Hello",
        ["only.source"] = "Source only",
        ["count.one"] = "{0} request",
        ["count.other"] = "{0} requests",
        ["title"] = "Nearby |adventurers",
        ["plain"] = "Settings",
        ["broken"] = "{0} of {1}",
    });

    private static Locale Find(string id) => Locales.ById(id)!;

    private static LocBook Book(string localeId, Dictionary<string, string>? active = null) =>
        new(Find(localeId), Source, active == null ? Source : new LocTable(active));

    [Theory]
    [InlineData(null, "en")]
    [InlineData("en", "en")]
    [InlineData("ko", "ko")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("es-419", "es-419")]
    [InlineData("from-a-newer-build", "en")]
    [InlineData("qps", "qps")]
    public void A_saved_language_is_used_and_anything_else_is_english(string? setting, string expected) =>
        Assert.Equal(expected, Locales.Resolve(setting).Id);

    [Fact]
    public void Seven_locales_ship_and_every_one_has_a_real_culture()
    {
        Assert.Equal(new[] { "en", "ja", "ko", "zh-Hans", "fr", "de", "es-419" }, Locales.All.Select(l => l.Id));
        Assert.All(Locales.All, l => Assert.NotEqual(string.Empty, l.Culture.Name));
    }

    [Fact]
    public void Active_table_wins_then_source_then_the_key_itself()
    {
        var book = Book("de", new Dictionary<string, string> { ["greeting"] = "Hallo" });

        Assert.Equal("Hallo", book.T("greeting"));
        Assert.Equal("Source only", book.T("only.source"));
        Assert.Equal("missing.key", book.T("missing.key"));
    }

    [Theory]
    [InlineData("en", 0, "0 requests")]
    [InlineData("en", 1, "1 request")]
    [InlineData("en", 2, "2 requests")]
    [InlineData("fr", 0, "0 request")]
    [InlineData("fr", 1, "1 request")]
    [InlineData("fr", 2, "2 requests")]
    [InlineData("ja", 1, "1 requests")]
    [InlineData("ko", 1, "1 requests")]
    [InlineData("zh-Hans", 1, "1 requests")]
    public void Plural_form_follows_the_locale_rule(string localeId, int count, string expected) =>
        Assert.Equal(expected, Book(localeId).N("count", count));

    [Fact]
    public void Numbers_format_in_the_locale_culture()
    {
        var active = new Dictionary<string, string> { ["count.other"] = "{0:N0}" };

        Assert.Equal("1,234", Book("en", new Dictionary<string, string> { ["count.other"] = "{0:N0}" }).N("count", 1234));
        Assert.Equal("1.234", Book("de", active).N("count", 1234));
    }

    [Fact]
    public void A_translation_with_a_bad_placeholder_shows_the_template_instead_of_throwing() =>
        Assert.Equal("{0} of {1}", Book("en").T("broken", 1));

    [Fact]
    public void Tones_split_on_the_break_and_tolerate_its_absence()
    {
        Assert.Equal(("Nearby ", "adventurers"), Book("en").Tones("title"));
        Assert.Equal(("Settings", string.Empty), Book("en").Tones("plain"));
    }

    [Fact]
    public void Glyphs_lists_only_the_non_latin_characters_of_the_active_table()
    {
        var book = Book("ko", new Dictionary<string, string> { ["greeting"] = "설정 Settings 설" });

        Assert.Equal(new[] { '설', '정' }, book.Glyphs().OrderBy(c => c));
    }

    [Fact]
    public void Pseudo_accents_pads_and_brackets_but_keeps_placeholders()
    {
        var pseudo = LocTable.Pseudolocalize("{0} pending");

        Assert.StartsWith("[{0} ", pseudo);
        Assert.EndsWith("]", pseudo);
        Assert.DoesNotContain("pending", pseudo);
        Assert.True(pseudo.Length > "{0} pending".Length + 2);
    }

    [Theory]
    [InlineData("h:mm tt")]
    [InlineData("MMM d, yyyy")]
    [InlineData("MMMM| yyyy")]
    [InlineData("t")]
    public void Pseudo_leaves_date_and_time_patterns_alone(string pattern)
    {
        var pseudo = LocTable.PseudoFrom(new LocTable(new Dictionary<string, string> { ["x.time"] = pattern }));

        Assert.Equal(pattern, pseudo.Strings["x.time"]);
    }

    [Fact]
    public void Pseudo_table_keeps_every_key_and_placeholder()
    {
        var pseudo = LocTable.PseudoFrom(Source);

        Assert.Equal(Source.Strings.Keys.OrderBy(k => k), pseudo.Strings.Keys.OrderBy(k => k));
        Assert.All(Source.Strings, p => Assert.Equal(Placeholders(p.Value), Placeholders(pseudo.Strings[p.Key])));
    }

    [Fact]
    public void Every_locale_ships_a_complete_table_with_matching_placeholders()
    {
        var english = LocTable.Load("en")!;
        foreach (var locale in Locales.All.Where(l => l.Id != "en"))
        {
            var table = LocTable.Load(locale.Id);
            Assert.True(table != null, $"{locale.Id}: no table");

            Assert.Empty(english.Strings.Keys.Except(table!.Strings.Keys).Select(k => $"{locale.Id}: missing {k}"));
            Assert.All(table.Strings, p =>
            {
                Assert.True(english.Strings.ContainsKey(p.Key), $"{locale.Id}: unknown key {p.Key}");
                Assert.False(string.IsNullOrWhiteSpace(p.Value), $"{locale.Id}: empty {p.Key}");
                Assert.Equal(Placeholders(english.Strings[p.Key]), Placeholders(p.Value));
            });
        }
    }

    [Fact]
    public void Two_tone_breaks_survive_only_where_the_script_has_an_italic()
    {
        var english = LocTable.Load("en")!;
        var toned = english.Strings.Where(p => p.Value.Contains(LocBook.ToneBreak)).Select(p => p.Key).ToArray();

        foreach (var locale in Locales.All.Where(l => l.Id != "en"))
        {
            var table = LocTable.Load(locale.Id)!;
            var expected = locale.Script == Script.Latin ? 1 : 0;
            Assert.All(toned, key => Assert.Equal(expected, table.Strings[key].Count(c => c == LocBook.ToneBreak)));
            Assert.All(table.Strings.Where(p => !toned.Contains(p.Key)), p => Assert.DoesNotContain(LocBook.ToneBreak, p.Value));
        }
    }

    [Fact]
    public void Date_and_time_patterns_format_in_every_locale()
    {
        var keys = new[]
        {
            "eventFormat.time", "eventFormat.day.weekday", "eventFormat.monthDay", "eventCreate.date.format",
            "chat.time", "chat.day.weekday", "chat.day.sameYear", "chat.day.otherYear", "messages.ago.date",
        };
        var sample = new DateTime(2026, 11, 10, 20, 5, 0);

        foreach (var locale in Locales.All)
        {
            var table = LocTable.Load(locale.Id)!;
            Assert.All(keys, key =>
            {
                var text = sample.ToString(table.Strings[key], locale.Culture);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{locale.Id}: {key}");
            });
        }
    }

    [Fact]
    public void Every_key_the_code_asks_for_exists_in_the_english_table()
    {
        var english = LocTable.Load("en")!.Strings;
        var keyShape = new Regex("\"([a-z][A-Za-z0-9]*(?:\\.[A-Za-z0-9]+)+)\"");
        var missing = new List<string>();

        foreach (var file in SourceFiles())
        {
            foreach (var line in File.ReadLines(file).Where(l => l.Contains("Loc.")))
            {
                foreach (Match m in keyShape.Matches(line))
                {
                    var key = m.Groups[1].Value;
                    var present = english.ContainsKey(key)
                        || (english.ContainsKey(key + ".one") && english.ContainsKey(key + ".other"));
                    if (!present)
                        missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        Assert.Empty(missing);
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Regex.Matches(text, "\\{(\\d+)").Select(m => m.Groups[1].Value).Distinct().OrderBy(v => v);

    private static IEnumerable<string> SourceFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Eikon.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        return new[] { "Screens", "UI", "Windows", "Notifications", "Content", "Net", "Services" }
            .Select(d => Path.Combine(dir!.FullName, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories));
    }
}
