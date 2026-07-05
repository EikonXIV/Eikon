using System.Threading;

namespace Eikon;

// Plugin-wide shutdown signal. Background work (HTTP calls, blob downloads, texture loads) observes this
// token, so when the plugin unloads, in-flight operations cancel promptly instead of running to
// completion. A task still executing keeps the plugin's assembly load context alive and makes Dalamud's
// unload fail ("Failed to unload plugin"); cancelling at the very start of teardown shrinks that window.
// Registered as a singleton and cancelled by Plugin.Dispose before the services that use it are torn down.
internal sealed class AppLifetime : IDisposable
{
    private readonly CancellationTokenSource cts = new();

    // Cancelled once the plugin begins unloading.
    public CancellationToken Token => this.cts.Token;

    // Signal shutdown. Idempotent; safe to call more than once and before Dispose.
    public void Cancel()
    {
        try { this.cts.Cancel(); }
        catch (ObjectDisposedException) { /* already torn down */ }
    }

    public void Dispose()
    {
        this.Cancel();
        this.cts.Dispose();
    }
}
