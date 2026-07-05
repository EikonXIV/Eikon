using System.Net.Http;
using System.Threading;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// Albums: the member's own collections, the access requests waiting on them, a peer's albums for their
// profile, and album-photo textures loaded through the album's grant-checked signed URL (cached by
// album+photo). Mutations are fire-and-forget and reload the affected list, mirroring PhotoService. The
// service key never reaches the client; only short-lived signed URLs do.
internal sealed class AlbumService : IDisposable
{
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly IPluginLog log;
    private readonly HttpClient http = new();
    private readonly CancellationToken lifetime;

    private readonly Dictionary<Guid, List<AlbumPhotoDto>> photosByAlbum = new();
    private readonly Dictionary<Guid, List<AlbumGranteeDto>> grantsByAlbum = new();
    private readonly Dictionary<Guid, List<PeerAlbumDto>> peerByUser = new();
    private readonly Dictionary<string, IDalamudTextureWrap?> textures = new();
    private readonly HashSet<string> loadingTextures = new();
    private readonly HashSet<Guid> loadingPhotos = new();
    private readonly HashSet<Guid> loadingGrants = new();
    private readonly HashSet<Guid> loadingPeers = new();
    private bool mineLoading;
    private bool requestsLoading;
    private bool requestsLoaded;

    public AlbumService(IApiClient api, AuthService auth, IPluginLog log, AppLifetime lifetime)
    {
        this.api = api;
        this.auth = auth;
        this.log = log;
        this.lifetime = lifetime.Token;
    }

    public bool Loaded { get; private set; }

    public IReadOnlyList<AlbumDto> Mine { get; private set; } = Array.Empty<AlbumDto>();

    public IReadOnlyList<AlbumRequestDto> Requests { get; private set; } = Array.Empty<AlbumRequestDto>();

    public void EnsureLoaded()
    {
        if (this.Loaded || this.mineLoading)
            return;
        this.Reload();
    }

    public void Reload()
    {
        this.mineLoading = true;
        this.Fire(async token =>
        {
            this.Mine = await this.api.ListAlbumsAsync(token, CancellationToken.None);
            this.Loaded = true;
        }, "Listing albums failed.", () => this.mineLoading = false);
    }

    // Load the incoming requests once (for the manager badge and the requests screen). Refreshed
    // explicitly after an approve or deny, not polled every frame.
    public void EnsureRequests()
    {
        if (this.requestsLoaded || this.requestsLoading)
            return;
        this.ReloadRequests();
    }

    public void ReloadRequests()
    {
        if (this.requestsLoading)
            return;
        this.requestsLoading = true;
        this.Fire(async token =>
        {
            this.Requests = await this.api.ListAlbumRequestsAsync(token, CancellationToken.None);
            this.requestsLoaded = true;
        }, "Listing album requests failed.", () => this.requestsLoading = false);
    }

    // ---- reads (cached; load on first access, return what's cached meanwhile) -----------------

    public IReadOnlyList<AlbumPhotoDto> Photos(Guid albumId)
    {
        if (this.photosByAlbum.TryGetValue(albumId, out var list))
            return list;
        if (this.loadingPhotos.Add(albumId))
            this.Fire(async token =>
            {
                this.photosByAlbum[albumId] = await this.api.ListAlbumPhotosAsync(token, albumId.ToString(), CancellationToken.None);
            }, "Listing album photos failed.", () => this.loadingPhotos.Remove(albumId));
        return Array.Empty<AlbumPhotoDto>();
    }

    public IReadOnlyList<AlbumGranteeDto> Grants(Guid albumId)
    {
        if (this.grantsByAlbum.TryGetValue(albumId, out var list))
            return list;
        if (this.loadingGrants.Add(albumId))
            this.Fire(async token =>
            {
                this.grantsByAlbum[albumId] = await this.api.ListAlbumGrantsAsync(token, albumId.ToString(), CancellationToken.None);
            }, "Listing album grants failed.", () => this.loadingGrants.Remove(albumId));
        return Array.Empty<AlbumGranteeDto>();
    }

    public IReadOnlyList<PeerAlbumDto> PeerAlbums(Guid userId)
    {
        if (this.peerByUser.TryGetValue(userId, out var list))
            return list;
        if (this.loadingPeers.Add(userId))
            this.Fire(async token =>
            {
                this.peerByUser[userId] = await this.api.ListPeerAlbumsAsync(token, userId.ToString(), CancellationToken.None);
            }, "Listing peer albums failed.", () => this.loadingPeers.Remove(userId));
        return Array.Empty<PeerAlbumDto>();
    }

    // Drop a peer's cached album list so the next PeerAlbums() call refetches their current access
    // state. Called when their profile is opened: an approval lands on the owner's client, not the
    // requester's, so without this the requester's cached "requested" state never clears within a
    // session and the now-granted album stays stuck looking locked.
    public void InvalidatePeer(Guid userId) => this.peerByUser.Remove(userId);

    // Album-photo texture through the album's own grant-checked mint. Cached by album+photo so the same
    // photo viewed from different albums does not clash.
    public IDalamudTextureWrap? Texture(Guid albumId, Guid photoId)
    {
        var key = albumId + ":" + photoId;
        if (this.textures.TryGetValue(key, out var wrap))
            return wrap;
        if (this.loadingTextures.Add(key))
            _ = this.LoadTexture(albumId, photoId, key);
        return null;
    }

