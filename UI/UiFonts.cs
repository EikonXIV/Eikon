using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;

namespace Eikon.UI;

// The Eikon type scale, ported to the warm-editorial families (Lovable prototype): Instrument Serif
// for the wordmark, titles and names (with an italic cut for the two-tone treatment), Inter Tight for
// UI and body, JetBrains Mono for eyebrows, counters and meta. Each handle is built at its real
// on-screen pixel size so text stays crisp. Fonts are bundled as embedded resources (see the csproj).
// The member-text handles additionally merge the game's Axis glyphs (see Make's cjk flag) so non-Latin
// names and bios fall back to the game font rather than rendering as tofu. On a global client Axis
// covers Japanese; full Chinese/Korean would need a bundled Noto CJK face, left out to keep this small.
internal sealed class UiFonts : IDisposable
{
    private const string SerifFile = "Eikon.Fonts.InstrumentSerif-Regular.ttf";
    private const string SerifItalicFile = "Eikon.Fonts.InstrumentSerif-Italic.ttf";
    private const string SansFile = "Eikon.Fonts.InterTight.ttf";
    private const string MonoFile = "Eikon.Fonts.JetBrainsMono.ttf";

    // Ranges merged from the game font, deliberately EXCLUDING Latin: merged glyphs override the base
    // font's, so an unbounded merge would silently replace Instrument Serif / Inter Tight letterforms
    // with the game's Axis face. Pairs, zero-terminated (ImGui glyph-range format).
    private static readonly ushort[] CjkRanges =
    {
        0x3000, 0x30FF,   // CJK punctuation, hiragana, katakana
        0x3400, 0x4DBF,   // CJK extension A
        0x4E00, 0x9FFF,   // CJK unified ideographs
        0xF900, 0xFAFF,   // CJK compatibility ideographs
        0xFF00, 0xFFEF,   // halfwidth and fullwidth forms
        0,
    };

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly List<IFontHandle> owned = new();

    public UiFonts(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        var atlas = pluginInterface.UiBuilder.FontAtlas;

        this.Title = this.Make(atlas, SerifFile, 22f);
        this.SerifTitle = this.Make(atlas, SerifFile, 28f, cjk: true);
        this.SerifName = this.Make(atlas, SerifFile, 22f, cjk: true);
        this.SerifItalicTitle = this.Make(atlas, SerifItalicFile, 28f, cjk: true);
        this.Body = this.Make(atlas, SansFile, 18f, cjk: true);
        this.Caption = this.Make(atlas, SansFile, 15f, cjk: true);
        this.Label = this.Make(atlas, SansFile, 15f, cjk: true);
        this.LabelSmall = this.Make(atlas, SansFile, 13f);
        this.Eyebrow = this.Make(atlas, MonoFile, 15f);
        this.Mono = this.Make(atlas, MonoFile, 12f);
        this.Count = this.Make(atlas, MonoFile, 18f);
    }

    public IFontHandle Title { get; }             // Instrument Serif 22 — wordmark, legacy headers
    public IFontHandle SerifTitle { get; }        // Instrument Serif 28 — screen titles
    public IFontHandle SerifName { get; }         // Instrument Serif 22 — list and message names
    public IFontHandle SerifItalicTitle { get; }  // Instrument Serif Italic 28 — two-tone titles
    public IFontHandle Body { get; }              // Inter Tight 18 — tile names, prominent content
    public IFontHandle Caption { get; }           // Inter Tight 15 — small content
    public IFontHandle Label { get; }             // Inter Tight 15 — nav, chips, values
    public IFontHandle LabelSmall { get; }        // Inter Tight 13 — dense labels
    public IFontHandle Eyebrow { get; }           // JetBrains Mono 15 — eyebrows, tabs, meta
    public IFontHandle Mono { get; }              // JetBrains Mono 12 — version tag (locked)
    public IFontHandle Count { get; }             // JetBrains Mono 18 — counters

    // The shared FontAwesome icon font. Owned by Dalamud, so it is not disposed here.
    public IFontHandle Icon => this.pluginInterface.UiBuilder.IconFontHandle;

    private IFontHandle Make(IFontAtlas atlas, string resource, float designPx, bool cjk = false)
    {
        var handle = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var px = designPx * ImGuiHelpers.GlobalScale;
            var font = tk.AddFontFromStream(
                typeof(UiFonts).Assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException($"Missing embedded font resource: {resource}"),
                new SafeFontConfig { SizePx = px },
                false,
                resource);

            // Fall back to the game's Axis glyphs for member-entered text, so a Japanese name or bio
            // renders in the game font instead of as tofu. Zero bundle cost. Restricted to CjkRanges:
            // an unbounded merge would override the bundled Latin letterforms too.
            if (cjk)
                tk.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, px), CjkRanges, font);
        }));
        this.owned.Add(handle);
        return handle;
    }

    public void Dispose()
    {
        foreach (var handle in this.owned)
            handle.Dispose();
    }
}
