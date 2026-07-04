using Eikon.Contracts;
using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

// ProfileMapper maps UI option indices to wire enums and back. These guard the index/flag round-trips
// and the out-of-range fallback (an unexpected index must not throw).
public class ProfileMapperTests
{
    [Fact]
    public void Pronoun_maps_index_to_the_wire_value()
        => Assert.Equal(PronounEnum.HeHim, ProfileMapper.Pronoun(0));

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void Pronoun_out_of_range_falls_back_to_the_first_option(int i)
        => Assert.Equal(ProfileMapper.Pronoun(0), ProfileMapper.Pronoun(i));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void IndexOfPronoun_inverts_Pronoun(int i)
        => Assert.Equal(i, ProfileMapper.IndexOfPronoun(ProfileMapper.Pronoun(i)));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void IndexOfPosition_inverts_Position(int i)
        => Assert.Equal(i, ProfileMapper.IndexOfPosition(ProfileMapper.Position(i)));

    [Fact]
    public void TribesOf_and_FromTribes_roundtrip_a_selection()
    {
        var flags = new bool[17];
        flags[0] = true;    // Twink
        flags[7] = true;    // Bear
        flags[16] = true;   // Discreet
        var tribes = ProfileMapper.TribesOf(flags);
        Assert.Equal(3, tribes.Count);
        Assert.Equal(flags, ProfileMapper.FromTribes(tribes));
    }

    [Fact]
    public void Selected_tolerates_a_flags_array_shorter_than_the_map()
    {
        var tribes = ProfileMapper.TribesOf(new[] { true, false });
        Assert.Single(tribes);
        Assert.Equal(TribeElement.Twink, tribes[0]);
    }
}
