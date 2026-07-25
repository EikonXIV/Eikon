using Eikon.Contracts;
using Eikon.Net;
using Eikon.UI;
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

    // Options.Positions is display text; the wire value is PositionElement, matched by index. Label()
    // indexes one list by the other, so a value added to one and not the other throws or mislabels at
    // runtime. Round-tripping every label pins them together.
    [Fact]
    public void Every_position_label_round_trips_through_its_wire_value()
    {
        for (var i = 0; i < Options.Positions.Length; i++)
            Assert.Equal(Options.Positions[i], ProfileMapper.Label(ProfileMapper.Position(i)));
    }

    // And every wire value has a label, so a position added to the contract cannot ship unlabelled.
    [Fact]
    public void Every_position_wire_value_has_a_label()
    {
        foreach (var value in Enum.GetValues<PositionElement>())
            Assert.Contains(ProfileMapper.Label(value), Options.Positions);
    }

    [Fact]
    public void Side_is_offered_as_a_position()
        => Assert.Contains("Side", Options.Positions);
}
