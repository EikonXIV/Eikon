using System.Linq;
using Eikon.UI;
using Xunit;

namespace Eikon.Tests;

public class GlyphSubsetsTests
{
    [Fact]
    public void ToRanges_merges_neighbours_into_zero_terminated_pairs()
    {
        var ranges = GlyphSubsets.ToRanges(new[] { 'c', 'a', 'b', 'b', 'x' });

        Assert.Equal(new ushort[] { 'a', 'c', 'x', 'x', 0 }, ranges);
    }

    [Fact]
    public void ToRanges_of_nothing_is_just_the_terminator()
    {
        Assert.Equal(new ushort[] { 0 }, GlyphSubsets.ToRanges(Enumerable.Empty<char>()));
    }

    [Fact]
    public void Expand_round_trips_ToRanges()
    {
        var chars = new[] { 'ぁ', 'あ', '一', '가' };

        Assert.Equal(chars, GlyphSubsets.Expand(GlyphSubsets.ToRanges(chars)));
    }

    [Fact]
    public void Without_leaves_no_overlap_and_loses_nothing_else()
    {
        ushort[] block = { 0x4E00, 0x4E0F, 0 };
        var excluded = new[] { '一', '丅', '丏' };

        var rest = GlyphSubsets.Expand(GlyphSubsets.Without(block, excluded)).ToArray();

        Assert.Empty(rest.Intersect(excluded));
        Assert.Equal(16 - 3, rest.Length);
    }
}
