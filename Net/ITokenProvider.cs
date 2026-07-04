using System.Threading;

namespace Eikon.Net;

// The one thing MessageCrypto needs from AuthService: the current access token. Extracting it behind an
// interface lets the crypto round-trip be driven with a stub token (a JWT whose `sub` is the test user
// id) instead of standing up the full auth state machine. AuthService implements this; DI injects it.
internal interface ITokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken ct);
}
