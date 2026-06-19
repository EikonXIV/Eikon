using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Eikon.Crypto;

// Thin wrapper over the audited libsodium primitives (NSec). We never implement primitives
// ourselves (ARCHITECTURE 5.2). Raw byte arrays in/out so callers can persist and wire them; keys
// are imported per call and disposed promptly.
internal static class Crypto
{
    public const int NonceSize = 24;   // XChaCha20-Poly1305
    public const int KeySize = 32;     // X25519 / Ed25519 public keys
    public const int SignatureSize = 64;

    // Validate the length of peer-supplied key material at the trust boundary, rather than relying on
    // NSec to throw deep inside an import.
    public static bool IsKey(byte[]? k) => k is { Length: KeySize };
    public static bool IsSignature(byte[]? s) => s is { Length: SignatureSize };

    private static readonly KeyAgreementAlgorithm DhAlg = KeyAgreementAlgorithm.X25519;
    private static readonly SignatureAlgorithm SignAlg = SignatureAlgorithm.Ed25519;
    private static readonly AeadAlgorithm AeadAlg = AeadAlgorithm.XChaCha20Poly1305;
    private static readonly AeadAlgorithm SealAeadAlg = AeadAlgorithm.ChaCha20Poly1305;   // 12-byte nonce (IETF)
    private static readonly KeyDerivationAlgorithm HkdfAlg = KeyDerivationAlgorithm.HkdfSha256;

    private static readonly byte[] SealSalt = "eikon-report-seal-v1"u8.ToArray();
    private static readonly byte[] SealInfo = "moderation-evidence"u8.ToArray();

    private static KeyCreationParameters Exportable => new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };

    public static byte[] Random(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    public static (byte[] PrivateKey, byte[] PublicKey) GenerateDhKeyPair()
    {
        using var key = Key.Create(DhAlg, Exportable);
        return (key.Export(KeyBlobFormat.RawPrivateKey), key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    }

    public static (byte[] PrivateKey, byte[] PublicKey) GenerateSignKeyPair()
    {
        using var key = Key.Create(SignAlg, Exportable);
        return (key.Export(KeyBlobFormat.RawPrivateKey), key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    }

    public static byte[] Sign(byte[] signPrivateKey, byte[] data)
    {
        using var key = Key.Import(SignAlg, signPrivateKey, KeyBlobFormat.RawPrivateKey);
        return SignAlg.Sign(key, data);
    }

    public static bool Verify(byte[] signPublicKey, byte[] data, byte[] signature)
    {
        var pub = PublicKey.Import(SignAlg, signPublicKey, KeyBlobFormat.RawPublicKey);
        return SignAlg.Verify(pub, data, signature);
    }

    // X25519 ECDH, then HKDF-SHA256 to a symmetric key of the requested length.
    public static byte[] Agree(byte[] dhPrivateKey, byte[] peerDhPublicKey, byte[] salt, byte[] info, int length)
    {
        using var priv = Key.Import(DhAlg, dhPrivateKey, KeyBlobFormat.RawPrivateKey);
        var pub = PublicKey.Import(DhAlg, peerDhPublicKey, KeyBlobFormat.RawPublicKey);
        using var shared = DhAlg.Agree(priv, pub) ?? throw new CryptographicException("X25519 agreement failed.");
        return HkdfAlg.DeriveBytes(shared, salt, info, length);
    }

    // HKDF-SHA256 over raw input keying material (BCL). Used by the message ratchet for the X3DH
    // session key and the symmetric chain steps, where the input is raw bytes (a chain key), not an
    // NSec SharedSecret. See MESSAGE-CRYPTO.md.
    public static byte[] Hkdf(byte[] inputKeyingMaterial, byte[] salt, byte[] info, int length)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, inputKeyingMaterial, length, salt, info);

    public static byte[] Encrypt(byte[] key, byte[] nonce, byte[] associatedData, byte[] plaintext)
    {
        using var k = Key.Import(AeadAlg, key, KeyBlobFormat.RawSymmetricKey);
        return AeadAlg.Encrypt(k, nonce, associatedData, plaintext);
    }

    public static bool TryDecrypt(byte[] key, byte[] nonce, byte[] associatedData, byte[] ciphertext, out byte[] plaintext)
    {
        plaintext = Array.Empty<byte>();
        if (ciphertext.Length < AeadAlg.TagSize)
            return false;

        using var k = Key.Import(AeadAlg, key, KeyBlobFormat.RawSymmetricKey);
        var output = new byte[ciphertext.Length - AeadAlg.TagSize];
        if (!AeadAlg.Decrypt(k, nonce, associatedData, ciphertext, output))
            return false;

        plaintext = output;
        return true;
    }

    // Encrypt a media blob under a caller-supplied per-blob key (XChaCha20-Poly1305). Layout:
    // nonce(24) || ciphertext+tag. Used for chat images: the blob is uploaded opaque, the key travels
    // in-band through the ratchet. Self-contained so the recipient needs only the key.
    public static byte[] EncryptBlob(byte[] key, byte[] plaintext)
    {
        var nonce = Random(NonceSize);
        var ciphertext = Encrypt(key, nonce, Array.Empty<byte>(), plaintext);
        var output = new byte[nonce.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, output, nonce.Length, ciphertext.Length);
        return output;
    }

    public static byte[]? DecryptBlob(byte[] key, byte[] blob)
    {
        if (blob.Length < NonceSize)
            return null;
        return TryDecrypt(key, blob[..NonceSize], Array.Empty<byte>(), blob[NonceSize..], out var plaintext) ? plaintext : null;
    }

    // Anonymous sealed box to a recipient's X25519 public key (report evidence -> moderation key).
    // Ephemeral X25519 + HKDF-SHA256 -> ChaCha20-Poly1305 (IETF, 12-byte nonce), with the ephemeral
    // public key bound as AAD. Output: ephPub(32) || nonce(12) || ciphertext+tag. Forward-secret
    // (the ephemeral private key is discarded) and only the holder of the private key can open it.
    public static byte[] Seal(byte[] recipientX25519Pub, byte[] plaintext)
    {
        var (ephPriv, ephPub) = GenerateDhKeyPair();
        var key = Agree(ephPriv, recipientX25519Pub, SealSalt, SealInfo, 32);
        var nonce = Random(12);
        using var k = Key.Import(SealAeadAlg, key, KeyBlobFormat.RawSymmetricKey);
        var ciphertext = SealAeadAlg.Encrypt(k, nonce, ephPub, plaintext);

        var output = new byte[ephPub.Length + nonce.Length + ciphertext.Length];
        Buffer.BlockCopy(ephPub, 0, output, 0, ephPub.Length);
        Buffer.BlockCopy(nonce, 0, output, ephPub.Length, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, output, ephPub.Length + nonce.Length, ciphertext.Length);
        return output;
    }

    // Argon2id stretch of a passphrase into a vault key. Parameters are supplied by the caller (and
    // recorded in the vault) so the cost can be raised over time without breaking existing vaults.
    public static byte[] DeriveFromPassphrase(string passphrase, byte[] salt, int length, int memoryKiB, int passes, int parallelism)
    {
        var pbkdf = PasswordBasedKeyDerivationAlgorithm.Argon2id(new Argon2Parameters
        {
            DegreeOfParallelism = parallelism,
            MemorySize = memoryKiB,
            NumberOfPasses = passes,
        });
        return pbkdf.DeriveBytes(Encoding.UTF8.GetBytes(passphrase), salt, length);
    }
}
