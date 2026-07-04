using Eikon.Net;

namespace Eikon.Tests.Fakes;

// No-op ILog. The crypto services only log on exceptional paths; the round-trip tests assert observable
// behavior (decrypt results, session state), not log output, so swallowing is fine.
internal sealed class NullLog : ILog
{
    public void Warning(Exception ex, string message) { }
}
