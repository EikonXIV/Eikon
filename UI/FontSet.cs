namespace Eikon.UI;

// One selectable typeface set: which bundled font each UI role loads. Display drives titles and names
// (with an italic cut for the two-tone treatment), Body drives all UI and content text, Mono the
// eyebrows and counters. Scale optically balances a set whose x-height differs from the rest so no set
// reads bigger or smaller than the others at the same Text size. Pure data + catalog (no Dalamud), so
// the list, the default and the id resolution are unit-tested; UiFonts turns the active set into handles.
internal sealed record FontSet(
    string Id,
    string Name,
    string Tag,
    string Display,
    string DisplayItalic,
    string Body,
    string Mono,
    float Scale = 1f);

internal static class FontSets
{
    // Fresh installs and any config an older build wrote (no font id) resolve here. Clean fixes the
    // "thin and squished" complaint about the original Editorial faces while keeping serif names.
    public const string Default = "clean";

    private const string InstrumentSerif = "Eikon.Fonts.InstrumentSerif-Regular.ttf";
    private const string InstrumentSerifItalic = "Eikon.Fonts.InstrumentSerif-Italic.ttf";
    private const string InterTight = "Eikon.Fonts.InterTight.ttf";
    private const string JetBrainsMono = "Eikon.Fonts.JetBrainsMono.ttf";
    private const string SourceSerif = "Eikon.Fonts.SourceSerif4-Regular.ttf";
    private const string SourceSerifItalic = "Eikon.Fonts.SourceSerif4-Italic.ttf";
    private const string PlexSans = "Eikon.Fonts.IBMPlexSans-Regular.ttf";
    private const string Atkinson = "Eikon.Fonts.AtkinsonHyperlegible-Regular.ttf";
    private const string AtkinsonBold = "Eikon.Fonts.AtkinsonHyperlegible-Bold.ttf";
    private const string AtkinsonItalic = "Eikon.Fonts.AtkinsonHyperlegible-Italic.ttf";
    private const string Lexend = "Eikon.Fonts.Lexend-Regular.ttf";
    private const string LexendSemiBold = "Eikon.Fonts.Lexend-SemiBold.ttf";
    private const string Lato = "Eikon.Fonts.Lato-Regular.ttf";
    private const string LatoSemiBold = "Eikon.Fonts.Lato-SemiBold.ttf";
    private const string LatoItalic = "Eikon.Fonts.Lato-Italic.ttf";
    private const string OpenSans = "Eikon.Fonts.OpenSans-Regular.ttf";
    private const string OpenSansSemiBold = "Eikon.Fonts.OpenSans-SemiBold.ttf";
    private const string OpenSansItalic = "Eikon.Fonts.OpenSans-Italic.ttf";

    // Sans-only sets (no serif) render titles and names in the family's SemiBold/Bold and use its italic
    // for the two-tone tail. Lexend has no italic cut, so its two-tone reads by weight and colour alone.
    // Mono is JetBrains Mono across every set: the eyebrow/counter voice is not what "too thin" was about.
    public static readonly IReadOnlyList<FontSet> All = new[]
    {
        new FontSet("editorial", "Editorial", "Original serif", InstrumentSerif, InstrumentSerifItalic, InterTight, JetBrainsMono),
        new FontSet("clean", "Clean", "Serif + sans", SourceSerif, SourceSerifItalic, PlexSans, JetBrainsMono),
        new FontSet("readable", "Readable", "High legibility", AtkinsonBold, AtkinsonItalic, Atkinson, JetBrainsMono, 0.96f),
        new FontSet("lexend", "Lexend", "Wide & calm", LexendSemiBold, LexendSemiBold, Lexend, JetBrainsMono, 0.95f),
        new FontSet("lato", "Lato", "Warm humanist", LatoSemiBold, LatoItalic, Lato, JetBrainsMono),
        new FontSet("opensans", "Open Sans", "Clean & neutral", OpenSansSemiBold, OpenSansItalic, OpenSans, JetBrainsMono, 0.98f),
    };

    // A saved id an older or newer build cannot resolve, or a null (fresh install / pre-font-selector
    // config), falls back to the default set rather than throwing.
    public static FontSet ById(string? id) =>
        All.FirstOrDefault(f => f.Id == id) ?? All.First(f => f.Id == Default);
}
