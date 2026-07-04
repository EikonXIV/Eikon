using System.Text;
using Eikon.Net;
using Xunit;
using CryptoLib = Eikon.Crypto.Crypto;

namespace Eikon.Tests;

// The pure symmetric-ratchet math (chain KDF, receive-with-skip, verify-before-commit, header codec),
// exercised directly on raw byte[] chains with no vault or network. Guards the security invariants from
// MESSAGE-CRYPTO.md and SECURITY-REVIEW-RATCHET.md.
public class RatchetMathTests
{
    [Fact]
    public void Kdf_is_deterministic_and_advances_the_chain()
    {
        var ck = CryptoLib.Random(32);
        var (mk1, next1) = MessageCrypto.Kdf(ck);
        var (mk2, next2) = MessageCrypto.Kdf(ck);
        Assert.Equal(mk1, mk2);
        Assert.Equal(next1, next2);
        Assert.NotEqual(mk1, next1);   // message key and next chain key are distinct
        Assert.Equal(32, mk1.Length);
    }

    [Fact]
    public void StepMk_derives_the_message_key_and_advances_the_chain_key()
    {
        var ck = CryptoLib.Random(32);
        var expected = MessageCrypto.Kdf(ck).Mk;
        var advanced = (byte[])ck.Clone();
        var mk = MessageCrypto.StepMk(ref advanced);
        Assert.Equal(expected, mk);
        Assert.NotEqual(ck, advanced);   // the ref chain key moved to the next step
    }

    [Fact]
    public void NewSession_gives_the_two_parties_complementary_send_and_recv_chains()
    {
        var sk = CryptoLib.Random(32);
        var lo = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var hi = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var a = MessageCrypto.NewSession(lo.ToString(), hi, sk, initiator: true);
        var b = MessageCrypto.NewSession(hi.ToString(), lo, sk, initiator: false);
        Assert.Equal(a.SendCk, b.RecvCk);
        Assert.Equal(a.RecvCk, b.SendCk);
    }

    // Encrypt message `n` the way a sender on the matching chain would: step a copy of the receive chain
    // n+1 times to reach message key n, then AEAD-seal under the caller's aad.
    private static (byte[] Nonce, byte[] Ciphertext) SealFor(byte[] recvCk, uint n, byte[] aad, string text)
    {
        var ck = (byte[])recvCk.Clone();
        var mk = Array.Empty<byte>();
        for (uint i = 0; i <= n; i++)
            mk = MessageCrypto.StepMk(ref ck);
        var nonce = CryptoLib.Random(CryptoLib.NonceSize);
        return (nonce, CryptoLib.Encrypt(mk, nonce, aad, Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void TryRecv_decrypts_in_order_and_advances_the_receive_counter()
    {
        var s = new MessageCrypto.Session { RecvCk = CryptoLib.Random(32) };
        var aad = "aad"u8.ToArray();
        var (nonce, ct) = SealFor(s.RecvCk, 0, aad, "hello");
        Assert.True(MessageCrypto.TryRecv(s, 0, nonce, aad, ct, out var text));
        Assert.Equal("hello", text);
        Assert.Equal(1u, s.RecvN);
    }

    [Fact]
    public void TryRecv_caches_skipped_keys_and_delivers_a_reordered_message()
    {
        var recvCk = CryptoLib.Random(32);
        var s = new MessageCrypto.Session { RecvCk = (byte[])recvCk.Clone() };
        var aad = "aad"u8.ToArray();

        // Deliver n=2 first: message keys 0 and 1 get cached as skipped.
        var (n2Nonce, n2Ct) = SealFor(recvCk, 2, aad, "third");
        Assert.True(MessageCrypto.TryRecv(s, 2, n2Nonce, aad, n2Ct, out var third));
        Assert.Equal("third", third);
        Assert.Equal(3u, s.RecvN);
        Assert.Equal(2, s.Skipped.Count);

        // The reordered n=0 now hits the cache and is consumed.
        var (n0Nonce, n0Ct) = SealFor(recvCk, 0, aad, "first");
        Assert.True(MessageCrypto.TryRecv(s, 0, n0Nonce, aad, n0Ct, out var first));
        Assert.Equal("first", first);
        Assert.Single(s.Skipped);   // key 0 consumed, key 1 still cached
    }

    [Fact]
    public void TryRecv_rejects_a_forged_frame_without_mutating_the_session()
    {
        var s = new MessageCrypto.Session { RecvCk = CryptoLib.Random(32) };
        var aad = "aad"u8.ToArray();
        var recvCkBefore = (byte[])s.RecvCk.Clone();

        var garbage = CryptoLib.Random(48);   // not a valid ciphertext under the derived key
        Assert.False(MessageCrypto.TryRecv(s, 0, CryptoLib.Random(CryptoLib.NonceSize), aad, garbage, out var text));
        Assert.Null(text);
        // Verify-before-commit: chain key, counter, and skipped cache are all untouched.
        Assert.Equal(recvCkBefore, s.RecvCk);
        Assert.Equal(0u, s.RecvN);
        Assert.Empty(s.Skipped);
    }

    [Fact]
    public void TryRecv_refuses_a_skip_beyond_MaxSkip()
    {
        var s = new MessageCrypto.Session { RecvCk = CryptoLib.Random(32) };
        Assert.False(MessageCrypto.TryRecv(
            s, (uint)MessageCrypto.MaxSkip + 1, CryptoLib.Random(CryptoLib.NonceSize), "aad"u8.ToArray(), CryptoLib.Random(48), out _));
        Assert.Equal(0u, s.RecvN);
    }

    [Fact]
    public void Header_roundtrips_for_initial_and_non_initial_frames()
    {
        var ek = CryptoLib.Random(32);
        var init = MessageCrypto.BuildHeader(true, 7, ek, 3, 9);
        Assert.True(MessageCrypto.ParseHeader(init, out var isInit, out var n, out var ekOut, out var spk, out var opk));
        Assert.True(isInit);
        Assert.Equal(7u, n);
        Assert.Equal(ek, ekOut);
        Assert.Equal(3, spk);
        Assert.Equal(9, opk);

        var plain = MessageCrypto.BuildHeader(false, 42, null, 0, 0);
        Assert.True(MessageCrypto.ParseHeader(plain, out var isInit2, out var n2, out _, out _, out _));
        Assert.False(isInit2);
        Assert.Equal(42u, n2);
    }

    [Fact]
    public void ParseHeader_rejects_truncated_or_wrong_version_frames()
    {
        Assert.False(MessageCrypto.ParseHeader(new byte[5], out _, out _, out _, out _, out _));   // too short
        var wrongVersion = MessageCrypto.BuildHeader(false, 1, null, 0, 0);
        wrongVersion[0] = 0x99;
        Assert.False(MessageCrypto.ParseHeader(wrongVersion, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void Context_is_canonical_regardless_of_which_side_computes_it()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.Equal(MessageCrypto.Context(a.ToString(), b), MessageCrypto.Context(b.ToString(), a));
    }
}
