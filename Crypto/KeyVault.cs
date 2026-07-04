using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eikon.Crypto;

internal sealed record PreKeyPublic(int KeyId, byte[] PublicKey, byte[]? Signature);

internal sealed record PublicKeyBundle(
    byte[] Ed25519Pub, byte[] X25519Pub, byte[] X25519Sig, PreKeyPublic SignedPreKey, IReadOnlyList<PreKeyPublic> OneTimePreKeys);

// Local E2E key store (ARCHITECTURE 5.7). Holds the identity keypairs and X3DH prekeys. Private keys
// are wrapped with an Argon2id passphrase key (XChaCha20-Poly1305) and then OS-sealed with DPAPI
// before being written to the plugin config directory. The passphrase is entered once at setup and
// required to unlock; private keys live in memory only while unlocked.
internal sealed class KeyVault
{
    private const int OneTimePreKeyCount = 50;
    private const int VaultKeySize = 32;
    private const int SaltSize = 16;

    private readonly string path;

    private byte[]? signPriv;
    private byte[]? signPub;
    private byte[]? dhPriv;
    private byte[]? dhPub;
    private SignedPreKey? signedPreKey;
    private SignedPreKey? prevSignedPreKey;     // kept one rotation back for in-flight handshakes
    private long spkCreatedAt;                  // unix seconds, for rotation age
    private int nextOtkId = 1;                  // monotonic so replenished one-time prekeys never reuse an id
    private ArgonParams argon = ArgonParams.Current;
    private readonly List<OneTimePreKey> oneTimePreKeys = new();

    // Argon2id cost for the passphrase KEK. Stored in the vault so the parameters can be raised over
    // time without breaking existing vaults (old files unlock with their recorded/legacy values).
    private readonly record struct ArgonParams(int MemKiB, int Passes, int Parallelism)
    {
        public static readonly ArgonParams Current = new(256 * 1024, 3, 1);   // 256 MiB
        public static readonly ArgonParams Legacy = new(64 * 1024, 3, 1);     // pre-stored-params default
    }

    // Kept in memory while unlocked so prekey changes (X3DH consuming a one-time prekey) can be
    // re-sealed without re-entering the passphrase. Cleared on lock.
    private byte[]? vaultKey;
    private byte[]? salt;

    // Whether the DPAPI auto-unlock material is kept on disk. Default on (normal relaunch is silent);
    // a member on a shared PC can turn it off ("require passphrase on this PC") so every launch prompts.
    private bool autoUnlock = true;

    // vaultPath is a test seam: production passes nothing and gets the Dalamud config-dir path (no
    // behavior change); a test can point the vault at a temp file so it needs no Dalamud runtime. The
    // Dalamud path lives in its own method so the '??' short-circuit means the ctor JIT-compiles and
    // runs without ever touching Dalamud when a path is supplied.
    public KeyVault(string? vaultPath = null)
    {
        this.path = vaultPath ?? DefaultVaultPath();
    }

