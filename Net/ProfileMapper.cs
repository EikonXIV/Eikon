using Eikon.Contracts;
using Eikon.UI;

namespace Eikon.Net;

// Maps the UI's option indices (Options.*) to the generated wire enums and back. The arrays below
// are in the SAME order as the matching Options.* arrays, so an index lines up with a wire value.
internal static class ProfileMapper
{
    private static readonly PronounEnum[] Pronouns =
        { PronounEnum.HeHim, PronounEnum.HeThey, PronounEnum.SheHer, PronounEnum.TheyThem, PronounEnum.TheyHe, PronounEnum.Any, PronounEnum.Ask };

    private static readonly GenderElement[] Genders =
        { GenderElement.CisMan, GenderElement.TransMan, GenderElement.Transmasc, GenderElement.NonBinary, GenderElement.Genderqueer, GenderElement.Intersex };

    private static readonly RaceElement[] Races =
        { RaceElement.Hyur, RaceElement.Miqote, RaceElement.Viera, RaceElement.Hrothgar, RaceElement.AuRa, RaceElement.Roegadyn, RaceElement.Elezen, RaceElement.Lalafell };

    private static readonly TribeElement[] Tribes =
    {
        TribeElement.Twink, TribeElement.Twunk, TribeElement.Femboy, TribeElement.Otter, TribeElement.Wolf, TribeElement.Jock,
        TribeElement.Muscle, TribeElement.Bear, TribeElement.Cub, TribeElement.Daddy, TribeElement.Pup, TribeElement.Leather,
        TribeElement.Geek, TribeElement.Rugged, TribeElement.Furry, TribeElement.Trans, TribeElement.Discreet,
    };

    private static readonly LookingForElement[] LookingFors =
    {
        LookingForElement.RightNow, LookingForElement.Hookups, LookingForElement.Erp, LookingForElement.Dates, LookingForElement.Relationship,
        LookingForElement.HangingOut, LookingForElement.ContentBuddies, LookingForElement.JustFriends, LookingForElement.Chat, LookingForElement.Penpals, LookingForElement.Rp,
    };

    private static readonly PositionElement[] Positions =
        { PositionElement.Top, PositionElement.VerseTop, PositionElement.Verse, PositionElement.VerseBottom, PositionElement.Bottom };

    private static readonly RoleEnum[] Roles = { RoleEnum.Dom, RoleEnum.Sub, RoleEnum.Switch };

    private static readonly SizeEnum[] Sizes = { SizeEnum.S, SizeEnum.M, SizeEnum.L, SizeEnum.Xl, SizeEnum.RatherNotSay };

    private static readonly MeetElement[] Meets = { MeetElement.InGame, MeetElement.Discord };

    public static PronounEnum Pronoun(int i) => At(Pronouns, i);

    public static GenderElement Gender(int i) => At(Genders, i);

    public static RaceElement Race(int i) => At(Races, i);

    public static PositionElement Position(int i) => At(Positions, i);

    public static RoleEnum Role(int i) => At(Roles, i);

    public static SizeEnum Size(int i) => At(Sizes, i);

    public static List<TribeElement> TribesOf(bool[] flags) => Selected(flags, Tribes);

    public static List<LookingForElement> LookingForOf(bool[] flags) => Selected(flags, LookingFors);

    public static List<MeetElement> MeetOf(bool[] flags) => Selected(flags, Meets);

    public static List<GenderElement> GendersOf(bool[] flags) => Selected(flags, Genders);

    public static List<PositionElement> PositionsOf(bool[] flags) => Selected(flags, Positions);

    public static List<RaceElement> RacesOf(bool[] flags) => Selected(flags, Races);

    // Reverse: wire value -> display label (for read-only views like profile detail).
    public static string Label(PronounEnum v) => Options.Pronouns[IndexOf(Pronouns, v)];

    public static string Label(GenderElement v) => Options.Genders[IndexOf(Genders, v)];

    public static string Label(RaceElement v) => Options.Races[IndexOf(Races, v)];

    public static string Label(PositionElement v) => Options.Positions[IndexOf(Positions, v)];

    public static string Label(RoleEnum v) => Options.Roles[IndexOf(Roles, v)];

    public static string Label(SizeEnum v) => Options.Sizes[IndexOf(Sizes, v)];

    public static string Label(LookingForElement v) => Options.LookingFor[IndexOf(LookingFors, v)];

    public static string Label(MeetElement v) => Options.Meet[IndexOf(Meets, v)];

    public static string[] Labels(IEnumerable<LookingForElement> values) => values.Select(Label).ToArray();

    public static string[] Labels(IEnumerable<RaceElement> values) => values.Select(Label).ToArray();

    // Reverse: wire value -> UI option index / flags (for loading the editable profile).
    public static int IndexOfPronoun(PronounEnum v) => IndexOf(Pronouns, v);

    public static int IndexOfGender(GenderElement v) => IndexOf(Genders, v);

    public static int IndexOfRace(RaceElement v) => IndexOf(Races, v);

    public static int IndexOfPosition(PositionElement v) => IndexOf(Positions, v);

    public static int IndexOfRole(RoleEnum v) => IndexOf(Roles, v);

    public static int IndexOfSize(SizeEnum v) => IndexOf(Sizes, v);

    public static bool[] FromTribes(IEnumerable<TribeElement> v) => Flags(v, Tribes);

    public static bool[] FromRaces(IEnumerable<RaceElement> v) => Flags(v, Races);

    public static bool[] FromLookingFor(IEnumerable<LookingForElement> v) => Flags(v, LookingFors);

    public static bool[] FromMeet(IEnumerable<MeetElement> v) => Flags(v, Meets);

    public static bool[] FromLabels(IEnumerable<string> values, string[] options)
    {
        var flags = new bool[options.Length];
        foreach (var v in values)
        {
            var i = Array.IndexOf(options, v);
            if (i >= 0)
                flags[i] = true;
        }

        return flags;
    }

    private static bool[] Flags<T>(IEnumerable<T> values, T[] map)
    {
        var flags = new bool[map.Length];
        foreach (var v in values)
        {
            var i = Array.IndexOf(map, v);
            if (i >= 0)
                flags[i] = true;
        }

        return flags;
    }

    private static int IndexOf<T>(T[] map, T value)
    {
        var i = Array.IndexOf(map, value);
        return i < 0 ? 0 : i;
    }

    private static T At<T>(T[] map, int i) => map[i < 0 || i >= map.Length ? 0 : i];

    private static List<T> Selected<T>(bool[] flags, T[] map)
    {
        var result = new List<T>();
        for (var i = 0; i < flags.Length && i < map.Length; i++)
            if (flags[i])
                result.Add(map[i]);
        return result;
    }
}
