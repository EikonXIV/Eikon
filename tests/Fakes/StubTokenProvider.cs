using System.Text;
using System.Threading;
using Eikon.Net;

namespace Eikon.Tests.Fakes;

// Returns a canned, unsigned JWT whose `sub` claim is the given user id. MessageCrypto reads only the
// payload `sub` to identify itself (it never verifies the token signature), so this is enough to drive
// the ratchet as a specific user without the real auth state machine.
internal sealed class StubTokenProvider : ITokenProvider
{
    private readonly string token;

    public StubTokenProvider(Guid userId) => this.token = MakeJwt(userId);

    public Task<string?> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult<string?>(this.token);

    private static string MakeJwt(Guid sub)
    {
        static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = B64Url(Encoding.UTF8.GetBytes($"{{\"sub\":\"{sub}\"}}"));
        return "e30." + payload + ".sig";   // header {} (e30), unsigned; only the payload is read
    }
}
