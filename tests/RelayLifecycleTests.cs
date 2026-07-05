using System.Threading;
using Eikon.Net;
using Eikon.Tests.Fakes;
using Xunit;

namespace Eikon.Tests;

// The relay runs a long-lived receive/reconnect loop on a background task. If Dispose only signalled
// cancellation and returned, that task could still be executing plugin code when Dalamud unloads the
// assembly, which roots the load context and surfaces to the user as "Failed to unload plugin". Dispose
// must join the loop (within a bounded wait) so it is fully stopped on return.
public class RelayLifecycleTests
{
    // Hands back no token, so the loop parks on its retry delay and never opens a socket: the disposal
    // behavior is exercised with no network and no timing dependence.
    private sealed class NoToken : ITokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }

    // Uses the string test seam so no Configuration (which pulls in the Dalamud runtime) is referenced.
    private static RelayClient NewRelay() => new("https://test.local", new NoToken(), new NullLog());

    [Fact]
    public void Dispose_joins_the_receive_loop()
    {
        var relay = NewRelay();
        relay.Start();
        var runner = relay.Runner;
        Assert.NotNull(runner);

        relay.Dispose();

        // Joined, not merely signalled: the loop task has finished by the time Dispose returns. A
        // fire-and-forget teardown would leave it still delaying here.
        Assert.True(runner!.IsCompleted);
    }

    [Fact]
    public void Dispose_is_safe_before_start_and_when_repeated()
    {
        var relay = NewRelay();
        relay.Dispose();   // never started: no loop, no socket, must not throw
        relay.Dispose();   // second call is a no-op, not a crash
    }
}
