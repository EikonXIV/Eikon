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

    public bool Saving { get; private set; }

    // Set when the most recent save was rejected or never reached the server; cleared by the next
    // successful save. Screens surface it instead of letting an edit silently evaporate.
    public bool SaveFailed { get; private set; }

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

    // Fire and forget; the profile is local-authoritative in the UI, the server copy follows. The
    // outcome lands in SaveFailed so the caller's next frame can show it.
    public void Save(SaveProfileRequest request) => _ = this.SaveAsync(request, CancellationToken.None);

    // Awaitable save for flows that must not proceed without a server-side profile (onboarding).
    public async Task<bool> SaveAsync(SaveProfileRequest request, CancellationToken ct)
    {
        this.Saving = true;
        try
        {
            var token = await this.auth.GetAccessTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                this.SaveFailed = true;
                return false;
            }

            await this.api.SaveProfileAsync(token, request, ct);
            this.Mine = request;
            this.Loaded = true;
            this.SaveFailed = false;
            return true;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Saving profile failed.");
            this.SaveFailed = true;
            return false;
        }
        finally
        {
            this.Saving = false;
        }
    }
}
