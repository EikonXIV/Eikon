namespace Eikon.UI.Theme;

// A resolved theme recipe. A solid preset sets all three role hues to one color, so the engine
// produces the exact five tokens (and Retint) it always has. A flag theme assigns a different hue per
// role and carries the full ordered stripe list (1..7 colors) used only for the decorative swatch and
// the flag bar. TintHue drives Palette.Retint. NearestSolidIndex lets an older client that cannot read
// ThemeId fall back to a sensible solid.
internal sealed record ThemeSpec(
    string Id,
    string Name,
    Vector4 PrimaryHue,
    Vector4 SecondaryHue,
    Vector4 TertiaryHue,
    Vector4 TintHue,
    IReadOnlyList<Vector4> Stripes,
    int NearestSolidIndex)
{
    // A solid accent: every role derives from the one preset hue and there are no stripes (so no flag
    // bar shows), reproducing the original single-accent behavior exactly.
    public static ThemeSpec Solid(int index)
    {
        var count = AccentPresets.All.Count;
        var i = index < 0 || index >= count ? 0 : index;
        var hue = Palette.Rgb(AccentPresets.All[i].Rgb);
        return new ThemeSpec(
            Id: $"solid.{i}",
            Name: AccentPresets.All[i].Name,
            PrimaryHue: hue,
            SecondaryHue: hue,
            TertiaryHue: hue,
            TintHue: hue,
            Stripes: Array.Empty<Vector4>(),
            NearestSolidIndex: i);
    }
}
