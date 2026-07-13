using System.Threading;
using Eikon.Contracts;
using Eikon.Net;
using Eikon.Tests.Fakes;
using Xunit;

namespace Eikon.Tests;

// A failed profile fetch must not wedge the screen on "Loading..." forever. The service retries a
// transient failure a few times and only reports Failed once the retries are spent; a terminal 4xx
// (blocked/deleted) is not retried. Regression for profiles that "load forever" after one blip.
public class ProfileDetailServiceTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly int[] NoDelay = { 0, 0, 0 };   // three instant retries, so the tests don't wait

    [Fact]
    public void A_transient_failure_that_recovers_on_retry_loads_without_error()
    {
        var api = new SequencedProfileApi(n => n == 0
            ? throw new ApiException("503", 503)
            : new ProfileDetailDto { UserId = User, DisplayName = "Aria" });
        var svc = new ProfileDetailService(api, new StubTokenProvider(User), new NullLog(), NoDelay);

        svc.Ensure(User);
        Settle(svc);

        Assert.False(svc.Failed);
        Assert.NotNull(svc.Current);
        Assert.Equal(User, svc.Current!.UserId);
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public void A_persistent_failure_reports_Failed_only_after_exhausting_retries()
    {
        var api = new SequencedProfileApi(_ => throw new ApiException("500", 500));
        var svc = new ProfileDetailService(api, new StubTokenProvider(User), new NullLog(), NoDelay);

        svc.Ensure(User);
        Settle(svc);

        Assert.True(svc.Failed);
        Assert.Null(svc.Current);
        Assert.Equal(1 + NoDelay.Length, api.Calls);   // the initial attempt plus one per backoff step
    }

    [Fact]
    public void A_terminal_4xx_fails_fast_without_retrying()
    {
        var api = new SequencedProfileApi(_ => throw new ApiException("404", 404));
        var svc = new ProfileDetailService(api, new StubTokenProvider(User), new NullLog(), NoDelay);

        svc.Ensure(User);
        Settle(svc);

        Assert.True(svc.Failed);
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public void Ensure_does_not_refetch_the_same_id_after_it_settles()
    {
        var api = new SequencedProfileApi(_ => throw new ApiException("500", 500));
        var svc = new ProfileDetailService(api, new StubTokenProvider(User), new NullLog(), NoDelay);

        svc.Ensure(User);
        Settle(svc);
        var afterFirst = api.Calls;
        svc.Ensure(User);   // same id still pinned -> no new load
        Settle(svc);

        Assert.Equal(afterFirst, api.Calls);
    }

    // Spin until the background Load task settles (Loading flips back to false). Load sets Loading true
    // synchronously in Ensure, so the wait can't observe a stale pre-call false.
    private static void Settle(ProfileDetailService svc)
        => Assert.True(SpinWait.SpinUntil(() => !svc.Loading, 2000), "profile load did not settle");

    // Answers GetProfileAsync from a per-call-index function, so a test can fail the first N attempts and
    // then succeed (or keep failing). Counts calls so the retry count is assertable.
    private sealed class SequencedProfileApi : StubApiClient
    {
        private readonly Func<int, ProfileDetailDto> respond;
        private int calls;

        public SequencedProfileApi(Func<int, ProfileDetailDto> respond) => this.respond = respond;

        public int Calls => Volatile.Read(ref this.calls);

        public override Task<ProfileDetailDto> GetProfileAsync(string accessToken, string userId, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref this.calls) - 1;
            try { return Task.FromResult(this.respond(n)); }
            catch (Exception ex) { return Task.FromException<ProfileDetailDto>(ex); }
        }
    }
}
