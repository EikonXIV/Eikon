namespace Eikon.UI.Theme;

// One accent "role" resolved into the five shades a consumer might need. Built from a single hue with
// the exact derivation the theme has always used, so a solid preset (all three role channels sharing
// one hue) reproduces the old five flat tokens byte-for-byte. Flag themes give each role its own hue.
internal readonly record struct AccentChannel(
    Vector4 Base,   // the hue itself: marks, links, and the button-hover shade
    Vector4 Deep,   // darker solid fill (buttons, active toggle)
    Vector4 Tint,   // low-alpha wash (chip / badge backgrounds)
    Vector4 Text,   // bright readable text or icon on a Tint wash
    Vector4 On)     // text or icon on a solid Deep / Base fill
{
    // Derive the five shades from one hue. On reads Palette.Bg, so callers must Retint first.
    public static AccentChannel FromHue(Vector4 hue) => new(
        Base: hue,
        Deep: new Vector4(hue.X * 0.72f, hue.Y * 0.72f, hue.Z * 0.72f, 1f),
        Tint: hue.WithAlpha(0.18f),
        Text: Palette.Lerp(hue, Palette.White, 0.45f),
        On: Palette.Luminance(hue) > 0.6f ? Palette.Bg : Palette.White);
}
