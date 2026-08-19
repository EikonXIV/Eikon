using Eikon.UI;
using Xunit;

namespace Eikon.Tests;

// The crossing must hold at least MinCross, wait for the fetch, never hang past MaxCross, and honour a
// skip click. Times are seconds on ImGui's clock.
public class TravelTransitionTests
{
    private static TravelTransition Begun(double at = 10.0)
    {
        var t = new TravelTransition();
        t.Begin(at, "MATERIA · AETHER");
        return t;
    }

    [Fact]
    public void Begin_enters_crossing_and_blocks_input()
    {
        var t = Begun();
        Assert.Equal(TravelTransition.Phase.Crossing, t.Current);
        Assert.True(t.BlocksInput);
        Assert.Equal("MATERIA · AETHER", t.Caption);
    }

    [Fact]
    public void Does_not_arrive_before_the_minimum_even_when_the_fetch_is_done()
    {
        var t = Begun();
        t.Update(10.0 + TravelTransition.MinCross - 0.05, loading: false);
        Assert.Equal(TravelTransition.Phase.Crossing, t.Current);
        t.Update(10.0 + TravelTransition.MinCross, loading: false);
        Assert.Equal(TravelTransition.Phase.Arriving, t.Current);
    }

    [Fact]
    public void A_slow_fetch_extends_the_crossing_until_it_lands()
    {
        var t = Begun();
        t.Update(12.5, loading: true);
        Assert.Equal(TravelTransition.Phase.Crossing, t.Current);
        t.Update(12.6, loading: false);
        Assert.Equal(TravelTransition.Phase.Arriving, t.Current);
    }

    [Fact]
    public void MaxCross_ends_the_crossing_even_if_still_loading()
    {
        var t = Begun();
        t.Update(10.0 + TravelTransition.MaxCross, loading: true);
        Assert.Equal(TravelTransition.Phase.Arriving, t.Current);
    }

    [Fact]
    public void Skip_when_not_loading_arrives_at_once()
    {
        var t = Begun();
        t.Skip();
        t.Update(10.2, loading: false);
        Assert.Equal(TravelTransition.Phase.Arriving, t.Current);
    }

    [Fact]
    public void Skip_while_loading_arrives_the_moment_loading_clears()
    {
        var t = Begun();
        t.Skip();
        t.Update(10.2, loading: true);
        Assert.Equal(TravelTransition.Phase.Crossing, t.Current);
        t.Update(10.3, loading: false);
        Assert.Equal(TravelTransition.Phase.Arriving, t.Current);
        Assert.False(t.BlocksInput);
    }

    [Fact]
    public void Arriving_returns_to_idle_after_its_duration()
    {
        var t = Begun();
        t.Update(11.5, loading: false);
        Assert.Equal(TravelTransition.Phase.Arriving, t.Current);
        Assert.Equal(0f, t.ArriveT(11.5), 5);
        t.Update(11.5 + (TravelTransition.ArriveDuration * 0.5), loading: false);
        Assert.Equal(0.5f, t.ArriveT(11.5 + (TravelTransition.ArriveDuration * 0.5)), 3);
        t.Update(11.5 + TravelTransition.ArriveDuration, loading: false);
        Assert.Equal(TravelTransition.Phase.Idle, t.Current);
        Assert.Equal(0f, t.CrossElapsed(20.0));
    }

    [Fact]
    public void Tiles_stagger_in_from_the_first_index_and_are_opaque_when_idle()
    {
        var idle = new TravelTransition();
        Assert.Equal(1f, idle.TileAlpha(0, 5.0));
        Assert.Equal(1f, idle.TileAlpha(9, 5.0));

        var t = Begun();
        t.Update(11.5, loading: false);
        var mid = 11.5 + (TravelTransition.ArriveDuration * 0.35);
        t.Update(mid, loading: false);
        Assert.Equal(1f, t.TileAlpha(0, 11.5 + TravelTransition.ArriveDuration), 5);
        Assert.True(t.TileAlpha(0, mid) > t.TileAlpha(5, mid));
        Assert.True(t.TileAlpha(5, mid) >= t.TileAlpha(30, mid));   // the stagger caps, later tiles share a start
        Assert.True(t.TileAlpha(0, 11.5) < 0.01f);
    }
}