    private static string DefaultVaultPath() =>
        Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "vault.json");

    public bool HasIdentity => File.Exists(this.path);

    public bool IsUnlocked { get; private set; }

    public void CreateIdentity(string passphrase)
    {
        var (sPriv, sPub) = Crypto.GenerateSignKeyPair();
        var (dPriv, dPub) = Crypto.GenerateDhKeyPair();

        var (spkPriv, spkPub) = Crypto.GenerateDhKeyPair();
        var signed = new SignedPreKey { Id = 1, Priv = spkPriv, Pub = spkPub, Signature = Crypto.Sign(sPriv, spkPub) };

        var otks = new List<OneTimePreKey>();
        for (var i = 0; i < OneTimePreKeyCount; i++)
        {
            var (p, pub) = Crypto.GenerateDhKeyPair();
            otks.Add(new OneTimePreKey { Id = i + 1, Priv = p, Pub = pub });
        }

        this.signPriv = sPriv;
        this.signPub = sPub;
        this.dhPriv = dPriv;
        this.dhPub = dPub;
        this.signedPreKey = signed;
        this.prevSignedPreKey = null;
        this.spkCreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        this.nextOtkId = OneTimePreKeyCount + 1;
        this.oneTimePreKeys.Clear();
        this.oneTimePreKeys.AddRange(otks);
        this.IsUnlocked = true;

        this.autoUnlock = true;
        this.argon = ArgonParams.Current;
        this.salt = Crypto.Random(SaltSize);
        this.vaultKey = Crypto.DeriveFromPassphrase(passphrase, this.salt, VaultKeySize, this.argon.MemKiB, this.argon.Passes, this.argon.Parallelism);
        this.PersistBundle();
    }

    public bool Unlock(string passphrase)
    {
        if (!this.HasIdentity)
            return false;
        try
        {
            var file = ReadFile(this.path);
            // Use the parameters recorded in the vault (legacy 64MiB/3/1 for files written before they
            // were stored), so raising the default never locks an existing member out.
            var p = file.ArgonMemKiB > 0 ? new ArgonParams(file.ArgonMemKiB, file.ArgonPasses, file.ArgonParallelism) : ArgonParams.Legacy;
            var key = Crypto.DeriveFromPassphrase(passphrase, Convert.FromBase64String(file.Salt), VaultKeySize, p.MemKiB, p.Passes, p.Parallelism);
            if (!this.OpenWith(key, file))
                return false;

            // A successful passphrase unlock re-enables DPAPI auto-unlock on this machine.
            this.PersistBundle();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Silent auto-unlock on the same Windows account (no passphrase) via the DPAPI-sealed vault key.
    // This is the normal launch path; the passphrase is only needed at setup or after logout/reset.
    public bool TryAutoUnlock()
    {
        if (!this.HasIdentity)
            return false;
        try
        {
            var file = ReadFile(this.path);
            if (string.IsNullOrEmpty(file.Auto))
                return false;
            return this.OpenWith(Dpapi.Unprotect(Convert.FromBase64String(file.Auto)), file);
        }
        catch
        {
            return false;
        }
    }

    // Whether silent DPAPI auto-unlock is enabled on this device (member preference).
    public bool AutoUnlockEnabled => this.autoUnlock;

    // Turn auto-unlock on/off ("require passphrase on this PC"). When off, the DPAPI material is dropped
    // and never re-written, so every launch prompts for the passphrase. Requires the vault unlocked.
    public void SetAutoUnlock(bool enabled)
    {
        this.RequireUnlocked();
        if (this.autoUnlock == enabled)
            return;
        this.autoUnlock = enabled;
        this.PersistBundle();   // writes or clears the Auto material per the new preference
    }

    // Drop the DPAPI auto-unlock material so the next launch requires the passphrase (logout/reset).
    public void ForgetAutoUnlock()
    {
        if (!this.HasIdentity)
            return;
        try
        {
            var file = ReadFile(this.path);
            file.Auto = null;
            WriteAtomic(this.path, JsonSerializer.Serialize(file));
        }
        catch
        {
            // Best effort.
        }
    }

    private bool OpenWith(byte[] key, VaultFile file)
    {
        var aead = Dpapi.Unprotect(Convert.FromBase64String(file.Sealed));
        if (!Crypto.TryDecrypt(key, Convert.FromBase64String(file.Nonce), Array.Empty<byte>(), aead, out var bundleJson))
            return false;

        var bundle = JsonSerializer.Deserialize<Bundle>(Encoding.UTF8.GetString(bundleJson)) ?? throw new InvalidDataException("empty bundle");
        this.signPriv = bundle.SignPriv;
        this.signPub = bundle.SignPub;
        this.dhPriv = bundle.DhPriv;
        this.dhPub = bundle.DhPub;
        this.signedPreKey = bundle.SignedPreKey;
        this.prevSignedPreKey = bundle.PrevSignedPreKey;
        this.spkCreatedAt = bundle.SpkCreatedAt;
        this.nextOtkId = bundle.NextOtkId > 0 ? bundle.NextOtkId : bundle.OneTimePreKeys.Count + 1;
        this.oneTimePreKeys.Clear();
        this.oneTimePreKeys.AddRange(bundle.OneTimePreKeys);
        this.vaultKey = key;
        this.salt = Convert.FromBase64String(file.Salt);
        this.autoUnlock = file.AutoUnlock;
        this.argon = file.ArgonMemKiB > 0 ? new ArgonParams(file.ArgonMemKiB, file.ArgonPasses, file.ArgonParallelism) : ArgonParams.Legacy;
        this.IsUnlocked = true;
        return true;
    }

    private static VaultFile ReadFile(string path) =>
        JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(path)) ?? throw new InvalidDataException("empty vault");

    // Destroy the local identity entirely (the "reset and start over" path). The old private keys
    // and the on-disk vault are gone, so previously received messages become unreadable; the member
    // re-onboards to mint a fresh key set and passphrase.
    public void Reset()
    {
        this.Lock();
        var dir = Path.GetDirectoryName(this.path)!;
        foreach (var file in new[] { this.path, Path.Combine(dir, "sessions.bin"), Path.Combine(dir, "sessions.bin.bak"), Path.Combine(dir, "threads.bin"), Path.Combine(dir, "threads.bin.bak") })
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // Best effort; a stale file is overwritten / unreadable under the new vault key.
            }
        }
    }

    public void Lock()
    {
        Array.Clear(this.signPriv ?? Array.Empty<byte>());
        Array.Clear(this.dhPriv ?? Array.Empty<byte>());
        Array.Clear(this.vaultKey ?? Array.Empty<byte>());
        Array.Clear(this.salt ?? Array.Empty<byte>());
        WipePrivate(this.signedPreKey);
        WipePrivate(this.prevSignedPreKey);
        foreach (var k in this.oneTimePreKeys)
            Array.Clear(k.Priv);
        this.signPriv = this.dhPriv = null;
        this.vaultKey = this.salt = null;
        this.signedPreKey = this.prevSignedPreKey = null;
        this.oneTimePreKeys.Clear();
        this.IsUnlocked = false;
    }

    private static void WipePrivate(SignedPreKey? key)
    {
        if (key != null)
            Array.Clear(key.Priv);
    }

    // Public material to publish to the server for X3DH (PublishKeysRequest).
    public PublicKeyBundle PublicBundle()
    {
        this.RequireUnlocked();
        var signed = new PreKeyPublic(this.signedPreKey!.Id, this.signedPreKey.Pub, this.signedPreKey.Signature);
        var otks = this.oneTimePreKeys.Select(k => new PreKeyPublic(k.Id, k.Pub, null)).ToList();
        var x25519Sig = Crypto.Sign(this.signPriv!, this.dhPub!);   // bind the DH identity to the signing identity
        return new PublicKeyBundle(this.signPub!, this.dhPub!, x25519Sig, signed, otks);
    }

    // The signing (Ed25519) identity public key, used to compute the safety number with a peer.
    public byte[] IdentityPublic()
    {
        this.RequireUnlocked();
        return this.signPub!;
    }

    // X3DH (MESSAGE-CRYPTO.md). Establishes the session secret SK for the symmetric ratchet. Each DH
    // is run through HKDF to 32 bytes (NSec shared secrets are not raw-exportable) and the results are
    // concatenated, then HKDF'd to SK. Both endpoints compute identical SK.
    private static readonly byte[] X3dhSalt = "eikon-x3dh-v2"u8.ToArray();
    private static readonly byte[] Dh1 = "dh1"u8.ToArray();
    private static readonly byte[] Dh2 = "dh2"u8.ToArray();
    private static readonly byte[] Dh3 = "dh3"u8.ToArray();
    private static readonly byte[] Dh4 = "dh4"u8.ToArray();
    private static readonly byte[] SkInfo = "sk"u8.ToArray();

    // Initiator: derive SK against a peer's fetched bundle. Verifies the signed prekey, mints an
    // ephemeral (discarded after, for handshake forward secrecy), returns SK + the ephemeral public
    // key and the one-time prekey id used (0 if none) for the message header.
    public bool TryX3dhInitiate(
        byte[] peerIkDh, byte[] peerEd, byte[] peerX25519Sig, byte[] peerSpkPub, byte[] peerSpkSig,
        byte[]? peerOpkPub, int peerOpkId, byte[] context,
        out byte[] sk, out byte[] ekPub, out int usedOpkId)
    {
        sk = Array.Empty<byte>();
        ekPub = Array.Empty<byte>();
        usedOpkId = 0;
        this.RequireUnlocked();

        if (!Crypto.IsKey(peerIkDh) || !Crypto.IsKey(peerEd) || !Crypto.IsKey(peerSpkPub)
            || !Crypto.IsSignature(peerX25519Sig) || !Crypto.IsSignature(peerSpkSig)
            || (peerOpkPub != null && !Crypto.IsKey(peerOpkPub)))
            return false;

        // Bind the peer's DH identity to its signing identity, and verify the signed prekey. A server
        // that swapped the DH key (keeping the Ed key for a future safety-number check) fails here.
        if (!Crypto.Verify(peerEd, peerIkDh, peerX25519Sig))
            return false;
        if (!Crypto.Verify(peerEd, peerSpkPub, peerSpkSig))
            return false;

        var (ekPriv, ekPublic) = Crypto.GenerateDhKeyPair();
        try
        {
            var dh1 = Crypto.Agree(this.dhPriv!, peerSpkPub, X3dhSalt, Dh1, 32);
            var dh2 = Crypto.Agree(ekPriv, peerIkDh, X3dhSalt, Dh2, 32);
            var dh3 = Crypto.Agree(ekPriv, peerSpkPub, X3dhSalt, Dh3, 32);
            byte[] ikm;
            byte[]? dh4 = null;
            if (peerOpkPub != null)
            {
                dh4 = Crypto.Agree(ekPriv, peerOpkPub, X3dhSalt, Dh4, 32);
                ikm = Concat(dh1, dh2, dh3, dh4);
                usedOpkId = peerOpkId;
            }
            else
            {
                ikm = Concat(dh1, dh2, dh3);
            }

            sk = Crypto.Hkdf(ikm, X3dhSalt, Concat(SkInfo, context), 32);
            ekPub = ekPublic;

            Array.Clear(dh1);
            Array.Clear(dh2);
            Array.Clear(dh3);
            if (dh4 != null)
                Array.Clear(dh4);
            Array.Clear(ikm);
            return true;
        }
        finally
        {
            Array.Clear(ekPriv);
        }
    }

    // Responder: derive the same SK from the initiator's identity key, ephemeral, and the one-time
    // prekey id carried in the header. Does NOT consume the prekey; the caller calls
    // ConsumeOneTimePreKey only after the first message authenticates, so a forged/replayed initial
    // can't drain the pool and a replay of a consumed prekey is rejected here (opk not found).
    public bool TryX3dhRespond(byte[] peerIkDh, byte[] ekPub, int spkId, int opkId, byte[] context, out byte[] sk)
    {
        sk = Array.Empty<byte>();
        this.RequireUnlocked();

        if (!Crypto.IsKey(peerIkDh) || !Crypto.IsKey(ekPub))
            return false;

        var spk = this.FindSignedPreKey(spkId);
        if (spk == null)
            return false;   // signed prekey rotated out; cannot match the initiator's SK

        var dh1 = Crypto.Agree(spk.Priv, peerIkDh, X3dhSalt, Dh1, 32);
        var dh2 = Crypto.Agree(this.dhPriv!, ekPub, X3dhSalt, Dh2, 32);
        var dh3 = Crypto.Agree(spk.Priv, ekPub, X3dhSalt, Dh3, 32);

        byte[] ikm;
        byte[]? dh4 = null;
        if (opkId != 0)
        {
            var opk = this.oneTimePreKeys.FirstOrDefault(k => k.Id == opkId);
            if (opk == null)
            {
                Array.Clear(dh1);
                Array.Clear(dh2);
                Array.Clear(dh3);
                return false;   // already consumed / unknown; cannot match the initiator's SK
            }

            dh4 = Crypto.Agree(opk.Priv, ekPub, X3dhSalt, Dh4, 32);
            ikm = Concat(dh1, dh2, dh3, dh4);
        }
        else
        {
            ikm = Concat(dh1, dh2, dh3);
        }

        sk = Crypto.Hkdf(ikm, X3dhSalt, Concat(SkInfo, context), 32);

        Array.Clear(dh1);
        Array.Clear(dh2);
        Array.Clear(dh3);
        if (dh4 != null)
            Array.Clear(dh4);
        Array.Clear(ikm);
        return true;
    }

    // The signed prekey by id: current or the most recent previous one (kept briefly across a
    // rotation so in-flight handshakes against the old key still complete).
    private SignedPreKey? FindSignedPreKey(int id)
    {
        if (this.signedPreKey?.Id == id)
            return this.signedPreKey;
        if (this.prevSignedPreKey?.Id == id)
            return this.prevSignedPreKey;
        return null;
    }

    // Delete a used one-time prekey and re-seal the vault (forward secrecy: the private half must not
    // survive on disk, or SK could be recomputed from long-term keys). Called on a verified handshake.
    public void ConsumeOneTimePreKey(int keyId)
    {
        var idx = this.oneTimePreKeys.FindIndex(k => k.Id == keyId);
        if (idx < 0)
            return;
        Array.Clear(this.oneTimePreKeys[idx].Priv);   // wipe the spent private before dropping it
        this.oneTimePreKeys.RemoveAt(idx);
        this.PersistBundle();
    }

    public bool SignedPreKeyOlderThan(TimeSpan age)
        => DateTimeOffset.UtcNow.ToUnixTimeSeconds() - this.spkCreatedAt > (long)age.TotalSeconds;

    // Rotate the signed prekey, keeping the previous one (one rotation back) so handshakes already in
    // flight against the old key still complete. Returns the new public bundle to republish.
    public PublicKeyBundle RotateSignedPreKey()
    {
        this.RequireUnlocked();
        WipePrivate(this.prevSignedPreKey);   // drop the key two rotations back
        var (spkPriv, spkPub) = Crypto.GenerateDhKeyPair();
        this.prevSignedPreKey = this.signedPreKey;
        this.signedPreKey = new SignedPreKey
        {
            Id = (this.signedPreKey?.Id ?? 0) + 1,
            Priv = spkPriv,
            Pub = spkPub,
            Signature = Crypto.Sign(this.signPriv!, spkPub),
        };
        this.spkCreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        this.PersistBundle();
        return this.PublicBundle();
    }

    public int AvailableOneTimePreKeys => this.oneTimePreKeys.Count;

    // Mint `count` new one-time prekeys (monotonic ids), persist, and return their public parts so the
    // caller can append them server-side (replenishment).
    public IReadOnlyList<PreKeyPublic> GenerateOneTimePreKeys(int count)
    {
        this.RequireUnlocked();
        var added = new List<PreKeyPublic>();
        for (var i = 0; i < count; i++)
        {
            var (priv, pub) = Crypto.GenerateDhKeyPair();
            var id = this.nextOtkId++;
            this.oneTimePreKeys.Add(new OneTimePreKey { Id = id, Priv = priv, Pub = pub });
            added.Add(new PreKeyPublic(id, pub, null));
        }

        this.PersistBundle();
        return added;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var p in parts)
            total += p.Length;
        var result = new byte[total];
        var offset = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }

    // Seal/open arbitrary local state (the message ratchet's session store) with the in-memory vault
    // key, so it is bound to the vault: a logout/reset that changes or drops the vault key makes the
    // sealed blob unreadable. Layout: nonce(24) || ciphertext+tag.
    public byte[] SealLocal(byte[] plaintext)
    {
        this.RequireUnlocked();
        var nonce = Crypto.Random(Crypto.NonceSize);
        var ciphertext = Crypto.Encrypt(this.vaultKey!, nonce, Array.Empty<byte>(), plaintext);
        var output = new byte[nonce.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, output, nonce.Length, ciphertext.Length);
        return output;
    }

    public byte[]? OpenLocal(byte[] blob)
    {
        this.RequireUnlocked();
        if (blob.Length < Crypto.NonceSize)
            return null;
        var nonce = blob[..Crypto.NonceSize];
        var ciphertext = blob[Crypto.NonceSize..];
        return Crypto.TryDecrypt(this.vaultKey!, nonce, Array.Empty<byte>(), ciphertext, out var plaintext) ? plaintext : null;
    }

    private void RequireUnlocked()
    {
        if (!this.IsUnlocked)
            throw new InvalidOperationException("Key vault is locked.");
    }

    // Seal the current key bundle to disk with the in-memory vault key (no passphrase needed). Used at
    // setup, on unlock (to refresh DPAPI auto-unlock), and whenever prekeys change.
    private void PersistBundle()
    {
        if (this.vaultKey is null || this.salt is null)
            throw new InvalidOperationException("Vault key not available.");

        var bundle = new Bundle
        {
            SignPriv = this.signPriv!, SignPub = this.signPub!, DhPriv = this.dhPriv!, DhPub = this.dhPub!,
            SignedPreKey = this.signedPreKey!, PrevSignedPreKey = this.prevSignedPreKey,
            SpkCreatedAt = this.spkCreatedAt, NextOtkId = this.nextOtkId,
            OneTimePreKeys = this.oneTimePreKeys.ToList(),
        };
        var bundleJson = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bundle));

        var nonce = Crypto.Random(Crypto.NonceSize);
        var aead = Crypto.Encrypt(this.vaultKey, nonce, Array.Empty<byte>(), bundleJson);
        var sealedBlob = Dpapi.Protect(aead);

        var file = new VaultFile
        {
            Salt = Convert.ToBase64String(this.salt),
            Nonce = Convert.ToBase64String(nonce),
            Sealed = Convert.ToBase64String(sealedBlob),
            Auto = this.autoUnlock ? Convert.ToBase64String(Dpapi.Protect(this.vaultKey)) : null,
            AutoUnlock = this.autoUnlock,
            ArgonMemKiB = this.argon.MemKiB,
            ArgonPasses = this.argon.Passes,
            ArgonParallelism = this.argon.Parallelism,
        };
        WriteAtomic(this.path, JsonSerializer.Serialize(file));
    }

    // Write via a temp file then atomically replace, so a crash mid-write cannot leave a truncated
    // vault that locks the member out of their identity.
    private static void WriteAtomic(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }

    private sealed class VaultFile
    {
        public int Version { get; set; } = 1;
        public string Salt { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        [JsonPropertyName("sealed")] public string Sealed { get; set; } = string.Empty;
        public string? Auto { get; set; }   // DPAPI-sealed vault key for silent auto-unlock
        public bool AutoUnlock { get; set; } = true;   // member preference; default on
        public int ArgonMemKiB { get; set; }
        public int ArgonPasses { get; set; }
        public int ArgonParallelism { get; set; }
    }

    private sealed class SignedPreKey
    {
        public int Id { get; set; }
        public byte[] Priv { get; set; } = Array.Empty<byte>();
        public byte[] Pub { get; set; } = Array.Empty<byte>();
        public byte[] Signature { get; set; } = Array.Empty<byte>();
    }

    private sealed class OneTimePreKey
    {
        public int Id { get; set; }
        public byte[] Priv { get; set; } = Array.Empty<byte>();
        public byte[] Pub { get; set; } = Array.Empty<byte>();
    }

    private sealed class Bundle
    {
        public byte[] SignPriv { get; set; } = Array.Empty<byte>();
        public byte[] SignPub { get; set; } = Array.Empty<byte>();
        public byte[] DhPriv { get; set; } = Array.Empty<byte>();
        public byte[] DhPub { get; set; } = Array.Empty<byte>();
        public SignedPreKey SignedPreKey { get; set; } = new();
        public SignedPreKey? PrevSignedPreKey { get; set; }
        public long SpkCreatedAt { get; set; }
        public int NextOtkId { get; set; }
        public List<OneTimePreKey> OneTimePreKeys { get; set; } = new();
    }
}
