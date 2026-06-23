using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;
using Eikon.Crypto;
using CryptoLib = Eikon.Crypto.Crypto;

namespace Eikon.Net;

// Per-conversation message encryption: the symmetric ratchet (MESSAGE-CRYPTO.md). The first sender
// runs X3DH against the peer's bundle to agree a session secret; that seeds two one-directional HKDF
// chains (one per direction). Each message ratchets its chain one step and the used key is wiped, so
// a later key/vault compromise cannot recover past messages (forward secrecy). The relay only ever
// sees ciphertext.
//
// Security posture (see SECURITY-REVIEW-RATCHET.md): the receive path is verify-before-commit: it
// decrypts against a trial of the session state and only mutates/persists chain state, flips the
// established flag, adopts a session, or consumes a one-time prekey AFTER the AEAD tag authenticates.
// This denies an active/malicious relay the ability to desync or poison sessions with forged frames.
internal sealed class MessageCrypto
{
    private const byte Version = 0x02;
    private const byte FlagInitial = 0x01;
    private const int MaxSkip = 1000;

    private static readonly byte[] ChainSalt = "eikon-chain"u8.ToArray();
    private static readonly byte[] Chain0 = "chain-0"u8.ToArray();
    private static readonly byte[] Chain1 = "chain-1"u8.ToArray();
    private static readonly byte[] StepSalt = "eikon-step"u8.ToArray();
    private static readonly byte[] StepMsg = "msg"u8.ToArray();
    private static readonly byte[] StepChain = "chain"u8.ToArray();

    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly KeyVault vault;
    private readonly IdentityService identity;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private readonly Dictionary<Guid, Session> sessions = new();
    private readonly string sessionPath;
    private bool loaded;
    private bool loadFailed;   // sessions file exists but couldn't be read/decrypted -> fail closed

