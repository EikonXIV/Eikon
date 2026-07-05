using System.Numerics;
using Eikon.UI.Theme;
using Xunit;

namespace Eikon.Tests;

// The role-channel rework must leave the twelve solid accents pixel-identical. A solid ThemeSpec sets
// all three role channels to one hue, so the flat tokens the UI still reads (Accent / AccentDeep /
// AccentTint / AccentText / OnAccent) must match the original single-hue derivation exactly. This also
// proves Palette.LiftForDark is a no-op on every solid — otherwise the lifted Secondary / Tertiary
// would drift AccentTint / AccentText away from the legacy values.
//
// The test drives the pure theme types directly (ThemeSpec.Solid + AccentChannel.FromHue + Palette),
// replicating ThemeService.ApplySpec step for step. It deliberately avoids constructing ThemeService /
// Configuration: Configuration implements Dalamud's IPluginConfiguration, which can't load under
// `dotnet test`. The service just wires these pieces to the same-named getters.
public class ThemeParityTests
{
    [Fact]
    public void Solid_presets_reproduce_the_legacy_accent_tokens()
    {
        for (var i = 0; i < AccentPresets.All.Count; i++)
        {
            var spec = ThemeSpec.Solid(i);

            // Mirror ApplySpec: Retint first (On and LiftForDark read the tinted Bg), then derive.
            Palette.Retint(spec.TintHue);
            var primary = AccentChannel.FromHue(spec.PrimaryHue);
            var secondary = AccentChannel.FromHue(Palette.LiftForDark(spec.SecondaryHue));
            var tertiary = AccentChannel.FromHue(Palette.LiftForDark(spec.TertiaryHue));

            var baseColor = Palette.Rgb(AccentPresets.All[i].Rgb);
            var name = AccentPresets.All[i].Name;

            // Flat aliases: Accent/AccentDeep/OnAccent => Primary; AccentTint/AccentText => Tertiary.
            Assert.Equal(baseColor, primary.Base);
            Assert.Equal(new Vector4(baseColor.X * 0.72f, baseColor.Y * 0.72f, baseColor.Z * 0.72f, 1f), primary.Deep);
            Assert.Equal(baseColor.WithAlpha(0.18f), tertiary.Tint);
            Assert.Equal(Palette.Lerp(baseColor, Palette.White, 0.45f), tertiary.Text);

            // All three role channels collapse to the one hue for a solid (proves LiftForDark is a no-op).
            Assert.Equal(primary.Base, secondary.Base);
            Assert.Equal(primary.Base, tertiary.Base);

            // OnAccent picks dark text for light accents (Amber, Lime) and white otherwise. Compared by
            // luminance rather than the exact tinted Bg so the check is robust to shared static state.
            if (Palette.Luminance(baseColor) > 0.6f)
                Assert.True(Palette.Luminance(primary.On) < 0.3f, $"{name} should take dark on-accent text");
            else
                Assert.Equal(Palette.White, primary.On);

            // A solid carries no stripes, so no flag bar shows.
            Assert.Empty(spec.Stripes);
        }
    }

    [Fact]
    public void Flag_presets_are_multi_hue_with_bounded_stripes()
    {
        Assert.NotEmpty(FlagPresets.All);
        foreach (var flag in FlagPresets.All)
        {
            Assert.False(string.IsNullOrEmpty(flag.Id));
            Assert.Equal(flag, FlagPresets.ById(flag.Id));
            Assert.InRange(flag.Stripes.Count, 1, 7);
            Assert.InRange(flag.NearestSolidIndex, 0, AccentPresets.All.Count - 1);
        }
    }
}
