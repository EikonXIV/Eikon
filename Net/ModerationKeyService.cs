using System.Buffers.Binary;
using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Crypto;

namespace Eikon.Net;

// Supplies the X25519 public key that report evidence is sealed to. The key is served by the server so
// it can be rotated without a client release, but it is trusted only if the pinned moderation root
// (ModerationKey.RootPublic) has signed it and its version is not a rollback. Starts on the embedded
// fallback (ModerationKey.Seal*) and upgrades to the served key once verified.
internal sealed class ModerationKeyService
{
    private readonly IApiClient api;
    private readonly IPluginLog log;
    private readonly object gate = new();

    private byte[] sealPublic;
    private long version;
    private bool loaded;
    private bool loading;

    public ModerationKeyService(IApiClient api, IPluginLog log)
    {
        this.api = api;
        this.log = log;
        this.sealPublic = Convert.FromBase64String(ModerationKey.SealPublicBase64);
        this.version = ModerationKey.SealVersion;

        // The embedded fallback must itself verify against the pinned root; if it doesn't, the build is
        // inconsistent and reports would be sealed to an unverified key.
        if (!VerifiesAgainstRoot(ModerationKey.SealPublicBase64, ModerationKey.SealSignatureBase64, ModerationKey.SealVersion))
            this.log.Error("Embedded moderation seal key fails the pinned-root signature check; rebuild from moderation/genkeys.mjs output.");
    }

    // The trusted X25519 seal public key (the embedded fallback until the server's key is verified).
    public byte[] Public
    {
        get { lock (this.gate) return this.sealPublic; }
    }

    // Fetch the served seal key once and adopt it only if root-signed and not older than the current
    // version. Retries on a later call until it succeeds, so a startup outage is not permanent.
    public void EnsureLoaded()
    {
        lock (this.gate)
        {
            if (this.loaded || this.loading)
                return;
            this.loading = true;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var res = await this.api.GetModerationKeyAsync(CancellationToken.None);
                if (res is null)
                    return;   // endpoint unavailable; keep the fallback and retry on the next call

                lock (this.gate)
                {
                    if (res.Version < this.version)
                        this.log.Warning("Served moderation key version is older than the current one; ignoring (rollback).");
                    else if (!VerifiesAgainstRoot(res.PublicKey, res.Signature, res.Version))
                        this.log.Warning("Served moderation key failed the pinned-root signature check; ignoring.");
                    else
                    {
                        this.sealPublic = Convert.FromBase64String(res.PublicKey);
                        this.version = res.Version;
                    }

                    this.loaded = true;
                }
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Fetching the moderation key failed; using the embedded fallback.");
            }
            finally
            {
                lock (this.gate)
                    this.loading = false;
            }
        });
    }

    // True only if `signature` is the pinned root's Ed25519 signature over (domain || version || key).
    private static bool VerifiesAgainstRoot(string sealPublicBase64, string signatureBase64, long ver)
    {
        try
        {
            var pub = Convert.FromBase64String(sealPublicBase64);
            var sig = Convert.FromBase64String(signatureBase64);
            if (!Eikon.Crypto.Crypto.IsKey(pub) || !Eikon.Crypto.Crypto.IsSignature(sig))
                return false;

            var domain = ModerationKey.SignatureDomain;
            var message = new byte[domain.Length + 4 + pub.Length];
            Buffer.BlockCopy(domain, 0, message, 0, domain.Length);
            BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(domain.Length, 4), (uint)ver);
            Buffer.BlockCopy(pub, 0, message, domain.Length + 4, pub.Length);

            return Eikon.Crypto.Crypto.Verify(ModerationKey.RootPublic, message, sig);
        }
        catch
        {
            return false;
        }
    }
}
