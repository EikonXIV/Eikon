using System.IO;
using System.Threading.Tasks;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Eikon.Config;

namespace Eikon.UI;

// The Eikon type scale. Each role handle (title, name, body, eyebrow...) is rasterized at its real
// on-screen pixel size (design px x the game HUD scale x the Text size factor x the set's optical scale)
// so text stays crisp. Which font file a role loads comes from the active FontSet (Settings > Font), not
// a fixed family: the build delegates read the current set at bake time, so switching sets and rebuilding
// re-rasterizes every role from the new files. A Text size change re-rasterizes in the background
// (Rebuild) while Ui scales the draw-list text instantly, so the change reads at once and then sharpens.
// Fonts are bundled as embedded resources (see the csproj). The member-text handles merge the game's Axis
// glyphs (see Make's cjk flag) so non-Latin names and bios fall back to the game font rather than tofu;
// the bundled faces cover Latin, Greek and Cyrillic, and full Chinese/Korean rides the game font. Each
// set also gets a pair of specimen handles (display + body) so the picker can preview every face at once.
internal sealed class UiFonts : IDisposable
{
    // Ranges merged from the game font, deliberately EXCLUDING Latin: merged glyphs override the base
    // font's, so an unbounded merge would silently replace the bundled letterforms with the game's Axis
    // face. Pairs, zero-terminated (ImGui glyph-range format).
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
    private readonly Configuration config;
    private readonly List<IFontHandle> owned = new();

    // Display + body preview handles per set id, baked from every set's own files regardless of the active
    // one, so the Font picker shows each typeface in its own letterforms.
    private readonly Dictionary<string, (IFontHandle Display, IFontHandle Body)> specimens = new();

    private float buildScale;   // the Text size factor the build delegates rasterize at (the rebuild target)
    private FontSet current;    // the active set the role delegates resolve their file from at bake time

    public UiFonts(IDalamudPluginInterface pluginInterface, Configuration config)
    {
        this.atlas = pluginInterface.UiBuilder.FontAtlas;
        this.config = config;
        this.current = FontSets.ById(config.FontSetId);
        this.buildScale = Ui.Scale;
        Ui.FontBakedScale = Ui.Scale;   // the first build rasterizes at the startup Text size

        this.Title = this.Make(s => s.Display, 22f);
        this.SerifTitle = this.Make(s => s.Display, 28f, cjk: true);
        this.SerifName = this.Make(s => s.Display, 22f, cjk: true);
        this.SerifItalicTitle = this.Make(s => s.DisplayItalic, 28f, cjk: true);
        this.Body = this.Make(s => s.Body, 18f, cjk: true);
        this.Caption = this.Make(s => s.Body, 15f, cjk: true);
        this.Label = this.Make(s => s.Body, 15f, cjk: true);
        this.LabelSmall = this.Make(s => s.Body, 13f);
        this.Eyebrow = this.Make(s => s.Mono, 15f);
        this.Mono = this.Make(s => s.Mono, 12f);
        this.Count = this.Make(s => s.Mono, 18f);
        this.Icon = this.MakeIcon(17f);

        foreach (var set in FontSets.All)
            this.specimens[set.Id] = (this.MakeSpecimen(set.Display, 21f, set.Scale), this.MakeSpecimen(set.Body, 15f, set.Scale));
    }

    public IFontHandle Title { get; }             // Display 22 — wordmark, legacy headers
    public IFontHandle SerifTitle { get; }        // Display 28 — screen titles
    public IFontHandle SerifName { get; }         // Display 22 — list and message names
    public IFontHandle SerifItalicTitle { get; }  // Display italic 28 — two-tone titles
    public IFontHandle Body { get; }              // Body 18 — tile names, prominent content
    public IFontHandle Caption { get; }           // Body 15 — small content
    public IFontHandle Label { get; }             // Body 15 — nav, chips, values
    public IFontHandle LabelSmall { get; }        // Body 13 — dense labels
    public IFontHandle Eyebrow { get; }           // Mono 15 — eyebrows, tabs, meta
    public IFontHandle Mono { get; }              // Mono 12 — version tag (locked)
    public IFontHandle Count { get; }             // Mono 18 — counters

    // Our own scaled FontAwesome handle rather than Dalamud's fixed-size shared one, so icons grow with
    // the text when the member enlarges it.
    public IFontHandle Icon { get; }

    public FontSet CurrentSet => this.current;

    public IReadOnlyList<FontSet> Sets => FontSets.All;

    public bool IsSelected(string id) => this.current.Id == id;

    // The preview pair for a set, for the Font picker cards. Falls back to the default set's pair rather
    // than throwing if an unknown id is asked for.
    public (IFontHandle Display, IFontHandle Body) Specimen(string id) =>
        this.specimens.TryGetValue(id, out var pair) ? pair : this.specimens[FontSets.Default];

    // Switch the active typeface set, persist it, and re-bake every role handle from the new set's files
    // in the background. The Text size is unchanged, so draw-list text keeps rendering (in the old face)
    // until the new glyphs land, then snaps to the new one - the same brief settle as a Text size change.
    public void SetFontSet(string id)
    {
        var set = FontSets.ById(id);
        if (set.Id == this.current.Id)
            return;

        this.current = set;
        this.config.FontSetId = set.Id;
        this.config.Save();
        this.atlas.BuildFontsAsync();
    }

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

    private float ScaledPx(float designPx) => designPx * ImGuiHelpers.GlobalScale * this.buildScale * this.current.Scale;

    private static Stream OpenResource(string resource) =>
        typeof(UiFonts).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded font resource: {resource}");

    private IFontHandle Make(Func<FontSet, string> role, float designPx, bool cjk = false)
    {
        var handle = this.atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var resource = role(this.current);
            var px = this.ScaledPx(designPx);
            var font = tk.AddFontFromStream(OpenResource(resource), new SafeFontConfig { SizePx = px }, false, resource);

            // Fall back to the game's Axis glyphs for member-entered text, so a Japanese name or bio
            // renders in the game font instead of as tofu. Zero bundle cost. Restricted to CjkRanges:
            // an unbounded merge would override the bundled Latin letterforms too.
            if (cjk)
                tk.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, px), CjkRanges, font);
        }));
        this.owned.Add(handle);
        return handle;
    }

    // A picker preview handle, baked from one set's file at a fixed design size (times the Text size, so
    // previews grow with it). Latin-only sample text, so no CJK merge.
    private IFontHandle MakeSpecimen(string resource, float designPx, float setScale)
    {
        var handle = this.atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddFontFromStream(
                OpenResource(resource),
                new SafeFontConfig { SizePx = designPx * ImGuiHelpers.GlobalScale * this.buildScale * setScale },
                false,
                resource)));
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
