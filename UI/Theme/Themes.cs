namespace Eikon.UI.Theme;

internal enum ThemeCategory
{
    Editorial,
    Color,
    Pride,
}

// One selectable theme: the resolved palette it applies, plus the picker metadata (name, tag, the four
// preview swatches, and a flag stripe for pride themes). Editorial themes set explicit tokens; color and
// pride themes derive a hue-tinted dark palette so the whole UI shifts, not just the accent.
internal sealed record ThemeDef(
    string Id,
    string Name,
    string Tag,
    ThemeCategory Category,
    Palette.Colors Colors,
    IReadOnlyList<Vector4> Swatches,
    IReadOnlyList<Vector4> Stripes);

// The theme catalog. Tokens are authored in OKLCH (converted in Palette.Oklch), matching the design
// source; the four editorial themes are explicit and the twelve colors / six flags share the tinted-dark
// recipe. Editorial dark is kept byte-identical to the shipped palette.
internal static class Themes
{
    public static readonly IReadOnlyList<ThemeDef> All = Build();

    public static ThemeDef ById(string? id) =>
        All.FirstOrDefault(t => t.Id == id) ?? All[0];

    public static IEnumerable<ThemeDef> InCategory(ThemeCategory c) => All.Where(t => t.Category == c);

    private static IReadOnlyList<ThemeDef> Build()
    {
        var list = new List<ThemeDef>
        {
            Editorial("editorial-dark", "Editorial dark", "Default", EditorialDark(), 0x231F1B, 0x2B2723, 0xF4ECD8, 0xE9C98A),
            Editorial("paper-light", "Paper light", "Daytime", PaperLight(), 0xF6F1E6, 0xE8E0CF, 0x1A1613, 0x8A6A2C),
            Editorial("astral", "Astral", "Umbral · Astral", Astral(), 0x0F1420, 0x1A2236, 0xE6ECF7, 0x7BA7FF),
            Editorial("gilded", "Gilded", "Ishgard", Gilded(), 0x161514, 0x242120, 0xEFE6D2, 0xC9A24A),

            Color("sky", "Sky", 0x5CB8FF, 235f, 0.80f, 0.13f),
            Color("cyan", "Cyan", 0x22D3EE, 210f, 0.82f, 0.13f),
            Color("blue", "Blue", 0x5C7CFA, 265f, 0.68f, 0.17f),
            Color("violet", "Violet", 0x8B5CF6, 295f, 0.68f, 0.19f),
            Color("magenta", "Magenta", 0xD946EF, 320f, 0.70f, 0.24f),
            Color("pink", "Pink", 0xEC4899, 350f, 0.72f, 0.22f),
            Color("red", "Red", 0xEF4444, 25f, 0.68f, 0.22f),
            Color("orange", "Orange", 0xF97316, 50f, 0.74f, 0.18f),
            Color("amber", "Amber", 0xF59E0B, 75f, 0.80f, 0.16f),
            Color("green", "Green", 0x10B981, 160f, 0.75f, 0.16f),
            Color("teal", "Teal", 0x14B8A6, 185f, 0.75f, 0.13f),
            Color("lime", "Lime", 0x84CC16, 130f, 0.82f, 0.19f),

            Pride("pride", "Pride", new uint[] { 0xE40303, 0xFF8C00, 0xFFED00, 0x008026, 0x004DFF, 0x750787 }, 0xE40303, 25f, 0.68f, 0.22f),
            Pride("mlm", "MLM", new uint[] { 0x078D70, 0x26CEAA, 0x98E8C1, 0xFFFFFF, 0x7BADE2, 0x5049CC, 0x3D1A78 }, 0x26CEAA, 175f, 0.78f, 0.14f),
            Pride("bisexual", "Bisexual", new uint[] { 0xD60270, 0xD60270, 0x9B4F96, 0x0038A8, 0x0038A8 }, 0xD60270, 340f, 0.62f, 0.22f),
            Pride("trans", "Trans", new uint[] { 0x5BCEFA, 0xF5A9B8, 0xFFFFFF, 0xF5A9B8, 0x5BCEFA }, 0xF5A9B8, 355f, 0.82f, 0.09f),
            Pride("ace", "Ace", new uint[] { 0x000000, 0xA3A3A3, 0xFFFFFF, 0x800080 }, 0xA3A3A3, 315f, 0.62f, 0.16f),
            Pride("nonbinary", "Non-binary", new uint[] { 0xFCF434, 0xFFFFFF, 0x9C59D1, 0x2C2C2C }, 0xFCF434, 100f, 0.90f, 0.17f),
        };
        return list;
    }

    private static ThemeDef Editorial(string id, string name, string tag, Palette.Colors colors, params uint[] swatchHex) =>
        new(id, name, tag, ThemeCategory.Editorial, colors, Hexes(swatchHex), Array.Empty<Vector4>());

    private static ThemeDef Color(string id, string name, uint hex, float hue, float accentL, float accentC)
    {
        var colors = TintedDark(hue, accentL, accentC);
        var swatches = new[] { colors.Bg, colors.Surface1, Palette.Rgb(0xF4ECD8), Palette.Rgb(hex) };
        return new(id, name, "Accent", ThemeCategory.Color, colors, swatches, Array.Empty<Vector4>());
    }

