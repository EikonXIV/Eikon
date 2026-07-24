namespace Eikon.UI.Theme;

// Resolves a theme choice saved by a pre-catalog build to an id in the current catalog, so an update
// keeps the member on the theme they picked instead of silently dropping them on the default. Pure so
// the mapping can be tested without constructing the config or the theme service.
internal static class ThemeMigration
{
    // The two flag ids that were shortened in the old build. Every other flag id carried over unchanged.
    private const string LegacyBisexual = "bi";
    private const string LegacyNonBinary = "nb";

    // The retired AccentPresets order (Blue, Sky, Indigo, Violet, Fuchsia, Pink, Rose, Orange, Amber,
    // Emerald, Teal, Lime) mapped onto the catalog theme carrying that color. Indigo and Rose have no
    // catalog twin and take their nearest neighbour. Index 0 was the old default, and a member who
    // never opened the picker is indistinguishable from one who chose Blue, so it resolves to the
    // current default rather than stranding untouched installs on an accent.
    private static readonly string[] AccentThemes =
    {
        DefaultTheme, "sky", "blue", "violet", "magenta", "pink",
        "red", "orange", "amber", "green", "teal", "lime",
    };

    public const string DefaultTheme = "editorial-dark";

    public static string Resolve(string? themeId, int accentIndex)
    {
        if (!string.IsNullOrWhiteSpace(themeId))
        {
            return themeId switch
            {
                LegacyBisexual => "bisexual",
                LegacyNonBinary => "nonbinary",
                _ => themeId,
            };
        }

        return accentIndex >= 0 && accentIndex < AccentThemes.Length
            ? AccentThemes[accentIndex]
            : DefaultTheme;
    }
}
