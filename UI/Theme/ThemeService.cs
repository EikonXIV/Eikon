using Eikon.Config;

namespace Eikon.UI.Theme;

// Holds the active theme and re-tints the shared background palette toward it, so picking a color (or
// a pride flag) reskins the whole app, not just the accent token. A theme resolves into three role
// channels — Primary (solid fills), Secondary (marks / strokes) and Tertiary (tint washes) — each a
// full AccentChannel. Solid presets set all three roles to one hue, reproducing the original single
// accent exactly; flag themes give each role a different hue and carry the flag's stripe list. The old
// flat tokens (Accent, AccentDeep, ...) survive as aliases so screens migrate to the roles one at a
// time. Persisted in config. See the plan: we-need-to-do-fluttering-sutherland.md.
internal sealed class ThemeService
{
    private readonly Configuration config;
    private ThemeSpec active = ThemeSpec.Solid(0);

    // The single warm-editorial theme (Lovable port): one restrained cream-gold signal across all
    // three role channels. Backgrounds are fixed in Palette; the preset/flag engine is left intact
    // but dormant for now.
    private static readonly ThemeSpec Editorial = new(
        "editorial", "Editorial",
        PrimaryHue: Palette.Signal, SecondaryHue: Palette.Signal, TertiaryHue: Palette.Signal,
        TintHue: Palette.Signal, Stripes: Array.Empty<Vector4>(), NearestSolidIndex: 0);

    public ThemeService(Configuration config)
    {
        this.config = config;

        // Warm-editorial is the single active theme for the port. The preset/flag engine stays intact
        // but dormant; SetAccent/SetTheme still function if the (now vestigial) picker is opened.
        this.ApplySpec(Editorial, null);
    }

    // Nearest solid index (the flag's fallback while a flag is active). Drives the solid picker's
    // selected-state, which additionally checks ThemeId is null so a flag never highlights a solid.
    public int AccentIndex { get; private set; }

    // Null for a solid accent; the flag id (e.g. "pride") when a flag theme is active.
    public string? ThemeId { get; private set; }

    // The three role channels. Migrated call-sites read these directly; everything else uses the aliases.
    public AccentChannel Primary { get; private set; }   // solid fills (buttons, toggle, my-message bubble)
    public AccentChannel Secondary { get; private set; } // marks / strokes (dots, links, stars, sliders)
    public AccentChannel Tertiary { get; private set; }  // tint washes (chips, empty state, pills)

    // Flat aliases kept for un-migrated call-sites. Accent maps to Primary (not Secondary) so the button
    // hover — which draws Accent over an AccentDeep fill — stays within the fill hue.
    public Vector4 Accent => this.Primary.Base;     // online dots, links, active icons (un-migrated)
    public Vector4 AccentDeep => this.Primary.Deep; // solid fills (buttons, active toggle)
    public Vector4 AccentTint => this.Tertiary.Tint; // chip backgrounds
    public Vector4 AccentText => this.Tertiary.Text; // text or icon on a tinted chip
    public Vector4 OnAccent => this.Primary.On;      // text or icon on a solid accent fill

    // The active theme's ordered stripe colors (1..7), empty for solids. Feeds the flag bar and swatch.
    public IReadOnlyList<Vector4> Stripes => this.active.Stripes;

    // The pride-flag themes offered by the picker.
    public IReadOnlyList<ThemeSpec> Flags => FlagPresets.All;

    // Display name of the active theme (solid preset name or flag name) for the settings preview.
    public string CurrentThemeName => this.active.Name;

    public void SetAccent(int index)
    {
        var count = AccentPresets.All.Count;
        var clamped = index < 0 || index >= count ? 0 : index;

        // Skip only when already sitting on this solid; a flag with a matching AccentIndex must still switch.
        if (this.ThemeId is null && clamped == this.AccentIndex)
            return;

        this.ApplySpec(ThemeSpec.Solid(clamped), null);
        this.config.ThemeId = null;
        this.config.AccentPresetIndex = clamped;
        this.config.Save();
    }

    public void SetTheme(string id)
    {
        if (this.ThemeId == id || FlagPresets.ById(id) is not { } spec)
            return;

        this.ApplySpec(spec, id);
        // Also record the nearest solid so an older build that ignores ThemeId still renders a color.
        this.config.ThemeId = id;
        this.config.AccentPresetIndex = spec.NearestSolidIndex;
        this.config.Save();
    }

    private void ApplySpec(ThemeSpec spec, string? themeId)
    {
        this.active = spec;
        this.ThemeId = themeId;
        this.AccentIndex = spec.NearestSolidIndex;

        // Retint first: AccentChannel.On and Palette.LiftForDark both read the freshly tinted Bg.
        Palette.Retint(spec.TintHue);

        // Primary fills own their contrast via On, so they keep the rich hue (never lifted). Secondary and
        // Tertiary are drawn as marks/washes on the dark bg, so a too-dark flag hue is lifted to stay legible;
        // for the twelve solids LiftForDark is a no-op, so their channels stay identical to before.
        this.Primary = AccentChannel.FromHue(spec.PrimaryHue);
        this.Secondary = AccentChannel.FromHue(Palette.LiftForDark(spec.SecondaryHue));
        this.Tertiary = AccentChannel.FromHue(Palette.LiftForDark(spec.TertiaryHue));
    }
}
