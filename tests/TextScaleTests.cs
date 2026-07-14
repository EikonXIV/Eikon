using Eikon.UI;
using Xunit;

namespace Eikon.Tests;

// TextScale maps the member's Text size percent to a UI zoom factor, clamps out-of-range persisted
// values, and snaps a percent to the nearest slider step. Pure logic behind the Appearance slider.
public class TextScaleTests
{
    [Theory]
    [InlineData(100, 100)]
    [InlineData(85, 85)]
    [InlineData(150, 150)]
    [InlineData(50, 85)]     // below range -> Min
    [InlineData(300, 150)]   // above range -> Max
    public void Clamp_keeps_the_percent_within_range(int input, int expected)
        => Assert.Equal(expected, TextScale.Clamp(input));

    [Theory]
    [InlineData(100, 1.0f)]
    [InlineData(130, 1.3f)]
    [InlineData(85, 0.85f)]
    public void ToFactor_maps_percent_to_a_scale(int percent, float expected)
        => Assert.Equal(expected, TextScale.ToFactor(percent), 3);

    [Fact]
    public void ToFactor_clamps_before_mapping()
        => Assert.Equal(TextScale.Max / 100f, TextScale.ToFactor(999), 3);

    [Theory]
    [InlineData(100, 2)]   // exact step
    [InlineData(102, 2)]   // nearest to 100
    [InlineData(110, 3)]   // nearest to 115
    [InlineData(40, 0)]    // below all -> first step
    [InlineData(999, 5)]   // above all -> last step
    public void NearestStepIndex_snaps_to_the_closest_step(int percent, int expectedIndex)
        => Assert.Equal(expectedIndex, TextScale.NearestStepIndex(percent));

    [Fact]
    public void Steps_are_ascending_and_bracket_the_default()
    {
        for (var i = 1; i < TextScale.Steps.Length; i++)
            Assert.True(TextScale.Steps[i] > TextScale.Steps[i - 1]);
        Assert.Contains(TextScale.Default, TextScale.Steps);
    }
}
