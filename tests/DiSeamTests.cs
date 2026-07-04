using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Eikon.Tests;

// The path seams (MessageCrypto sessionPath, IdentityService pinsPath, KeyVault vaultPath) are optional
// ctor args with default values; the production container registers no string, so it must fall back to
// the default at resolve time. This pins that Microsoft.Extensions.DependencyInjection behavior, so a
// regression (or an accidental removal of a default) surfaces here rather than only at plugin startup.
public class DiSeamTests
{
    private sealed class NeedsDefault
    {
        public string Value { get; }

        // Mirrors the crypto ctors: one resolvable dependency plus an unregistered optional with a default.
        public NeedsDefault(IFormatProvider resolvable, string optional = "fallback") => this.Value = optional;
    }

    [Fact]
    public void Container_uses_default_values_for_unregistered_ctor_parameters()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IFormatProvider>(CultureInfo.InvariantCulture)
            .AddSingleton<NeedsDefault>()
            .BuildServiceProvider();

        Assert.Equal("fallback", provider.GetRequiredService<NeedsDefault>().Value);
    }
}
