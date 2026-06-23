using System.IO;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Eikon.Crypto;

namespace Eikon.Net;

// Decrypted chat images, sealed to the vault on disk (chatmedia/<id>.bin) and turned into textures on
// demand. The plaintext image is only ever written sealed (DPAPI + vault key); the server only ever
// held the E2E-encrypted blob. Textures are cached and disposed with the plugin.
internal sealed class ChatMediaCache : IDisposable
{
    private readonly KeyVault vault;
    private readonly IPluginLog log;
    private readonly string dir;
    private readonly object gate = new();
    private readonly Dictionary<string, IDalamudTextureWrap> textures = new();   // only successes are cached
    private readonly HashSet<string> loading = new();
    private readonly Dictionary<string, DateTime> retryAfter = new();   // throttle reloads of not-yet-ready images

    public ChatMediaCache(KeyVault vault, IPluginLog log)
    {
        this.vault = vault;
        this.log = log;
        this.dir = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "chatmedia");
        Directory.CreateDirectory(this.dir);
    }

    // Persist a decrypted image, sealed to the vault, so it survives restarts without re-downloading.
    public void Save(string id, byte[] imageBytes)
    {
        if (!this.vault.IsUnlocked)
            return;
        try
        {
            File.WriteAllBytes(Path.Combine(this.dir, id + ".bin"), this.vault.SealLocal(imageBytes));
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Saving chat image failed.");
        }
    }

    public bool Has(string id) => File.Exists(Path.Combine(this.dir, id + ".bin"));

    // The texture for a stored image, or null while it loads (call again next frame). A failed/not-yet-
    // written image is retried (throttled) rather than cached as null, so an image still uploading when
    // its bubble first draws renders as soon as its sealed file lands.
    public IDalamudTextureWrap? Texture(string id)
    {
        lock (this.gate)
        {
            if (this.textures.TryGetValue(id, out var existing))
                return existing;
            if (this.loading.Contains(id))
                return null;
            if (this.retryAfter.TryGetValue(id, out var when) && DateTime.UtcNow < when)
                return null;
            this.loading.Add(id);
            this.retryAfter[id] = DateTime.UtcNow.AddSeconds(1);
        }

        _ = Task.Run(async () =>
        {
            IDalamudTextureWrap? wrap = null;
            try
            {
                var path = Path.Combine(this.dir, id + ".bin");
                if (this.vault.IsUnlocked && File.Exists(path))
                {
                    var bytes = this.vault.OpenLocal(File.ReadAllBytes(path));
                    if (bytes != null)
                        wrap = await Plugin.TextureProvider.CreateFromImageAsync(bytes);
                }
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Loading chat image failed.");
            }

            lock (this.gate)
            {
                this.loading.Remove(id);
                if (wrap != null)
                    this.textures[id] = wrap;   // cache successes only; failures retry after the window
            }
        });
        return null;
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            foreach (var t in this.textures.Values)
                t?.Dispose();
            this.textures.Clear();
        }
    }
}
