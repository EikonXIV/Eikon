using Xunit;
using CryptoLib = Eikon.Crypto.Crypto;   // 'Crypto' alone binds to the Eikon.Crypto namespace from here

namespace Eikon.Tests;

// Unit tests for the pure NSec/libsodium wrapper (Crypto.cs). No Dalamud, nothing on disk: every test
// generates ephemeral key material at runtime and asserts round-trips + tamper rejection.
public class CryptoPrimitivesTests
{
    [Fact]
    public void Sign_then_Verify_roundtrips()
    {
        var (priv, pub) = CryptoLib.GenerateSignKeyPair();
        var data = "hello ratchet"u8.ToArray();
        Assert.True(CryptoLib.Verify(pub, data, CryptoLib.Sign(priv, data)));
    }

    [Fact]
    public void Verify_rejects_tampered_data_signature_and_wrong_key()
    {
        var (priv, pub) = CryptoLib.GenerateSignKeyPair();
        var data = "hello"u8.ToArray();
        var sig = CryptoLib.Sign(priv, data);

        var badData = (byte[])data.Clone();
        badData[0] ^= 0xFF;
        Assert.False(CryptoLib.Verify(pub, badData, sig));

        var badSig = (byte[])sig.Clone();
        badSig[0] ^= 0xFF;
        Assert.False(CryptoLib.Verify(pub, data, badSig));

        var (_, otherPub) = CryptoLib.GenerateSignKeyPair();
        Assert.False(CryptoLib.Verify(otherPub, data, sig));
    }

    [Fact]
    public void Agree_is_symmetric_and_length_correct()
    {
        var (aPriv, aPub) = CryptoLib.GenerateDhKeyPair();
        var (bPriv, bPub) = CryptoLib.GenerateDhKeyPair();
        var ab = CryptoLib.Agree(aPriv, bPub, "salt"u8.ToArray(), "info"u8.ToArray(), 32);
        var ba = CryptoLib.Agree(bPriv, aPub, "salt"u8.ToArray(), "info"u8.ToArray(), 32);
        Assert.Equal(ab, ba);
        Assert.Equal(32, ab.Length);
    }

    [Fact]
    public void Agree_binds_salt_and_info()
    {
        var (aPriv, _) = CryptoLib.GenerateDhKeyPair();
        var (_, bPub) = CryptoLib.GenerateDhKeyPair();
        var baseKey = CryptoLib.Agree(aPriv, bPub, "s1"u8.ToArray(), "i1"u8.ToArray(), 32);
        Assert.NotEqual(baseKey, CryptoLib.Agree(aPriv, bPub, "s2"u8.ToArray(), "i1"u8.ToArray(), 32));
        Assert.NotEqual(baseKey, CryptoLib.Agree(aPriv, bPub, "s1"u8.ToArray(), "i2"u8.ToArray(), 32));
    }

    [Fact]
    public void Hkdf_is_deterministic_and_honours_length()
    {
        var ikm = "input"u8.ToArray();
        var a = CryptoLib.Hkdf(ikm, "salt"u8.ToArray(), "info"u8.ToArray(), 32);
        var b = CryptoLib.Hkdf(ikm, "salt"u8.ToArray(), "info"u8.ToArray(), 32);
        Assert.Equal(a, b);
        Assert.Equal(32, a.Length);
        Assert.Equal(64, CryptoLib.Hkdf(ikm, "salt"u8.ToArray(), "info"u8.ToArray(), 64).Length);
    }

    [Fact]
    public void Encrypt_then_TryDecrypt_roundtrips()
    {
        var key = CryptoLib.Random(CryptoLib.KeySize);
        var nonce = CryptoLib.Random(CryptoLib.NonceSize);
        var plaintext = "secret message"u8.ToArray();
        var ct = CryptoLib.Encrypt(key, nonce, "aad"u8.ToArray(), plaintext);
        Assert.True(CryptoLib.TryDecrypt(key, nonce, "aad"u8.ToArray(), ct, out var back));
        Assert.Equal(plaintext, back);
    }

    [Fact]
    public void TryDecrypt_rejects_tampered_ciphertext_wrong_aad_and_wrong_key()
    {
        var key = CryptoLib.Random(CryptoLib.KeySize);
        var nonce = CryptoLib.Random(CryptoLib.NonceSize);
        var ct = CryptoLib.Encrypt(key, nonce, "aad"u8.ToArray(), "hi"u8.ToArray());

        var flipped = (byte[])ct.Clone();
        flipped[0] ^= 0xFF;
        Assert.False(CryptoLib.TryDecrypt(key, nonce, "aad"u8.ToArray(), flipped, out _));
        Assert.False(CryptoLib.TryDecrypt(key, nonce, "other"u8.ToArray(), ct, out _));
        Assert.False(CryptoLib.TryDecrypt(CryptoLib.Random(CryptoLib.KeySize), nonce, "aad"u8.ToArray(), ct, out _));
    }

    [Fact]
    public void EncryptBlob_roundtrips_and_DecryptBlob_rejects_short_input()
    {
        var key = CryptoLib.Random(CryptoLib.KeySize);
        var plaintext = "blob"u8.ToArray();
        Assert.Equal(plaintext, CryptoLib.DecryptBlob(key, CryptoLib.EncryptBlob(key, plaintext)));
        Assert.Null(CryptoLib.DecryptBlob(key, new byte[10]));   // shorter than the 24-byte nonce
    }

    [Fact]
    public void DeriveFromPassphrase_is_deterministic_for_fixed_salt_and_params()
    {
        var salt = CryptoLib.Random(16);
        var a = CryptoLib.DeriveFromPassphrase("hunter2", salt, 32, 8192, 1, 1);
        var b = CryptoLib.DeriveFromPassphrase("hunter2", salt, 32, 8192, 1, 1);
        Assert.Equal(a, b);
        Assert.NotEqual(a, CryptoLib.DeriveFromPassphrase("different", salt, 32, 8192, 1, 1));
        Assert.Equal(32, a.Length);
    }

    [Fact]
    public void Seal_has_the_documented_layout_and_a_fresh_ephemeral_each_call()
    {
        var (_, recipientPub) = CryptoLib.GenerateDhKeyPair();
        var plaintext = "evidence"u8.ToArray();
        var s1 = CryptoLib.Seal(recipientPub, plaintext);
        var s2 = CryptoLib.Seal(recipientPub, plaintext);

        // ephPub(32) || nonce(12) || ciphertext + 16-byte tag
        Assert.Equal(32 + 12 + plaintext.Length + 16, s1.Length);
        Assert.Equal(s1.Length, s2.Length);
        Assert.NotEqual(s1[..32], s2[..32]);   // ephemeral public key differs per call
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(31, false)]
    [InlineData(33, false)]
    public void IsKey_gates_on_length(int length, bool expected)
        => Assert.Equal(expected, CryptoLib.IsKey(new byte[length]));

    [Fact]
    public void IsKey_and_IsSignature_reject_null_and_check_sizes()
    {
        Assert.False(CryptoLib.IsKey(null));
        Assert.False(CryptoLib.IsSignature(null));
        Assert.True(CryptoLib.IsSignature(new byte[64]));
        Assert.False(CryptoLib.IsSignature(new byte[63]));
    }
}
