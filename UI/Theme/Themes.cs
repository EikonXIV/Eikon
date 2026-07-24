namespace Eikon.UI.Theme;

internal enum ThemeCategory
{
    Editorial,
    Color,
    Pride,
}

// One selectable theme: the resolved palette it applies, plus the picker metadata (name, tag, the four
// preview swatches, and a stripe for flags and blends). Editorial themes set explicit tokens; colors,
// blends and flags derive a hue-tinted dark palette so the whole UI shifts, not just the accent.
// Group is the picker section it sits under. Mark and Wash are optional second and third hues: a blend
// drives its accent roles from all three, everything else derives them from the one signal.
internal sealed record ThemeDef(
    string Id,
    string Name,
    string Tag,
    string Group,
    ThemeCategory Category,
    Palette.Colors Colors,
    IReadOnlyList<Vector4> Swatches,
    IReadOnlyList<Vector4> Stripes,
    Vector4? Mark = null,
    Vector4? Wash = null);

// The theme catalog. Tokens are authored in OKLCH (converted in Palette.Oklch); solid and blend themes
// are authored as the hex they were designed as and converted back with Palette.OklchOf, so the accent
// renders as the picked color rather than a hand-tuned approximation. Editorial dark is kept
// byte-identical to the shipped palette.
internal static class Themes
{
    public const string GroupEditorial = "Editorial";
    public const string GroupClassic = "Classic";
    public const string GroupPastels = "Pastels";
    public const string GroupMetallics = "Metallics";
    public const string GroupNeons = "Neons";
    public const string GroupEarth = "Earth & muted";
    public const string GroupJewel = "Jewel tones";
    public const string GroupBlends = "Blends";
    public const string GroupPride = "Pride flags";

    // Picker section order.
    public static readonly IReadOnlyList<string> Groups = new[]
    {
        GroupEditorial, GroupClassic, GroupPastels, GroupMetallics,
        GroupNeons, GroupEarth, GroupJewel, GroupBlends, GroupPride,
    };

    public static readonly IReadOnlyList<ThemeDef> All = Build();

    public static ThemeDef ById(string? id) =>
        All.FirstOrDefault(t => t.Id == id) ?? All[0];

    public static IEnumerable<ThemeDef> InGroup(string group) => All.Where(t => t.Group == group);

