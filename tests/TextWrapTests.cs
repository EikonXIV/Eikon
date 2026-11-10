using Eikon.UI;
using Xunit;

namespace Eikon.Tests;

public sealed class TextWrapTests
{
    [Fact]
    public void Latin_text_breaks_after_spaces()
    {
        Assert.Equal(new[] { "Sign ", "in ", "with ", "Discord." }, Ui.BreakUnits("Sign in with Discord.").ToArray());
    }

    [Fact]
    public void Cjk_text_breaks_between_any_two_characters()
    {
        Assert.Equal(new[] { "設", "定", "を", "開", "く" }, Ui.BreakUnits("設定を開く").ToArray());
    }

    [Fact]
    public void Mixed_text_keeps_latin_words_whole()
    {
        var units = Ui.BreakUnits("Eikonは18+の場です").ToArray();

        Assert.Equal("Eikon", units[0]);
        Assert.Equal("は", units[1]);
        Assert.Equal("18+", units[2]);
        Assert.Equal(string.Concat(units), "Eikonは18+の場です");
    }

    [Fact]
    public void Closing_punctuation_and_small_kana_stay_with_the_character_before_them()
    {
        Assert.Equal(new[] { "本", "名、", "住", "所。" }, Ui.BreakUnits("本名、住所。").ToArray());
        Assert.Equal(new[] { "ちょっ", "と" }, Ui.BreakUnits("ちょっと").ToArray());
        Assert.Equal(new[] { "サー", "バー" }, Ui.BreakUnits("サーバー").ToArray());
    }

    [Fact]
    public void Opening_brackets_stay_with_the_character_after_them()
    {
        Assert.Equal(new[] { "設", "定", "「言", "語」" }, Ui.BreakUnits("設定「言語」").ToArray());
    }

    [Fact]
    public void Korean_breaks_at_spaces_not_between_syllables()
    {
        Assert.Equal(new[] { "지금 ", "접속 ", "중" }, Ui.BreakUnits("지금 접속 중").ToArray());
        Assert.False(Ui.NeedsCjkBreaks("지금 접속 중"));
    }

    [Fact]
    public void Only_text_with_kana_or_ideographs_takes_the_cjk_path()
    {
        Assert.False(Ui.NeedsCjkBreaks("Sign in with Discord."));
        Assert.False(Ui.NeedsCjkBreaks("Réglages œ ß ñ"));
        Assert.True(Ui.NeedsCjkBreaks("Eikonは18+の場です"));
        Assert.True(Ui.NeedsCjkBreaks("附近的冒险者"));
    }
}
