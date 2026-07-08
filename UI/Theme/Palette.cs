namespace Eikon.UI.Theme;

// Warm editorial dark palette, ported from the Lovable prototype (src/styles.css). A fixed warm
// near-black "paper", warm off-white "ink", and one restrained cream-gold "signal" used sparingly.
// Backgrounds are fixed (no per-accent retint); the single accent resolves through ThemeService.
// OKLCH design tokens were converted to sRGB once (see the port notes).
internal static class Palette
{
    // Fixed editorial surfaces.
    public static Vector4 Bg { get; } = Rgb(0x0E0C0A);        // panel — the window fill
    public static Vector4 Surface1 { get; } = Rgb(0x0E0C0A);  // bars / panels (split from the window by hairlines, not fill)
    public static Vector4 Surface2 { get; } = Rgb(0x151210);  // tiles, cards, inputs

    public static readonly Vector4 Paper = Rgb(0x0A0806);     // page behind the window (kept for parity; unused in-game)
    public static readonly Vector4 Ink = Rgb(0xF3F0EA);
    public static readonly Vector4 Signal = Rgb(0xFCD999);    // the single restrained accent

    public static readonly Vector4 Border = new(1f, 1f, 1f, 0.09f);        // hairline
    public static readonly Vector4 BorderStrong = new(1f, 1f, 1f, 0.16f);  // hairline-strong

    public static readonly Vector4 TextPrimary = Rgb(0xF3F0EA);    // ink
    public static readonly Vector4 TextSecondary = Rgb(0x837F7B);  // muted-foreground
    public static readonly Vector4 TextMuted = Rgb(0x56534E);      // dimmer hints / inactive

    public static readonly Vector4 Scrim = new(0f, 0f, 0f, 0.55f);

    public static readonly Vector4 Danger = Rgb(0xF4514F);      // destructive
    public static readonly Vector4 DangerFill = Rgb(0xC23E3C);

    // Presence (hardcoded in the prototype, not themeable).
    public static readonly Vector4 Online = Rgb(0x5FD37F);
    public static readonly Vector4 Duty = Rgb(0xF5B75B);
    public static readonly Vector4 Afk = Rgb(0x73716E);

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

    // Marks drawn straight on the dark bg need a minimum contrast; a too-dark hue is blended toward
    // white just until it clears the floor. Light hues (the signal) clear it and return unchanged.
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

    // Editorial backgrounds are fixed. Retint is a deliberate no-op so ThemeService's existing call
    // site stays unchanged while the single accent stops tinting the surfaces.
    public static void Retint(Vector4 accent)
    {
    }
}