    // ---- mutations (fire-and-forget; reload the affected list) --------------------------------

    public void Create(string name, string visibility) =>
        this.Fire(async token =>
        {
            await this.api.CreateAlbumAsync(token, name, visibility, CancellationToken.None);
            this.Reload();
        }, "Creating album failed.");

    public void Rename(Guid albumId, string name) => this.Patch(albumId, name, null, null);

    public void SetVisibility(Guid albumId, string visibility) => this.Patch(albumId, null, visibility, null);

    public void SetCover(Guid albumId, Guid photoId) => this.Patch(albumId, null, null, photoId.ToString());

    public void Delete(Guid albumId) =>
        this.Fire(async token =>
        {
            await this.api.DeleteAlbumAsync(token, albumId.ToString(), CancellationToken.None);
            this.photosByAlbum.Remove(albumId);
            this.grantsByAlbum.Remove(albumId);
            this.Reload();
        }, "Deleting album failed.");

    public void AddPhoto(Guid albumId, byte[] bytes, string contentType) =>
        this.Fire(async token =>
        {
            await this.api.AddAlbumPhotoAsync(token, albumId.ToString(), bytes, contentType, CancellationToken.None);
            await this.RefreshPhotos(token, albumId);
            this.Reload();   // photo count / cover changed
        }, "Adding album photo failed.");

    public void RemovePhoto(Guid albumId, Guid photoId) =>
        this.Fire(async token =>
        {
            await this.api.RemoveAlbumPhotoAsync(token, albumId.ToString(), photoId.ToString(), CancellationToken.None);
            await this.RefreshPhotos(token, albumId);
            this.Reload();
        }, "Removing album photo failed.");

    public void Grant(Guid albumId, Guid granteeId, string source) =>
        this.Fire(async token =>
        {
            await this.api.GrantAlbumAsync(token, albumId.ToString(), granteeId, source, CancellationToken.None);
            await this.RefreshGrants(token, albumId);
            this.Reload();
        }, "Granting album access failed.");

    public void Revoke(Guid albumId, Guid granteeId) =>
        this.Fire(async token =>
        {
            await this.api.RevokeAlbumAsync(token, albumId.ToString(), granteeId.ToString(), CancellationToken.None);
            await this.RefreshGrants(token, albumId);
            this.Reload();
        }, "Revoking album access failed.");

    public void RequestAccess(Guid albumId, Guid ownerId) =>
        this.Fire(async token =>
        {
            await this.api.RequestAlbumAccessAsync(token, albumId.ToString(), CancellationToken.None);
            this.peerByUser.Remove(ownerId);   // the album's access state flips to "requested"
        }, "Requesting album access failed.");

    public void Approve(Guid requestId) =>
        this.Fire(async token =>
        {
            await this.api.ApproveAlbumRequestAsync(token, requestId.ToString(), CancellationToken.None);
            this.ReloadRequests();
            this.Reload();
        }, "Approving album request failed.");

    public void Deny(Guid requestId) =>
        this.Fire(async token =>
        {
            await this.api.DenyAlbumRequestAsync(token, requestId.ToString(), CancellationToken.None);
            this.ReloadRequests();
            this.Reload();
        }, "Denying album request failed.");

    // ---- internals ---------------------------------------------------------------------------

    private void Patch(Guid albumId, string? name, string? visibility, string? coverPhotoId) =>
        this.Fire(async token =>
        {
            await this.api.UpdateAlbumAsync(token, albumId.ToString(), name, visibility, coverPhotoId, CancellationToken.None);
            this.Reload();
        }, "Updating album failed.");

    private async Task RefreshPhotos(string token, Guid albumId) =>
        this.photosByAlbum[albumId] = await this.api.ListAlbumPhotosAsync(token, albumId.ToString(), CancellationToken.None);

    private async Task RefreshGrants(string token, Guid albumId) =>
        this.grantsByAlbum[albumId] = await this.api.ListAlbumGrantsAsync(token, albumId.ToString(), CancellationToken.None);

    private async Task LoadTexture(Guid albumId, Guid photoId, string key)
    {
        try
        {
            var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrEmpty(token))
            {
                this.textures[key] = null;
                return;
            }

            var url = await this.api.AlbumPhotoViewUrlAsync(token, albumId.ToString(), photoId.ToString(), this.lifetime);
            var bytes = await this.http.GetByteArrayAsync(url, this.lifetime);
            this.textures[key] = await Plugin.TextureProvider.CreateFromImageAsync(bytes, cancellationToken: this.lifetime);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Loading album photo texture failed.");
            this.textures[key] = null;
        }
    }

    // Run an action with a fresh access token off the UI thread, logging on failure. `onDone` always
    // runs (loading-flag reset), success or not.
    private void Fire(Func<string, Task> action, string what, Action? onDone = null)
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
                this.log.Warning(ex, what);
            }
            finally
            {
                onDone?.Invoke();
            }
        });
    }

    public void Dispose()
    {
        this.http.Dispose();
        foreach (var wrap in this.textures.Values)
            wrap?.Dispose();
        this.textures.Clear();
    }
}
