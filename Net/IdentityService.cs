using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dalamud.Plugin.Services;
using Eikon.Crypto;
using CryptoLib = Eikon.Crypto.Crypto;

namespace Eikon.Net;

internal enum IdentityResult
{
    Ok,         // matches the pinned identity, or pinned for the first time (trust-on-first-use)
    Mismatch,   // the identity key changed since first contact, a possible MITM; reject
    Invalid,    // the identity is internally inconsistent (Ed/X25519 binding signature bad)
}

// Identity trust (SECURITY-REVIEW-RATCHET.md). Verifies the Ed25519<->X25519 binding, pins a peer's
// identity on first contact (TOFU), and rejects a later silent change. Also computes the safety number
// for out-of-band verification. Pins are sealed to the vault and persisted (like the ratchet sessions).
internal sealed class IdentityService
{
    private readonly KeyVault vault;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private readonly Dictionary<Guid, Pin> pins = new();
    private readonly HashSet<Guid> mismatched = new();   // peers whose served identity changed since pinning
    private readonly string path;
    private bool loaded;
    private bool loadFailed;   // pins file exists but couldn't be read/decrypted -> fail closed

    public IdentityService(KeyVault vault, IPluginLog log)
    {
        this.vault = vault;
        this.log = log;
        this.path = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "pins.bin");
    }

    private sealed class Pin
    {
        public byte[] Ed = Array.Empty<byte>();
        public byte[] X25519 = Array.Empty<byte>();
        public bool Verified;
    }

    // Verify the binding signature, then pin-on-first-contact / reject-on-change.
    public IdentityResult Resolve(Guid peer, byte[] ed, byte[] x25519, byte[] x25519Sig)
    {
        if (!CryptoLib.IsKey(ed) || !CryptoLib.IsKey(x25519) || !CryptoLib.IsSignature(x25519Sig)
            || !CryptoLib.Verify(ed, x25519, x25519Sig))
            return IdentityResult.Invalid;

        this.EnsureLoaded();
        lock (this.gate)
        {
            if (this.loadFailed)
                return IdentityResult.Invalid;   // pins unreadable: refuse rather than re-TOFU silently
            if (this.pins.TryGetValue(peer, out var pin))
            {
                if (pin.Ed.AsSpan().SequenceEqual(ed) && pin.X25519.AsSpan().SequenceEqual(x25519))
                {
                    this.mismatched.Remove(peer);
                    return IdentityResult.Ok;
                }

                this.mismatched.Add(peer);   // changed identity: flag for the UI, do NOT auto-update
                return IdentityResult.Mismatch;
            }

            this.pins[peer] = new Pin { Ed = ed, X25519 = x25519, Verified = false };
            this.mismatched.Remove(peer);
            this.Save();
            return IdentityResult.Ok;
        }
    }

    public bool IsPinned(Guid peer)
    {
        this.EnsureLoaded();
        lock (this.gate)
            return this.pins.ContainsKey(peer);
    }

    public bool IsVerified(Guid peer)
    {
        this.EnsureLoaded();
        lock (this.gate)
            return this.pins.TryGetValue(peer, out var pin) && pin.Verified;
    }

    // The peer's served identity changed since we pinned it, a possible MITM. Surfaced in the chat UI.
    public bool Mismatched(Guid peer)
    {
        this.EnsureLoaded();
        lock (this.gate)
            return this.mismatched.Contains(peer);
    }

    // Drop a peer's pin so the next contact re-pins (the member has acknowledged the identity change).
    public void ForgetPin(Guid peer)
    {
        this.EnsureLoaded();
        lock (this.gate)
        {
            this.pins.Remove(peer);
            this.mismatched.Remove(peer);
            this.Save();
        }
    }

    public void MarkVerified(Guid peer)
    {
        this.EnsureLoaded();
        lock (this.gate)
        {
            if (this.pins.TryGetValue(peer, out var pin))
            {
                pin.Verified = true;
                this.Save();
            }
        }
    }

    // Replace a changed identity with the new one (user accepted the change after re-verifying), and
    // reset its verified state. Returns false if the new identity isn't internally consistent.
    public bool AcceptChange(Guid peer, byte[] ed, byte[] x25519, byte[] x25519Sig)
    {
        if (!CryptoLib.IsKey(ed) || !CryptoLib.IsKey(x25519) || !CryptoLib.IsSignature(x25519Sig)
            || !CryptoLib.Verify(ed, x25519, x25519Sig))
            return false;
        this.EnsureLoaded();
        lock (this.gate)
        {
            this.pins[peer] = new Pin { Ed = ed, X25519 = x25519, Verified = false };
            this.Save();
            return true;
        }
    }

    // The 60-digit safety number both peers can compare out of band. Deterministic and symmetric: it
    // is a fingerprint of the two Ed25519 identity keys in a fixed (byte-sorted) order. Returns null if
    // the peer isn't pinned yet or the vault is locked.
    public string? SafetyNumber(Guid peer)
    {
        if (!this.vault.IsUnlocked)
            return null;
        this.EnsureLoaded();
        byte[] mine = this.vault.IdentityPublic();
        byte[] theirs;
        lock (this.gate)
        {
            if (!this.pins.TryGetValue(peer, out var pin))
                return null;
            theirs = pin.Ed;
        }

        var (first, second) = mine.AsSpan().SequenceCompareTo(theirs) <= 0 ? (mine, theirs) : (theirs, mine);
        return Group(Fingerprint(first, second));
    }

    // 60 decimal digits derived from BOTH identity keys via an iterated SHA-512 (Signal-style), so the
    // number cross-binds the pair and the iteration count raises the cost of grinding a colliding key.
    private const int FingerprintRounds = 5200;

    private static string Fingerprint(byte[] first, byte[] second)
    {
        var domain = new byte[] { 0x45, 0x49, 0x4B, 0x01 };   // "EIK" + version
        var input = Concat(domain, first, second);
        var h = input;
        for (var i = 0; i < FingerprintRounds; i++)
            h = SHA512.HashData(Concat(h, input));

        var sb = new StringBuilder(72);
        for (var i = 0; i < 12; i++)   // 12 groups of 5 digits = 60 digits, from 12 x 5 bytes
        {
            long v = ((long)h[i * 5] << 32) | ((long)h[(i * 5) + 1] << 24) | ((uint)h[(i * 5) + 2] << 16) | ((uint)h[(i * 5) + 3] << 8) | h[(i * 5) + 4];
            sb.Append((v % 100000).ToString("D5"));
        }

        return sb.ToString();
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

    private static string Group(string digits)
    {
        var sb = new StringBuilder(digits.Length + (digits.Length / 5));
        for (var i = 0; i < digits.Length; i += 5)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(digits, i, Math.Min(5, digits.Length - i));
        }

        return sb.ToString();
    }

    private void EnsureLoaded()
    {
        lock (this.gate)
        {
            if (this.loaded || !this.vault.IsUnlocked)
                return;

            var primary = this.path;
            var backup = this.path + ".bak";
            if (!File.Exists(primary) && !File.Exists(backup))
            {
                this.loaded = true;   // no pins yet: legitimately empty
                return;
            }

            foreach (var p in new[] { primary, backup })
            {
                try
                {
                    if (!File.Exists(p))
                        continue;
                    var json = this.vault.OpenLocal(File.ReadAllBytes(p));
                    if (json is null)
                        continue;
                    var dto = JsonSerializer.Deserialize<Dictionary<string, PinDto>>(Encoding.UTF8.GetString(json));
                    if (dto is null)
                        continue;
                    foreach (var (key, value) in dto)
                        if (Guid.TryParse(key, out var peer))
                            this.pins[peer] = new Pin { Ed = Convert.FromBase64String(value.Ed), X25519 = Convert.FromBase64String(value.X25519), Verified = value.Verified };
                    this.loaded = true;
                    return;
                }
                catch (Exception ex)
                {
                    this.log.Warning(ex, $"Loading identity pins from {Path.GetFileName(p)} failed.");
                }
            }

            // Files exist but neither could be read/decrypted: fail closed (block establishing new
            // sessions) rather than silently treating pins as empty and re-TOFUing attacker keys.
            this.loadFailed = true;
            this.loaded = true;
        }
    }

    // Caller holds the gate.
    private void Save()
    {
        if (!this.vault.IsUnlocked || this.loadFailed)
            return;   // never overwrite an existing-but-unreadable pins file
        try
        {
            var dto = new Dictionary<string, PinDto>();
            foreach (var (peer, pin) in this.pins)
                dto[peer.ToString()] = new PinDto { Ed = Convert.ToBase64String(pin.Ed), X25519 = Convert.ToBase64String(pin.X25519), Verified = pin.Verified };
            var sealedBytes = this.vault.SealLocal(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto)));

            var tmp = this.path + ".tmp";
            File.WriteAllBytes(tmp, sealedBytes);
            if (File.Exists(this.path))
                File.Replace(tmp, this.path, this.path + ".bak");
            else
                File.Move(tmp, this.path);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Saving identity pins failed.");
        }
    }

    private sealed class PinDto
    {
        public string Ed { get; set; } = string.Empty;
        public string X25519 { get; set; } = string.Empty;
        public bool Verified { get; set; }
    }
}
