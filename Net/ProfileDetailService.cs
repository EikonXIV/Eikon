using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// Fetches a member's profile detail by id (from the current Selection) and caches the last result.
internal sealed class ProfileDetailService
{
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly IPluginLog log;
    private Guid loadedFor;

    public ProfileDetailService(IApiClient api, AuthService auth, IPluginLog log)
    {
        this.api = api;
        this.auth = auth;
        this.log = log;
    }

    public bool Loading { get; private set; }

    public ProfileDetailDto? Current { get; private set; }

    public void Ensure(Guid userId)
    {
        if (this.loadedFor == userId)
            return;
        this.loadedFor = userId;
        this.Current = null;
        this.Load(userId);
    }

    // Force the next Ensure to refetch (e.g. previewing your own profile after editing it).
    public void Invalidate() => this.loadedFor = Guid.Empty;

    private void Load(Guid userId)
    {
        this.Loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                var dto = await this.api.GetProfileAsync(token, userId.ToString(), CancellationToken.None);
                if (this.loadedFor == userId)
                    this.Current = dto;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Load profile detail failed.");
            }
            finally
            {
                this.Loading = false;
            }
        });
    }
}
