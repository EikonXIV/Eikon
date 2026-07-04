using Eikon.Config;
using Xunit;

namespace Eikon.Tests;

// The release-only Version 1->2 migration resets a persisted loopback dev URL to production, while
// leaving a self-hoster's custom URL alone. ServerUrl.ResetLoopbackIfNeeded holds that pure logic.
public class ConfigMigrationTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://[::1]:8080")]
    public void ResetLoopbackIfNeeded_moves_loopback_to_production(string url)
        => Assert.Equal("https://api.eikon.chat", ServerUrl.ResetLoopbackIfNeeded(url));

    [Theory]
    [InlineData("https://api.eikon.chat")]
    [InlineData("https://selfhosted.example.com")]
    [InlineData("not-a-url")]
    public void ResetLoopbackIfNeeded_leaves_non_loopback_untouched(string url)
        => Assert.Equal(url, ServerUrl.ResetLoopbackIfNeeded(url));
}
