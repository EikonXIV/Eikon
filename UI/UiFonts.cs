using System.Threading.Tasks;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;

namespace Eikon.UI;

// The Eikon type scale, ported to the warm-editorial families (Lovable prototype): Instrument Serif
// for the wordmark, titles and names (with an italic cut for the two-tone treatment), Inter Tight for
// UI and body, JetBrains Mono for eyebrows, counters and meta. Each handle is rasterized at its real
// on-screen pixel size (design px x the game HUD scale x buildScale) so text stays crisp; a Text size
// change re-rasterizes in the background (Rebuild) while Ui scales the draw-list text instantly, so the
// change reads at once and then sharpens. Fonts are bundled as embedded resources (see the csproj). The
// member-text handles merge the game's Axis glyphs (see Make's cjk flag) so non-Latin names and bios
// fall back to the game font rather than tofu; full Chinese/Korean would need a bundled Noto CJK face.
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

    private readonly IFontAtlas atlas;
    private readonly List<IFontHandle> owned = new();
    private float buildScale;   // the factor the build delegates rasterize at (the rebuild target)

    public UiFonts(IDalamudPluginInterface pluginInterface)
    {
        this.atlas = pluginInterface.UiBuilder.FontAtlas;
        this.buildScale = Ui.Scale;
        Ui.FontBakedScale = Ui.Scale;   // the first build rasterizes at the startup Text size

        this.Title = this.Make(SerifFile, 22f);
        this.SerifTitle = this.Make(SerifFile, 28f, cjk: true);
        this.SerifName = this.Make(SerifFile, 22f, cjk: true);
        this.SerifItalicTitle = this.Make(SerifItalicFile, 28f, cjk: true);
        this.Body = this.Make(SansFile, 18f, cjk: true);
        this.Caption = this.Make(SansFile, 15f, cjk: true);
        this.Label = this.Make(SansFile, 15f, cjk: true);
        this.LabelSmall = this.Make(SansFile, 13f);
        this.Eyebrow = this.Make(MonoFile, 15f);
        this.Mono = this.Make(MonoFile, 12f);
        this.Count = this.Make(MonoFile, 18f);
        this.Icon = this.MakeIcon(17f);
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

    // Our own scaled FontAwesome handle rather than Dalamud's fixed-size shared one, so icons grow with
    // the text when the member enlarges it.
    public IFontHandle Icon { get; }

    // Re-rasterize every handle at targetScale in the background. The build delegates read buildScale, so
    // the rebuild bakes at the new size; Ui.FontBakedScale flips to match only once the build finishes, so
    // draw-list text (scaled by Scale/FontBakedScale in Ui) reads at the new size immediately and sharpens
    // then.
    public void Rebuild(float targetScale)
    {
        this.buildScale = targetScale;
        this.atlas.BuildFontsAsync().ContinueWith(
            t => { if (t.Status == TaskStatus.RanToCompletion) Ui.FontBakedScale = targetScale; },
            TaskScheduler.Default);
    }

    private float ScaledPx(float designPx) => designPx * ImGuiHelpers.GlobalScale * this.buildScale;

    private IFontHandle Make(string resource, float designPx, bool cjk = false)
    {
        var handle = this.atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var px = this.ScaledPx(designPx);
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

    private IFontHandle MakeIcon(float designPx)
    {
        var handle = this.atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = this.ScaledPx(designPx) })));
        this.owned.Add(handle);
        return handle;
    }

    public void Dispose()
    {
        foreach (var handle in this.owned)
            handle.Dispose();
    }
}
