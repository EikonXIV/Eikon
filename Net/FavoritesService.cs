using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// Loads the member's favorited profiles (discovery cards) from /api/favorites.
internal sealed class FavoritesService
{
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly IPluginLog log;
    private bool loading;

    public FavoritesService(IApiClient api, AuthService auth, IPluginLog log)
    {
        this.api = api;
        this.auth = auth;
        this.log = log;
    }

    public bool Loaded { get; private set; }

    public IReadOnlyList<BasicProfileDto> Profiles { get; private set; } = new List<BasicProfileDto>();

    public void EnsureLoaded()
    {
        if (this.Loaded || this.loading)
            return;
        this.Refresh();
    }

    // Mark the cached list stale (e.g. after a favorite toggles); the next EnsureLoaded refetches.
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
                this.Profiles = await this.api.GetFavoritesAsync(token, CancellationToken.None);
                this.Loaded = true;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Loading favorites failed.");
            }
            finally
            {
                this.loading = false;
            }
        });
    }
}
