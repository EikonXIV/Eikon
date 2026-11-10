namespace Eikon.UI;

// Display labels for the profile taxonomy from DESIGN.md section 7. Shared by onboarding, the
// profile editor, and the discovery filter. Wire format mapping to the contract enums happens in
// phase C; these are the human facing strings.
internal static class Options
{
    public static readonly string[] Pronouns =
        { "he/him", "he/they", "she/her", "they/them", "they/he", "any", "ask" };

    public static readonly string[] Genders =
        { "Cis man", "Trans man", "Transmasc", "Non-binary", "Genderqueer", "Intersex" };

    public static readonly string[] Tribes =
    {
        "Twink", "Twunk", "Femboy", "Otter", "Wolf", "Jock", "Muscle", "Bear",
        "Cub", "Daddy", "Pup", "Leather", "Geek", "Rugged", "Furry", "Trans", "Discreet",
    };

    public static readonly string[] Races =
        { "Hyur", "Miqo'te", "Viera", "Hrothgar", "Au Ra", "Roegadyn", "Elezen", "Lalafell" };

    public static readonly string[] LookingFor =
    {
        "Right now", "Hookups", "ERP", "Dates", "Relationship", "Hanging out",
        "Content buddies", "Just friends", "Chat", "Penpals", "RP",
    };

    public static readonly string[] Interests =
    {
        "Savage", "Ultimate", "Maps", "Hunts", "GPose", "Glam", "Housing", "Crafting",
        "PvP", "Roleplay", "Deep dungeons", "Music", "Lore", "Screenshots",
    };

    // Display labels only; the wire value is PositionElement, mapped by index in ProfileMapper. "Vers"
    // is the community's spelling of versatile, so a saved profile is unaffected by the wording here.
    public static readonly string[] Positions =
        { "Top", "Vers top", "Vers", "Vers bottom", "Bottom", "Side" };

    public static readonly string[] Roles = { "Dom", "Sub", "Switch" };

    public static readonly string[] Sizes = { "S", "M", "L", "XL", "Rather not say" };

    public static readonly string[] Meet = { "In-game", "Discord" };

    public static readonly string[] Kinks =
    {
        "Roleplay", "Praise", "Degradation", "Bondage", "Exhibition",
        "Voyeur", "Pet play", "Edging", "Aftercare", "Impact", "Sensory",
    };

    private static readonly Dictionary<string[], string[]> Keys = new()
    {
        [Pronouns] = KeysFor("pronoun", Pronouns),
        [Genders] = KeysFor("gender", Genders),
        [Tribes] = KeysFor("tribe", Tribes),
        [Races] = KeysFor("race", Races),
        [LookingFor] = KeysFor("lookingFor", LookingFor),
        [Interests] = KeysFor("interest", Interests),
        [Positions] = KeysFor("position", Positions),
        [Roles] = KeysFor("role", Roles),
        [Sizes] = KeysFor("size", Sizes),
        [Meet] = KeysFor("meet", Meet),
        [Kinks] = KeysFor("kink", Kinks),
    };

    private static readonly Dictionary<string[], string[]> Translated = new();

    private static string? translatedFor;

    public static string[] Labels(string[] values)
    {
        if (!Keys.TryGetValue(values, out var keys))
            return values;

        var locale = Loc.Current.Id;
        if (translatedFor != locale)
        {
            Translated.Clear();
            translatedFor = locale;
        }

        if (!Translated.TryGetValue(values, out var labels))
        {
            labels = Array.ConvertAll(keys, key => Loc.T(key));
            Translated[values] = labels;
        }

        return labels;
    }

    public static string Label(string[] values, string value)
    {
        var index = Array.IndexOf(values, value);
        return index >= 0 ? Labels(values)[index] : value;
    }

    public static string[] Labels(string[] values, IEnumerable<string> chosen) =>
        chosen.Select(value => Label(values, value)).ToArray();

    private static string[] KeysFor(string group, string[] values) =>
        Array.ConvertAll(values, value => "option." + group + "." + Slug(value));

    private static string Slug(string value)
    {
        var slug = new System.Text.StringBuilder(value.Length);
        var upper = false;
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upper = slug.Length > 0;
                continue;
            }

            slug.Append(upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            upper = false;
        }

        return slug.ToString();
    }
}
