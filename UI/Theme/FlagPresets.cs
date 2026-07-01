namespace Eikon.UI.Theme;

// The pride-flag themes, keyed by a short stable id persisted in Configuration.ThemeId. Kept separate
// from the frozen twelve-solid AccentPresets.All. Each spec assigns a hue to the Primary (solid fills),
// Secondary (marks / strokes) and Tertiary (tint washes) roles, plus the full ordered flag stripe list
// used only for the swatch and the flag bar. Role hues never use a flag's neutral white/black/grey
// stripes (those would be illegible as fills or marks) — neutrals appear only in Stripes. See the plan:
// we-need-to-do-fluttering-sutherland.md.
internal static class FlagPresets
{
    public static readonly IReadOnlyList<ThemeSpec> All = new[]
    {
        // Pride keeps a violet fill so it never collides with the fixed red Danger button; red is
        // demoted to the Secondary marks, blue to the Tertiary wash.
        new ThemeSpec("pride", "Pride",
            PrimaryHue: Palette.Rgb(0x750787), SecondaryHue: Palette.Rgb(0xE40303), TertiaryHue: Palette.Rgb(0x004DFF),
            TintHue: Palette.Rgb(0x750787),
            Stripes: Bands(0xE40303, 0xFF8C00, 0xFFED00, 0x008026, 0x004DFF, 0x750787),
            NearestSolidIndex: 3),   // Violet

        new ThemeSpec("mlm", "MLM",
            PrimaryHue: Palette.Rgb(0x078D70), SecondaryHue: Palette.Rgb(0x5049CC), TertiaryHue: Palette.Rgb(0x26CEAA),
            TintHue: Palette.Rgb(0x078D70),
            Stripes: Bands(0x078D70, 0x26CEAA, 0x98E8C1, 0xFFFFFF, 0x7BADE2, 0x5049CC, 0x3D1A78),
            NearestSolidIndex: 10),  // Teal

        new ThemeSpec("bi", "Bisexual",
            PrimaryHue: Palette.Rgb(0xD60270), SecondaryHue: Palette.Rgb(0x0038A8), TertiaryHue: Palette.Rgb(0x9B4F96),
            TintHue: Palette.Rgb(0xD60270),
            Stripes: Bands(0xD60270, 0x9B4F96, 0x0038A8),
            NearestSolidIndex: 5),   // Pink

        // Trans has two usable hues; Tertiary shares the Secondary pink. White shows only in Stripes.
        new ThemeSpec("trans", "Trans",
            PrimaryHue: Palette.Rgb(0x5BCEFA), SecondaryHue: Palette.Rgb(0xF5A9B8), TertiaryHue: Palette.Rgb(0xF5A9B8),
            TintHue: Palette.Rgb(0x5BCEFA),
            Stripes: Bands(0x5BCEFA, 0xF5A9B8, 0xFFFFFF, 0xF5A9B8, 0x5BCEFA),
            NearestSolidIndex: 1),   // Sky

        // Ace has one usable hue; all three roles take the purple, neutrals stay decorative in Stripes.
        new ThemeSpec("ace", "Ace",
            PrimaryHue: Palette.Rgb(0x800080), SecondaryHue: Palette.Rgb(0x800080), TertiaryHue: Palette.Rgb(0x800080),
            TintHue: Palette.Rgb(0x800080),
            Stripes: Bands(0x000000, 0xA3A3A3, 0xFFFFFF, 0x800080),
            NearestSolidIndex: 3),   // Violet

        // Non-binary: purple drives fills and marks; the bright yellow only tints Tertiary washes.
        new ThemeSpec("nb", "Non-binary",
            PrimaryHue: Palette.Rgb(0x9C59D1), SecondaryHue: Palette.Rgb(0x9C59D1), TertiaryHue: Palette.Rgb(0xFCF434),
            TintHue: Palette.Rgb(0x9C59D1),
            Stripes: Bands(0xFCF434, 0xFFFFFF, 0x9C59D1, 0x2C2C2C),
            NearestSolidIndex: 3),   // Violet
    };

    public static ThemeSpec? ById(string id) => All.FirstOrDefault(t => t.Id == id);

    private static IReadOnlyList<Vector4> Bands(params uint[] hexes)
    {
        var list = new Vector4[hexes.Length];
        for (var i = 0; i < hexes.Length; i++)
            list[i] = Palette.Rgb(hexes[i]);
        return list;
    }
}
