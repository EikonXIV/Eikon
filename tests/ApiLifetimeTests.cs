using System.Net.Http;
using System.Threading;
using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

// The plugin-lifetime token is linked into every ApiClient request, so when the plugin unloads an
// in-flight call is cancelled at once instead of running out its 30s timeout while keeping the assembly's
// load context alive. This pins that wiring end to end.
public class ApiLifetimeTests
{
    // Never completes on its own; unblocks (as a cancellation) only when the request's token fires.
    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();   // unreachable
        }
    }

    [Fact]
    public async Task Lifetime_cancellation_aborts_an_in_flight_request()
    {
        var lifetime = new AppLifetime();
        var http = new HttpClient(new BlockingHandler()) { BaseAddress = new Uri("https://test.local") };
        var api = new ApiClient(http, lifetime);

        // A request with no caller-side cancellation of its own: only the lifetime can stop it.
        var inFlight = api.GetWorldsAsync(CancellationToken.None);
        Assert.False(inFlight.IsCompleted);

        lifetime.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inFlight);
    }
}
