using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Eikon.UI;
using Xunit;

namespace Eikon.Tests;

// Member text bakes only the characters that have been on screen. These guard the two properties that
// keep that from costing unbounded memory: the tracker has a hard ceiling, and the gate that turns newly
// seen characters into atlas rebuilds can never loop.
public class GlyphBakingTests
{
    [Fact]
    public void Tracker_sorts_characters_by_script_and_ignores_the_rest()
    {
        var tracker = new GlyphTracker();

        tracker.Observe("Hello 한글 简体 かな ★ é");

        Assert.Equal(new[] { '한', '글' }, tracker.Hangul());
        Assert.Equal(new[] { '简', '体', 'か', 'な' }, tracker.Cjk());
    }

    [Fact]
    public void Tracker_counts_each_character_once()
    {
        var tracker = new GlyphTracker();

        tracker.Observe("한한한");
        tracker.Observe("한");
        tracker.Observe(null);
        tracker.Observe(string.Empty);

        Assert.Equal(1, tracker.HangulCount);
    }

    [Fact]
    public void Tracker_stops_growing_at_the_cap()
    {
        var tracker = new GlyphTracker();
        var everyHanzi = new string(Enumerable.Range(0x4E00, 0x9FFF - 0x4E00 + 1).Select(c => (char)c).ToArray());
        var everySyllable = new string(Enumerable.Range(0xAC00, 0xD7A3 - 0xAC00 + 1).Select(c => (char)c).ToArray());

        for (var pass = 0; pass < 3; pass++)
        {
            tracker.Observe(everyHanzi);
            tracker.Observe(everySyllable);
        }

        Assert.Equal(GlyphTracker.Cap, tracker.CjkCount);
        Assert.Equal(GlyphTracker.Cap, tracker.HangulCount);
        Assert.Equal(GlyphTracker.Cap, tracker.Cjk().Length);
        Assert.Equal(GlyphTracker.Cap, tracker.Hangul().Length);
    }

    [Fact]
    public void Gate_never_builds_when_nothing_new_was_seen()
    {
        var gate = new RebuildGate();

        for (long now = 0; now < 600_000; now += 16)
            Assert.False(gate.ShouldBuild(0, now));
    }

    [Fact]
    public void Gate_waits_for_the_demand_to_settle_then_builds_once()
    {
        var gate = new RebuildGate();

        Assert.False(gate.ShouldBuild(5, 10_000));
        Assert.False(gate.ShouldBuild(5, 10_000 + RebuildGate.SettleMs - 1));
        Assert.True(gate.ShouldBuild(5, 10_000 + RebuildGate.SettleMs));
        Assert.True(gate.InFlight);
    }

    [Fact]
    public void Gate_builds_after_the_maximum_wait_even_if_demand_keeps_growing()
    {
        var gate = new RebuildGate();
        var builds = 0;
        var demand = 0;

        for (long now = 10_000; now <= 10_000 + RebuildGate.MaxWaitMs; now += 100)
        {
            if (gate.ShouldBuild(++demand, now))
                builds++;
        }

        Assert.Equal(1, builds);
    }

    [Fact]
    public void Gate_runs_one_build_at_a_time()
    {
        var gate = new RebuildGate();
        StartBuild(gate, 1, 10_000);

        var demand = 1;
        for (long now = 10_000; now < 600_000; now += 16)
            Assert.False(gate.ShouldBuild(++demand, now));
    }

    [Fact]
    public void Gate_does_not_rebuild_the_same_demand_after_a_build_finishes()
    {
        var gate = new RebuildGate();
        StartBuild(gate, 1, 10_000);
        gate.Completed(failed: false, 10_500);

        for (long now = 10_500; now < 600_000; now += 16)
            Assert.False(gate.ShouldBuild(1, now));
    }

    [Fact]
    public void Gate_keeps_a_minimum_gap_between_builds()
    {
        var gate = new RebuildGate();
        StartBuild(gate, 1, 10_000);
        gate.Completed(failed: false, 10_100);

        Assert.False(gate.ShouldBuild(2, 10_200));
        Assert.False(gate.ShouldBuild(2, 10_000 + RebuildGate.MinIntervalMs - 1));
        Assert.True(gate.ShouldBuild(2, 10_000 + RebuildGate.MinIntervalMs));
    }

    [Fact]
    public void Gate_does_not_retry_a_failed_build()
    {
        var gate = new RebuildGate();
        StartBuild(gate, 1, 10_000);
        gate.Completed(failed: true, 10_100);

        for (long now = 10_100; now < 3_600_000; now += 16)
            Assert.False(gate.ShouldBuild(1, now));
    }

    [Fact]
    public void Gate_backs_off_after_a_failure_even_when_more_arrives()
    {
        var gate = new RebuildGate();
        StartBuild(gate, 1, 10_000);
        gate.Completed(failed: true, 10_100);

        Assert.False(gate.ShouldBuild(2, 10_200));
        Assert.False(gate.ShouldBuild(2, 10_100 + RebuildGate.FailureBackoffMs - 1));
        Assert.True(gate.ShouldBuild(2, 10_100 + RebuildGate.FailureBackoffMs));
    }

    [Fact]
    public void Gate_caps_builds_per_minute_under_a_constant_stream_of_new_characters()
    {
        var gate = new RebuildGate();
        var builds = 0;
        var demand = 0;

        for (long now = 10_000; now < 70_000; now += 16)
        {
            if (gate.InFlight)
                gate.Completed(failed: false, now);
            if (gate.ShouldBuild(++demand, now))
                builds++;
        }

        Assert.InRange(builds, 1, (int)(60_000 / RebuildGate.MinIntervalMs) + 1);
    }

    [Fact]
    public void Gate_treats_demand_baked_by_another_rebuild_as_covered()
    {
        var gate = new RebuildGate();
        gate.Cover(7);

        for (long now = 10_000; now < 20_000; now += 16)
            Assert.False(gate.ShouldBuild(7, now));
    }

    [Fact]
    public void Screens_draw_text_through_Ui_so_the_tracker_sees_it()
    {
        var direct = new Regex(@"ImGui\.(Text|TextUnformatted|TextWrapped|TextColored|TextDisabled|BulletText|LabelText)\(");
        var offenders = SourceFiles()
            .Where(f => Path.GetFileName(f) != "Ui.cs")
            .Where(f => direct.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static void StartBuild(RebuildGate gate, int demand, long at)
    {
        Assert.False(gate.ShouldBuild(demand, at - RebuildGate.SettleMs));
        Assert.True(gate.ShouldBuild(demand, at));
    }

    private static string[] SourceFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Eikon.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        return new[] { "Screens", "UI", "Windows", "Notifications", "Content", "Services" }
            .Select(d => Path.Combine(dir!.FullName, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .ToArray();
    }
}