    private static IReadOnlyList<ThemeDef> Build()
    {
        var list = new List<ThemeDef>
        {
            Editorial("editorial-dark", "Editorial dark", "Default", EditorialDark(), 0x231F1B, 0x2B2723, 0xF4ECD8, 0xE9C98A),
            Editorial("paper-light", "Paper light", "Daytime", PaperLight(), 0xF6F1E6, 0xE8E0CF, 0x1A1613, 0x8A6A2C),
            Editorial("astral", "Astral", "Umbral · Astral", Astral(), 0x0F1420, 0x1A2236, 0xE6ECF7, 0x7BA7FF),
            Editorial("gilded", "Gilded", "Ishgard", Gilded(), 0x161514, 0x242120, 0xEFE6D2, 0xC9A24A),

            Color("sky", "Sky", GroupClassic, 0x5CB8FF),
            Color("cyan", "Cyan", GroupClassic, 0x22D3EE),
            Color("blue", "Blue", GroupClassic, 0x5C7CFA),
            Color("violet", "Violet", GroupClassic, 0x8B5CF6),
            Color("magenta", "Magenta", GroupClassic, 0xD946EF),
            Color("pink", "Pink", GroupClassic, 0xEC4899),
            Color("red", "Red", GroupClassic, 0xEF4444),
            Color("orange", "Orange", GroupClassic, 0xF97316),
            Color("amber", "Amber", GroupClassic, 0xF59E0B),
            Color("green", "Green", GroupClassic, 0x10B981),
            Color("teal", "Teal", GroupClassic, 0x14B8A6),
            Color("lime", "Lime", GroupClassic, 0x84CC16),

            Color("powder", "Powder", GroupPastels, 0xA7C7F0),
            Color("mint", "Mint", GroupPastels, 0xA9E5C9),
            Color("lilac", "Lilac", GroupPastels, 0xC9B8F2),
            Color("peach", "Peach", GroupPastels, 0xFBCBA4),
            Color("blush", "Blush", GroupPastels, 0xF4B8C6),
            Color("butter", "Butter", GroupPastels, 0xF1E3A0),

            Color("gold", "Gold", GroupMetallics, 0xD4AF37),
            Color("rose-gold", "Rose gold", GroupMetallics, 0xE4A392),
            Color("copper", "Copper", GroupMetallics, 0xC17A47),
            Color("bronze", "Bronze", GroupMetallics, 0xA9743B),
            Color("silver", "Silver", GroupMetallics, 0xB8C0CC),
            Color("gunmetal", "Gunmetal", GroupMetallics, 0x6C7A89),

            Color("laser", "Laser", GroupNeons, 0x1FF0E6),
            Color("neon-magenta", "Neon magenta", GroupNeons, 0xFF33C7),
            Color("acid", "Acid", GroupNeons, 0x4BFF7A),
            Color("volt", "Volt", GroupNeons, 0xEAFF33),
            Color("plasma", "Plasma", GroupNeons, 0xA64DFF),
            Color("ember", "Ember", GroupNeons, 0xFF5B2E),

            Color("terracotta", "Terracotta", GroupEarth, 0xC56B4A),
            Color("clay", "Clay", GroupEarth, 0xB4896A),
            Color("olive", "Olive", GroupEarth, 0x8E8B52),
            Color("moss", "Moss", GroupEarth, 0x6C8A57),
            Color("sand", "Sand", GroupEarth, 0xC9B78E),
            Color("rust", "Rust", GroupEarth, 0x9E5637),

            Color("emerald", "Emerald", GroupJewel, 0x0E8A5C),
            Color("sapphire", "Sapphire", GroupJewel, 0x2C63E0),
            Color("ruby", "Ruby", GroupJewel, 0xC61E52),
            Color("amethyst", "Amethyst", GroupJewel, 0x8E3FC4),
            Color("topaz", "Topaz", GroupJewel, 0xDDA019),
            Color("peacock", "Peacock", GroupJewel, 0x17879B),

            Blend("blend.sunset", "Sunset", 0xFF7A45, 0xFF3D8B, 0x7A4DFF),
            Blend("blend.ocean", "Ocean", 0x16A6C6, 0x2E6BE6, 0x5AD9C4),
            Blend("blend.aurora", "Aurora", 0x35D39B, 0x4DA8FF, 0x9D6BFF),
            Blend("blend.galaxy", "Galaxy", 0x6D5DF6, 0xB24DFF, 0xF45D9E),
            Blend("blend.cyberpunk", "Cyberpunk", 0xFF2CC6, 0x22E0FF, 0x7A3BFF),
            Blend("blend.wildfire", "Wildfire", 0xFF3D2E, 0xFF8A2A, 0xFFD23D),
            Blend("blend.vaporwave", "Vaporwave", 0xFF6AD5, 0x26D0F5, 0xB983FF),
            Blend("blend.toxic", "Toxic", 0xB6FF3C, 0x39FFB0, 0x1FE0E0),
            Blend("blend.mainframe", "Mainframe", 0x12F26A, 0x0AA85A, 0x8CFFB0),
            Blend("blend.venom", "Venom", 0x7CFF3C, 0x9C3BFF, 0x2BE08A),
            Blend("blend.bloodmoon", "Bloodmoon", 0xFF3B3B, 0xC21E3A, 0x6B0F2A),
            Blend("blend.frostbite", "Frostbite", 0x8FD8FF, 0xDFF4FF, 0x2E9BD6),
            Blend("blend.allagan", "Allagan", 0xE8C567, 0x2FD2C8, 0x8A6BFF),
            Blend("blend.void", "Void", 0x6A2BB5, 0xD14BC7, 0x2A1140),
            Blend("blend.sakura", "Sakura", 0xFFC2D8, 0xFFF0F3, 0xD98BA8),

            Pride("pride", "Pride", new uint[] { 0xE40303, 0xFF8C00, 0xFFED00, 0x008026, 0x004DFF, 0x750787 }, 0xE40303),
            Pride("mlm", "MLM", new uint[] { 0x078D70, 0x26CEAA, 0x98E8C1, 0xFFFFFF, 0x7BADE2, 0x5049CC, 0x3D1A78 }, 0x26CEAA),
            Pride("bisexual", "Bisexual", new uint[] { 0xD60270, 0xD60270, 0x9B4F96, 0x0038A8, 0x0038A8 }, 0xD60270),
            Pride("trans", "Trans", new uint[] { 0x5BCEFA, 0xF5A9B8, 0xFFFFFF, 0xF5A9B8, 0x5BCEFA }, 0xF5A9B8),
            Pride("ace", "Ace", new uint[] { 0x000000, 0xA3A3A3, 0xFFFFFF, 0x800080 }, 0x800080),
            Pride("nonbinary", "Non-binary", new uint[] { 0xFCF434, 0xFFFFFF, 0x9C59D1, 0x2C2C2C }, 0x9C59D1),
            Pride("pansexual", "Pansexual", new uint[] { 0xFF218C, 0xFFD800, 0x21B1FF }, 0xFF218C),
            Pride("aromantic", "Aromantic", new uint[] { 0x3DA542, 0xA7D379, 0xFFFFFF, 0xA9A9A9, 0x000000 }, 0x3DA542),
            Pride("genderfluid", "Genderfluid", new uint[] { 0xFF75A2, 0xFFFFFF, 0xBE18D6, 0x000000, 0x333EBD }, 0xBE18D6),
            Pride("agender", "Agender", new uint[] { 0x000000, 0xBCC4C7, 0xFFFFFF, 0xB7F684, 0xFFFFFF, 0xBCC4C7, 0x000000 }, 0xB7F684),
            Pride("bear", "Bear", new uint[] { 0x623804, 0xD56300, 0xFEDD63, 0xFEE6B8, 0xFFFFFF, 0x000000 }, 0xD56300),

            // Leather's flag carries a red heart over blue and black bars; the stripe keeps the bars and
            // the heart's red becomes the accent, so it reads as itself without a device to draw.
            Pride("leather", "Leather", new uint[] { 0x0000FF, 0x000000, 0x0000FF, 0x000000, 0xFFFFFF, 0x000000, 0x0000FF, 0x000000, 0x0000FF }, 0xE00000),
        };
        return list;
    }

