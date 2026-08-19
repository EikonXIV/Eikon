using Eikon.Contracts;
using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

// The client mirrors the server's SaveProfileRequest caps so a save can't bounce with a bare 400.
// Clamp is what every bounded text field runs through; it must count what zod counts (UTF-16 units)
// and never leave a dangling surrogate that would fail to serialize.
public class LimitsTests
{
    [Fact]
    public void Clamp_leaves_short_or_unbounded_values_alone()
    {
        Assert.Equal("Boba", Limits.Clamp("Boba", 20));
        Assert.Equal("Boba", Limits.Clamp("Boba", 4));
        Assert.Equal(new string('x', 500), Limits.Clamp(new string('x', 500), 0));
        Assert.Equal(string.Empty, Limits.Clamp(string.Empty, 20));
    }

    [Fact]
    public void Clamp_truncates_to_the_cap_in_utf16_units()
    {
        var name = "Boba Fett of Malboro Crystal";
        var clamped = Limits.Clamp(name, Limits.DisplayNameMax);
        Assert.Equal(Limits.DisplayNameMax, clamped.Length);
        Assert.Equal(name[..Limits.DisplayNameMax], clamped);
    }

    [Fact]
    public void Clamp_never_splits_a_surrogate_pair()
    {
        // 19 ASCII chars then an emoji (2 UTF-16 units): a naive cut at 20 would strand the high surrogate.
        var value = new string('a', 19) + "\U0001F600";
        var clamped = Limits.Clamp(value, 20);
        Assert.Equal(19, clamped.Length);
        Assert.False(char.IsHighSurrogate(clamped[^1]));
    }

    // The generated contract enforces string caps at serialization (quicktype's MinMaxLength converters
    // throw), so a client cap that drifted from the contract would either throw here or let a too-long
    // value through to a server 400. Serialize at the cap and one past it to pin the two together.
    [Theory]
    [InlineData(nameof(SaveProfileRequest.DisplayName), Limits.DisplayNameMax)]
    [InlineData(nameof(SaveProfileRequest.Bio), Limits.BioMax)]
    public void Client_string_caps_match_the_generated_contract(string field, int max)
    {
        Assert.NotNull(Serialize(WithField(field, new string('a', max))));
        Assert.ThrowsAny<Exception>(() => Serialize(WithField(field, new string('a', max + 1))));
    }

    private static SaveProfileRequest WithField(string field, string value)
    {
        var request = new SaveProfileRequest
        {
            DisplayName = "Boba", Pronoun = PronounEnum.HeHim, Gender = GenderElement.CisMan, Age = 25,
            Races = new List<RaceElement> { RaceElement.Hrothgar }, Tribes = new List<TribeElement>(),
            LookingFor = new List<LookingForElement>(), Interests = new List<string>(), NsfwEnabled = false,
        };
        typeof(SaveProfileRequest).GetProperty(field)!.SetValue(request, value);
        return request;
    }

    private static string Serialize(SaveProfileRequest request) =>
        System.Text.Json.JsonSerializer.Serialize(request, Converter.Settings);
}
