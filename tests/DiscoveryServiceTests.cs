using System.Threading;
using Eikon.Contracts;
using Eikon.Net;
using Eikon.Tests.Fakes;
using Xunit;

namespace Eikon.Tests;

// Refresh re-pulls discovery from the top so freshly-online members surface, without losing the active
// tier/filters. Guards the grid's refresh control.
public class DiscoveryServiceTests
{
    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Refresh_reruns_the_current_query_from_the_top()
    {
        var api = new RecordingDiscoverApi();
        var svc = new DiscoveryService(api, new StubTokenProvider(User), new NullLog());

        svc.SetTier(Tier.Dc);   // establishes the query and does the first fetch
        await svc.FetchTask;
        var callsAfterFirst = api.Calls;

        svc.Refresh();
        await svc.FetchTask;

        Assert.Equal(callsAfterFirst + 1, api.Calls);   // refresh issued another fetch
        Assert.Equal(Tier.Dc, api.LastQuery?.Tier);     // active tier preserved
        Assert.Null(api.LastQuery?.Cursor);             // fresh pull, paging reset to the top
    }

    private sealed class RecordingDiscoverApi : StubApiClient
    {
        private int calls;

        public int Calls => Volatile.Read(ref this.calls);

        public DiscoverQuery? LastQuery { get; private set; }

        public override Task<DiscoverResult> DiscoverAsync(string accessToken, DiscoverQuery query, CancellationToken ct)
        {
            Interlocked.Increment(ref this.calls);
            this.LastQuery = query;
            return Task.FromResult(new DiscoverResult { Profiles = new List<BasicProfileDto>(), NextCursor = null! });
        }
    }
}
