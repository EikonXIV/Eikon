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

    public static readonly string[] Positions =
        { "Top", "Verse top", "Verse", "Verse bottom", "Bottom" };

    public static readonly string[] Roles = { "Dom", "Sub", "Switch" };

    public static readonly string[] Sizes = { "S", "M", "L", "XL", "Rather not say" };

    public static readonly string[] Meet = { "In-game", "Discord" };

    public static readonly string[] Kinks =
    {
        "Roleplay", "Praise", "Degradation", "Bondage", "Exhibition",
        "Voyeur", "Pet play", "Edging", "Aftercare", "Impact", "Sensory",
    };
}
