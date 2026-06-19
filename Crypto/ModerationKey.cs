namespace Eikon.Crypto;

// Trust anchors for the moderation seal key.
//
// The ROOT (Ed25519) key is the long-lived anchor, pinned here at compile time; rotating it is a
// client release. The server serves the rotatable X25519 SEAL key (the one report evidence is sealed
// to via Crypto.Seal) plus the root's signature over it. ModerationKeyService trusts a served seal key
// only if that signature verifies against this root AND its version is not older than the one shipped
// below, so a compromised server cannot substitute a key it controls.
//
// The Seal* values are the seal key shipped with this build; ModerationKeyService uses them as the
// offline fallback when /keys/moderation is unreachable. Private keys are never in this repo:
// moderation/genkeys.mjs writes them encrypted and held offline.
internal static class ModerationKey
{
    // Pinned root Ed25519 verify key (the anchor).
    public const string RootPublicBase64 = "Y7A27uv+jCmLvuHctAokM4bLK2sJ7U3QR8t0uVXwiaw=";

    // The seal key shipped with this build: X25519 public, the root signature over it, and its version.
    public const string SealPublicBase64 = "w+HPg4vyZW7lYkkwPLTSQ/U10aTizOxd+4vFKCGZyHs=";
    public const string SealSignatureBase64 = "fwKr3MnroAubzX94F/yVEPNJsQ5lSoO3IGnR2SVo67xk9BQjEcragWDpzuu0FDicWV7iVN8IrSPCyNS7YzOVDQ==";
    public const long SealVersion = 1;

    // Signed message layout: this tag || uint32be(version) || sealPublicKey(32). Must match
    // moderation/genkeys.mjs and rotate-seal.mjs.
    public static byte[] SignatureDomain => System.Text.Encoding.UTF8.GetBytes("eikon-moderation-seal-sig-v1");

    public static byte[] RootPublic => System.Convert.FromBase64String(RootPublicBase64);
}
