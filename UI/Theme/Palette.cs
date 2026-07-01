namespace Eikon.UI.Theme;

// Base palette for the Eikon dark skin (DESIGN.md section 2). The text/overlay tokens are fixed,
// but the backgrounds are re-tinted toward the active accent at runtime (see Retint / ThemeService)
// so the whole skin shifts with the chosen color, not just the accent token.
internal static class Palette
{
    // Neutral dark base the runtime tint blends away from.
    private static readonly Vector4 BaseBg = Rgb(0x0D1320);
    private static readonly Vector4 BaseSurface1 = Rgb(0x141B29);
    private static readonly Vector4 BaseSurface2 = Rgb(0x1A2334);

    // How far the backgrounds are pulled toward the (darkened) accent. 0 = neutral, 1 = full accent.
    private const float TintStrength = 0.40f;

    public static Vector4 Bg { get; private set; } = BaseBg;
    public static Vector4 Surface1 { get; private set; } = BaseSurface1;
    public static Vector4 Surface2 { get; private set; } = BaseSurface2;
    public static readonly Vector4 Border = new(1f, 1f, 1f, 0.10f);
    public static readonly Vector4 TextPrimary = Rgb(0xE6EAF0);
    public static readonly Vector4 TextSecondary = Rgb(0x9AA4B2);
    public static readonly Vector4 TextMuted = Rgb(0x6B7585);
    public static readonly Vector4 Scrim = new(0f, 0f, 0f, 0.50f);
    public static readonly Vector4 Danger = Rgb(0xF26B7A);
    public static readonly Vector4 DangerFill = Rgb(0xC2384A);
    public static readonly Vector4 White = new(1f, 1f, 1f, 1f);

    public static Vector4 Rgb(uint rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        1f);

    public static Vector4 WithAlpha(this Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) => new(
        a.X + ((b.X - a.X) * t),
        a.Y + ((b.Y - a.Y) * t),
        a.Z + ((b.Z - a.Z) * t),
        a.W + ((b.W - a.W) * t));

    public static uint U32(this Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public static float Luminance(Vector4 c) => (0.299f * c.X) + (0.587f * c.Y) + (0.114f * c.Z);

    // Marks (dots, 1px strokes, small glyphs) drawn straight on the dark Bg need a minimum contrast
    // to stay visible. A dark, saturated flag hue (e.g. Bi blue, Ace purple) fails that against the
    // tinted dark background, so we blend it toward white just until it clears the floor. The twelve
    // solid-accent hues already clear it, so they return unchanged (t == 0) and stay pixel-identical.
    // Call after Retint — it reads the freshly tinted Bg.
    private const float MarkContrastFloor = 3.0f;

    public static Vector4 LiftForDark(Vector4 hue)
    {
        for (var t = 0f; t <= 1f; t += 0.1f)
        {
            var lifted = Lerp(hue, White, t);
            if (Contrast(Luminance(lifted), Luminance(Bg)) >= MarkContrastFloor)
                return lifted;
        }

        return White;
    }

    private static float Contrast(float l1, float l2)
    {
        var hi = MathF.Max(l1, l2);
        var lo = MathF.Min(l1, l2);
        return (hi + 0.05f) / (lo + 0.05f);
    }

    // Re-tint the themeable backgrounds toward the active accent. Each surface blends toward a
    // progressively brighter dark shade of the accent so depth is preserved, but everything stays
    // dark (we tint toward a darkened accent, never the bright token) so light text keeps its contrast.
    public static void Retint(Vector4 accent)
    {
        Bg = Lerp(BaseBg, Shade(accent, 0.16f), TintStrength);
        Surface1 = Lerp(BaseSurface1, Shade(accent, 0.24f), TintStrength);
        Surface2 = Lerp(BaseSurface2, Shade(accent, 0.32f), TintStrength);
    }

    private static Vector4 Shade(Vector4 c, float scale) => new(c.X * scale, c.Y * scale, c.Z * scale, 1f);
}