    private static ThemeDef Pride(string id, string name, uint[] stripe, uint accentHex, float hue, float accentL, float accentC)
    {
        var colors = TintedDark(hue, accentL, accentC);
        var swatches = new[] { colors.Bg, colors.Surface1, Palette.Rgb(0xF4ECD8), Palette.Rgb(accentHex) };
        return new(id, name, "Pride", ThemeCategory.Pride, colors, swatches, Hexes(stripe));
    }

    // A whole-UI hue-tinted dark palette: dark surfaces and hairlines lean toward the accent hue, the ink
    // gets a faint lean, and the signal is the accent. Mirrors the source's tintedDarkVars.
    private static Palette.Colors TintedDark(float hue, float accentL, float accentC)
    {
        var bg = Palette.Oklch(0.145f, 0.022f, hue);
        var textSec = Palette.Oklch(0.62f, 0.02f, hue);
        return new Palette.Colors(
            Bg: bg,
            Surface1: Palette.Oklch(0.180f, 0.028f, hue),
            Surface2: Palette.Oklch(0.220f, 0.034f, hue),
            Ink: Palette.Oklch(0.965f, 0.012f, hue),
            Signal: Palette.Oklch(accentL, accentC, hue),
            TextSecondary: textSec,
            TextMuted: Palette.Lerp(textSec, bg, 0.42f),
            Border: Palette.Oklch(0.85f, 0.06f, hue).WithAlpha(0.12f),
            BorderStrong: Palette.Oklch(0.85f, 0.08f, hue).WithAlpha(0.22f),
            IsLight: false);
    }

    // Kept byte-identical to the shipped editorial palette. A method (not a field) so it never reads as a
    // zero default during the All initializer above, which builds before any field below it.
    private static Palette.Colors EditorialDark() => new(
        Bg: Palette.Rgb(0x0E0C0A),
        Surface1: Palette.Rgb(0x0E0C0A),
        Surface2: Palette.Rgb(0x151210),
        Ink: Palette.Rgb(0xF3F0EA),
        Signal: Palette.Rgb(0xFCD999),
        TextSecondary: Palette.Rgb(0x837F7B),
        TextMuted: Palette.Rgb(0x56534E),
        Border: new Vector4(1f, 1f, 1f, 0.09f),
        BorderStrong: new Vector4(1f, 1f, 1f, 0.16f),
        IsLight: false);

    private static Palette.Colors PaperLight()
    {
        var bg = Palette.Oklch(0.965f, 0.012f, 85f);
        var textSec = Palette.Oklch(0.42f, 0.012f, 65f);
        return new Palette.Colors(
            Bg: bg,
            Surface1: Palette.Oklch(0.935f, 0.014f, 85f),
            Surface2: Palette.Oklch(0.905f, 0.016f, 85f),
            Ink: Palette.Oklch(0.18f, 0.010f, 60f),
            Signal: Palette.Oklch(0.55f, 0.13f, 65f),
            TextSecondary: textSec,
            TextMuted: Palette.Lerp(textSec, bg, 0.40f),
            Border: new Vector4(0f, 0f, 0f, 0.10f),
            BorderStrong: new Vector4(0f, 0f, 0f, 0.20f),
            IsLight: true);
    }

    private static Palette.Colors Astral()
    {
        var bg = Palette.Oklch(0.16f, 0.03f, 265f);
        var textSec = Palette.Oklch(0.60f, 0.008f, 70f);
        return new(
            bg,
            Palette.Oklch(0.20f, 0.035f, 265f),
            Palette.Oklch(0.24f, 0.04f, 265f),
            Palette.Oklch(0.96f, 0.015f, 250f),
            Palette.Oklch(0.78f, 0.14f, 250f),
            textSec,
            Palette.Lerp(textSec, bg, 0.42f),
            new Vector4(1f, 1f, 1f, 0.09f),
            new Vector4(1f, 1f, 1f, 0.16f),
            false);
    }

    private static Palette.Colors Gilded()
    {
        var bg = Palette.Oklch(0.145f, 0.006f, 40f);
        var textSec = Palette.Oklch(0.60f, 0.008f, 70f);
        return new(
            bg,
            Palette.Oklch(0.175f, 0.007f, 40f),
            Palette.Oklch(0.205f, 0.008f, 40f),
            Palette.Oklch(0.955f, 0.008f, 85f),
            Palette.Oklch(0.78f, 0.12f, 82f),
            textSec,
            Palette.Lerp(textSec, bg, 0.42f),
            new Vector4(1f, 1f, 1f, 0.09f),
            new Vector4(1f, 1f, 1f, 0.16f),
            false);
    }

    private static IReadOnlyList<Vector4> Hexes(uint[] hexes)
    {
        var list = new Vector4[hexes.Length];
        for (var i = 0; i < hexes.Length; i++)
            list[i] = Palette.Rgb(hexes[i]);
        return list;
    }
}
