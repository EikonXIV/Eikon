using Eikon.Config;

namespace Eikon.UI.Theme;

// Holds the active theme and applies it: the base palette (surfaces, ink, hairlines, light/dark) flows
// through Palette.Apply, and the accent roles (Primary fills, Secondary marks, Tertiary washes) are
// derived from the theme's signal so a single pick reskins the whole app. Persisted in config by id.
internal sealed class ThemeService
{
    private readonly Configuration config;
    private ThemeDef active;

    public ThemeService(Configuration config)
    {
        this.config = config;
        this.active = Themes.ById(config.ThemeId);
        this.Apply(this.active);
    }

    public ThemeDef Current => this.active;

    public string CurrentThemeName => this.active.Name;

    public IReadOnlyList<ThemeDef> All => Themes.All;

    public bool IsSelected(string id) => this.active.Id == id;

    // The active theme's ordered stripe colors (pride only), empty otherwise. Feeds the flag bar/swatch.
    public IReadOnlyList<Vector4> Stripes => this.active.Stripes;

    // The three accent roles, derived from the active theme's signal.
    public AccentChannel Primary { get; private set; }
    public AccentChannel Secondary { get; private set; }
    public AccentChannel Tertiary { get; private set; }

    // Flat aliases kept for call-sites that read a single accent shade.
    public Vector4 Accent => this.Primary.Base;
    public Vector4 AccentDeep => this.Primary.Deep;
    public Vector4 AccentTint => this.Tertiary.Tint;
    public Vector4 AccentText => this.Tertiary.Text;
    public Vector4 OnAccent => this.Primary.On;

    public void SetTheme(string id)
    {
        var def = Themes.ById(id);
        if (def.Id == this.active.Id)
            return;

        this.Apply(def);
        this.config.ThemeId = def.Id;
        this.config.Save();
    }

    private void Apply(ThemeDef def)
    {
        this.active = def;

        // Base palette first: AccentChannel.On and LiftForDark both read the freshly applied Bg.
        Palette.Apply(def.Colors);

        var signal = def.Colors.Signal;
        this.Primary = AccentChannel.FromHue(signal);
        this.Secondary = AccentChannel.FromHue(Palette.LiftForDark(signal));
        this.Tertiary = AccentChannel.FromHue(signal);
    }
}
