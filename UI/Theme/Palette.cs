namespace Eikon.UI.Theme;

// Themeable editorial palette. The token colors (surfaces, ink, signal, hairlines) are set by
// ThemeService.Apply for the active theme; the default below is the warm editorial dark, kept
// pixel-identical to the shipped look. Light themes flip the hairline and the overlay ink so borders
// and hover washes read on a light surface. Design tokens are authored in OKLCH (see the color themes)
// and converted to sRGB here.
internal static class Palette
{
    // ---- themed tokens (set by Apply; defaults = editorial dark) ----
    public static Vector4 Bg = Rgb(0x0E0C0A);         // paper / the window fill
    public static Vector4 Surface1 = Rgb(0x0E0C0A);   // bars / panels (split by hairlines, not fill)
    public static Vector4 Surface2 = Rgb(0x151210);   // tiles, cards, inputs
    public static Vector4 Ink = Rgb(0xF3F0EA);        // primary text
    public static Vector4 Signal = Rgb(0xFCD999);     // the single restrained accent
    public static Vector4 TextSecondary = Rgb(0x837F7B);
    public static Vector4 TextMuted = Rgb(0x56534E);
    public static Vector4 Border = new(1f, 1f, 1f, 0.09f);        // hairline
    public static Vector4 BorderStrong = new(1f, 1f, 1f, 0.16f);  // hairline-strong
    public static bool IsLight;

    // Aliases that track the themed tokens: primary text is the ink, and text on the accent (or the page
    // behind the window) is the paper, which equals the background.
    public static Vector4 TextPrimary => Ink;
    public static Vector4 Paper => Bg;

    // Ink used for hover washes / pressed fills that sit on a surface: white on a dark theme, near-black
    // on a light one, so a small-alpha wash reads as a subtle lift either way.
    public static Vector4 Overlay => IsLight ? Black : White;

    // ---- fixed tokens (not themed) ----
    public static readonly Vector4 Scrim = new(0f, 0f, 0f, 0.55f);   // over photos, dark on every theme
    public static readonly Vector4 Danger = Rgb(0xF4514F);
    public static readonly Vector4 DangerFill = Rgb(0xC23E3C);
    public static readonly Vector4 Online = Rgb(0x5FD37F);
    public static readonly Vector4 Duty = Rgb(0xF5B75B);
    public static readonly Vector4 Afk = Rgb(0x73716E);
    public static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 Black = new(0f, 0f, 0f, 1f);

    // The resolved sRGB tokens for one theme. ThemeService builds these (from OKLCH) and applies them.
    public readonly record struct Colors(
        Vector4 Bg, Vector4 Surface1, Vector4 Surface2, Vector4 Ink, Vector4 Signal,
        Vector4 TextSecondary, Vector4 TextMuted, Vector4 Border, Vector4 BorderStrong, bool IsLight);

    public static void Apply(in Colors c)
    {
        Bg = c.Bg;
        Surface1 = c.Surface1;
        Surface2 = c.Surface2;
        Ink = c.Ink;
        Signal = c.Signal;
        TextSecondary = c.TextSecondary;
        TextMuted = c.TextMuted;
        Border = c.Border;
        BorderStrong = c.BorderStrong;
        IsLight = c.IsLight;
    }

    public static Vector4 Rgb(uint rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        1f);

    // OKLCH -> sRGB (the design source authors tokens in OKLCH). L in 0..1, C chroma, H degrees.
    // OKLCH -> OKLab -> linear sRGB -> gamma sRGB, clamped to the gamut.
    public static Vector4 Oklch(float l, float c, float hDeg, float alpha = 1f)
    {
        var h = hDeg * (MathF.PI / 180f);
        var a = c * MathF.Cos(h);
        var b = c * MathF.Sin(h);

        var l_ = l + (0.3963377774f * a) + (0.2158037573f * b);
        var m_ = l - (0.1055613458f * a) - (0.0638541728f * b);
        var s_ = l - (0.0894841775f * a) - (1.2914855480f * b);
        var l3 = l_ * l_ * l_;
        var m3 = m_ * m_ * m_;
        var s3 = s_ * s_ * s_;

        var r = (+4.0767416621f * l3) - (3.3077115913f * m3) + (0.2309699292f * s3);
        var g = (-1.2684380046f * l3) + (2.6097574011f * m3) - (0.3413193965f * s3);
        var bl = (-0.0041960863f * l3) - (0.7034186147f * m3) + (1.7076147010f * s3);

        return new Vector4(ToSrgb(r), ToSrgb(g), ToSrgb(bl), alpha);
    }

    private static float ToSrgb(float lin)
    {
        lin = Math.Clamp(lin, 0f, 1f);
        return lin <= 0.0031308f ? 12.92f * lin : (1.055f * MathF.Pow(lin, 1f / 2.4f)) - 0.055f;
    }

    public static Vector4 WithAlpha(this Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) => new(
        a.X + ((b.X - a.X) * t),
        a.Y + ((b.Y - a.Y) * t),
        a.Z + ((b.Z - a.Z) * t),
        a.W + ((b.W - a.W) * t));

    public static uint U32(this Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public static float Luminance(Vector4 c) => (0.299f * c.X) + (0.587f * c.Y) + (0.114f * c.Z);

    // Marks drawn straight on the bg need a minimum contrast; a hue too close to the bg is blended toward
    // the ink until it clears the floor. Uses the live Bg/Ink so it works on light themes too.
    private const float MarkContrastFloor = 3.0f;

    public static Vector4 LiftForDark(Vector4 hue)
    {
        var target = IsLight ? Black : White;
        for (var t = 0f; t <= 1f; t += 0.1f)
        {
            var lifted = Lerp(hue, target, t);
            if (Contrast(Luminance(lifted), Luminance(Bg)) >= MarkContrastFloor)
                return lifted;
        }

        return target;
    }

    private static float Contrast(float l1, float l2)
    {
        var hi = MathF.Max(l1, l2);
        var lo = MathF.Min(l1, l2);
        return (hi + 0.05f) / (lo + 0.05f);
    }

    // Kept so ThemeService's existing call site compiles; theming now flows through Apply.
    public static void Retint(Vector4 accent)
    {
    }
}
