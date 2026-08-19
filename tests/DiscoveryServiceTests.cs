using System.Threading;
using Eikon.Contracts;
using Eikon.Net;
using Eikon.Tests.Fakes;
using Xunit;

namespace Eikon.Tests;

// Refresh re-pulls discovery from the top so freshly-online members surface, without losing the active
// tier/filters. Guards the grid's refresh control. The travel tests pin how the data center travel set
// rides every request (fresh fetch, load-more, preview) and survives the filter sheet.
public class DiscoveryServiceTests
{
    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DiscoveryService Make(IApiClient api, IEnumerable<int>? travel = null) =>
        new(api, new StubTokenProvider(User), new NullLog(), travel);

    [Fact]
    public async Task Refresh_reruns_the_current_query_from_the_top()
    {
        var api = new RecordingDiscoverApi();
        var svc = Make(api);

        svc.SetTier(Tier.Dc);   // establishes the query and does the first fetch
        await svc.FetchTask;
        var callsAfterFirst = api.Calls;

        svc.Refresh();
        await svc.FetchTask;

        Assert.Equal(callsAfterFirst + 1, api.Calls);   // refresh issued another fetch
        Assert.Equal(Tier.Dc, api.LastQuery?.Tier);     // active tier preserved
        Assert.Null(api.LastQuery?.Cursor);             // fresh pull, paging reset to the top
    }

    [Fact]
    public async Task Reloading_is_true_while_a_fetch_is_in_flight_and_false_once_it_settles()
    {
        var gate = new TaskCompletionSource();
        var svc = Make(new GatedDiscoverApi(gate.Task));

        svc.Refresh();
        Assert.True(svc.Reloading);   // set synchronously in Fetch, so it holds until the gated fetch returns

        gate.SetResult();
        await svc.FetchTask;

        Assert.False(svc.Reloading);
    }

    [Fact]
    public async Task SetTravel_refetches_with_the_dc_ids_sorted_and_paging_reset()
    {
        var api = new RecordingDiscoverApi();
        var svc = Make(api);
        svc.SetTier(Tier.Dc);
        await svc.FetchTask;
        var before = api.Calls;

        var changed = svc.SetTravel(new[] { 5, 1 });
        await svc.FetchTask;

        Assert.True(changed);
        Assert.Equal(before + 1, api.Calls);
        Assert.Equal(new long[] { 1, 5 }, api.LastQuery?.DcIds);
        Assert.Null(api.LastQuery?.Cursor);
        Assert.Equal(new[] { 1, 5 }, svc.TravelDcIds);
    }

    [Fact]
    public async Task SetTravel_from_the_world_tier_switches_to_dc()
    {
        var api = new RecordingDiscoverApi();
        var svc = Make(api);
        svc.EnsureInitial();
        await svc.FetchTask;

        svc.SetTravel(new[] { 3 });
        await svc.FetchTask;

        Assert.Equal(Tier.Dc, svc.Tier);
        Assert.Equal(Tier.Dc, api.LastQuery?.Tier);
        Assert.Equal(new long[] { 3 }, api.LastQuery?.DcIds);
    }

    [Fact]
    public async Task SetTravel_with_the_same_set_is_a_no_op()
    {
        var api = new RecordingDiscoverApi();
        var svc = Make(api);
        svc.SetTier(Tier.Region);
        await svc.FetchTask;
        svc.SetTravel(new[] { 2, 4 });
        await svc.FetchTask;
        var before = api.Calls;

        var changed = svc.SetTravel(new[] { 4, 2, 2 });

        Assert.False(changed);
        Assert.Equal(before, api.Calls);
    }

