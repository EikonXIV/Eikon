using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// Loads the member's blocked users from /api/blocks (Settings > Blocked users). Invalidated by
// SafetyService whenever a block or unblock happens, so the list stays in sync.
internal sealed class BlockedService
{
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly IPluginLog log;
    private bool loading;

    public BlockedService(IApiClient api, AuthService auth, IPluginLog log)
    {
        this.api = api;
        this.auth = auth;
        this.log = log;
    }

    public bool Loaded { get; private set; }

    public IReadOnlyList<User> Users { get; private set; } = new List<User>();

    public void EnsureLoaded()
    {
        if (this.Loaded || this.loading)
            return;
        this.Refresh();
    }

    public void Invalidate() => this.Loaded = false;

    public void Refresh()
    {
        this.loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                this.Users = await this.api.GetBlockedAsync(token, CancellationToken.None);
                this.Loaded = true;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Loading blocked users failed.");
            }
            finally
            {
                this.loading = false;
            }
        });
    }
}