    private static ThemeDef Editorial(string id, string name, string tag, Palette.Colors colors, params uint[] swatchHex) =>
        new(id, name, tag, GroupEditorial, ThemeCategory.Editorial, colors, Hexes(swatchHex), Array.Empty<Vector4>());

    private static ThemeDef Color(string id, string name, string group, uint hex)
    {
        var colors = TintedDark(hex);
        var swatches = new[] { colors.Bg, colors.Surface1, Palette.Rgb(0xF4ECD8), Palette.Rgb(hex) };
        return new(id, name, group, group, ThemeCategory.Color, colors, swatches, Array.Empty<Vector4>());
    }

    // A blend tints from its primary but keeps all three hues: the second drives the mark channel, the
    // third the wash, and the stripe shows the trio in the picker.
    private static ThemeDef Blend(string id, string name, uint primary, uint mark, uint wash)
    {
        var colors = TintedDark(primary);
        var swatches = new[] { colors.Bg, colors.Surface1, Palette.Rgb(0xF4ECD8), Palette.Rgb(primary) };
        return new(
            id, name, "Blend", GroupBlends, ThemeCategory.Color, colors, swatches,
            Hexes(new[] { primary, mark, wash }),
            Palette.Rgb(mark),
            Palette.Rgb(wash));
    }

    private static ThemeDef Pride(string id, string name, uint[] stripe, uint accentHex)
    {
        var colors = TintedDark(accentHex);
        var swatches = new[] { colors.Bg, colors.Surface1, Palette.Rgb(0xF4ECD8), Palette.Rgb(accentHex) };
        return new(id, name, "Pride", GroupPride, ThemeCategory.Pride, colors, swatches, Hexes(stripe));
    }

    // A whole-UI hue-tinted dark palette derived from the accent's own OKLCH: dark surfaces and
    // hairlines lean toward the accent hue, the ink gets a faint lean, and the signal is the accent as
    // designed. Mirrors the source's tintedDarkVars, but takes the hex so the swatch and the applied
    // accent cannot drift apart.
    private static Palette.Colors TintedDark(uint hex)
    {
        var (accentL, accentC, hue) = Palette.OklchOf(hex);
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
