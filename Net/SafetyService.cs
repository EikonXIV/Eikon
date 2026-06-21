using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// Block / report / favorite, fire-and-forget with a fresh access token. Enforcement is server-side.
internal sealed class SafetyService
{
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly FavoritesService favorites;
    private readonly BlockedService blocked;
    private readonly IPluginLog log;

    public SafetyService(IApiClient api, AuthService auth, FavoritesService favorites, BlockedService blocked, IPluginLog log)
    {
        this.api = api;
        this.auth = auth;
        this.favorites = favorites;
        this.blocked = blocked;
        this.log = log;
    }

    public void Block(Guid targetId) => this.Run(async token =>
    {
        await this.api.BlockAsync(token, targetId, CancellationToken.None);
        this.blocked.Invalidate();
        this.favorites.Invalidate();   // a block also drops favorites either direction server-side
    });

    public void Unblock(Guid targetId) => this.Run(async token =>
    {
        await this.api.UnblockAsync(token, targetId, CancellationToken.None);
        this.blocked.Invalidate();
    });

    public void Report(Guid targetId, ReportReasonEnum reason, string? details, string? sealedEvidence = null) =>
        this.Run(token => this.api.ReportAsync(token, new ReportRequest { TargetId = targetId, Reason = reason, Details = details, SealedEvidence = sealedEvidence }, CancellationToken.None));

    public void Favorite(Guid targetId, bool on) => this.Run(async token =>
    {
        await this.api.FavoriteAsync(token, targetId, on, CancellationToken.None);
        this.favorites.Invalidate();
    });

    private void Run(Func<string, Task> action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                await action(token);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Safety action failed.");
            }
        });
    }
}
