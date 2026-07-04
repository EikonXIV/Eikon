using Dalamud.Plugin.Services;

namespace Eikon.Net;

// A minimal logging seam so the crypto services (MessageCrypto, IdentityService) do not take a hard
// dependency on Dalamud's IPluginLog. That keeps them constructible under `dotnet test`, where the
// Dalamud runtime is not present, without widening any crypto surface. Production wraps IPluginLog.
internal interface ILog
{
    void Warning(Exception ex, string message);
}

// Production adapter: forwards to the real Dalamud logger. Registered in Plugin.cs; never used by tests.
internal sealed class PluginLogAdapter : ILog
{
    private readonly IPluginLog inner;

    public PluginLogAdapter(IPluginLog inner) => this.inner = inner;

    public void Warning(Exception ex, string message) => this.inner.Warning(ex, message);
}
