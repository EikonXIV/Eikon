using Eikon.Contracts;
using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

// The pure event formatting and logic helpers shared across the event screens: kind labels, duration
// wording, the relative day label, the ended cutoff, and entry-code generation over the ambiguity-free
// alphabet.
public class EventFormatTests
{
    [Theory]
    [InlineData(EventKindElement.Club, "Club night")]
    [InlineData(EventKindElement.Gathering, "Gathering")]
    [InlineData(EventKindElement.Performance, "Performance")]
    [InlineData(EventKindElement.Raid, "Raid")]
    [InlineData(EventKindElement.Roleplay, "Roleplay")]
    [InlineData(EventKindElement.Market, "Market")]
    public void KindLabel_maps_each_kind(EventKindElement kind, string expected)
    {
        Assert.Equal(expected, EventFormat.KindLabel(kind));
    }

    [Theory]
    [InlineData(15, "15m")]
    [InlineData(45, "45m")]
    [InlineData(60, "1h")]
    [InlineData(90, "1h 30m")]
    [InlineData(120, "2h")]
    [InlineData(150, "2h 30m")]
    [InlineData(720, "12h")]
    public void Duration_reads_in_hours_and_minutes(long mins, string expected)
    {
        Assert.Equal(expected, EventFormat.Duration(mins));
    }

    [Theory]
    [InlineData(0, 0, "12:00 AM")]
    [InlineData(9, 5, "9:05 AM")]
    [InlineData(12, 0, "12:00 PM")]
    [InlineData(13, 30, "1:30 PM")]
    [InlineData(20, 0, "8:00 PM")]
    [InlineData(23, 45, "11:45 PM")]
    public void Clock12_reads_in_twelve_hour_am_pm(int hour, int minute, string expected)
    {
        Assert.Equal(expected, EventFormat.Clock12(hour, minute));
    }

    [Theory]
    [InlineData("20:00", "8:00 PM")]
    [InlineData("00:15", "12:15 AM")]
    [InlineData("09:30", "9:30 AM")]
    public void Clock12_parses_a_stored_clock_string(string clock24, string expected)
    {
        Assert.Equal(expected, EventFormat.Clock12(clock24));
    }

    [Fact]
    public void Clock12_returns_unparseable_input_unchanged()
    {
        Assert.Equal("later", EventFormat.Clock12("later"));
    }

    [Fact]
    public void DayLabel_is_today_tomorrow_then_weekday()
    {
        var now = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);   // a Friday
        Assert.Equal("Today", EventFormat.DayLabel(now, now));
        Assert.Equal("Today", EventFormat.DayLabel(now.AddHours(9), now));   // later the same day
        Assert.Equal("Tomorrow", EventFormat.DayLabel(now.AddDays(1), now));
        Assert.Equal("Sunday", EventFormat.DayLabel(now.AddDays(2), now));
        Assert.Equal("Monday", EventFormat.DayLabel(now.AddDays(3), now));
    }

    [Fact]
    public void DayLabel_of_a_past_day_is_that_weekday_not_today()
    {
        var now = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);   // Friday
        Assert.Equal("Thursday", EventFormat.DayLabel(now.AddDays(-1), now));
    }

    [Fact]
    public void IsEnded_is_true_only_once_start_plus_duration_has_passed()
    {
        var now = new DateTimeOffset(2026, 8, 7, 20, 0, 0, TimeSpan.Zero);
        var start = now.AddHours(-1);   // began an hour ago

        Assert.False(EventFormat.IsEnded(start, 90, cancelled: false, now));  // 90 min run still going
        Assert.True(EventFormat.IsEnded(start, 30, cancelled: false, now));   // 30 min run finished
        Assert.False(EventFormat.IsEnded(now.AddHours(2), 60, cancelled: false, now)); // starts later
    }

    [Fact]
    public void IsEnded_is_false_for_a_cancelled_event()
    {
        var now = new DateTimeOffset(2026, 8, 7, 20, 0, 0, TimeSpan.Zero);
        var longPast = now.AddDays(-3);
        Assert.False(EventFormat.IsEnded(longPast, 60, cancelled: true, now));
    }

    [Fact]
    public void GenerateCode_is_six_chars_from_the_ambiguity_free_alphabet()
    {
        for (var i = 0; i < 256; i++)
        {
            var code = EventFormat.GenerateCode();
            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.Contains(c, EventFormat.CodeAlphabet));
        }
    }

    [Fact]
    public void Code_alphabet_excludes_the_ambiguous_glyphs()
    {
        foreach (var ambiguous in "IO01")
            Assert.DoesNotContain(ambiguous, EventFormat.CodeAlphabet);
    }
}
