using Eikon.Crypto;
using Xunit;

namespace Eikon.Tests;

// X3DH agreement via KeyVault.TryX3dhInitiate/Respond: the two sides derive the same session secret, the
// identity binding is enforced, and the participant context is bound into SK. Uses temp-dir vaults with
// throwaway passphrases (the vaultPath seam) so no Dalamud runtime is required. No real keys touched.
public class X3dhTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "eikon-x3dh-" + Guid.NewGuid().ToString("N"));

    public X3dhTests() => Directory.CreateDirectory(this.dir);

    public void Dispose()
    {
        try { Directory.Delete(this.dir, recursive: true); } catch { /* best effort */ }
    }

    private KeyVault NewVault(string name)
    {
        var v = new KeyVault(Path.Combine(this.dir, name + ".json"));
        v.CreateIdentity("passphrase-" + name);
        return v;
    }

    [Fact]
    public void Initiator_and_responder_derive_the_same_session_secret()
    {
        var alice = NewVault("alice");
        var bob = NewVault("bob");
        var bundle = bob.PublicBundle();
        var opk = bundle.OneTimePreKeys[0];
        var context = "participant-context"u8.ToArray();

        Assert.True(alice.TryX3dhInitiate(
            bundle.X25519Pub, bundle.Ed25519Pub, bundle.X25519Sig,
            bundle.SignedPreKey.PublicKey, bundle.SignedPreKey.Signature!,
            opk.PublicKey, opk.KeyId, context,
            out var aliceSk, out var ekPub, out var usedOpk));
        Assert.Equal(opk.KeyId, usedOpk);

        Assert.True(bob.TryX3dhRespond(
            alice.PublicBundle().X25519Pub, ekPub, bundle.SignedPreKey.KeyId, usedOpk, context, out var bobSk));

        Assert.Equal(aliceSk, bobSk);
        Assert.Equal(32, aliceSk.Length);
    }

    [Fact]
    public void Agreement_works_without_a_one_time_prekey()
    {
        var alice = NewVault("alice");
        var bob = NewVault("bob");
        var bundle = bob.PublicBundle();
        var context = "ctx"u8.ToArray();

        Assert.True(alice.TryX3dhInitiate(
            bundle.X25519Pub, bundle.Ed25519Pub, bundle.X25519Sig,
            bundle.SignedPreKey.PublicKey, bundle.SignedPreKey.Signature!,
            null, 0, context, out var aliceSk, out var ekPub, out var usedOpk));
        Assert.Equal(0, usedOpk);

        Assert.True(bob.TryX3dhRespond(
            alice.PublicBundle().X25519Pub, ekPub, bundle.SignedPreKey.KeyId, 0, context, out var bobSk));
        Assert.Equal(aliceSk, bobSk);
    }

    [Fact]
    public void Initiate_fails_when_the_identity_binding_signature_is_tampered()
    {
        var alice = NewVault("alice");
        var bob = NewVault("bob");
        var bundle = bob.PublicBundle();
        var opk = bundle.OneTimePreKeys[0];
        var badSig = (byte[])bundle.X25519Sig.Clone();
        badSig[0] ^= 0xFF;

        // A server that swapped the DH key while keeping the Ed key must fail the binding check.
        Assert.False(alice.TryX3dhInitiate(
            bundle.X25519Pub, bundle.Ed25519Pub, badSig,
            bundle.SignedPreKey.PublicKey, bundle.SignedPreKey.Signature!,
            opk.PublicKey, opk.KeyId, "ctx"u8.ToArray(), out _, out _, out _));
    }

    [Fact]
    public void A_mismatched_context_yields_a_different_session_secret()
    {
        var alice = NewVault("alice");
        var bob = NewVault("bob");
        var bundle = bob.PublicBundle();
        var opk = bundle.OneTimePreKeys[0];

        alice.TryX3dhInitiate(
            bundle.X25519Pub, bundle.Ed25519Pub, bundle.X25519Sig,
            bundle.SignedPreKey.PublicKey, bundle.SignedPreKey.Signature!,
            opk.PublicKey, opk.KeyId, "context-A"u8.ToArray(), out var aliceSk, out var ekPub, out var usedOpk);
        bob.TryX3dhRespond(
            alice.PublicBundle().X25519Pub, ekPub, bundle.SignedPreKey.KeyId, usedOpk, "context-B"u8.ToArray(), out var bobSk);

        Assert.NotEqual(aliceSk, bobSk);
    }

    [Fact]
    public void Respond_rejects_an_unknown_signed_prekey_id()
    {
        var alice = NewVault("alice");
        var bob = NewVault("bob");
        var bundle = bob.PublicBundle();
        var opk = bundle.OneTimePreKeys[0];

        alice.TryX3dhInitiate(
            bundle.X25519Pub, bundle.Ed25519Pub, bundle.X25519Sig,
            bundle.SignedPreKey.PublicKey, bundle.SignedPreKey.Signature!,
            opk.PublicKey, opk.KeyId, "ctx"u8.ToArray(), out _, out var ekPub, out var usedOpk);

        Assert.False(bob.TryX3dhRespond(
            alice.PublicBundle().X25519Pub, ekPub, spkId: 9999, usedOpk, "ctx"u8.ToArray(), out _));
    }
}
