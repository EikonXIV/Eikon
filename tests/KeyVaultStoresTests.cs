using Eikon.Crypto;
using Xunit;

namespace Eikon.Tests;

// A new or reset identity re-seals under a different vault key, so the vault-sealed local stores
// (ratchet sessions, decrypted threads, identity pins) left behind by a prior identity are unreadable
// and their owners fail closed. CreateIdentity and Reset must clear them so those services re-TOFU /
// re-establish cleanly instead of refusing every peer. Uses temp-dir vaults (the vaultPath seam).
public class KeyVaultStoresTests : IDisposable
{
    private static readonly string[] Stores =
        { "pins.bin", "pins.bin.bak", "sessions.bin", "sessions.bin.bak", "threads.bin", "threads.bin.bak" };

    private readonly string dir = Path.Combine(Path.GetTempPath(), "eikon-vault-" + Guid.NewGuid().ToString("N"));

    public KeyVaultStoresTests() => Directory.CreateDirectory(this.dir);

    public void Dispose()
    {
        try { Directory.Delete(this.dir, recursive: true); } catch { /* best effort */ }
    }

    private void SeedStores()
    {
        foreach (var name in Stores)
            File.WriteAllText(Path.Combine(this.dir, name), "stale-from-a-prior-identity");
    }

    private void AssertStoresCleared()
    {
        foreach (var name in Stores)
            Assert.False(File.Exists(Path.Combine(this.dir, name)), name + " should have been cleared");
    }

    [Fact]
    public void CreateIdentity_clears_stores_orphaned_by_a_prior_identity()
    {
        var vaultPath = Path.Combine(this.dir, "vault.json");
        this.SeedStores();

        new KeyVault(vaultPath).CreateIdentity("pw");

        this.AssertStoresCleared();
    }

    [Fact]
    public void Reset_clears_the_vault_and_every_dependent_store_including_pins()
    {
        var vaultPath = Path.Combine(this.dir, "vault.json");
        var vault = new KeyVault(vaultPath);
        vault.CreateIdentity("pw");
        Assert.True(File.Exists(vaultPath));
        this.SeedStores();

        vault.Reset();

        Assert.False(File.Exists(vaultPath), "vault.json should have been deleted");
        this.AssertStoresCleared();
    }
}