    public MessageCrypto(IApiClient api, AuthService auth, KeyVault vault, IdentityService identity, IPluginLog log)
    {
        this.api = api;
        this.auth = auth;
        this.vault = vault;
        this.identity = identity;
        this.log = log;
        this.sessionPath = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "sessions.bin");
    }

    // Binds the session secret to the participant pair and protocol version (defense in depth: a
    // session can't be transplanted to a different pair or silently downgraded). Both peers compute
    // the same bytes from the canonically-ordered ids.
    private static byte[] Context(string myId, Guid peer)
    {
        var peerId = peer.ToString();
        var (lo, hi) = string.CompareOrdinal(myId, peerId) < 0 ? (myId, peerId) : (peerId, myId);
        return Encoding.UTF8.GetBytes($"v{Version}:{lo}:{hi}");
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, result, 0, a.Length);
        Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
        return result;
    }

    private sealed class Session
    {
        public byte[] SendCk = Array.Empty<byte>();
        public uint SendN;
        public byte[] RecvCk = Array.Empty<byte>();
        public uint RecvN;
        public readonly Dictionary<uint, byte[]> Skipped = new();
        public bool IsInitiator;
        public bool PeerEstablished;        // initiator stops sending the initial header once the peer replies
        public byte[]? EkPub;               // initiator: ephemeral public key to carry until established
        public int SpkId;                   // initiator: which signed prekey of the peer was used
        public int OpkId;                   // initiator: one-time prekey id used (0 if none)
    }

    public async Task<(string Ciphertext, string Header, string Nonce)?> EncryptAsync(Guid peer, string text, CancellationToken ct)
    {
        if (!this.vault.IsUnlocked)
            return null;
        this.EnsureLoaded();
        if (this.loadFailed)
            return null;

        try
        {
            var token = await this.auth.GetAccessTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
                return null;
            var myId = SelfId(token);
            if (myId is null)
                return null;

            Session? session;
            lock (this.gate)
                this.sessions.TryGetValue(peer, out session);

            if (session is null)
            {
                var bundle = await this.api.GetKeyBundleAsync(token, peer.ToString(), ct);
                if (bundle.SignedPreKey?.Signature is null)
                    return null;

                var ed = Convert.FromBase64String(bundle.Ed25519Pub);
                var ikDh = Convert.FromBase64String(bundle.X25519Pub);
                // TOFU: verify the identity binding and pin/compare the peer identity before any session.
                if (this.identity.Resolve(peer, ed, ikDh, Convert.FromBase64String(bundle.X25519Sig)) != IdentityResult.Ok)
                    return null;

                var opkPub = bundle.OneTimePreKey is { } otk ? Convert.FromBase64String(otk.PublicKey) : null;
                var opkId = bundle.OneTimePreKey is { } o ? (int)o.KeyId : 0;
                var spkId = (int)bundle.SignedPreKey.KeyId;
                if (!this.vault.TryX3dhInitiate(
                        ikDh, ed, Convert.FromBase64String(bundle.X25519Sig),
                        Convert.FromBase64String(bundle.SignedPreKey.PublicKey),
                        Convert.FromBase64String(bundle.SignedPreKey.Signature),
                        opkPub, opkId, Context(myId, peer),
                        out var sk, out var ekPub, out var usedOpkId))
                    return null;

                lock (this.gate)
                {
                    if (!this.sessions.TryGetValue(peer, out session))
                    {
                        session = NewSession(myId, peer, sk, initiator: true);
                        session.EkPub = ekPub;
                        session.SpkId = spkId;
                        session.OpkId = usedOpkId;
                        this.sessions[peer] = session;
                    }
                }

                Array.Clear(sk);   // copied into the chains (or discarded on the race); wipe either way
            }

            byte[] mk;
            byte[] header;
            lock (this.gate)
            {
                if (session.SendN == uint.MaxValue)
                    return null;   // chain exhausted; refuse rather than wrap
                var n = session.SendN;
                mk = StepMk(ref session.SendCk);
                session.SendN++;
                var initial = session.IsInitiator && !session.PeerEstablished;
                header = BuildHeader(initial, n, session.EkPub, session.SpkId, session.OpkId);
                this.Save();
            }

            var nonce = CryptoLib.Random(CryptoLib.NonceSize);
            var aad = Concat(Context(myId, peer), header);   // bind the participant pair into the AEAD
            var ciphertext = CryptoLib.Encrypt(mk, nonce, aad, Encoding.UTF8.GetBytes(text));
            Array.Clear(mk);
            return (Convert.ToBase64String(ciphertext), Convert.ToBase64String(header), Convert.ToBase64String(nonce));
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Message encrypt failed.");
            return null;
        }
    }

    public async Task<string?> DecryptAsync(Guid peer, EncryptedMessageDto dto, CancellationToken ct)
    {
        if (!this.vault.IsUnlocked)
            return null;
        this.EnsureLoaded();
        if (this.loadFailed)
            return null;

        try
        {
            var header = Convert.FromBase64String(dto.Header);
            if (!ParseHeader(header, out var initial, out var n, out var ekPub, out var spkId, out var opkId))
                return null;
            var nonce = Convert.FromBase64String(dto.Nonce);
            var ciphertext = Convert.FromBase64String(dto.Ciphertext);

            var token = await this.auth.GetAccessTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
                return null;
            var myId = SelfId(token);
            if (myId is null)
                return null;

            var aad = Concat(Context(myId, peer), header);   // must match the sender's AEAD binding

            // 1) Try the existing session first (covers all non-initial messages and initial messages
            //    once the session is established). State is mutated only on a successful tag check.
            lock (this.gate)
            {
                if (this.sessions.TryGetValue(peer, out var existing)
                    && TryRecv(existing, n, nonce, aad, ciphertext, out var existingText))
                {
                    existing.PeerEstablished = true;
                    this.Save();
                    return existingText;
                }
            }

            // 2) An initial frame that did not decrypt under an existing session: try to establish a
            //    new session and decrypt under it. Commit only if the message authenticates, so a
            //    forged/garbage initial cannot overwrite or poison the existing session.
            if (!initial)
                return null;

            var peerIdentity = await this.api.GetIdentityAsync(token, peer.ToString(), ct);
            var peerEd = Convert.FromBase64String(peerIdentity.Ed25519Pub);
            var peerIkDh = Convert.FromBase64String(peerIdentity.X25519Pub);
            // TOFU: verify the binding and pin/compare before responding to a handshake.
            if (this.identity.Resolve(peer, peerEd, peerIkDh, Convert.FromBase64String(peerIdentity.X25519Sig)) != IdentityResult.Ok)
                return null;
            if (!this.vault.TryX3dhRespond(peerIkDh, ekPub, spkId, opkId, Context(myId, peer), out var sk))
                return null;

            lock (this.gate)
            {
                // Another thread may have established the session while we fetched/derived.
                if (this.sessions.TryGetValue(peer, out var current)
                    && TryRecv(current, n, nonce, aad, ciphertext, out var raced))
                {
                    current.PeerEstablished = true;
                    this.Save();
                    Array.Clear(sk);
                    return raced;
                }

                var trial = NewSession(myId, peer, sk, initiator: false);
                Array.Clear(sk);
                if (!TryRecv(trial, n, nonce, aad, ciphertext, out var trialText))
                {
                    WipeSession(trial);
                    return null;   // forged/garbage initial: existing session (if any) is left intact
                }

                // Tie-break: if we hold our own un-established initiator session and win the id compare,
                // keep ours (the peer will adopt it); otherwise adopt this verified session.
                if (current is { IsInitiator: true, PeerEstablished: false }
                    && string.CompareOrdinal(myId, peer.ToString()) < 0)
                {
                    WipeSession(trial);
                    return null;
                }

                if (current != null)
                    WipeSession(current);
                trial.PeerEstablished = true;
                this.sessions[peer] = trial;
                if (opkId != 0)
                    this.vault.ConsumeOneTimePreKey(opkId);
                this.Save();
                return trialText;
            }
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Message decrypt failed.");
            return null;
        }
    }

    // Drop the local session with a peer so the next outgoing message re-runs X3DH (a fresh handshake).
    // Recovery for the rare desync where the peer lost their session and can no longer decrypt our
    // non-initial messages (see SECURITY-REVIEW-RATCHET.md residual edge).
    public void ResetSession(Guid peer)
    {
        lock (this.gate)
        {
            this.EnsureLoaded();
            if (this.sessions.Remove(peer, out var s))
                WipeSession(s);
            this.Save();
        }
    }

    // Whether we currently hold a session for a peer.
    public bool HasSession(Guid peer)
    {
        lock (this.gate)
        {
            this.EnsureLoaded();
            return this.sessions.ContainsKey(peer);
        }
    }

    // Whether a (base64) header carries the X3DH initial handshake. A non-initial header from a peer we
    // have no session for is a desync (vs a forged initial); the caller can request a re-handshake.
    public static bool IsInitialHeader(string headerBase64)
    {
        try
        {
            var h = Convert.FromBase64String(headerBase64);
            return ParseHeader(h, out var initial, out _, out _, out _, out _) && initial;
        }
        catch
        {
            return false;
        }
    }

    private static Session NewSession(string myId, Guid peer, byte[] sk, bool initiator)
    {
        var party0 = string.CompareOrdinal(myId, peer.ToString()) < 0;
        var ck0 = CryptoLib.Hkdf(sk, ChainSalt, Chain0, 32);
        var ck1 = CryptoLib.Hkdf(sk, ChainSalt, Chain1, 32);
        return new Session
        {
            SendCk = party0 ? ck0 : ck1,
            RecvCk = party0 ? ck1 : ck0,
            IsInitiator = initiator,
        };
    }

    // Verify-before-commit receive. Decrypts message `n` against `s` WITHOUT mutating it; only on a
    // successful AEAD tag does it advance the chain / consume the skipped key. Caller holds the gate.
    private static bool TryRecv(Session s, uint n, byte[] nonce, byte[] aad, byte[] ciphertext, out string? text)
    {
        text = null;

        // Out-of-order: a previously cached skipped key. Consume only on success (so a duplicate or a
        // forged frame at the same number can't evict a real pending key).
        if (n < s.RecvN)
        {
            if (!s.Skipped.TryGetValue(n, out var cached))
                return false;
            if (!CryptoLib.TryDecrypt(cached, nonce, aad, ciphertext, out var early))
                return false;
            s.Skipped.Remove(n);
            Array.Clear(cached);
            text = Encoding.UTF8.GetString(early);
            return true;
        }

        if (n - s.RecvN > MaxSkip)
            return false;

        // Derive forward to n on a copy of the chain; commit the advance only if the tag checks out.
        var ck = s.RecvCk;
        var pending = new List<KeyValuePair<uint, byte[]>>();
        var rn = s.RecvN;
        while (rn < n)
        {
            var (skipMk, next) = Kdf(ck);
            pending.Add(new KeyValuePair<uint, byte[]>(rn, skipMk));
            ck = next;
            rn++;
        }

        var (mk, finalCk) = Kdf(ck);
        if (!CryptoLib.TryDecrypt(mk, nonce, aad, ciphertext, out var plain))
        {
            Array.Clear(mk);
            Array.Clear(finalCk);
            foreach (var kv in pending)
                Array.Clear(kv.Value);
            return false;
        }

        Array.Clear(s.RecvCk);
        s.RecvCk = finalCk;
        s.RecvN = rn + 1;
        foreach (var kv in pending)
        {
            s.Skipped[kv.Key] = kv.Value;
            EvictSkipped(s);
        }

        Array.Clear(mk);
        text = Encoding.UTF8.GetString(plain);
        return true;
    }

    private static void EvictSkipped(Session s)
    {
        if (s.Skipped.Count <= MaxSkip)
            return;
        var oldest = uint.MaxValue;
        foreach (var k in s.Skipped.Keys)
            if (k < oldest)
                oldest = k;
        if (s.Skipped.Remove(oldest, out var evicted))
            Array.Clear(evicted);
    }

    private static void WipeSession(Session s)
    {
        Array.Clear(s.SendCk);
        Array.Clear(s.RecvCk);
        foreach (var v in s.Skipped.Values)
            Array.Clear(v);
        s.Skipped.Clear();
        if (s.EkPub != null)
            Array.Clear(s.EkPub);
    }

    // One KDF chain step for sending: derive the message key, advance (and wipe) the chain key.
    private static byte[] StepMk(ref byte[] ck)
    {
        var (mk, next) = Kdf(ck);
        Array.Clear(ck);
        ck = next;
        return mk;
    }

    // Derive (message key, next chain key) from a chain key without mutating the input.
    private static (byte[] Mk, byte[] Next) Kdf(byte[] ck)
        => (CryptoLib.Hkdf(ck, StepSalt, StepMsg, 32), CryptoLib.Hkdf(ck, StepSalt, StepChain, 32));

    private static byte[] BuildHeader(bool initial, uint n, byte[]? ekPub, int spkId, int opkId)
    {
        if (initial && ekPub is not null)
        {
            var h = new byte[6 + 32 + 4 + 4];
            h[0] = Version;
            h[1] = FlagInitial;
            WriteUInt32(h, 2, n);
            Buffer.BlockCopy(ekPub, 0, h, 6, 32);
            WriteUInt32(h, 38, (uint)spkId);
            WriteUInt32(h, 42, (uint)opkId);
            return h;
        }

        var head = new byte[6];
        head[0] = Version;
        WriteUInt32(head, 2, n);
        return head;
    }

    private static bool ParseHeader(byte[] h, out bool initial, out uint n, out byte[] ekPub, out int spkId, out int opkId)
    {
        initial = false;
        n = 0;
        ekPub = Array.Empty<byte>();
        spkId = 0;
        opkId = 0;
        if (h.Length < 6 || h[0] != Version)
            return false;

        initial = (h[1] & FlagInitial) != 0;
        n = ReadUInt32(h, 2);
        if (initial)
        {
            if (h.Length < 46)
                return false;
            ekPub = h[6..38];
            spkId = (int)ReadUInt32(h, 38);
            opkId = (int)ReadUInt32(h, 42);
        }

        return true;
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];

    // The signed-in user's id (the access token subject), normalized to canonical form so both peers
    // order the chains identically.
    private static string? SelfId(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length < 2)
                return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload,
            };
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return doc.RootElement.TryGetProperty("sub", out var sub) && sub.GetString() is { } s
                ? Guid.Parse(s).ToString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    // Sessions are sealed to the vault and persisted so the ratchet survives app restarts (otherwise a
    // recipient restart after the handshake would drop the sender's subsequent messages). Persisting
    // the current chain key does not weaken forward secrecy: the KDF is one-way, so past message keys
    // remain unrecoverable. A reset/new identity reseals under a new vault key, leaving the old file
    // unreadable (treated as empty).
    private void EnsureLoaded()
    {
        lock (this.gate)
        {
            if (this.loaded)
                return;

            var primary = this.sessionPath;
            var backup = this.sessionPath + ".bak";
            if (!File.Exists(primary) && !File.Exists(backup))
            {
                this.loaded = true;   // no sessions yet: legitimately empty
                return;
            }

            // Prefer the primary file, fall back to the atomic-write backup before giving up.
            foreach (var path in new[] { primary, backup })
            {
                try
                {
                    if (!File.Exists(path))
                        continue;
                    var json = this.vault.OpenLocal(File.ReadAllBytes(path));
                    if (json is null)
                        continue;
                    var dto = JsonSerializer.Deserialize<Dictionary<string, SessionDto>>(Encoding.UTF8.GetString(json));
                    if (dto is null)
                        continue;
                    foreach (var (key, value) in dto)
                        if (Guid.TryParse(key, out var peer))
                            this.sessions[peer] = FromDto(value);
                    this.loaded = true;
                    return;
                }
                catch (Exception ex)
                {
                    this.log.Warning(ex, $"Loading message sessions from {Path.GetFileName(path)} failed.");
                }
            }

            // Files exist but neither could be read/decrypted: fail closed (block messaging) rather
            // than treating sessions as empty and overwriting them on the next save.
            this.loadFailed = true;
            this.loaded = true;
        }
    }

    // Caller holds the gate. Atomic write (temp + replace) with a .bak, so a crash mid-write cannot
    // corrupt the store and silently drop every conversation.
    private void Save()
    {
        if (this.loadFailed)
            return;   // never overwrite an existing-but-unreadable sessions file
        try
        {
            var dto = new Dictionary<string, SessionDto>();
            foreach (var (peer, session) in this.sessions)
                dto[peer.ToString()] = ToDto(session);
            var sealedBytes = this.vault.SealLocal(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto)));

            var tmp = this.sessionPath + ".tmp";
            File.WriteAllBytes(tmp, sealedBytes);
            if (File.Exists(this.sessionPath))
                File.Replace(tmp, this.sessionPath, this.sessionPath + ".bak");
            else
                File.Move(tmp, this.sessionPath);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Saving message sessions failed.");
        }
    }

    // Skipped message keys are intentionally NOT persisted: they are raw AEAD keys, and writing them to
    // disk would let a seized device decrypt undelivered messages (forward secrecy). They live only in
    // memory for the lifetime of the session.
    private static SessionDto ToDto(Session s) => new()
    {
        SendCk = Convert.ToBase64String(s.SendCk),
        SendN = s.SendN,
        RecvCk = Convert.ToBase64String(s.RecvCk),
        RecvN = s.RecvN,
        IsInitiator = s.IsInitiator,
        PeerEstablished = s.PeerEstablished,
        EkPub = s.EkPub is null ? null : Convert.ToBase64String(s.EkPub),
        SpkId = s.SpkId,
        OpkId = s.OpkId,
    };

    private static Session FromDto(SessionDto d) => new()
    {
        SendCk = Convert.FromBase64String(d.SendCk),
        SendN = d.SendN,
        RecvCk = Convert.FromBase64String(d.RecvCk),
        RecvN = d.RecvN,
        IsInitiator = d.IsInitiator,
        PeerEstablished = d.PeerEstablished,
        EkPub = string.IsNullOrEmpty(d.EkPub) ? null : Convert.FromBase64String(d.EkPub),
        SpkId = d.SpkId,
        OpkId = d.OpkId,
    };

    private sealed class SessionDto
    {
        public string SendCk { get; set; } = string.Empty;
        public uint SendN { get; set; }
        public string RecvCk { get; set; } = string.Empty;
        public uint RecvN { get; set; }
        public bool IsInitiator { get; set; }
        public bool PeerEstablished { get; set; }
        public string? EkPub { get; set; }
        public int SpkId { get; set; }
        public int OpkId { get; set; }
    }
}
