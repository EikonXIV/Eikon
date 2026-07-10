using Eikon.Contracts;
using Eikon.Crypto;
using Eikon.Net;
using Eikon.Tests.Fakes;
using Xunit;

namespace Eikon.Tests;

// End-to-end exercise of the message ratchet across two independent MessageCrypto instances (Alice and
// Bob), wired only through a StubApiClient (peer bundles/identity) and temp-dir vaults/sessions/pins.
// No Dalamud runtime, no network. These assert the invariants from MESSAGE-CRYPTO.md and
// SECURITY-REVIEW-RATCHET.md against the real production code paths (EncryptAsync/DecryptAsync).
public class RatchetRoundTripTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "eikon-rt-" + Guid.NewGuid().ToString("N"));
    private readonly StubApiClient api = new();
    private readonly NullLog log = new();

    public RatchetRoundTripTests() => Directory.CreateDirectory(this.root);

    public void Dispose()
    {
        try { Directory.Delete(this.root, recursive: true); } catch { /* best effort cleanup */ }
    }

    private sealed class Peer
    {
        public Guid Id;
        public KeyVault Vault = null!;
        public IdentityService Identity = null!;
        public MessageCrypto Crypto = null!;
        public string Dir = string.Empty;
    }

    // A fully wired peer: a fresh identity vault, its identity/session stores in a temp dir, and its
    // public bundle registered with the shared stub server under a random user id.
    private Peer NewPeer(string name)
    {
        var dir = Path.Combine(this.root, name);
        Directory.CreateDirectory(dir);
        var id = Guid.NewGuid();
        var vault = new KeyVault(Path.Combine(dir, "vault.json"));
        vault.CreateIdentity("pw-" + name);
        this.api.Set(id, vault);
        var identity = new IdentityService(vault, this.log, Path.Combine(dir, "pins.bin"));
        var crypto = new MessageCrypto(this.api, new StubTokenProvider(id), vault, identity, this.log, Path.Combine(dir, "sessions.bin"));
        return new Peer { Id = id, Vault = vault, Identity = identity, Crypto = crypto, Dir = dir };
    }

    private static EncryptedMessageDto Dto((string Ciphertext, string Header, string Nonce) m) =>
        new() { Ciphertext = m.Ciphertext, Header = m.Header, Nonce = m.Nonce };

    private static async Task<EncryptedMessageDto> Send(Peer from, Peer to, string text)
    {
        var m = await from.Crypto.EncryptAsync(to.Id, text, default);
        Assert.NotNull(m);
        return Dto(m!.Value);
    }

    private static string Tamper(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        bytes[^1] ^= 0xFF;
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public async Task Round_trips_messages_in_both_directions()
    {
        var alice = NewPeer("alice");
        var bob = NewPeer("bob");

        var m0 = await Send(alice, bob, "hey bob");
        Assert.Equal("hey bob", await bob.Crypto.DecryptAsync(alice.Id, m0, default));

        var r0 = await Send(bob, alice, "hey alice");
        Assert.Equal("hey alice", await alice.Crypto.DecryptAsync(bob.Id, r0, default));

        var m1 = await Send(alice, bob, "how are you");
        Assert.Equal("how are you", await bob.Crypto.DecryptAsync(alice.Id, m1, default));

        var r1 = await Send(bob, alice, "good, you?");
        Assert.Equal("good, you?", await alice.Crypto.DecryptAsync(bob.Id, r1, default));
    }

    [Fact]
    public async Task Delivers_out_of_order_messages()
    {
        var alice = NewPeer("alice");
        var bob = NewPeer("bob");

        var m0 = await Send(alice, bob, "establish");
        Assert.Equal("establish", await bob.Crypto.DecryptAsync(alice.Id, m0, default));

        var a1 = await Send(alice, bob, "one");
        var a2 = await Send(alice, bob, "two");
        var a3 = await Send(alice, bob, "three");

        // Arrive 3, 1, 2. The receive path caches the skipped keys and consumes them out of order.
        Assert.Equal("three", await bob.Crypto.DecryptAsync(alice.Id, a3, default));
        Assert.Equal("one", await bob.Crypto.DecryptAsync(alice.Id, a1, default));
        Assert.Equal("two", await bob.Crypto.DecryptAsync(alice.Id, a2, default));
    }

    [Fact]
    public async Task A_consumed_message_key_cannot_decrypt_again()
    {
        var alice = NewPeer("alice");
        var bob = NewPeer("bob");

        var m0 = await Send(alice, bob, "m0");
        Assert.Equal("m0", await bob.Crypto.DecryptAsync(alice.Id, m0, default));
        var m1 = await Send(alice, bob, "m1");
        Assert.Equal("m1", await bob.Crypto.DecryptAsync(alice.Id, m1, default));

        // The receive chain advanced past m0/m1 and wiped their keys; the one-way KDF means the current
        // session state cannot recover them. Replaying an already-consumed frame yields nothing
        // (forward secrecy at the receive path, and replay protection as a consequence).
        Assert.Null(await bob.Crypto.DecryptAsync(alice.Id, m0, default));
        Assert.Null(await bob.Crypto.DecryptAsync(alice.Id, m1, default));
    }

    [Fact]
    public async Task A_forged_frame_is_rejected_and_leaves_the_session_intact()
    {
        var alice = NewPeer("alice");
        var bob = NewPeer("bob");

        var m0 = await Send(alice, bob, "hello");
        Assert.Equal("hello", await bob.Crypto.DecryptAsync(alice.Id, m0, default));

        var real = await Send(alice, bob, "real message");
        var forged = new EncryptedMessageDto { Header = real.Header, Nonce = real.Nonce, Ciphertext = Tamper(real.Ciphertext) };

        // Verify-before-commit: the failed AEAD tag is rejected without advancing or poisoning the
        // receive chain, so the genuine frame at the same counter still decrypts afterward.
        Assert.Null(await bob.Crypto.DecryptAsync(alice.Id, forged, default));
        Assert.Equal("real message", await bob.Crypto.DecryptAsync(alice.Id, real, default));
    }

    [Fact]
    public async Task Session_survives_a_simulated_restart()
    {
        var alice = NewPeer("alice");
        var bob = NewPeer("bob");

        var m0 = await Send(alice, bob, "m0");
        Assert.Equal("m0", await bob.Crypto.DecryptAsync(alice.Id, m0, default));
        // Bob replies so Alice's session becomes established and her next frame is non-initial (a
        // non-initial frame can only be read from a persisted session, not re-established from scratch).
        var r0 = await Send(bob, alice, "r0");
        Assert.Equal("r0", await alice.Crypto.DecryptAsync(bob.Id, r0, default));

        // Bob "restarts": a fresh MessageCrypto over the same vault and sessions.bin path.
        var bobRestarted = new MessageCrypto(
            this.api, new StubTokenProvider(bob.Id), bob.Vault, bob.Identity, this.log, Path.Combine(bob.Dir, "sessions.bin"));

        var m1 = await Send(alice, bob, "after restart");
        Assert.False(MessageCrypto.IsInitialHeader(m1.Header));
        Assert.Equal("after restart", await bobRestarted.DecryptAsync(alice.Id, m1, default));
    }

    [Fact]
    public async Task A_changed_peer_identity_is_rejected_after_pinning()
    {
        var alice = NewPeer("alice");
        var bob = NewPeer("bob");

        var m0 = await Send(alice, bob, "hello");
        Assert.Equal("hello", await bob.Crypto.DecryptAsync(alice.Id, m0, default));   // Bob pins Alice (TOFU)

        // A malicious server swaps Alice's published identity for a different keypair under the same id.
        var impDir = Path.Combine(this.root, "imposter");
        Directory.CreateDirectory(impDir);
        var impVault = new KeyVault(Path.Combine(impDir, "vault.json"));
        impVault.CreateIdentity("pw-imposter");
        var impIdentity = new IdentityService(impVault, this.log, Path.Combine(impDir, "pins.bin"));
        var impCrypto = new MessageCrypto(
            this.api, new StubTokenProvider(alice.Id), impVault, impIdentity, this.log, Path.Combine(impDir, "sessions.bin"));
        this.api.Set(alice.Id, impVault);

        var forged = await impCrypto.EncryptAsync(bob.Id, "i am alice, trust me", default);
        Assert.NotNull(forged);
        // The served identity no longer matches Bob's pin, so the handshake is refused.
        Assert.Null(await bob.Crypto.DecryptAsync(alice.Id, Dto(forged!.Value), default));

        // The real Alice, restored, still talks to Bob over the untouched original session.
        this.api.Set(alice.Id, alice.Vault);
        var real = await Send(alice, bob, "still the real me");
        Assert.Equal("still the real me", await bob.Crypto.DecryptAsync(alice.Id, real, default));
    }

    // The recovery contract the phantom-unread fix depends on: when a peer reinstalls (new identity under
    // the same id), their greeting is undecryptable AND flagged as a mismatch (the signal the chat layer
    // reads to ACK the message instead of nagging forever). TOFU holds until the user re-verifies, and
    // only then does the reinstalled peer's message decrypt.
    [Fact]
    public async Task A_reinstalled_peer_recovers_only_after_the_pin_is_forgotten()
    {
        var alice = NewPeer("alice");
        var bob = NewPeer("bob");

        var m0 = await Send(alice, bob, "hello");
        Assert.Equal("hello", await bob.Crypto.DecryptAsync(alice.Id, m0, default));   // Bob pins Alice (TOFU)

        // Alice reinstalls: a brand-new identity + vault (new prekeys, no session) under the same user id.
        var reDir = Path.Combine(this.root, "alice-reinstalled");
        Directory.CreateDirectory(reDir);
        var reVault = new KeyVault(Path.Combine(reDir, "vault.json"));
        reVault.CreateIdentity("pw-alice-2");
        var reIdentity = new IdentityService(reVault, this.log, Path.Combine(reDir, "pins.bin"));
        var reAlice = new MessageCrypto(this.api, new StubTokenProvider(alice.Id), reVault, reIdentity, this.log, Path.Combine(reDir, "sessions.bin"));
        this.api.Set(alice.Id, reVault);

        var greeting = await reAlice.EncryptAsync(bob.Id, "it's me, i reinstalled", default);
        Assert.NotNull(greeting);
        var greetingDto = Dto(greeting!.Value);
        Assert.True(MessageCrypto.IsInitialHeader(greetingDto.Header));

        // Bob can't decrypt it, and crucially records the mismatch — this is exactly what OnMessage reads
        // to decide "ack (stop the endless redelivery), don't rekey".
        Assert.Null(await bob.Crypto.DecryptAsync(alice.Id, greetingDto, default));
        Assert.True(bob.Crypto.Mismatched(alice.Id));

        // TOFU holds: without an explicit re-verification the reinstalled identity stays rejected no matter
        // how often the frame is retried (no silent re-pin -> no silent MITM).
        Assert.Null(await bob.Crypto.DecryptAsync(alice.Id, greetingDto, default));

        // Bob reviews the safety number and accepts the change ("identity changed - tap to review").
        bob.Identity.ForgetPin(alice.Id);

        // Now, and only now, the reinstalled peer's greeting decrypts and the mismatch clears.
        Assert.Equal("it's me, i reinstalled", await bob.Crypto.DecryptAsync(alice.Id, greetingDto, default));
        Assert.False(bob.Crypto.Mismatched(alice.Id));
    }
}
