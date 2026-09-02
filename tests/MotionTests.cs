using Eikon.UI;
using Xunit;

namespace Eikon.Tests;

public class MotionTests
{
    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    public void Easings_pin_their_endpoints_and_clamp(float t, float expected)
    {
        Assert.Equal(expected, Motion.EaseOutCubic(t), 5);
        Assert.Equal(expected, Motion.EaseInOutCubic(t), 5);
        Assert.Equal(expected, Motion.Clamp01(t), 5);
    }

    [Fact]
    public void Easings_are_monotonic()
    {
        var lastOut = 0f;
        var lastInOut = 0f;
        for (var i = 1; i <= 100; i++)
        {
            var t = i / 100f;
            var o = Motion.EaseOutCubic(t);
            var io = Motion.EaseInOutCubic(t);
            Assert.True(o >= lastOut);
            Assert.True(io >= lastInOut);
            lastOut = o;
            lastInOut = io;
        }

        Assert.Equal(0.5f, Motion.EaseInOutCubic(0.5f), 5);
        Assert.True(Motion.EaseOutCubic(0.5f) > 0.5f);   // out-easing front-loads the motion
    }

    [Fact]
    public void Segment_maps_a_sub_window_to_zero_one()
    {
        Assert.Equal(0f, Motion.Segment(0.1f, 0.2f, 0.6f));
        Assert.Equal(0.5f, Motion.Segment(0.4f, 0.2f, 0.6f), 5);
        Assert.Equal(1f, Motion.Segment(0.9f, 0.2f, 0.6f));
        Assert.Equal(0f, Motion.Segment(0.4f, 0.5f, 0.5f));   // degenerate window: step at `end`
        Assert.Equal(1f, Motion.Segment(0.5f, 0.5f, 0.5f));
    }

    [Fact]
    public void Lerp_interpolates_and_clamps()
    {
        Assert.Equal(10f, Motion.Lerp(10f, 20f, -1f));
        Assert.Equal(15f, Motion.Lerp(10f, 20f, 0.5f));
        Assert.Equal(20f, Motion.Lerp(10f, 20f, 3f));
    }

    [Fact]
    public void Timeline_tracks_elapsed_progress_and_done()
    {
        var tl = new Timeline();
        Assert.False(tl.Running);
        Assert.Equal(0f, tl.Elapsed(100.0));
        Assert.False(tl.Done(100.0, 1f));

        tl.Start(100.0);
        Assert.True(tl.Running);
        Assert.Equal(0.5f, tl.Elapsed(100.5), 5);
        Assert.Equal(0.25f, tl.Progress(100.5, 2f), 5);
        Assert.False(tl.Done(101.9, 2f));
        Assert.True(tl.Done(102.0, 2f));
        Assert.Equal(1f, tl.Progress(105.0, 2f));

        tl.Stop();
        Assert.False(tl.Running);
        Assert.Equal(0f, tl.Elapsed(110.0));
    }
}
