using System.Security.Cryptography;

namespace Eikon.Crypto;

// Windows DPAPI (CurrentUser scope). Used to OS-seal already-encrypted blobs at rest, so a stolen
// file cannot be unwrapped on another machine/account even before the passphrase layer. Dalamud is
// Windows-only, so this is always available.
internal static class Dpapi
{
    private static readonly byte[] Entropy = "eikon.v1"u8.ToArray();

    public static byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
