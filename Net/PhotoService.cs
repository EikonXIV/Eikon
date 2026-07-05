using System.Net.Http;
using System.Threading;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// Photos: lists the member's own photos, uploads new ones, and loads any photo's bytes through a
// short-lived signed URL into a texture (cached by id). The service key never reaches the client.
internal sealed class PhotoService : IDisposable
{
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly IPluginLog log;
    private readonly HttpClient http = new();
    private readonly CancellationToken lifetime;
    private readonly Dictionary<Guid, IDalamudTextureWrap?> textures = new();
    private readonly HashSet<Guid> loadingTextures = new();
    private bool listLoading;

    public PhotoService(IApiClient api, AuthService auth, IPluginLog log, AppLifetime lifetime)
    {
        this.api = api;
        this.auth = auth;
        this.log = log;
        this.lifetime = lifetime.Token;
    }

    public bool Loaded { get; private set; }

    public IReadOnlyList<PhotoDto> Mine { get; private set; } = Array.Empty<PhotoDto>();

    public void EnsureLoaded()
    {
        if (this.Loaded || this.listLoading)
            return;
        this.Reload();
    }

    public void Reload()
    {
        this.listLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                this.Mine = await this.api.ListPhotosAsync(token, CancellationToken.None);
                this.Loaded = true;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Listing photos failed.");
            }
            finally
            {
                this.listLoading = false;
            }
        });
    }

    // Returns the cached texture, or null while it loads (kicks off the load once).
    public IDalamudTextureWrap? Texture(Guid id)
    {
        if (this.textures.TryGetValue(id, out var wrap))
            return wrap;
        if (this.loadingTextures.Add(id))
            _ = this.LoadTexture(id);
        return null;
    }

    public void Upload(byte[] bytes, string contentType)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                await this.api.UploadPhotoAsync(token, bytes, contentType, CancellationToken.None);
                this.Reload();
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Uploading photo failed.");
            }
        });
    }

    public void Delete(Guid id)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                await this.api.DeletePhotoAsync(token, id.ToString(), CancellationToken.None);
                this.Reload();
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Deleting photo failed.");
            }
        });
    }

    public void SetMain(Guid id)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                await this.api.SetMainPhotoAsync(token, id.ToString(), CancellationToken.None);
                this.Reload();
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Setting main photo failed.");
            }
        });
    }

    private async Task LoadTexture(Guid id)
    {
        try
        {
            var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrEmpty(token))
            {
                this.textures[id] = null;
                return;
            }

            var url = await this.api.PhotoViewUrlAsync(token, id.ToString(), this.lifetime);
            var bytes = await this.http.GetByteArrayAsync(url, this.lifetime);
            this.textures[id] = await Plugin.TextureProvider.CreateFromImageAsync(bytes, cancellationToken: this.lifetime);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Loading photo texture failed.");
            this.textures[id] = null;
        }
    }

    public void Dispose()
    {
        this.http.Dispose();
        foreach (var wrap in this.textures.Values)
            wrap?.Dispose();
        this.textures.Clear();
    }
}
