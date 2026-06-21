using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// Saves and loads the member's profile through the API, fetching a fresh access token as needed.
internal sealed class ProfileService
{
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly IPluginLog log;

    private bool loading;

    public ProfileService(IApiClient api, AuthService auth, IPluginLog log)
    {
        this.api = api;
        this.auth = auth;
        this.log = log;
    }

    public bool Loaded { get; private set; }

    public SaveProfileRequest? Mine { get; private set; }

    // Load the member's own profile once, in the background.
    public void EnsureLoaded()
    {
        if (this.Loaded || this.loading)
            return;
        this.loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                this.Mine = await this.api.GetMyProfileAsync(token, CancellationToken.None);
                this.Loaded = true;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Loading own profile failed.");
            }
            finally
            {
                this.loading = false;
            }
        });
    }

    // Fire and forget; the profile is local-authoritative in the UI, the server copy follows.
    public void Save(SaveProfileRequest request)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                await this.api.SaveProfileAsync(token, request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Saving profile failed.");
            }
        });
    }
}
