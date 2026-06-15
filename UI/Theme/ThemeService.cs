using Eikon.Config;

namespace Eikon.UI.Theme;

// Holds the active accent, derives its shades, and re-tints the shared background palette toward it
// so picking a color reskins the whole app (background and surfaces), not just the accent token.
// Persisted in config.
internal sealed class ThemeService
{
    private readonly Configuration config;

    public ThemeService(Configuration config)
    {
        this.config = config;
        this.Apply(config.AccentPresetIndex);
    }

    public int AccentIndex { get; private set; }

    public Vector4 Accent { get; private set; }     // online dots, links, active icons
    public Vector4 AccentDeep { get; private set; } // solid fills (buttons, active toggle)
    public Vector4 AccentTint { get; private set; } // chip backgrounds
    public Vector4 AccentText { get; private set; } // text or icon on a tinted chip
    public Vector4 OnAccent { get; private set; }   // text or icon on a solid accent fill

    public void SetAccent(int index)
    {
        if (index == this.AccentIndex)
            return;

        this.Apply(index);
        this.config.AccentPresetIndex = this.AccentIndex;
        this.config.Save();
    }

    private void Apply(int index)
    {
        var count = AccentPresets.All.Count;
        this.AccentIndex = index < 0 || index >= count ? 0 : index;

        var baseColor = Palette.Rgb(AccentPresets.All[this.AccentIndex].Rgb);
        this.Accent = baseColor;
        this.AccentDeep = new Vector4(baseColor.X * 0.72f, baseColor.Y * 0.72f, baseColor.Z * 0.72f, 1f);
        this.AccentTint = baseColor.WithAlpha(0.18f);
        this.AccentText = Palette.Lerp(baseColor, Palette.White, 0.45f);

        // Reskin the shared backgrounds toward this accent (kept dark, so text contrast holds).
        Palette.Retint(baseColor);

        // Light accents (amber, lime) need dark text for contrast; everything else takes white.
        this.OnAccent = Palette.Luminance(baseColor) > 0.6f ? Palette.Bg : Palette.White;
    }
}