    [Fact]
    public async Task Apply_and_Reset_keep_the_travel_set()
    {
        var api = new RecordingDiscoverApi();
        var svc = Make(api);
        svc.SetTier(Tier.Dc);
        await svc.FetchTask;
        svc.SetTravel(new[] { 1 });
        await svc.FetchTask;

        svc.Apply(new DiscoverQuery { Tier = Tier.Dc, OnlineOnly = true, AgeMin = 21, AgeMax = 40 });
        await svc.FetchTask;
        Assert.Equal(new long[] { 1 }, api.LastQuery?.DcIds);
        Assert.True(api.LastQuery?.OnlineOnly);

        svc.Reset();
        await svc.FetchTask;
        Assert.Equal(new long[] { 1 }, api.LastQuery?.DcIds);
        Assert.Equal(new[] { 1 }, svc.TravelDcIds);
    }

    [Fact]
    public async Task LoadMore_and_Preview_carry_the_travel_set()
    {
        var api = new RecordingDiscoverApi { NextCursor = "seed~30" };
        var svc = Make(api);
        svc.SetTier(Tier.Dc);
        await svc.FetchTask;
        svc.SetTravel(new[] { 7 });
        await svc.FetchTask;

        svc.LoadMore();
        await api.WaitForCalls(3);
        Assert.Equal("seed~30", api.LastQuery?.Cursor);
        Assert.Equal(new long[] { 7 }, api.LastQuery?.DcIds);

        svc.Preview(new DiscoverQuery { Tier = Tier.Region });
        await api.WaitForCalls(4);
        Assert.Equal(new long[] { 7 }, api.LastQuery?.DcIds);
        Assert.Null(api.LastQuery?.Cursor);
    }

    [Fact]
    public async Task Travel_set_is_seeded_from_configuration()
    {
        var api = new RecordingDiscoverApi();
        var svc = Make(api, new[] { 7, 7, 3, 0, -2 });

        svc.EnsureInitial();
        await svc.FetchTask;

        Assert.Equal(new[] { 3, 7 }, svc.TravelDcIds);
        Assert.Equal(new long[] { 3, 7 }, api.LastQuery?.DcIds);
    }

    [Fact]
    public async Task Empty_travel_set_sends_no_dc_ids()
    {
        var api = new RecordingDiscoverApi();
        var svc = Make(api);

        svc.SetTier(Tier.Dc);
        await svc.FetchTask;

        Assert.Null(api.LastQuery?.DcIds);
    }

    [Fact]
    public void Normalize_dedupes_drops_non_positive_sorts_and_caps_at_16()
    {
        var input = Enumerable.Range(-3, 30).Reverse().Concat(new[] { 5, 5 });
        var result = DiscoveryService.Normalize(input);

        Assert.Equal(16, result.Count);
        Assert.Equal(Enumerable.Range(1, 16), result);
        Assert.Empty(DiscoveryService.Normalize(null));
    }

    private sealed class RecordingDiscoverApi : StubApiClient
    {
        private int calls;

        public int Calls => Volatile.Read(ref this.calls);

        public DiscoverQuery? LastQuery { get; private set; }

        public string? NextCursor { get; init; }

        public override Task<DiscoverResult> DiscoverAsync(string accessToken, DiscoverQuery query, CancellationToken ct)
        {
            this.LastQuery = query;
            Interlocked.Increment(ref this.calls);
            return Task.FromResult(new DiscoverResult { Profiles = new List<BasicProfileDto>(), NextCursor = this.NextCursor! });
        }

        // LoadMore and Preview are fire-and-forget (no FetchTask seam), so poll for the call to land.
        public async Task WaitForCalls(int count)
        {
            for (var i = 0; i < 200 && this.Calls < count; i++)
                await Task.Delay(10);
            Assert.True(this.Calls >= count, $"expected {count} discover calls, saw {this.Calls}");
        }
    }

    // Holds the fetch open until the test releases the gate, so Reloading can be observed mid-flight.
    private sealed class GatedDiscoverApi : StubApiClient
    {
        private readonly Task gate;

        public GatedDiscoverApi(Task gate) => this.gate = gate;

        public override async Task<DiscoverResult> DiscoverAsync(string accessToken, DiscoverQuery query, CancellationToken ct)
        {
            await this.gate;
            return new DiscoverResult { Profiles = new List<BasicProfileDto>(), NextCursor = null! };
        }
    }
}
